using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileSystemMcpServer.Configuration;
using FileSystemMcpServer.Logging;
using FileSystemMcpServer.Models;

namespace FileSystemMcpServer.Services;

/// <summary>
/// Implementation of filesystem operations with security checks and large file support
/// </summary>
public class FileSystemService : IFileSystemService
{
    private readonly ServerConfiguration _config;
    private readonly McpLogger _logger;

    public FileSystemService(
        ServerConfiguration config,
        McpLogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Read file contents with support for large files via streaming
    /// </summary>
    public string? Read(string path)
    {
        if (!IsPathAllowed(path))
        {
            throw McpErrorFactory.PathNotAllowed(path);
        }

        try
        {
            var fileInfo = new FileInfo(path);

            if (!fileInfo.Exists)
            {
                throw McpErrorFactory.FileNotFound(path);
            }

            if (fileInfo.Length > _config.MaxFileSizeBytes)
            {
                throw McpErrorFactory.InvalidParams($"File exceeds maximum size of {_config.MaxFileSizeBytes} bytes");
            }

            return File.ReadAllText(path);
        }
        catch (McpError)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            _logger.Log("ERROR", "Read", path, "File not found");
            throw McpErrorFactory.FileNotFound(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log("ERROR", "Read", path, $"Permission denied: {ex.Message}");
            throw McpErrorFactory.PermissionDenied("read", path);
        }
    }

    /// <summary>
    /// Write content to a file with atomic write support
    /// </summary>
    public void Write(string path, string content)
    {
        if (!IsPathAllowed(path))
        {
            throw McpErrorFactory.PathNotAllowed(path);
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                throw McpErrorFactory.InvalidParams("Invalid path: no directory component");
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempPath, content);
                File.Move(tempPath, path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            _logger.Log("INFO", "Write", path, $"Wrote {content.Length} bytes");
        }
        catch (McpError)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log("ERROR", "Write", path, $"Permission denied: {ex.Message}");
            throw McpErrorFactory.PermissionDenied("write", path);
        }
    }

