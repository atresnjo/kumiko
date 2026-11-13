using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.Desktop;
using Kumiko.Documents;

namespace Kumiko.RichText;

[JsonSerializable(typeof(RichValue))]
internal partial class RichJson : JsonSerializerContext;

/// <summary>Versioned private rich transfer plus safe, bounded interchange. Never fetches links or attachments.</summary>
public static partial class RichExchange
{
    public const string Format = "application/x-kumiko-richtext+json";
    public static bool SafeLink(string link) => link.Length <= 8192 && !link.Any(char.IsControl) &&
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
    public static byte[] Encode(RichValue value)
    {
        using var output = new MemoryStream();
        using (var json = new Utf8JsonWriter(output))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 1);
            json.WritePropertyName("value");
            JsonSerializer.Serialize(json, value, RichJson.Default.RichValue);
            json.WriteEndObject();
        }
        return output.ToArray();
    }
    public static RichValue Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Rich import exceeds 32 MiB.");
        using var json = JsonDocument.Parse(bytes, new()
        {
            MaxDepth = 32
        });
        if (json.RootElement.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unknown rich-text transfer version.");
        return json.RootElement.GetProperty("value").Deserialize(RichJson.Default.RichValue) ?? throw new InvalidDataException("Missing rich value.");
    }
    public static TransferData Transfer(RichValue value) => new(new TransferItem(
        new TransferRepresentation(Format, Encode(value)),
        new TransferRepresentation(TransferFormats.HTML, Encoding.UTF8.GetBytes(Html(value))),
        new TransferRepresentation(TransferFormats.Text, Encoding.UTF8.GetBytes(value.PlainText))));

    /// <summary>Reads preferred safe representations. Malformed rich data falls back to plain text.</summary>
    public static RichValue FromTransfer(TransferData data)
    {
        foreach (var format in new[] { Format, TransferFormats.HTML, TransferFormats.Text })
        {
            var representation = data.Items.SelectMany(i => i.Representations).FirstOrDefault(r => r.Format == format);
            if (representation is null)
                continue;
            try
            {
                var bytes = representation.Fetch();
                if (bytes.Length > 32 * 1024 * 1024)
                    continue;
                return format == Format ? Decode(bytes) : format == TransferFormats.HTML ? ParseHtml(Encoding.UTF8.GetString(bytes)) : RichValue.Text(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception error) when (error is JsonException or System.Xml.XmlException or InvalidDataException or ArgumentException or KeyNotFoundException or InvalidOperationException)
            {
            }
        }
        return RichValue.Text("");
    }
}

public sealed record RichValueCodec : IDocumentCodec<RichValue>
{
    public ReadOnlyMemory<byte> Encode(RichValue value) => RichExchange.Encode(value);
    public RichValue Decode(ReadOnlyMemory<byte> bytes) => RichExchange.Decode(bytes);
}
