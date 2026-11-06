using System.Text.Json;
using System.Text.Json.Serialization;

namespace Drenalol.Telegram.Bot.Forms.Persistence;

/// <summary>
/// The JSON codec shared by the built-in stores: a snapshot is one UTF-8 JSON document,
/// camelCase, tolerant of unknown extra properties written by a newer bot version.
/// </summary>
public static class ChatHistoryCodec
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  public static string Serialize(ChatHistorySnapshot snapshot) =>
    JsonSerializer.Serialize(snapshot, Options);

  public static ChatHistorySnapshot? Deserialize(string json) =>
    JsonSerializer.Deserialize<ChatHistorySnapshot>(json, Options);

  public static byte[] ToUtf8(ChatHistorySnapshot snapshot) =>
    JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);

  public static ChatHistorySnapshot? FromUtf8(ReadOnlySpan<byte> utf8Json) =>
    JsonSerializer.Deserialize<ChatHistorySnapshot>(utf8Json, Options);
}
