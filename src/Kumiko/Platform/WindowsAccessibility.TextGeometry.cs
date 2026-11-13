using System.Globalization;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed record UiaTextGeometry(int[] Positions, Rect[] Carets, Rect Clip)
{
    internal int HitTest(Point point)
    {
        float distance = float.PositiveInfinity;
        int result = 0;
        for (int i = 0; i < Carets.Length; i++)
        {
            float dx = Carets[i].X - point.X;
            float dy = Math.Clamp(point.Y, Carets[i].Y, Carets[i].Bottom) - point.Y;
            float current = dx * dx + dy * dy * 16;
            if (current < distance)
            {
                distance = current;
                result = Positions[i];
            }
        }
        return result;
    }

    internal Rect[] Bounds(int start, int end)
    {
        var result = new List<Rect>();
        for (int i = 0; i + 1 < Positions.Length; i++)
        {
            if (Positions[i] < start || Positions[i] >= end)
                continue;
            var a = Carets[i];
            var b = Carets[i + 1];
            float left = Math.Min(a.X, b.X), width = Math.Abs(a.X - b.X);
            if (a.Y != b.Y)
            {
                left = a.X;
                width = 1;
            }
            var box = new Rect(left, a.Y, Math.Max(1, width), a.Height).Intersect(Clip);
            if (box.Width <= 0 || box.Height <= 0)
                continue;
            int line = result.FindIndex(rect => rect.Y == box.Y && rect.X <= box.Right + 1 && box.X <= rect.Right + 1);
            if (line >= 0)
            {
                var previous = result[line];
                float x = Math.Min(previous.X, box.X);
                result[line] = new(x, box.Y, Math.Max(previous.Right, box.Right) - x, box.Height);
            }
            else
                result.Add(box);
        }
        return result.ToArray();
    }

    internal static Dictionary<ulong, UiaTextGeometry> Capture(Context context)
    {
        var result = new Dictionary<ulong, UiaTextGeometry>();
        foreach (var node in context.Accessibility)
        {
            if (node.Role != Role.TextField || node.Properties?.Password == true)
                continue;
            string text = node.Value ?? "";
            var p = node.Properties;
            var positions = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
            var carets = new Rect[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                var caret = context.TextSystem.CaretRectangle(text, context.Theme.Typography.Body with
                {
                    Wrap = p?.Multiline == true
                }, positions[i], p?.TextWidth ?? node.Rect.Width);
                carets[i] = caret with
                {
                    X = node.Rect.X + (p?.TextOffsetX ?? 0) + caret.X,
                    Y = node.Rect.Y + (p?.TextOffsetY ?? 0) + caret.Y
                };
            }
            result[node.Id] = new(positions, carets, node.Rect);
        }
        return result;
    }
}
