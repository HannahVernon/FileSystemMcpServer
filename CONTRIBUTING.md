# Contributing to FileSystemMcpServer

Thank you for your interest in contributing.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

## Branch model

Branch | Purpose
-------|--------
`main` | Release branch. Always stable.
`dev` | Integration branch. Feature work merges here first.
`feature/xxx` | Feature branches, created from `dev`.
`fix/xxx` | Bug fix branches, created from `dev`.

## Getting started

1. Fork and clone the repository.
2. Create a feature or fix branch from `dev`:
   ```shell
   git switch dev
   git pull
   git switch -c feature/my-feature
   ```
3. Make your changes.
4. Verify the build:
   ```shell
   dotnet build
   ```
5. Commit with a clear, descriptive message.
6. Push and open a pull request targeting `dev`.

## Coding standards

- Use C# latest language features (file-scoped namespaces, pattern matching, etc.).
- Nullable reference types are enabled; do not suppress warnings without justification.
- Keep diagnostic/log output on stderr; stdout is reserved for JSON-RPC protocol traffic.
- All file operations must validate paths against the allowed directory list.

## Pull request expectations

- One logical change per PR.
- Fill out the PR template completely.
- Ensure `dotnet build` completes with 0 errors, 0 warnings.
- Update documentation (`README.md`, `ARCHITECTURE.md`) if user-visible behavior changes.
- New dependencies must be MIT or Apache-2.0 licensed and noted in `THIRD-PARTY-NOTICES.md`.

## Code of Conduct

This project follows the [Contributor Covenant v2.1](CODE_OF_CONDUCT.md). Please read it before participating.
