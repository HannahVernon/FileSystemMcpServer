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

You can add additional directories at runtime using the `filesystem/configureDirectories` method.

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

Add the server to your LM Studio MCP configuration:

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

## Security

All file operations are restricted to directories listed in the server configuration. Path traversal attacks are mitigated by normalizing all paths with `Path.GetFullPath` before checking against the allow list. See [SECURITY.md](SECURITY.md) for vulnerability reporting.

## License

[MIT](LICENSE)
