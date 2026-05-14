using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace FileSystemMcpServer.Configuration;

/// <summary>
/// Server configuration with allowed directories and settings
/// </summary>
public class ServerConfiguration
{
    private readonly ReaderWriterLockSlim _dirLock = new();
    private readonly List<string> _allowedDirectories = new();

    [JsonProperty("allowedDirectories")]
    public List<string> AllowedDirectories
    {
        get
        {
            _dirLock.EnterReadLock();
            try
            {
                return _allowedDirectories.ToList();
            }
            finally
            {
                _dirLock.ExitReadLock();
            }
        }
        set
        {
            _dirLock.EnterWriteLock();
            try
            {
                _allowedDirectories.Clear();
                if (value != null)
                {
                    _allowedDirectories.AddRange(value);
                }
            }
            finally
            {
                _dirLock.ExitWriteLock();
            }
        }
    }

    [JsonProperty("logFilePath", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? LogFilePath { get; set; } = "mcp-server.log";

    [JsonProperty("maxFileSizeBytes", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long MaxFileSizeBytes { get; set; } = 1024 * 1024 * 100; // 100MB default

    [JsonProperty("enableFileSystemWatcher", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool EnableFileSystemWatcher { get; set; } = true;

    [JsonProperty("readOnly", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool ReadOnly { get; set; } = false;

    [JsonProperty("allowDelete", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool AllowDelete { get; set; } = true;

    [JsonProperty("allowRename", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool AllowRename { get; set; } = true;

    [JsonProperty("allowConfigureDirectories", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool AllowConfigureDirectories { get; set; } = true;

    /// <summary>
    /// Returns true if write operations are permitted (not read-only mode).
    /// </summary>
    public bool CanWrite => !ReadOnly;

    /// <summary>
    /// Returns true if delete operations are permitted (not read-only and allowDelete).
    /// </summary>
    public bool CanDelete => !ReadOnly && AllowDelete;

    /// <summary>
    /// Returns true if rename/move operations are permitted (not read-only and allowRename).
    /// </summary>
    public bool CanRename => !ReadOnly && AllowRename;

    /// <summary>
    /// Returns true if runtime directory configuration is permitted.
    /// </summary>
    public bool CanConfigureDirectories => !ReadOnly && AllowConfigureDirectories;

    /// <summary>
    /// Validates that allowed directories exist (only checks directories present on this OS)
    /// </summary>
    public void Validate()
    {
        _dirLock.EnterWriteLock();
        try
        {
            var invalid = _allowedDirectories
                .Where(dir => Path.IsPathRooted(dir) && !Directory.Exists(dir))
                .ToList();

            foreach (var dir in invalid)
            {
                _allowedDirectories.Remove(dir);
            }

            if (_allowedDirectories.Count == 0)
            {
                throw new InvalidOperationException("No valid allowed directories configured. At least one must exist.");
            }
        }
        finally
        {
            _dirLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Adds a new allowed directory and validates it
    /// </summary>
    public void AddAllowedDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty", nameof(path));
        }

        var normalizedPath = Path.GetFullPath(path);

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"Directory does not exist: {normalizedPath}");
        }

        _dirLock.EnterWriteLock();
        try
        {
            if (!_allowedDirectories.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
            {
                _allowedDirectories.Add(normalizedPath);
            }
        }
        finally
        {
            _dirLock.ExitWriteLock();
        }
    }
}
