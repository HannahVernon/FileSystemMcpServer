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

                    config.AllowedDirectories.AddRange(new[]
                    {
                        "/tmp/mcp-files",
                        @"C:\Users\Public\MCPFiles",
                        "/home/user/.mcp"
                    });

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
    private static void RunMcpLoop(IHost host)
    {
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout);

        while (true)
        {
            string? line;
            try
            {
                line = reader.ReadLine();

                if (string.IsNullOrEmpty(line)) continue;

                var request = JsonConvert.DeserializeObject<McpRequest>(line);

                if (request == null || !request.Validate())
                {
                    SendError(writer, McpErrorFactory.ParseError("Invalid JSON-RPC request"));
                    continue;
                }

                var response = HandleMethod(request, host.Services);

                writer.WriteLine(JsonConvert.SerializeObject(response));
                writer.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing request: {ex.Message}");
                SendError(writer, McpErrorFactory.InternalError(ex.Message));
            }
        }
    }

    /// <summary>
    /// Handle MCP method calls
    /// </summary>
    private static McpResponse HandleMethod(McpRequest request, IServiceProvider services)
    {
        var fileService = services.GetRequiredService<IFileSystemService>();

        return request.Method switch
        {
            "files/read" => HandleRead(request, fileService),
            "files/write" => HandleWrite(request, fileService),
            "files/delete" => HandleDelete(request, fileService),
            "files/list" => HandleList(request, fileService),
            "files/renameOrMove" => HandleRenameOrMove(request, fileService),
            "filesystem/configureDirectories" => HandleConfigureDirectories(request, services),
            _ => McpResponse.Error(McpErrorFactory.MethodNotFound(request.Method), request.Id)
        };
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

    private static McpResponse HandleWrite(McpRequest request, IFileSystemService fileService)
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
}
