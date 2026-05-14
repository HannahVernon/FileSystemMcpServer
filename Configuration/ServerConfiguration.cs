using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace FileSystemMcpServer.Configuration;

/// <summary>
/// Server configuration with allowed directories and settings
/// </summary>
public class ServerConfiguration
{
    [JsonProperty("allowedDirectories")]
    public List<string> AllowedDirectories { get; set; } = new();

    [JsonProperty("logFilePath", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? LogFilePath { get; set; } = "mcp-server.log";

    [JsonProperty("maxFileSizeBytes", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public long MaxFileSizeBytes { get; set; } = 1024 * 1024 * 100; // 100MB default

    [JsonProperty("enableFileSystemWatcher", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool EnableFileSystemWatcher { get; set; } = true;

    /// <summary>
    /// Validates that allowed directories exist (only checks directories present on this OS)
    /// </summary>
    public void Validate()
    {
        var invalid = AllowedDirectories
            .Where(dir => Path.IsPathRooted(dir) && !Directory.Exists(dir))
            .ToList();

        foreach (var dir in invalid)
        {
            AllowedDirectories.Remove(dir);
        }

        if (AllowedDirectories.Count == 0)
        {
            throw new InvalidOperationException("No valid allowed directories configured. At least one must exist.");
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

        if (!AllowedDirectories.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            AllowedDirectories.Add(normalizedPath);
        }
    }
}
