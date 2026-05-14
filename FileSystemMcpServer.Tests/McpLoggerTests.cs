using FileSystemMcpServer.Logging;

namespace FileSystemMcpServer.Tests;

public class McpLoggerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _logPath;

    public McpLoggerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"mcp-log-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _logPath = Path.Combine(_testDir, "test.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void Log_CreatesLogFile()
    {
        var logger = new McpLogger(_logPath);
        logger.Log("INFO", "Test", "/path", "test message");

        Assert.True(File.Exists(_logPath));
    }

    [Fact]
    public void Log_WritesFormattedEntry()
    {
        var logger = new McpLogger(_logPath);
        logger.Log("ERROR", "Read", "/some/path", "File not found");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("[Read]", content);
        Assert.Contains("/some/path", content);
        Assert.Contains("File not found", content);
    }

    [Fact]
    public void Log_IncludesCallerIdentity()
    {
        var logger = new McpLogger(_logPath);
        logger.Log("INFO", "Test", "/path", "identity check");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("user=", content);
        Assert.Contains("pid=", content);
    }

    [Fact]
    public void Log_AppendsMultipleEntries()
    {
        var logger = new McpLogger(_logPath);
        logger.Log("INFO", "Op1", "/a", "first");
        logger.Log("WARN", "Op2", "/b", "second");

        var lines = File.ReadAllLines(_logPath);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void Log_RotatesWhenSizeExceeded()
    {
        // Create a logger and fill the file past 10 MB
        var logger = new McpLogger(_logPath);

        // Write a large chunk to get past the rotation threshold
        var bigMessage = new string('x', 1024);
        for (int i = 0; i < 11_000; i++)
        {
            logger.Log("INFO", "Bulk", "/path", bigMessage);
        }

        // After rotation, the .1 file should exist
        Assert.True(File.Exists(_logPath + ".1"), "Rotated log file .1 should exist");
    }

    [Fact]
    public void Constructor_CreatesLogDirectory()
    {
        var nestedLogPath = Path.Combine(_testDir, "sub", "deep", "app.log");
        var logger = new McpLogger(nestedLogPath);
        logger.Log("INFO", "Test", "/path", "dir creation test");

        Assert.True(File.Exists(nestedLogPath));
    }

    [Fact]
    public void IsEnabled_AlwaysReturnsTrue()
    {
        var logger = new McpLogger(_logPath);
        Assert.True(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Trace));
        Assert.True(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Critical));
    }

    [Fact]
    public void BeginScope_ReturnsWithoutThrowing()
    {
        var logger = new McpLogger(_logPath);
        // BeginScope returns a null-forgiving null; verify it does not throw
        var scope = logger.BeginScope("test");
        // Scope may be null (no-op implementation); just verify no exception
    }
}
