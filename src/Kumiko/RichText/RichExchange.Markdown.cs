using System.Collections.Immutable;
using System.Text;

namespace Kumiko.RichText;

public static partial class RichExchange
{
    static string MarkdownEscape(string value)
    {
        var result = new StringBuilder();
        int first = 0, last = value.Length - 1;
        while (first < value.Length && value[first] is ' ' or '\t')
            first++;
        while (last >= first && value[last] is ' ' or '\t')
            last--;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if ((i < first || i > last) && ch is ' ' or '\t')
            {
                result.Append(ch == ' ' ? "&#32;" : "&#9;");
                continue;
            }
            if ("\\`*_{}[]()#+-.!>~&".Contains(ch))
                result.Append('\\');
            result.Append(ch);
        }
        return result.ToString();
    }
    /// <summary>Exports headings, flat lists/tasks, quotations, code and inline formats/links.
    /// Underline/alignment and attachment bytes require HTML/private exchange.</summary>
    public static string Markdown(RichValue value) => string.Join('\n', value.Paragraphs.Select(paragraph =>
    {
        var s = paragraph.Style;
        string prefix = new(' ', s.Indent * 2);
        prefix += s.Heading > 0 ? new string('#', s.Heading) + " " : s.Quote ? "> " : s.List switch
        {
            RichList.Bullet => "- ",
            RichList.Number => "1. ",
            RichList.Task => s.Checked ? "- [x] " : "- [ ] ",
            _ => ""
        };
        var text = new StringBuilder(prefix);
        foreach (var run in paragraph.Runs)
        {
            var a = run.Attributes;
            string content = MarkdownEscape(a.Attachment?.Name ?? run.Text);
            if (a.Code)
                content = "`" + content + "`";
            if (a.Bold)
                content = "**" + content + "**";
            if (a.Italic)
                content = "*" + content + "*";
            if (a.Strike)
                content = "~~" + content + "~~";
            if (a.Link is { } link)
                content = "[" + content + "](" + link.Replace(")", "%29", StringComparison.Ordinal) + ")";
            text.Append(content);
        }
        if (!s.Code)
            return text.ToString();
        int longest = 0, current = 0;
        foreach (char ch in paragraph.Text)
        {
            current = ch == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        string fence = new('`', Math.Max(3, longest + 1));
        return fence + "\n" + paragraph.Text + "\n" + fence;
    }));

    /// <summary>Bounded Markdown subset. Raw HTML is literal text; no embedded content is fetched.</summary>
    public static RichValue ParseMarkdown(string markdown)
    {
        if (markdown.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("Markdown exceeds the import limit.");
        var paragraphs = ImmutableList.CreateBuilder<RichParagraph>();
        int fence = 0;
        foreach (string source in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            string line = source;
            bool delimiter = line.Length >= Math.Max(3, fence) && line.All(ch => ch == '`');
            if (delimiter)
            {
                fence = fence == 0 ? line.Length : 0;
                continue;
            }
            bool code = fence > 0;
            var style = new RichParagraphStyle(Code: code);
            if (!code)
            {
                int indent = line.TakeWhile(c => c == ' ').Count();
                style = style with
                {
                    Indent = Math.Min(32, indent / 2)
                };
                line = line[indent..];
                int heading = line.TakeWhile(c => c == '#').Count();
                if (heading is > 0 and <= 6 && line.Length > heading && line[heading] == ' ')
                {
                    style = style with
                    {
                        Heading = heading
                    };
                    line = line[(heading + 1)..];
                }
                else if (line.StartsWith("- [ ] ", StringComparison.Ordinal) || line.StartsWith("- [x] ", StringComparison.Ordinal))
                {
                    style = style with
                    {
                        List = RichList.Task,
                        Checked = line[3] == 'x'
                    };
                    line = line[6..];
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                {
                    style = style with
                    {
                        List = RichList.Bullet
                    };
                    line = line[2..];
                }
                else if (line.StartsWith("1. ", StringComparison.Ordinal))
                {
                    style = style with
                    {
                        List = RichList.Number
                    };
                    line = line[3..];
                }
                else if (line.StartsWith("> ", StringComparison.Ordinal))
                {
                    style = style with
                    {
                        Quote = true
                    };
                    line = line[2..];
                }
            }
            paragraphs.Add(new(code ? ImmutableList.Create(new RichRun(line, new(Code: true))) : Inline(line, new(), 0), style));
        }
        return paragraphs.Count > 0 ? new(paragraphs.ToImmutable()) : RichValue.Text("");
    }
    static ImmutableList<RichRun> Inline(string text, RichAttributes attributes, int depth)
    {
        if (depth > 16)
            return ImmutableList.Create(new RichRun(text, attributes));
        var result = new List<RichRun>();
        var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length > 0)
                result.Add(new(plain.ToString(), attributes));
            plain.Clear();
        }
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                plain.Append(text[++i]);
                continue;
            }
            if (text.AsSpan(i).StartsWith("&#32;", StringComparison.Ordinal) || text.AsSpan(i).StartsWith("&#9;", StringComparison.Ordinal))
            {
                bool space = text[i + 2] == '3';
                plain.Append(space ? ' ' : '\t');
                i += space ? 4 : 3;
                continue;
            }
            if (text[i] == '[')
            {
                int middle = text.IndexOf("](", i, StringComparison.Ordinal), end = middle < 0 ? -1 : text.IndexOf(')', middle + 2);
                if (middle >= 0 && end >= 0 && SafeLink(text[(middle + 2)..end]))
                {
                    Flush();
                    result.AddRange(Inline(text[(i + 1)..middle], attributes with
                    {
                        Link = text[(middle + 2)..end]
                    }, depth + 1));
                    i = end;
                    continue;
                }
            }
            string? mark = text.AsSpan(i).StartsWith("***") ? "***" : text.AsSpan(i).StartsWith("**") ? "**" : text.AsSpan(i).StartsWith("~~") ? "~~" : text[i] is '*' or '`' ? text[i].ToString() : null;
            int close = mark is null ? -1 : Closing(text, mark, i + mark.Length);
            if (mark is not null && close > i + mark.Length)
            {
                Flush();
                var next = mark switch
                {
                    "***" => attributes with { Bold = true, Italic = true },
                    "**" => attributes with { Bold = true },
                    "~~" => attributes with { Strike = true },
                    "`" => attributes with { Code = true },
                    _ => attributes with { Italic = true }
                };
                result.AddRange(Inline(text[(i + mark.Length)..close], next, depth + 1));
                i = close + mark.Length - 1;
            }
            else
                plain.Append(text[i]);
        }
        Flush();
        return RichValue.Normalize(result);
    }
    static int Closing(string text, string mark, int start)
    {
        for (int i = start; i <= text.Length - mark.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }
            if (text.AsSpan(i).StartsWith(mark, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
