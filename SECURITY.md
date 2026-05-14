# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it responsibly.

**Email:** vuln@mvct.com

You can also use [GitHub's private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) to submit a report directly through this repository.

## What to include

- A description of the vulnerability
- Steps to reproduce it
- The potential impact
- Any suggested fix (optional)

## Response timeline

- **Acknowledgement:** within 3 business days
- **Status update:** within 10 business days
- **Fix or mitigation:** as soon as practical, depending on severity

## Scope

This policy covers the FileSystemMcpServer codebase. It does not cover third-party dependencies, though we will coordinate with upstream maintainers if a transitive vulnerability is reported.

## Supported versions

Only the latest release on the `main` branch receives security updates.

## Security model

### stdio transport

This MCP server communicates via JSON-RPC over stdin/stdout using the
[stdio transport](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports#stdio)
defined in the MCP specification. Under this model:

- The **host process** (e.g., LM Studio, Ollama, VS Code) launches the server as a
  child process and owns stdin/stdout.
- **Authentication is the host's responsibility.** The MCP specification does not define
  an authentication mechanism for stdio transport. Any process with access to the
  server's stdin can send requests.
- The server enforces an **allowed-directory boundary**: only paths within configured
  allowed directories are accessible, and symlinks that resolve outside those
  directories are rejected.

### Recommendations for host integrators

- Launch the server with the **minimum necessary privileges**. Do not run as
  administrator/root unless required.
- Restrict the allowed directories to the narrowest scope needed for your use case.
- Do not expose the server's stdin/stdout to untrusted processes.
- Consider running the server in a sandboxed environment (container, restricted user
  account) for defense in depth.

### configureDirectories

The `filesystem/configureDirectories` method allows runtime expansion of the allowed
directory list. This is a privileged operation: it is only available after the MCP
`initialize` handshake completes. Host integrators should be aware that any client that
completes initialization can call this method to add new directories.
