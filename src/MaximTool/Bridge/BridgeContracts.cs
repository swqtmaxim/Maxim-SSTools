using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class BridgeCall {
  [JsonPropertyName("id")]
  public string Id { get; set; } = "";

  [JsonPropertyName("method")]
  public string Method { get; set; } = "";

  [JsonPropertyName("args")]
  public JsonElement[] Args { get; set; } = Array.Empty<JsonElement>();
}

internal sealed class BridgeReply {
  [JsonPropertyName("id")]
  public string Id {
    get;
  }

  [JsonPropertyName("result")]
  public object? Result {
    get;
  }

  [JsonPropertyName("error")]
  public string? Error {
    get;
  }

  public BridgeReply(string id, object? finding, string? error) {
    Id = id;
    Result = finding;
    Error = error;
  }
}
