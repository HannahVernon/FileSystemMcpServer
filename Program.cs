using FileSystemMcpServer.Configuration;
using FileSystemMcpServer.Logging;
using FileSystemMcpServer.Models;
using FileSystemMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FileSystemMcpServer;

/// <summary>
/// Typed arguments extracted from MCP JSON-RPC requests
/// </summary>
public class McpArgs
{
    public string? Path { get; set; }
    public string? Content { get; set; }
    public string? SourcePath { get; set; }
    public string? TargetPath { get; set; }
    public bool? Recursive { get; set; }
    public string? Directories { get; set; }
}

/// <summary>
/// .NET 10 Filesystem MCP Server for Ollama and LM Studio
/// Supports: read, write, delete, list, rename, move files
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        try
        {
            Console.Error.WriteLine("Starting FileSystem MCP Server...");

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging(builder =>
                    {
                        builder.ClearProviders();
                        builder.SetMinimumLevel(LogLevel.Information);
                        builder.AddConsole(options =>
                        {
                            options.LogToStandardErrorThreshold = LogLevel.Trace;
                        });
                    });

                    var config = new ServerConfiguration();

                    if (args.Length == 0)
                    {
                        Console.Error.WriteLine("Usage: FileSystemMcpServer <directory> [directory ...]");
                        Console.Error.WriteLine("At least one allowed directory must be specified.");
                        Environment.Exit(1);
                    }

                    foreach (var dir in args)
                    {
                        var fullPath = Path.GetFullPath(dir);
                        if (!Directory.Exists(fullPath))
                        {
                            Console.Error.WriteLine($"Warning: directory does not exist and will be skipped: {fullPath}");
                            continue;
                        }
                        config.AddAllowedDirectory(fullPath);
                    }

                    services.AddSingleton(config);
                    services.AddSingleton<IFileSystemService, FileSystemService>();

                    var logger = new McpLogger(config.LogFilePath);
                    services.AddSingleton(logger);

                    if (config.EnableFileSystemWatcher)
                    {
                        services.AddSingleton<FileSystemWatcherService>();
                    }

                    config.Validate();
                })
                .Build();

            Console.Error.WriteLine("Allowed directories:");
            foreach (var dir in host.Services.GetRequiredService<ServerConfiguration>().AllowedDirectories)
            {
                Console.Error.WriteLine($"  - {dir}");
            }

            Console.Error.WriteLine("\nMCP Server is ready. Waiting for JSON-RPC requests...");
            Console.Error.WriteLine("Supported methods: files/read, files/write, files/delete, files/list, files/renameOrMove");

            var config2 = host.Services.GetRequiredService<ServerConfiguration>();
            var watcherService = host.Services.GetService<FileSystemWatcherService>();
            if (watcherService != null)
            {
                watcherService.StartWatching();
            }

            RunMcpLoop(host);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Main loop for handling JSON-RPC requests via stdio
    /// </summary>
    private const int MaxLineLength = 10 * 1024 * 1024; // 10 MB
    internal static bool _initialized = false;

    internal static void RunMcpLoop(IHost host)
    {
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout);

        var jsonSettings = new JsonSerializerSettings { MaxDepth = 32 };

        while (true)
        {
            string? line;
            try
            {
                line = ReadLineBounded(reader, MaxLineLength);

                if (line == null)
                {
                    Console.Error.WriteLine("stdin closed. Shutting down.");
                    return;
                }

                if (line.Length == 0) continue;

                var request = JsonConvert.DeserializeObject<McpRequest>(line, jsonSettings);

                if (request == null || !request.Validate())
                {
                    SendError(writer, McpErrorFactory.ParseError("Invalid JSON-RPC request"));
                    continue;
                }

                // notifications/initialized is a notification (no id), just acknowledge
                if (request.Method == "notifications/initialized")
                {
                    Console.Error.WriteLine("Client initialization complete.");
                    continue;
                }

                // ping is allowed at any time per the MCP spec
                if (request.Method == "ping")
                {
                    writer.WriteLine(JsonConvert.SerializeObject(McpResponse.Success(new { }, request.Id)));
                    writer.Flush();
                    continue;
                }

                // Before initialization, only initialize is accepted
                if (!_initialized && request.Method != "initialize")
                {
                    SendError(writer, McpErrorFactory.InvalidRequest("Server not initialized. Send 'initialize' first."), request.Id);
                    continue;
                }

                var response = HandleMethod(request, host.Services);

                writer.WriteLine(JsonConvert.SerializeObject(response));
                writer.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing request: {ex.Message}");
                SendError(writer, McpErrorFactory.InternalError("An internal error occurred."));
            }
        }
    }

    /// <summary>
    /// Handle MCP method calls
    /// </summary>
    internal static McpResponse HandleMethod(McpRequest request, IServiceProvider services)
    {
        var fileService = services.GetRequiredService<IFileSystemService>();
        var config = services.GetRequiredService<ServerConfiguration>();

        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "files/read" => HandleRead(request, fileService),
            "files/write" => RequireCapability(config.CanWrite, "write", request)
                             ?? HandleWrite(request, fileService, config),
            "files/delete" => RequireCapability(config.CanDelete, "delete", request)
                              ?? HandleDelete(request, fileService),
            "files/list" => HandleList(request, fileService),
            "files/renameOrMove" => RequireCapability(config.CanRename, "renameOrMove", request)
                                    ?? HandleRenameOrMove(request, fileService),
            "filesystem/configureDirectories" => RequireCapability(config.CanConfigureDirectories, "configureDirectories", request)
                                                 ?? HandleConfigureDirectories(request, services),
            _ => McpResponse.Error(McpErrorFactory.MethodNotFound(request.Method), request.Id)
        };
    }

    internal static McpResponse? RequireCapability(bool allowed, string operation, McpRequest request)
    {
        if (allowed) return null;
        return McpResponse.Error(McpErrorFactory.OperationNotSupported(
            $"{operation} is disabled by server configuration"), request.Id);
    }

    internal static McpResponse HandleInitialize(McpRequest request)
    {
        _initialized = true;
        Console.Error.WriteLine("MCP initialization handshake accepted.");

        return McpResponse.Success(new
        {
            protocolVersion = "2025-03-26",
            capabilities = new
            {
                tools = new { listChanged = false }
            },
            serverInfo = new
            {
                name = "FileSystemMcpServer",
                version = "1.0.0"
            }
        }, request.Id);
    }

    private static McpResponse HandleRead(McpRequest request, IFileSystemService fileService)
    {
        var args = GetArgs(request);

        if (args == null || string.IsNullOrEmpty(args.Path))
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams("Missing or invalid 'path' argument"), request.Id);
        }

        try
        {
            var content = fileService.Read(args.Path);
            return McpResponse.Success(new { path = args.Path, content }, request.Id);
        }
        catch (McpError ex)
        {
            return McpResponse.Error(ex, request.Id);
        }
    }

    private static McpResponse HandleWrite(McpRequest request, IFileSystemService fileService, ServerConfiguration config)
    {
        var args = GetArgs(request);

        if (args == null || string.IsNullOrEmpty(args.Path))
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams("Missing or invalid 'path' argument"), request.Id);
        }

        if (args.Content == null)
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams("Missing 'content' argument"), request.Id);
        }

        if (args.Content.Length > config.MaxFileSizeBytes)
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams(
                $"Content exceeds maximum file size of {config.MaxFileSizeBytes} bytes"), request.Id);
        }

        try
        {
            fileService.Write(args.Path, args.Content);
            return McpResponse.Success(new
            {
                path = args.Path,
                writtenBytes = args.Content.Length
            }, request.Id);
        }
        catch (McpError ex)
        {
            return McpResponse.Error(ex, request.Id);
        }
    }

    private static McpResponse HandleDelete(McpRequest request, IFileSystemService fileService)
    {
        var args = GetArgs(request);

        if (args == null || string.IsNullOrEmpty(args.Path))
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams("Missing or invalid 'path' argument"), request.Id);
        }

        try
        {
            fileService.Delete(args.Path);
            return McpResponse.Success(new { path = args.Path, deleted = true }, request.Id);
        }
        catch (McpError ex)
        {
            return McpResponse.Error(ex, request.Id);
        }
    }

    private static McpResponse HandleList(McpRequest request, IFileSystemService fileService)
    {
        var args = GetArgs(request);

        if (args == null || string.IsNullOrEmpty(args.Path))
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams("Missing or invalid 'path' argument"), request.Id);
        }

        try
        {
            var entries = fileService.List(args.Path, args.Recursive ?? false);
            return McpResponse.Success(entries, request.Id);
        }
        catch (McpError ex)
        {
            return McpResponse.Error(ex, request.Id);
        }
    }

    private static McpResponse HandleRenameOrMove(McpRequest request, IFileSystemService fileService)
    {
        var args = GetArgs(request);

        if (args == null || string.IsNullOrEmpty(args.SourcePath) || string.IsNullOrEmpty(args.TargetPath))
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams("Missing 'sourcePath' and/or 'targetPath' arguments"), request.Id);
        }

        try
        {
            fileService.RenameOrMove(args.SourcePath, args.TargetPath);
            return McpResponse.Success(new
            {
                source = args.SourcePath,
                target = args.TargetPath,
                moved = true
            }, request.Id);
        }
        catch (McpError ex)
        {
            return McpResponse.Error(ex, request.Id);
        }
    }

    private static McpResponse HandleConfigureDirectories(McpRequest request, IServiceProvider services)
    {
        var config = services.GetRequiredService<ServerConfiguration>();
        var args = GetArgs(request);

        if (args == null || string.IsNullOrEmpty(args.Directories))
        {
            return McpResponse.Error(McpErrorFactory.InvalidParams("Missing 'directories' argument"), request.Id);
        }

        try
        {
            foreach (var dir in args.Directories.Split(','))
            {
                config.AddAllowedDirectory(dir.Trim());
            }

            return McpResponse.Success(new
            {
                message = "Directories configured successfully",
                allowedDirectories = string.Join(", ", config.AllowedDirectories)
            }, request.Id);
        }
        catch (McpError ex)
        {
            return McpResponse.Error(ex, request.Id);
        }
    }

    /// <summary>
    /// Extract typed arguments from MCP request
    /// </summary>
    private static McpArgs? GetArgs(McpRequest request)
    {
        if (request.Params?.Arguments == null) return null;

        try
        {
            var token = request.Params.Arguments as JObject
                        ?? JObject.FromObject(request.Params.Arguments);
            return token.ToObject<McpArgs>();
        }
        catch
        {
            return null;
        }
    }

    private static void SendError(StreamWriter writer, McpError error)
    {
        var response = McpResponse.Error(error, null);
        writer.WriteLine(JsonConvert.SerializeObject(response));
        writer.Flush();
    }

    private static void SendError(StreamWriter writer, McpError error, object? id)
    {
        var response = McpResponse.Error(error, id);
        writer.WriteLine(JsonConvert.SerializeObject(response));
        writer.Flush();
    }

    /// <summary>
    /// Reads a line from the stream with a maximum length to prevent memory exhaustion.
    /// Returns null at end-of-stream.
    /// </summary>
    internal static string? ReadLineBounded(StreamReader reader, int maxLength)
    {
        var sb = new System.Text.StringBuilder();
        int ch;
        while ((ch = reader.Read()) != -1)
        {
            if (ch == '\n') break;
            if (ch == '\r')
            {
                if (reader.Peek() == '\n') reader.Read();
                break;
            }

            if (sb.Length >= maxLength)
            {
                throw new InvalidOperationException(
                    $"Input line exceeds maximum length of {maxLength} bytes. Discarding.");
            }

            sb.Append((char)ch);
        }

        if (ch == -1 && sb.Length == 0) return null;
        return sb.ToString();
    }
}