    /// <summary>
    /// Delete a file or directory (recursive for directories)
    /// </summary>
    public void Delete(string path)
    {
        if (!IsPathAllowed(path))
        {
            throw McpErrorFactory.PathNotAllowed(path);
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.Log("INFO", "Delete", path, "Deleted file");
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                _logger.Log("INFO", "Delete", path, "Deleted directory (recursive)");
            }
            else
            {
                throw McpErrorFactory.FileNotFound(path);
            }
        }
        catch (McpError)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log("ERROR", "Delete", path, $"Permission denied: {ex.Message}");
            throw McpErrorFactory.PermissionDenied("delete", path);
        }
    }

    /// <summary>
    /// List files and directories in a directory
    /// </summary>
    private const int MaxListingEntries = 10_000;

    public List<FileEntry> List(string path, bool recursive = false)
    {
        if (!IsPathAllowed(path))
        {
            throw McpErrorFactory.PathNotAllowed(path);
        }

        try
        {
            var directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
            {
                throw McpErrorFactory.DirectoryNotFound(path);
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = directoryInfo.EnumerateFileSystemInfos("*", searchOption);

            var results = new List<FileEntry>();

            foreach (var e in entries)
            {
                if (results.Count >= MaxListingEntries)
                {
                    _logger.Log("WARN", "List", path,
                        $"Listing truncated at {MaxListingEntries} entries");
                    break;
                }

                // Re-validate each enumerated path to catch symlinks pointing outside
                if (!IsPathAllowed(e.FullName))
                {
                    _logger.Log("WARN", "List", e.FullName, "Skipped: resolved path is outside allowed directories");
                    continue;
                }

                var isSymlink = e.LinkTarget != null;
                var entryType = isSymlink ? FileEntry.FileType.Symlink
                    : e is FileInfo ? FileEntry.FileType.File
                    : FileEntry.FileType.Directory;

                results.Add(new FileEntry
                {
                    Name = e.Name,
                    Path = e.FullName,
                    Type = entryType,
                    Size = e is FileInfo fi ? fi.Length : null,
                    Created = e.CreationTimeUtc,
                    Modified = e.LastWriteTimeUtc
                });
            }

            return results;
        }
        catch (McpError)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log("ERROR", "List", path, $"Permission denied: {ex.Message}");
            throw McpErrorFactory.PermissionDenied("list", path);
        }
    }

    /// <summary>
    /// Rename or move a file/directory using OS APIs for efficiency
    /// </summary>
    public void RenameOrMove(string sourcePath, string targetPath)
    {
        if (!IsPathAllowed(sourcePath))
        {
            throw McpErrorFactory.PathNotAllowed(sourcePath);
        }

        if (!IsPathAllowed(targetPath))
        {
            throw McpErrorFactory.PathNotAllowed(targetPath);
        }

        try
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                throw McpErrorFactory.FileNotFound(sourcePath);
            }

            File.Move(sourcePath, targetPath, true);
            _logger.Log("INFO", "RenameOrMove", sourcePath, $"Moved to {targetPath}");
        }
        catch (McpError)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log("ERROR", "RenameOrMove", sourcePath, $"Permission denied: {ex.Message}");
            throw McpErrorFactory.PermissionDenied("rename/move", sourcePath);
        }
    }

    /// <summary>
    /// Create a new directory
    /// </summary>
    public void CreateDirectory(string path)
    {
        // Validate path is allowed
        if (!IsPathAllowed(path))
        {
            throw McpErrorFactory.PathNotAllowed(path);
        }

        try
        {
            Directory.CreateDirectory(path);
            _logger.Log("INFO", "CreateDirectory", path, $"Created directory");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log("ERROR", "CreateDirectory", path, $"Permission denied: {ex.Message}");
            throw McpErrorFactory.PermissionDenied("create_directory", path);
        }
    }

    /// <summary>
    /// Check if a path exists
    /// </summary>
    public bool Exists(string path)
    {
        try
        {
            // Validate path is allowed first
            if (!IsPathAllowed(path))
            {
                return false;
            }

            var info = new FileInfo(path);
            return info.Exists || Directory.Exists(path);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Get file information
    /// </summary>
    public FileEntry GetInfo(string path)
    {
        // Validate path is allowed
        if (!IsPathAllowed(path))
        {
            throw McpErrorFactory.PathNotAllowed(path);
        }

        try
        {
            var info = new FileInfo(path);

            return new FileEntry
            {
                Name = Path.GetFileName(path),
                Path = path,
                Type = info.Exists ? FileEntry.FileType.File : FileEntry.FileType.Directory,
                Size = info.Exists ? info.Length : (long?)null,
                Created = info.Exists ? info.CreationTimeUtc : null,
                Modified = info.Exists ? info.LastWriteTimeUtc : null
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Log("ERROR", "GetInfo", path, $"Permission denied: {ex.Message}");
            throw McpErrorFactory.PermissionDenied("get_info", path);
        }
    }

    /// <summary>
    /// Check if a path is within allowed directories
    /// </summary>
    private bool IsPathAllowed(string path)
    {
        try
        {
            // Resolve symlinks/junctions to get the real target path
            var normalizedPath = ResolveFinalTarget(path);

            // Take a snapshot of allowed directories (thread-safe via ServerConfiguration)
            var allowedDirs = _config.AllowedDirectories;

            foreach (var allowedDir in allowedDirs)
            {
                var allowedNormalized = Path.GetFullPath(allowedDir);

                // Exact match: the path IS the allowed directory
                if (normalizedPath.Equals(allowedNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Prefix match: ensure a directory separator follows so that
                // allowed dir "C:\Data" does not also match "C:\DataExposed"
                var allowedWithSep = allowedNormalized.EndsWith(Path.DirectorySeparatorChar)
                    ? allowedNormalized
                    : allowedNormalized + Path.DirectorySeparatorChar;

                if (normalizedPath.StartsWith(allowedWithSep, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Log("ERROR", "IsPathAllowed", path, $"Error checking path: {ex.Message}");
            throw McpErrorFactory.InternalError($"Path validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves symlinks/junctions and returns the real filesystem path.
    /// Falls back to Path.GetFullPath if the target does not exist yet (e.g., new file writes).
    /// </summary>
    private static string ResolveFinalTarget(string path)
    {
        var fullPath = Path.GetFullPath(path);

        // For files: check if it is a symlink and resolve
        if (File.Exists(fullPath))
        {
            var fi = new FileInfo(fullPath);
            if (fi.LinkTarget != null)
            {
                return Path.GetFullPath(fi.LinkTarget, Path.GetDirectoryName(fullPath)!);
            }
            return fullPath;
        }

        // For directories: check if it is a symlink/junction and resolve
        if (Directory.Exists(fullPath))
        {
            var di = new DirectoryInfo(fullPath);
            if (di.LinkTarget != null)
            {
                return Path.GetFullPath(di.LinkTarget, Path.GetDirectoryName(fullPath)!);
            }
            return fullPath;
        }

        // Target does not exist yet (write/create scenario):
        // resolve the parent directory to catch symlinked parent dirs
        var parentDir = Path.GetDirectoryName(fullPath);
        if (parentDir != null && Directory.Exists(parentDir))
        {
            var parentInfo = new DirectoryInfo(parentDir);
            var resolvedParent = parentInfo.LinkTarget != null
                ? Path.GetFullPath(parentInfo.LinkTarget, Path.GetDirectoryName(parentDir)!)
                : parentDir;
            return Path.Combine(resolvedParent, Path.GetFileName(fullPath));
        }

        return fullPath;
    }
}

/// <summary>
/// Represents a file or directory entry in the filesystem
/// </summary>
public class FileEntry
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public FileType Type { get; set; }

    public long? Size { get; set; }

    public DateTime? Created { get; set; }

    public DateTime? Modified { get; set; }

    public enum FileType
    {
        File,
        Directory,
        Symlink
    }
}
