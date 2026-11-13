using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kumiko.RichText;

public static partial class RichExchange
{
    static string Escape(string value) => WebUtility.HtmlEncode(value);
    /// <summary>Safe semantic HTML subset. Attachment data stays in the private format; HTML uses its accessible name.</summary>
    public static string Html(RichValue value)
    {
        var html = new StringBuilder();
        foreach (var paragraph in value.Paragraphs)
        {
            var style = paragraph.Style;
            string tag = style.Heading > 0 ? "h" + style.Heading : style.Code ? "pre" : style.Quote ? "blockquote" : "p";
            html.Append('<').Append(tag).Append(" data-list=\"").Append(style.List).Append("\" data-checked=\"")
                .Append(style.Checked ? "true" : "false").Append("\" data-indent=\"").Append(style.Indent)
                .Append("\" data-align=\"").Append(style.Alignment).Append("\">");
            foreach (var run in paragraph.Runs)
            {
                var a = run.Attributes;
                string text = Escape(a.Attachment?.Name ?? run.Text);
                if (a.Code)
                    text = "<code>" + text + "</code>";
                if (a.Bold)
                    text = "<strong>" + text + "</strong>";
                if (a.Italic)
                    text = "<em>" + text + "</em>";
                if (a.Underline)
                    text = "<u>" + text + "</u>";
                if (a.Strike)
                    text = "<del>" + text + "</del>";
                if (a.Link is { } link)
                    text = "<a href=\"" + Escape(link) + "\">" + text + "</a>";
                html.Append(text);
            }
            html.Append("</").Append(tag).Append('>');
        }
        return html.ToString();
    }
    /// <summary>Imports well-formed HTML/XHTML fragments, not a browser's permissive error recovery.
    /// Ignores scripts, styles and active content. DTD/entities are prohibited. No network or filesystem access.</summary>
    public static RichValue ParseHtml(string html)
    {
        if (html.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("HTML exceeds the import limit.");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16 * 1024 * 1024,
            MaxCharactersFromEntities = 1024
        };
        using var reader = XmlReader.Create(new StringReader("<rich>" + html + "</rich>"), settings);
        var root = XElement.Load(reader);
        var paragraphs = ImmutableList.CreateBuilder<RichParagraph>();
        var runs = new List<RichRun>();
        var currentStyle = new RichParagraphStyle();
        int nodes = 0;
        void Flush(bool force)
        {
            if (force || runs.Count > 0)
                paragraphs.Add(new(RichValue.Normalize(runs), currentStyle));
            runs.Clear();
        }
        void Visit(XNode node, RichAttributes attributes, RichParagraphStyle style, int depth)
        {
            if (++nodes > 100000 || depth > 32)
                throw new InvalidDataException("HTML structure exceeds the import limit.");
            if (node is XText text)
            {
                if (text.Value.Length > 0)
                    runs.Add(new(text.Value.Replace('\r', ' ').Replace('\n', ' '), attributes));
                return;
            }
            if (node is not XElement element)
                return;
            string tag = element.Name.LocalName.ToLowerInvariant();
            if (tag is "script" or "style" or "iframe" or "object" or "embed" or "svg" or "math")
                return;
            bool block = tag is "p" or "div" or "li" or "blockquote" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
            if (block)
            {
                Flush(false);
                style = style with
                {
                    Heading = tag.Length == 2 && tag[0] == 'h' ? tag[1] - '0' : 0,
                    Quote = tag == "blockquote",
                    Code = tag == "pre"
                };
                if (Enum.TryParse<RichList>((string?)element.Attribute("data-list"), out var list) && Enum.IsDefined(list))
                    style = style with
                    {
                        List = list
                    };
                if (int.TryParse((string?)element.Attribute("data-indent"), out int indent) && indent is >= 0 and <= 32)
                    style = style with
                    {
                        Indent = indent
                    };
                if (Enum.TryParse<RichAlignment>((string?)element.Attribute("data-align"), out var alignment) && Enum.IsDefined(alignment))
                    style = style with
                    {
                        Alignment = alignment
                    };
                style = style with
                {
                    Checked = (string?)element.Attribute("data-checked") == "true"
                };
                currentStyle = style;
            }
            if (tag == "ul")
                style = style with
                {
                    List = RichList.Bullet
                };
            if (tag == "ol")
                style = style with
                {
                    List = RichList.Number
                };
            attributes = tag switch
            {
                "b" or "strong" => attributes with { Bold = true },
                "i" or "em" => attributes with { Italic = true },
                "u" => attributes with { Underline = true },
                "s" or "del" => attributes with { Strike = true },
                "code" => attributes with { Code = true },
                "a" when (string?)element.Attribute("href") is { } url && SafeLink(url) => attributes with { Link = url },
                _ => attributes
            };
            if (tag == "br")
            {
                Flush(true);
                currentStyle = style;
            }
            else if (tag == "img")
                runs.Add(new((string?)element.Attribute("alt") ?? "[image]", attributes));
            else
                foreach (var child in element.Nodes())
                    Visit(child, attributes, style, depth + 1);
            if (block)
                Flush(true);
        }
        foreach (var child in root.Nodes())
            Visit(child, new(), new(), 0);
        Flush(false);
        return paragraphs.Count == 0 ? RichValue.Text("") : new(paragraphs.ToImmutable());
    }
}
