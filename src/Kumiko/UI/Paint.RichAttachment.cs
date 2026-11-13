using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Kumiko.Rendering;
using Kumiko.RichText;

namespace Kumiko.UI;

internal static partial class Paint
{
    sealed record Decoded(Image? Image);
    static readonly ConditionalWeakTable<RichAttachment, Decoded> Attachments = new();
    static Decoded DecodeAttachment(RichAttachment attachment)
    {
        var bytes = attachment.Data.AsSpan();
        // Preflight dimensions before invoking a decoder. Imported remote images are never fetched.
        if (attachment.Mime != "image/png" || bytes.Length < 24 || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return new(null);
        uint width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..]);
        if (width is 0 or > 4096 || height is 0 or > 4096 || (long)width * height > 4 * 1024 * 1024)
            return new(null);
        try
        {
            return new(Image.Decode(bytes));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or OverflowException)
        {
            return new(null);
        }
    }
    static void PaintAttachment(Context context, RichAttachment attachment, Rect rect, Rect clip, float opacity, List<Instance> instances)
    {
        var decoded = Attachments.GetValue(attachment, DecodeAttachment);
        if (decoded.Image is { } image)
        {
            var sprite = context.TextSystem.Picture(image);
            var quad = Instance.Quad(rect, Color.White.Alpha(opacity), clip);
            quad.Kind = 3;
            quad.UV = new(sprite.X, sprite.Y, sprite.X + sprite.Width, sprite.Y + sprite.Height);
            instances.Add(quad);
        }
        else
        {
            instances.Add(Instance.Quad(rect, context.Theme.Background.Alpha(opacity), clip));
            var style = context.Theme.Typography.Caption with
            {
                Color = context.Theme.Muted
            };
            DrawRun(context, context.TextSystem.Shape(attachment.Name, style), style, rect.X + 4, rect.Y + 4, clip.Intersect(rect), opacity, 0, instances);
        }
    }
}
