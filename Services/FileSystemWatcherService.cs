using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using FileSystemMcpServer.Configuration;

namespace FileSystemMcpServer.Services;

/// <summary>
/// Service for monitoring filesystem changes using FileSystemWatcher
/// </summary>
public class FileSystemWatcherService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ServerConfiguration _config;
    private readonly ILogger<FileSystemWatcherService> _logger;
    private readonly Dictionary<string, DateTime> _recentEvents = new();
    private readonly object _debounceLock = new();
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(500);

    public FileSystemWatcherService(ServerConfiguration config, ILogger<FileSystemWatcherService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Start watching all allowed directories for changes
    /// </summary>
    public void StartWatching()
    {
        if (!_config.EnableFileSystemWatcher) return;

        foreach (var dir in _config.AllowedDirectories)
        {
            try
            {
                var watcher = new FileSystemWatcher(dir, "*.*")
                {
                    EnableRaisingEvents = true,
                    NotifyFilter = NotifyFilters.CreationTime | 
                                   NotifyFilters.FileName | 
                                   NotifyFilters.LastWrite | 
                                   NotifyFilters.Size | 
                                   NotifyFilters.DirectoryName
                };

                watcher.Created += OnCreated;
                watcher.Deleted += OnDeleted;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnRenamed;
                
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException)
            {
                _logger.LogWarning($"Could not start FileSystemWatcher for: {dir} - {ex.Message}");
            }
        }

        _logger.LogInformation("FileSystemWatcher started for all allowed directories");
    }

    /// <summary>
    /// Stop watching all directories
    /// </summary>
    public void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error disposing FileSystemWatcher: {ex.Message}");
            }
        }

        _watchers.Clear();
    }

    private void OnCreated(object? sender, FileSystemEventArgs e) => LogChange("CREATED", e.FullPath);
    private void OnDeleted(object? sender, FileSystemEventArgs e) => LogChange("DELETED", e.FullPath);
    private void OnChanged(object? sender, FileSystemEventArgs e) => LogChange("CHANGED", e.FullPath);
    private void OnRenamed(object? sender, RenamedEventArgs e) => LogChange("RENAMED", $"{e.OldFullPath} -> {e.Name}");

    private void LogChange(string action, string path)
    {
        lock (_debounceLock)
        {
            var key = $"{action}:{path}";
            var now = DateTime.UtcNow;

            if (_recentEvents.TryGetValue(key, out var lastTime) && (now - lastTime) < DebounceWindow)
            {
                return;
            }

            _recentEvents[key] = now;
        }

        _logger.LogInformation($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{action}] {path}");
    }

    public void Dispose()
    {
        StopWatching();
    }
}
