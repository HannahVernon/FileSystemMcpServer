# Architecture

## Project structure

```
FileSystemMcpServer/
  .github/
    workflows/
      ci.yml                    - GitHub Actions CI (build + test on push/PR)
      version-bump.yml          - Auto-tag on PR merge to main
      build-release.yml         - Build, publish, and create GitHub Release
    ISSUE_TEMPLATE/             - Bug report and feature request forms
    PULL_REQUEST_TEMPLATE.md    - PR template
  Configuration/
    ServerConfiguration.cs      - Allowed directories, file size limits, watcher toggle
  Logging/
    McpLogger.cs                - File-based logger implementing ILogger
  Models/
    McpError.cs                 - Error codes, McpError exception, McpErrorFactory
    McpRequestResponse.cs       - McpRequest, McpParams, McpResponse DTOs
  Services/
    IFileSystemService.cs       - Interface for filesystem operations
    FileSystemService.cs        - Implementation with path validation and security checks
    FileSystemWatcherService.cs - Optional directory change monitoring
  FileSystemMcpServer.Tests/
    ServerConfigurationTests.cs - ServerConfiguration unit tests
    FileSystemServiceTests.cs   - FileSystemService unit tests
    McpLoggerTests.cs           - McpLogger unit tests
    McpProtocolTests.cs         - MCP JSON-RPC protocol handler tests
  Program.cs                    - Entry point, DI setup, JSON-RPC stdio loop, McpArgs DTO
  Directory.Build.props         - MinVer versioning configuration
  FileSystemMcpServer.csproj    - Project file (.NET 10)
  FileSystemMcpServer.sln       - Solution file
```

## Communication flow

```
LM Studio / Ollama
    |
    | stdin (JSON-RPC 2.0 request, one JSON object per line)
    v
Program.RunMcpLoop()
    |
    +--> JsonConvert.DeserializeObject<McpRequest>(line)
    +--> HandleMethod() -- routes by request.Method
    |       |
    |       +--> HandleRead / HandleWrite / HandleDelete / HandleList / HandleRenameOrMove
    |       |       |
    |       |       +--> IFileSystemService (path validation, file I/O)
    |       |
    |       +--> HandleConfigureDirectories (modifies ServerConfiguration)
    |
    +--> JsonConvert.SerializeObject(response)
    |
    | stdout (JSON-RPC 2.0 response, one JSON object per line)
    v
LM Studio / Ollama
```

## Key components

### Program.cs

Entry point.  Sets up dependency injection via `Microsoft.Extensions.Hosting`, then runs the main stdio loop (`RunMcpLoop`).  Allowed directories are accepted as command-line arguments (no hardcoded defaults).  Contains the `McpArgs` DTO used to deserialize JSON-RPC arguments, and all `Handle*` methods that map MCP methods to `IFileSystemService` calls.

### ServerConfiguration

Holds the list of allowed directories, maximum file size, log file path, and the file watcher toggle.  Allowed directories are supplied via command-line arguments at startup; there are no hardcoded defaults.  `Validate()` prunes directories that do not exist on the current OS.  `AddAllowedDirectory()` normalizes paths via `Path.GetFullPath` to prevent traversal attacks.

### FileSystemService

Implements all file operations. Every method checks `IsPathAllowed()` before touching the filesystem. Write operations use atomic temp-file-then-rename. The `List` method returns `FileEntry` objects with name, path, type, size, and timestamps.

### McpError / McpErrorFactory

`McpError` extends `Exception` so it can be thrown and caught in service methods, then serialized into JSON-RPC error responses. `McpErrorFactory` provides static factory methods for standard JSON-RPC errors (-32700 through -32603) and custom filesystem errors (4001 through 4007).

### McpLogger

A simple file-appending logger that implements `ILogger`. Writes timestamped entries to `mcp-server.log`. Thread-safe via `lock`.

### FileSystemWatcherService

Optional service that monitors allowed directories for changes using `FileSystemWatcher`. Logs create, delete, change, and rename events. Enabled by default; controlled by `ServerConfiguration.EnableFileSystemWatcher`.

## JSON serialization

The project uses Newtonsoft.Json throughout. Model classes use `[JsonProperty]` attributes for JSON field naming and `DefaultValueHandling` for null suppression. `Program.cs` uses `JsonConvert.SerializeObject` / `DeserializeObject` and `JObject` for argument extraction.

## Security model

All file operations are gated by `IsPathAllowed()`, which normalizes the requested path and checks that it falls under at least one entry in `ServerConfiguration.AllowedDirectories`. Paths are compared case-insensitively using `StringComparison.OrdinalIgnoreCase`.
