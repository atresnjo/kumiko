using Kumiko.RichText;

static class ValueFlows
{
    public static void Run(Action<bool, string> check)
    {
        var original = RichDocument.Create("Hello 👨‍👩‍👧‍👦 e\u0301\nworld");
        var formatted = original.Select(0, 5).Apply(new RichCommand.Toggle(RichFormat.Bold));
        check(formatted.Value.Paragraphs[0].Runs[0].Attributes.Bold, "range formatting changes attributed value");
        check(!original.Value.Paragraphs[0].Runs[0].Attributes.Bold, "old value remains immutable");
        var inserted = formatted.Apply(new RichCommand.Insert("Hi"));
        check(inserted.Value.PlainText.StartsWith("Hi 👨", StringComparison.Ordinal) && inserted.Value.Paragraphs[0].Runs[0].Attributes.Bold, "selection replacement inherits formatting");
        var undone = inserted.Apply(new RichCommand.Undo());
        check(undone.Value == formatted.Value && undone.Selection == formatted.Selection, "undo restores value formatting and selection");
        check(undone.Apply(new RichCommand.Redo()).Value == inserted.Value, "redo restores rich edit");
        var emoji = original.Select(7, 8);
        check(emoji.Selection.Start == 6 && emoji.Selection.End == 17, "selection expands to whole emoji grapheme");
        var deleted = emoji.Apply(new RichCommand.Insert(""));
        check(!deleted.Value.PlainText.Contains('\u200d'), "grapheme deletion cannot leave ZWJ fragments");
        var heading = original.Select(0, 0).Apply(new RichCommand.Paragraph(new(Heading: 2)));
        check(heading.Value.Paragraphs[0].Style.Heading == 2 && heading.Value.Paragraphs[1].Style.Heading == 0, "paragraph formatting targets selected paragraphs");
        var link = original.Select(0, 5).Apply(new RichCommand.Link("https://example.com"));
        var edge = link.Select(5, 5).Apply(new RichCommand.Insert("!"));
        check(edge.Value.AttributesAt(5).Link is null, "typing at link edge does not extend link");
        try
        {
            original.Apply(new RichCommand.Link("javascript:alert(1)"));
            check(false, "unsafe link rejected");
        }
        catch (ArgumentException)
        {
            check(true, "unsafe link rejected");
        }
        var html = RichExchange.Html(formatted.Value);
        check(RichExchange.ParseHtml(html).PlainText == original.Value.PlainText, "HTML preserves Unicode paragraph content");
        var markdown = RichExchange.Markdown(heading.Value);
        check(RichExchange.ParseMarkdown(markdown).Paragraphs[0].Style.Heading == 2, "Markdown heading roundtrip");
        var rich = RichExchange.Encode(formatted.Value);
        check(RichExchange.Decode(rich) == formatted.Value, "rich interchange restores structural value equality");
        var attachment = RichDocument.Create("").Apply(new RichCommand.Attachment(new("id", "image", "image/png",
            System.Collections.Immutable.ImmutableArray.Create<byte>(1, 2, 3)))).Value;
        check(RichExchange.Decode(RichExchange.Encode(attachment)) == attachment,
            "attachment byte content participates in rich value equality");
        check(RichExchange.Decode(rich).Paragraphs[0].Runs[0].Attributes.Bold, "private rich format preserves attributes");
        foreach (var invalid in new[]
        {
            "<p>e<strong>\u0301</strong></p>",
            "<p>👩<strong>\u200d💻</strong></p>"
        })
        {
            try
            {
                RichExchange.ParseHtml(invalid);
                check(false, "grapheme-splitting HTML rejected");
            }
            catch (ArgumentException)
            {
                check(true, "grapheme-splitting HTML rejected");
            }
        }
        foreach (var invalid in new[] { "<p>unclosed", "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><p>&e;</p>" })
        {
            try
            {
                RichExchange.ParseHtml(invalid);
                check(false, "malformed HTML rejected");
            }
            catch (System.Xml.XmlException)
            {
                check(true, "malformed HTML/DTD rejected without external I/O");
            }
        }
        try
        {
            RichExchange.Decode("{\"version\":1,\"value\":{\"Paragraphs\":null}}"u8.ToArray());
            check(false, "null rich structure rejected");
        }
        catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException or InvalidDataException)
        {
            check(true, "null rich structure rejected as an import error");
        }
        var sanitized = RichExchange.ParseHtml("<p><script>bad()</script><a href='javascript:bad()'>safe label</a></p>");
        check(sanitized.PlainText == "safe label" && sanitized.AttributesAt(0).Link is null, "HTML discards scripts and unsafe links");
    }
}
