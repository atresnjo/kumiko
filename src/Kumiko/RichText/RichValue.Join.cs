using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Kumiko.RichText;

public sealed partial record RichValue
{
    // Editing may join previously valid runs into one grapheme. The attributes
    // of the grapheme's first scalar win; import construction remains strict.
    static ImmutableList<RichRun> JoinedRuns(IEnumerable<RichRun> runs)
    {
        var source = Normalize(runs);
        string text = string.Concat(source.Select(r => r.Text));
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var valid = boundaries.Append(text.Length).ToHashSet();
        int offset = 0;
        if (source.All(run => valid.Contains(offset += run.Text.Length)))
            return source;
        var result = ImmutableList.CreateBuilder<RichRun>();
        var content = new StringBuilder();
        int index = 0, runEnd = source[0].Text.Length;
        RichAttributes? attributes = null;
        for (int i = 0; i < boundaries.Length; i++)
        {
            int start = boundaries[i], end = i + 1 < boundaries.Length ? boundaries[i + 1] : text.Length;
            while (start >= runEnd && index < source.Count - 1)
                runEnd += source[++index].Text.Length;
            var next = source[index].Attributes;
            if (attributes is not null && (attributes != next || next.Attachment is not null))
            {
                result.Add(new(content.ToString(), attributes));
                content.Clear();
            }
            attributes = next;
            content.Append(text.AsSpan(start, end - start));
        }
        if (attributes is not null)
            result.Add(new(content.ToString(), attributes));
        return result.ToImmutable();
    }
}
