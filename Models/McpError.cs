using System;
using Newtonsoft.Json;

namespace FileSystemMcpServer.Models;

/// <summary>
/// Standard MCP error codes and messages
/// Based on Model Context Protocol specification
/// </summary>
public static class McpErrorCode
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;

    // Custom filesystem errors
    public const int FileNotFound = 4001;
    public const int DirectoryNotFound = 4002;
    public const int PermissionDenied = 4003;
    public const int PathNotAllowed = 4004;
    public const int InvalidPath = 4005;
    public const int FileLocked = 4006;
    public const int OperationNotSupported = 4007;
}

public class McpError : Exception
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("message")]
    public new string? Message { get; set; }

    [JsonProperty("data", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public new object? Data { get; set; }
}

public static class McpErrorFactory
{
    public static McpError ParseError(string message) => new()
    {
        Code = McpErrorCode.ParseError,
        Message = $"Parse error: {message}"
    };

    public static McpError InvalidRequest(string method, string message) => new()
    {
        Code = McpErrorCode.InvalidRequest,
        Message = $"Invalid request for '{method}': {message}"
    };

    public static McpError MethodNotFound(string method) => new()
    {
        Code = McpErrorCode.MethodNotFound,
        Message = $"Method not found: {method}"
    };

    public static McpError InvalidParams(string message) => new()
    {
        Code = McpErrorCode.InvalidParams,
        Message = message
    };

    public static McpError InternalError(string message) => new()
    {
        Code = McpErrorCode.InternalError,
        Message = $"Internal error: {message}"
    };

    // Filesystem-specific errors
    public static McpError FileNotFound(string path) => new()
    {
        Code = McpErrorCode.FileNotFound,
        Message = $"File not found: {path}",
        Data = new { Path = path }
    };

    public static McpError DirectoryNotFound(string path) => new()
    {
        Code = McpErrorCode.DirectoryNotFound,
        Message = $"Directory not found: {path}",
        Data = new { Path = path }
    };

    public static McpError PermissionDenied(string operation, string path) => new()
    {
        Code = McpErrorCode.PermissionDenied,
        Message = $"Permission denied for '{operation}' on '{path}'",
        Data = new { Operation = operation, Path = path }
    };

    public static McpError PathNotAllowed(string path) => new()
    {
        Code = McpErrorCode.PathNotAllowed,
        Message = $"Path not allowed: '{path}'. Only configured directories are accessible.",
        Data = new { Path = path }
    };

    public static McpError InvalidPath(string path) => new()
    {
        Code = McpErrorCode.InvalidPath,
        Message = $"Invalid path format: '{path}'",
        Data = new { Path = path }
    };

    public static McpError FileLocked(string path) => new()
    {
        Code = McpErrorCode.FileLocked,
        Message = $"File is locked and cannot be accessed: '{path}'",
        Data = new { Path = path }
    };

    public static McpError OperationNotSupported(string operation) => new()
    {
        Code = McpErrorCode.OperationNotSupported,
        Message = $"Operation not supported: '{operation}'",
        Data = new { Operation = operation }
    };
}
