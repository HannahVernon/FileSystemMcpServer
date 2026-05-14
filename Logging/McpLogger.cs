using System;
using Microsoft.Extensions.Logging;

namespace FileSystemMcpServer.Logging;

/// <summary>
/// Logger for MCP server operations with file output
/// </summary>
public class McpLogger : ILogger
{
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private const long MaxLogFileSize = 10 * 1024 * 1024; // 10 MB
    private const int MaxLogFiles = 5;

    public McpLogger(string? logFilePath)
    {
        _logFilePath = logFilePath ?? "mcp-server.log";

        var logDir = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
    }

    /// <summary>
    /// Logs a message to the file in plain text format
    /// </summary>
    public void Log(string level, string operation, string path, string details)
    {
        var timestamp = DateTime.UtcNow.ToString("o");
        var logEntry = $"[{timestamp}] [{level}] [{operation}] {path}: {details}";

        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to write log: {ex.Message}");
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logFilePath)) return;

        var fileInfo = new FileInfo(_logFilePath);
        if (fileInfo.Length < MaxLogFileSize) return;

        // Shift existing rotated files: .4 -> .5 (delete), .3 -> .4, .2 -> .3, .1 -> .2
        for (int i = MaxLogFiles - 1; i >= 1; i--)
        {
            var source = $"{_logFilePath}.{i}";
            var target = $"{_logFilePath}.{i + 1}";

            if (File.Exists(target)) File.Delete(target);
            if (File.Exists(source)) File.Move(source, target);
        }

        // Current -> .1
        File.Move(_logFilePath, $"{_logFilePath}.1");
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var levelStr = logLevel switch
        {
            LogLevel.Critical => "CRITICAL",
            LogLevel.Error => "ERROR",
            LogLevel.Warning => "WARNING",
            LogLevel.Information => "INFO",
            LogLevel.Debug => "DEBUG",
            _ => "UNKNOWN"
        };

        Log(levelStr, eventId.Id.ToString(), message, exception?.Message ?? "");
    }
}
