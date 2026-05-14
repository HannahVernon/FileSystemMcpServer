using FileSystemMcpServer.Configuration;
using FileSystemMcpServer.Logging;
using FileSystemMcpServer.Models;
using FileSystemMcpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace FileSystemMcpServer.Tests;

/// <summary>
/// Tests for the MCP JSON-RPC protocol handling in Program.cs
/// </summary>
public class McpProtocolTests : IDisposable
{
    private readonly string _testDir;
    private readonly IServiceProvider _services;

    public McpProtocolTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"mcp-proto-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        var config = new ServerConfiguration();
        config.AddAllowedDirectory(_testDir);

        var logger = new McpLogger(Path.Combine(_testDir, "test.log"));

        var sc = new ServiceCollection();
        sc.AddSingleton(config);
        sc.AddSingleton(logger);
        sc.AddSingleton<IFileSystemService, FileSystemService>();
        _services = sc.BuildServiceProvider();

        // Ensure initialized state for HandleMethod tests
        Program._initialized = true;
    }

    public void Dispose()
    {
        Program._initialized = false;
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    // --- Initialize ---

    [Fact]
    public void HandleInitialize_ReturnsProtocolVersion()
    {
        var request = MakeRequest("initialize", null, 1);
        var response = Program.HandleInitialize(request);

        Assert.NotNull(response.Result);
        Assert.Null(response.ErrorDetail);

        var result = JObject.FromObject(response.Result);
        Assert.Equal("2025-03-26", result["protocolVersion"]?.ToString());
        Assert.NotNull(result["serverInfo"]);
    }

    // --- HandleMethod routing ---

    [Fact]
    public void HandleMethod_ReturnsMethodNotFound()
    {
        var request = MakeRequest("unknown/method", null, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.NotNull(response.ErrorDetail);
        Assert.Equal(McpErrorCode.MethodNotFound, response.ErrorDetail.Code);
    }

    [Fact]
    public void HandleMethod_FilesRead_RequiresPath()
    {
        var request = MakeRequest("files/read", new { }, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.NotNull(response.ErrorDetail);
        Assert.Equal(McpErrorCode.InvalidParams, response.ErrorDetail.Code);
    }

    [Fact]
    public void HandleMethod_FilesRead_ReturnsContent()
    {
        var filePath = Path.Combine(_testDir, "read-test.txt");
        File.WriteAllText(filePath, "protocol test content");

        var request = MakeRequest("files/read", new { path = filePath }, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.Null(response.ErrorDetail);
        var result = JObject.FromObject(response.Result!);
        Assert.Equal("protocol test content", result["content"]?.ToString());
    }

    [Fact]
    public void HandleMethod_FilesWrite_CreatesFile()
    {
        var filePath = Path.Combine(_testDir, "write-test.txt");

        var request = MakeRequest("files/write", new { path = filePath, content = "written" }, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.Null(response.ErrorDetail);
        Assert.True(File.Exists(filePath));
        Assert.Equal("written", File.ReadAllText(filePath));
    }

    [Fact]
    public void HandleMethod_FilesWrite_RequiresContent()
    {
        var filePath = Path.Combine(_testDir, "no-content.txt");

        var request = MakeRequest("files/write", new { path = filePath }, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.NotNull(response.ErrorDetail);
        Assert.Equal(McpErrorCode.InvalidParams, response.ErrorDetail.Code);
    }

    [Fact]
    public void HandleMethod_FilesDelete_RemovesFile()
    {
        var filePath = Path.Combine(_testDir, "delete-test.txt");
        File.WriteAllText(filePath, "doomed");

        var request = MakeRequest("files/delete", new { path = filePath }, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.Null(response.ErrorDetail);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void HandleMethod_FilesList_ReturnsEntries()
    {
        File.WriteAllText(Path.Combine(_testDir, "list-a.txt"), "a");
        File.WriteAllText(Path.Combine(_testDir, "list-b.txt"), "b");

        var request = MakeRequest("files/list", new { path = _testDir }, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.Null(response.ErrorDetail);
        var result = JArray.FromObject(response.Result!);
        Assert.True(result.Count >= 2);
    }

    [Fact]
    public void HandleMethod_FilesRenameOrMove_Works()
    {
        var src = Path.Combine(_testDir, "mv-src.txt");
        var dst = Path.Combine(_testDir, "mv-dst.txt");
        File.WriteAllText(src, "moving");

        var request = MakeRequest("files/renameOrMove",
            new { sourcePath = src, targetPath = dst }, 1);
        var response = Program.HandleMethod(request, _services);

        Assert.Null(response.ErrorDetail);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
    }

    // --- Capability enforcement ---

    [Fact]
    public void HandleMethod_FilesWrite_BlockedInReadOnlyMode()
    {
        var config = _services.GetRequiredService<ServerConfiguration>();
        config.ReadOnly = true;
        try
        {
            var request = MakeRequest("files/write",
                new { path = Path.Combine(_testDir, "blocked.txt"), content = "x" }, 1);
            var response = Program.HandleMethod(request, _services);

            Assert.NotNull(response.ErrorDetail);
            Assert.Equal(McpErrorCode.OperationNotSupported, response.ErrorDetail.Code);
        }
        finally
        {
            config.ReadOnly = false;
        }
    }

    [Fact]
    public void HandleMethod_FilesDelete_BlockedWhenDisabled()
    {
        var config = _services.GetRequiredService<ServerConfiguration>();
        config.AllowDelete = false;
        try
        {
            var filePath = Path.Combine(_testDir, "no-del.txt");
            File.WriteAllText(filePath, "safe");

            var request = MakeRequest("files/delete", new { path = filePath }, 1);
            var response = Program.HandleMethod(request, _services);

            Assert.NotNull(response.ErrorDetail);
            Assert.Equal(McpErrorCode.OperationNotSupported, response.ErrorDetail.Code);
            Assert.True(File.Exists(filePath), "File should not have been deleted");
        }
        finally
        {
            config.AllowDelete = true;
        }
    }

    [Fact]
    public void HandleMethod_FilesRenameOrMove_BlockedWhenDisabled()
    {
        var config = _services.GetRequiredService<ServerConfiguration>();
        config.AllowRename = false;
        try
        {
            var src = Path.Combine(_testDir, "mv-blocked.txt");
            File.WriteAllText(src, "stay");

            var request = MakeRequest("files/renameOrMove",
                new { sourcePath = src, targetPath = Path.Combine(_testDir, "mv-dst2.txt") }, 1);
            var response = Program.HandleMethod(request, _services);

            Assert.NotNull(response.ErrorDetail);
            Assert.Equal(McpErrorCode.OperationNotSupported, response.ErrorDetail.Code);
        }
        finally
        {
            config.AllowRename = true;
        }
    }

    // --- RequireCapability ---

    [Fact]
    public void RequireCapability_ReturnsNull_WhenAllowed()
    {
        var request = MakeRequest("test", null, 1);
        Assert.Null(Program.RequireCapability(true, "test", request));
    }

    [Fact]
    public void RequireCapability_ReturnsError_WhenBlocked()
    {
        var request = MakeRequest("test", null, 1);
        var result = Program.RequireCapability(false, "test", request);

        Assert.NotNull(result);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal(McpErrorCode.OperationNotSupported, result.ErrorDetail.Code);
    }

    // --- ReadLineBounded ---

    [Fact]
    public void ReadLineBounded_ReturnsLine()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello\n"));
        using var reader = new StreamReader(stream);

        var line = Program.ReadLineBounded(reader, 1024);
        Assert.Equal("hello", line);
    }

    [Fact]
    public void ReadLineBounded_ReturnsNullAtEndOfStream()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        using var reader = new StreamReader(stream);

        var line = Program.ReadLineBounded(reader, 1024);
        Assert.Null(line);
    }

    [Fact]
    public void ReadLineBounded_ThrowsWhenExceedingMax()
    {
        var longLine = new string('x', 100) + "\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(longLine));
        using var reader = new StreamReader(stream);

        Assert.Throws<InvalidOperationException>(() => Program.ReadLineBounded(reader, 50));
    }

    [Fact]
    public void ReadLineBounded_HandlesCRLF()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("line\r\n"));
        using var reader = new StreamReader(stream);

        var line = Program.ReadLineBounded(reader, 1024);
        Assert.Equal("line", line);
    }

    // --- McpRequest validation ---

    [Fact]
    public void McpRequest_Validate_RequiresJsonRpcAndMethod()
    {
        var valid = new McpRequest { JsonRpc = "2.0", Method = "test" };
        Assert.True(valid.Validate());

        var noMethod = new McpRequest { JsonRpc = "2.0", Method = "" };
        Assert.False(noMethod.Validate());

        var wrongVersion = new McpRequest { JsonRpc = "1.0", Method = "test" };
        Assert.False(wrongVersion.Validate());
    }

    // --- McpResponse factories ---

    [Fact]
    public void McpResponse_Success_SetsResultAndId()
    {
        var response = McpResponse.Success(new { value = 42 }, 7);
        Assert.Equal("2.0", response.JsonRpc);
        Assert.NotNull(response.Result);
        Assert.Null(response.ErrorDetail);
        Assert.Equal(7, response.Id);
    }

    [Fact]
    public void McpResponse_Error_SetsErrorAndId()
    {
        var error = McpErrorFactory.InternalError("boom");
        var response = McpResponse.Error(error, 9);

        Assert.Null(response.Result);
        Assert.NotNull(response.ErrorDetail);
        Assert.Equal(McpErrorCode.InternalError, response.ErrorDetail.Code);
        Assert.Equal(9, response.Id);
    }

    // --- Helper ---

    private static McpRequest MakeRequest(string method, object? args, object? id)
    {
        return new McpRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = args != null
                ? new McpParams { Arguments = JObject.FromObject(args) }
                : null,
            Id = id
        };
    }
}
