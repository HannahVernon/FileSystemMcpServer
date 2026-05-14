using FileSystemMcpServer.Configuration;

namespace FileSystemMcpServer.Tests;

public class ServerConfigurationTests : IDisposable
{
    private readonly string _testDir;

    public ServerConfigurationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"mcp-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [Fact]
    public void AllowedDirectories_StartsEmpty()
    {
        var config = new ServerConfiguration();
        Assert.Empty(config.AllowedDirectories);
    }

    [Fact]
    public void AddAllowedDirectory_NormalizesPath()
    {
        var config = new ServerConfiguration();
        config.AddAllowedDirectory(_testDir);

        var dirs = config.AllowedDirectories;
        Assert.Single(dirs);
        Assert.Equal(Path.GetFullPath(_testDir), dirs[0]);
    }

    [Fact]
    public void AddAllowedDirectory_RejectsDuplicate()
    {
        var config = new ServerConfiguration();
        config.AddAllowedDirectory(_testDir);
        config.AddAllowedDirectory(_testDir);

        Assert.Single(config.AllowedDirectories);
    }

    [Fact]
    public void AddAllowedDirectory_ThrowsOnEmpty()
    {
        var config = new ServerConfiguration();
        Assert.Throws<ArgumentException>(() => config.AddAllowedDirectory(""));
    }

    [Fact]
    public void AddAllowedDirectory_ThrowsOnNonexistentDirectory()
    {
        var config = new ServerConfiguration();
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(() => config.AddAllowedDirectory(fakePath));
    }

    [Fact]
    public void AddAllowedDirectory_ThrowsWhenMaxReached()
    {
        var config = new ServerConfiguration { MaxAllowedDirectories = 1 };
        config.AddAllowedDirectory(_testDir);

        var secondDir = Path.Combine(Path.GetTempPath(), $"mcp-test2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(secondDir);
        try
        {
            Assert.Throws<InvalidOperationException>(() => config.AddAllowedDirectory(secondDir));
        }
        finally
        {
            Directory.Delete(secondDir, true);
        }
    }

    [Fact]
    public void AddAllowedDirectory_ThrowsWhenPathTooLong()
    {
        var config = new ServerConfiguration { MaxPathLength = 5 };
        Assert.Throws<ArgumentException>(() => config.AddAllowedDirectory(_testDir));
    }

    [Fact]
    public void Validate_PrunesNonexistentDirectories()
    {
        var config = new ServerConfiguration();
        config.AddAllowedDirectory(_testDir);

        // Add a nonexistent path directly via the setter to bypass AddAllowedDirectory checks
        var dirs = config.AllowedDirectories;
        dirs.Add(Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N")));
        config.AllowedDirectories = dirs;

        config.Validate();

        Assert.Single(config.AllowedDirectories);
        Assert.Equal(Path.GetFullPath(_testDir), config.AllowedDirectories[0]);
    }

    [Fact]
    public void Validate_ThrowsWhenAllDirectoriesInvalid()
    {
        var config = new ServerConfiguration();
        var dirs = new List<string>
        {
            Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N"))
        };
        config.AllowedDirectories = dirs;

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void CanWrite_FalseWhenReadOnly()
    {
        var config = new ServerConfiguration { ReadOnly = true };
        Assert.False(config.CanWrite);
    }

    [Fact]
    public void CanWrite_TrueByDefault()
    {
        var config = new ServerConfiguration();
        Assert.True(config.CanWrite);
    }

    [Fact]
    public void CanDelete_RequiresBothFlags()
    {
        var config = new ServerConfiguration();
        Assert.True(config.CanDelete);

        config.AllowDelete = false;
        Assert.False(config.CanDelete);

        config.AllowDelete = true;
        config.ReadOnly = true;
        Assert.False(config.CanDelete);
    }

    [Fact]
    public void CanRename_RequiresBothFlags()
    {
        var config = new ServerConfiguration();
        Assert.True(config.CanRename);

        config.AllowRename = false;
        Assert.False(config.CanRename);

        config.AllowRename = true;
        config.ReadOnly = true;
        Assert.False(config.CanRename);
    }

    [Fact]
    public void CanConfigureDirectories_RequiresBothFlags()
    {
        var config = new ServerConfiguration();
        Assert.True(config.CanConfigureDirectories);

        config.AllowConfigureDirectories = false;
        Assert.False(config.CanConfigureDirectories);

        config.AllowConfigureDirectories = true;
        config.ReadOnly = true;
        Assert.False(config.CanConfigureDirectories);
    }

    [Fact]
    public void AllowedDirectories_ThreadSafe_SetterReplacesAll()
    {
        var config = new ServerConfiguration();
        config.AddAllowedDirectory(_testDir);

        var secondDir = Path.Combine(Path.GetTempPath(), $"mcp-test2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(secondDir);
        try
        {
            config.AllowedDirectories = new List<string> { secondDir };
            var dirs = config.AllowedDirectories;
            Assert.Single(dirs);
            Assert.Equal(secondDir, dirs[0]);
        }
        finally
        {
            Directory.Delete(secondDir, true);
        }
    }

    [Fact]
    public void DefaultValues_AreReasonable()
    {
        var config = new ServerConfiguration();
        Assert.Equal(100 * 1024 * 1024, config.MaxFileSizeBytes);
        Assert.Equal(260, config.MaxPathLength);
        Assert.Equal(50, config.MaxAllowedDirectories);
        Assert.True(config.EnableFileSystemWatcher);
        Assert.Equal("mcp-server.log", config.LogFilePath);
    }
}
