using FileSystemMcpServer.Configuration;
using FileSystemMcpServer.Logging;
using FileSystemMcpServer.Models;
using FileSystemMcpServer.Services;

namespace FileSystemMcpServer.Tests;

public class FileSystemServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly ServerConfiguration _config;
    private readonly McpLogger _logger;
    private readonly FileSystemService _service;

    public FileSystemServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"mcp-fs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _config = new ServerConfiguration();
        _config.AddAllowedDirectory(_testDir);

        var logPath = Path.Combine(_testDir, "test.log");
        _logger = new McpLogger(logPath);

        _service = new FileSystemService(_config, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    // --- Read ---

    [Fact]
    public void Read_ReturnsFileContents()
    {
        var filePath = Path.Combine(_testDir, "hello.txt");
        File.WriteAllText(filePath, "Hello, world!");

        var content = _service.Read(filePath);
        Assert.Equal("Hello, world!", content);
    }

    [Fact]
    public void Read_ThrowsForNonexistentFile()
    {
        var filePath = Path.Combine(_testDir, "missing.txt");
        var ex = Assert.Throws<McpError>(() => _service.Read(filePath));
        Assert.Equal(McpErrorCode.FileNotFound, ex.Code);
    }

    [Fact]
    public void Read_ThrowsForPathOutsideAllowedDirs()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}", "test.txt");
        var ex = Assert.Throws<McpError>(() => _service.Read(outsidePath));
        Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
    }

    [Fact]
    public void Read_ThrowsForFileTooLarge()
    {
        _config.MaxFileSizeBytes = 10;
        var filePath = Path.Combine(_testDir, "big.txt");
        File.WriteAllText(filePath, new string('x', 100));

        var ex = Assert.Throws<McpError>(() => _service.Read(filePath));
        Assert.Equal(McpErrorCode.InvalidParams, ex.Code);
    }

    // --- Write ---

    [Fact]
    public void Write_CreatesNewFile()
    {
        var filePath = Path.Combine(_testDir, "new.txt");
        _service.Write(filePath, "new content");

        Assert.True(File.Exists(filePath));
        Assert.Equal("new content", File.ReadAllText(filePath));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var filePath = Path.Combine(_testDir, "existing.txt");
        File.WriteAllText(filePath, "old");

        _service.Write(filePath, "updated");
        Assert.Equal("updated", File.ReadAllText(filePath));
    }

    [Fact]
    public void Write_ThrowsForPathOutsideAllowedDirs()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}", "test.txt");
        var ex = Assert.Throws<McpError>(() => _service.Write(outsidePath, "data"));
        Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
    }

    [Fact]
    public void Write_CreatesSubdirectory()
    {
        var filePath = Path.Combine(_testDir, "subdir", "nested.txt");
        _service.Write(filePath, "nested");

        Assert.True(File.Exists(filePath));
        Assert.Equal("nested", File.ReadAllText(filePath));
    }

    // --- Delete ---

    [Fact]
    public void Delete_RemovesFile()
    {
        var filePath = Path.Combine(_testDir, "todelete.txt");
        File.WriteAllText(filePath, "bye");

        _service.Delete(filePath);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void Delete_RemovesDirectoryRecursively()
    {
        var subDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "child.txt"), "child");

        _service.Delete(subDir);
        Assert.False(Directory.Exists(subDir));
    }

    [Fact]
    public void Delete_ThrowsForNonexistent()
    {
        var filePath = Path.Combine(_testDir, "ghost.txt");
        var ex = Assert.Throws<McpError>(() => _service.Delete(filePath));
        Assert.Equal(McpErrorCode.FileNotFound, ex.Code);
    }

    [Fact]
    public void Delete_ThrowsForPathOutsideAllowedDirs()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}", "test.txt");
        var ex = Assert.Throws<McpError>(() => _service.Delete(outsidePath));
        Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
    }

    // --- List ---

    [Fact]
    public void List_ReturnsDirectoryContents()
    {
        File.WriteAllText(Path.Combine(_testDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_testDir, "b.txt"), "b");

        var entries = _service.List(_testDir);

        // At least 2 files (might also contain test.log)
        Assert.True(entries.Count >= 2);
        Assert.Contains(entries, e => e.Name == "a.txt");
        Assert.Contains(entries, e => e.Name == "b.txt");
    }

    [Fact]
    public void List_RecursiveIncludesSubdirectories()
    {
        var subDir = Path.Combine(_testDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "deep.txt"), "deep");

        var entries = _service.List(_testDir, recursive: true);
        Assert.Contains(entries, e => e.Name == "deep.txt");
    }

    [Fact]
    public void List_ThrowsForNonexistentDirectory()
    {
        var fakePath = Path.Combine(_testDir, "nope");
        var ex = Assert.Throws<McpError>(() => _service.List(fakePath));
        Assert.Equal(McpErrorCode.DirectoryNotFound, ex.Code);
    }

    [Fact]
    public void List_ThrowsForPathOutsideAllowedDirs()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}");
        var ex = Assert.Throws<McpError>(() => _service.List(outsidePath));
        Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
    }

    [Fact]
    public void List_ReturnsCorrectFileTypes()
    {
        File.WriteAllText(Path.Combine(_testDir, "file.txt"), "f");
        var subDir = Path.Combine(_testDir, "dir");
        Directory.CreateDirectory(subDir);

        var entries = _service.List(_testDir);

        var fileEntry = entries.First(e => e.Name == "file.txt");
        Assert.Equal(FileEntry.FileType.File, fileEntry.Type);

        var dirEntry = entries.First(e => e.Name == "dir");
        Assert.Equal(FileEntry.FileType.Directory, dirEntry.Type);
    }

    // --- RenameOrMove ---

    [Fact]
    public void RenameOrMove_MovesFile()
    {
        var src = Path.Combine(_testDir, "source.txt");
        var dst = Path.Combine(_testDir, "dest.txt");
        File.WriteAllText(src, "moving");

        _service.RenameOrMove(src, dst);

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("moving", File.ReadAllText(dst));
    }

    [Fact]
    public void RenameOrMove_ThrowsWhenSourceMissing()
    {
        var src = Path.Combine(_testDir, "gone.txt");
        var dst = Path.Combine(_testDir, "dest.txt");

        var ex = Assert.Throws<McpError>(() => _service.RenameOrMove(src, dst));
        Assert.Equal(McpErrorCode.FileNotFound, ex.Code);
    }

    [Fact]
    public void RenameOrMove_ThrowsForPathOutsideAllowedDirs()
    {
        var src = Path.Combine(_testDir, "ok.txt");
        File.WriteAllText(src, "data");
        var outsideDst = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}", "moved.txt");

        var ex = Assert.Throws<McpError>(() => _service.RenameOrMove(src, outsideDst));
        Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
    }

    // --- CreateDirectory ---

    [Fact]
    public void CreateDirectory_CreatesNewDirectory()
    {
        var newDir = Path.Combine(_testDir, "brand-new");
        _service.CreateDirectory(newDir);
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void CreateDirectory_ThrowsForPathOutsideAllowedDirs()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}");
        var ex = Assert.Throws<McpError>(() => _service.CreateDirectory(outsidePath));
        Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
    }

    // --- Exists ---

    [Fact]
    public void Exists_ReturnsTrueForExistingFile()
    {
        var filePath = Path.Combine(_testDir, "exists.txt");
        File.WriteAllText(filePath, "yes");
        Assert.True(_service.Exists(filePath));
    }

    [Fact]
    public void Exists_ReturnsTrueForExistingDirectory()
    {
        Assert.True(_service.Exists(_testDir));
    }

    [Fact]
    public void Exists_ReturnsFalseForMissingPath()
    {
        var missing = Path.Combine(_testDir, "missing.txt");
        Assert.False(_service.Exists(missing));
    }

    [Fact]
    public void Exists_ReturnsFalseForPathOutsideAllowedDirs()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}");
        Assert.False(_service.Exists(outsidePath));
    }

    // --- GetInfo ---

    [Fact]
    public void GetInfo_ReturnsFileInfo()
    {
        var filePath = Path.Combine(_testDir, "info.txt");
        File.WriteAllText(filePath, "info content");

        var entry = _service.GetInfo(filePath);
        Assert.Equal("info.txt", entry.Name);
        Assert.Equal(FileEntry.FileType.File, entry.Type);
        Assert.NotNull(entry.Size);
        Assert.True(entry.Size > 0);
    }

    [Fact]
    public void GetInfo_ThrowsForPathOutsideAllowedDirs()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}", "test.txt");
        var ex = Assert.Throws<McpError>(() => _service.GetInfo(outsidePath));
        Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
    }

    // --- IsPathAllowed boundary test ---

    [Fact]
    public void IsPathAllowed_RejectsSimilarlyNamedDirectory()
    {
        // Allowed: _testDir (e.g., "...mcp-fs-test-abc123")
        // Should reject: _testDir + "Exposed" (e.g., "...mcp-fs-test-abc123Exposed")
        var sneakyDir = _testDir + "Exposed";
        Directory.CreateDirectory(sneakyDir);
        try
        {
            var filePath = Path.Combine(sneakyDir, "secret.txt");
            File.WriteAllText(filePath, "secret");

            var ex = Assert.Throws<McpError>(() => _service.Read(filePath));
            Assert.Equal(McpErrorCode.PathNotAllowed, ex.Code);
        }
        finally
        {
            Directory.Delete(sneakyDir, true);
        }
    }
}
