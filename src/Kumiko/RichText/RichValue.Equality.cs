namespace Kumiko.RichText;

public sealed partial record RichAttachment
{
    public bool Equals(RichAttachment? other) => ReferenceEquals(this, other) || other is not null &&
        Id == other.Id && Name == other.Name && Mime == other.Mime && Width == other.Width && Height == other.Height &&
        Data.IsDefault == other.Data.IsDefault && Data.AsSpan().SequenceEqual(other.Data.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(Mime);
        hash.Add(Width);
        hash.Add(Height);
        foreach (byte value in Data.AsSpan())
            hash.Add(value);
        return hash.ToHashCode();
    }
}

public sealed partial record RichValue
{
    public bool Equals(RichValue? other)
    {
        if (ReferenceEquals(this, other) || other is not null && ReferenceEquals(Paragraphs, other.Paragraphs))
            return true;
        if (other is null || PlainText != other.PlainText || Paragraphs.Count != other.Paragraphs.Count)
            return false;
        for (int i = 0; i < Paragraphs.Count; i++)
        {
            var left = Paragraphs[i];
            var right = other.Paragraphs[i];
            if (left.Style != right.Style || !left.Runs.SequenceEqual(right.Runs))
                return false;
        }
        return true;
    }
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var paragraph in Paragraphs)
        {
            hash.Add(paragraph.Style);
            foreach (var run in paragraph.Runs)
                hash.Add(run);
        }
        return hash.ToHashCode();
    }
}
