using System.Globalization;
using Kumiko.UI;

namespace Kumiko.Rendering;

public sealed partial class TextSystem
{
    IEnumerable<TextLine> RichLines(string text, TextStyle style, float width, TextSpan[] spans)
    {
        int offset = 0;
        foreach (string paragraph in text.Split('\n'))
        {
            if (!style.Wrap || paragraph.Length == 0)
            {
                yield return new(paragraph, Shape(paragraph, style));
                offset += paragraph.Length + 1;
                continue;
            }
            var boundaries = StringInfo.ParseCombiningCharacters(paragraph).Append(paragraph.Length).ToArray();
            int first = 0;
            while (first + 1 < boundaries.Length)
            {
                int low = first + 1, high = boundaries.Length - 1, fit = low;
                while (low <= high)
                {
                    int mid = low + (high - low) / 2;
                    string candidate = paragraph[boundaries[first]..boundaries[mid]];
                    float measured = DisplayLine(candidate, style, spans, offset + boundaries[first], candidate.Length, false, false, 0, width).Width;
                    if (measured <= width)
                    {
                        fit = mid;
                        low = mid + 1;
                    }
                    else
                        high = mid - 1;
                }
                int end = fit;
                if (end < boundaries.Length - 1)
                {
                    for (int word = end; word > first + 1; word--)
                        if (char.IsWhiteSpace(paragraph[boundaries[word] - 1]))
                        {
                            end = word;
                            break;
                        }
                }
                string line = paragraph[boundaries[first]..boundaries[end]];
                yield return new(line, Shape(line, style));
                first = end;
            }
            offset += paragraph.Length + 1;
        }
    }
}
