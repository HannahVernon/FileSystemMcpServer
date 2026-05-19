# FileSystemMcpServer

A .NET 10 filesystem MCP (Model Context Protocol) server that exposes file operations over JSON-RPC 2.0 via stdio. Designed for use with LM Studio, Ollama, or any MCP-compatible client.

## Features

- **Read** file contents with size limit enforcement
- **Write** files with atomic write support (temp file + rename)
- **Delete** files and directories (recursive)
- **List** directory contents (flat or recursive)
- **Rename/Move** files and directories
- **Configure** allowed directories at runtime
- **Security** - all operations restricted to explicitly allowed directories
- **File watching** - optional filesystem change monitoring via `FileSystemWatcher`

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Building

```shell
dotnet build
```

## Running

```shell
dotnet run -- /path/to/allowed-dir1 /path/to/allowed-dir2
```

Pass one or more directory paths as command-line arguments.  The server restricts all file operations to those directories.  Directories that do not exist are skipped with a warning.

The server communicates via stdin/stdout using JSON-RPC 2.0.  Diagnostic output goes to stderr so it does not interfere with the protocol.

## Configuration

The server accepts allowed directories as command-line arguments.  You can add more at runtime using the `filesystem/configureDirectories` method.

### Server capabilities

The server supports several capability flags that control which operations are permitted.  These are set in `ServerConfiguration` and can be adjusted if you extend the server with a config file or environment variables:

Option | Default | Description
-------|---------|------------
`ReadOnly` | `false` | When `true`, blocks all write, delete, and rename operations
`AllowDelete` | `true` | When `false`, blocks file and directory deletion
`AllowRename` | `true` | When `false`, blocks rename and move operations
`AllowConfigureDirectories` | `true` | When `false`, blocks runtime directory additions
`MaxFileSizeBytes` | 100 MB | Maximum file size for read and write operations
`MaxAllowedDirectories` | 50 | Maximum number of allowed directories
`MaxPathLength` | 260 | Maximum path length accepted
`EnableFileSystemWatcher` | `true` | Enables directory change monitoring

## JSON-RPC Methods

Method | Description | Required arguments
-------|-------------|-------------------
`files/read` | Read file contents | `path`
`files/write` | Write content to a file | `path`, `content`
`files/delete` | Delete a file or directory | `path`
`files/list` | List directory contents | `path`, optional `recursive` (bool)
`files/renameOrMove` | Rename or move a file/directory | `sourcePath`, `targetPath`
`filesystem/configureDirectories` | Add allowed directories | `directories` (comma-separated)

### Example request

```json
{
  "jsonrpc": "2.0",
  "method": "files/read",
  "params": {
    "arguments": {
      "path": "C:\\Users\\Public\\MCPFiles\\example.txt"
    }
  },
  "id": 1
}
```

### Example response

```json
{
  "jsonrpc": "2.0",
  "result": {
    "path": "C:\\Users\\Public\\MCPFiles\\example.txt",
    "content": "Hello, world!"
  },
  "id": 1
}
```

## LM Studio integration

Add the server to your LM Studio MCP configuration (typically `~/.lmstudio/mcp.json` or via Settings > MCP):

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\FileSystemMcpServer", "--", "C:\\Users\\Public\\MCPFiles"]
    }
  }
}
```

## Claude Desktop

Add to your Claude Desktop config file (`%APPDATA%\Claude\claude_desktop_config.json` on Windows, `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/FileSystemMcpServer", "--", "/home/user/documents", "/tmp/workspace"]
    }
  }
}
```

## VS Code (GitHub Copilot)

Add to your VS Code `settings.json` or workspace `.vscode/settings.json`:

```json
{
  "github.copilot.chat.mcpServers": {
    "filesystem": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\FileSystemMcpServer", "--", "C:\\projects"]
    }
  }
}
```

## Cursor

Add to your Cursor MCP config file (`~/.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/FileSystemMcpServer", "--", "/home/user/projects"]
    }
  }
}
```

## Windsurf

Add to your Windsurf MCP config file (`~/.codeium/windsurf/mcp_config.json`):

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/FileSystemMcpServer", "--", "/home/user/projects"]
    }
  }
}
```

## Ollama (via Open WebUI or other MCP-capable front-ends)

Ollama itself does not natively support MCP, but front-ends like Open WebUI can connect to MCP servers.  Consult your front-end's documentation for the MCP configuration format; the command and arguments are the same:

```
command: dotnet
args:    run --project /path/to/FileSystemMcpServer -- /home/user/documents
```

## Using a published executable

If you publish the server as a standalone executable, replace `dotnet run --project ...` with the path to the built binary:

```json
{
  "mcpServers": {
    "filesystem": {
      "command": "C:\\tools\\FileSystemMcpServer.exe",
      "args": ["C:\\Users\\Public\\MCPFiles", "D:\\shared"]
    }
  }
}
```

To publish a self-contained binary:

```shell
dotnet publish -c Release -r win-x64 --self-contained
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r osx-arm64 --self-contained
```

## Security

All file operations are restricted to directories listed in the server configuration. Path traversal attacks are mitigated by normalizing all paths with `Path.GetFullPath` before checking against the allow list. See [SECURITY.md](SECURITY.md) for vulnerability reporting.

## License

[MIT](LICENSE)
