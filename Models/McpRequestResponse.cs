using System;
using Newtonsoft.Json;

namespace FileSystemMcpServer.Models;

/// <summary>
/// MCP Request structure for JSON-RPC protocol
/// </summary>
public class McpRequest
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonProperty("method")]
    public string Method { get; set; } = string.Empty;

    [JsonProperty("params", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public McpParams? Params { get; set; }

    [JsonProperty("id")]
    public object? Id { get; set; }

    /// <summary>
    /// Validates the request and returns error if invalid
    /// </summary>
    public bool Validate()
    {
        return JsonRpc == "2.0" && !string.IsNullOrEmpty(Method);
    }
}

public class McpParams
{
    [JsonProperty("method")]
    public string? Method { get; set; }

    [JsonProperty("arguments", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public object? Arguments { get; set; }
}

/// <summary>
/// MCP Response structure for JSON-RPC protocol
/// </summary>
public class McpResponse
{
    [JsonProperty("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonProperty("result", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public object? Result { get; set; }

    [JsonProperty("error", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public McpError? ErrorDetail { get; set; }

    [JsonProperty("id")]
    public object? Id { get; set; }

    /// <summary>
    /// Create a successful response
    /// </summary>
    public static McpResponse Success(object result, object? id) => new()
    {
        JsonRpc = "2.0",
        Result = result,
        Id = id
    };

    /// <summary>
    /// Create an error response
    /// </summary>
    public static McpResponse Error(McpError error, object? id) => new()
    {
        JsonRpc = "2.0",
        ErrorDetail = error,
        Id = id
    };
}
