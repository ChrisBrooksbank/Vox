using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vox.Core.Remote;

/// <summary>
/// One message of a remote session (JSON inside a <see cref="SecureChannel"/> frame). The
/// controlling side sends keys and whether it is sending them; the controlled side sends what it
/// says and its braille line. Never logged: speech and braille can be anything on the screen.
/// </summary>
public sealed record RemoteMessage
{
    public const string Hello = "hello";
    public const string Key = "key";
    public const string Control = "control";
    public const string Speak = "speak";
    public const string Braille = "braille";
    public const string KeepAlive = "ping";
    public const string Bye = "bye";

    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;

    // hello: the computer's name, as said when the session starts
    [JsonPropertyName("name")] public string? Name { get; init; }

    // key
    [JsonPropertyName("vk")] public int Vk { get; init; }
    [JsonPropertyName("scan")] public int Scan { get; init; }
    [JsonPropertyName("down")] public bool Down { get; init; }
    [JsonPropertyName("keypad")] public bool Keypad { get; init; }
    [JsonPropertyName("extended")] public bool Extended { get; init; }
    /// <summary>The key is the controlling side's screen reader key: the controlled side presses its own.</summary>
    [JsonPropertyName("voxKey")] public bool VoxKey { get; init; }

    // control: keys are (or no longer are) being sent
    [JsonPropertyName("on")] public bool On { get; init; }

    // speak and braille
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("priority")] public string? Priority { get; init; }
    [JsonPropertyName("cursor")] public int? Cursor { get; init; }

    private static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Options);

    /// <summary>The message in <paramref name="bytes"/>, or null when it isn't one.</summary>
    public static RemoteMessage? FromBytes(byte[] bytes)
    {
        try
        {
            var message = JsonSerializer.Deserialize<RemoteMessage>(bytes, Options);
            return message is { Type.Length: > 0 } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
