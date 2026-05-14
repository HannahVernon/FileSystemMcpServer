using System.Collections.Generic;
using FileSystemMcpServer.Models;

namespace FileSystemMcpServer.Services;

/// <summary>
/// Interface for filesystem operations
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Read file contents
    /// </summary>
    string? Read(string path);

    /// <summary>
    /// Write content to a file
    /// </summary>
    void Write(string path, string content);

    /// <summary>
    /// Delete a file or directory (recursive for directories)
    /// </summary>
    void Delete(string path);

    /// <summary>
    /// List files and directories in a directory
    /// </summary>
    List<FileEntry> List(string path, bool recursive = false);

    /// <summary>
    /// Rename or move a file/directory
    /// </summary>
    void RenameOrMove(string sourcePath, string targetPath);

    /// <summary>
    /// Create a new directory
    /// </summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Check if a path exists
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Get file information (size, modified time, etc.)
    /// </summary>
    FileEntry GetInfo(string path);
}
