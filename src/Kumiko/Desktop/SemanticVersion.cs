namespace Kumiko.Desktop;

/// <summary>Strict SemVer 2 precedence. Build metadata is ignored when comparing releases.</summary>
public sealed record SemanticVersion : IComparable<SemanticVersion>
{
    public string Value
    {
        get;
    }
    public bool IsPrerelease => prerelease.Length > 0;
    readonly string[] core, prerelease;
    SemanticVersion(string value, string[] core, string[] prerelease)
    {
        Value = value;
        this.core = core;
        this.prerelease = prerelease;
    }
    static bool Numeric(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);
    static bool Identifier(string value) => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    public static SemanticVersion Parse(string value)
    {
        if (value.Length is 0 or > 256)
            throw new FormatException("Expected a bounded semantic version.");
        string[] metadata = value.Split('+');
        if (metadata.Length > 2 || metadata.Length == 2 && metadata[1].Split('.').Any(v => !Identifier(v)))
            throw new FormatException("Invalid semantic version metadata.");
        string[] split = metadata[0].Split('-', 2), core = split[0].Split('.');
        string[] prerelease = split.Length == 2 ? split[1].Split('.') : [];
        if (core.Length != 3 || core.Any(v => !Numeric(v) || v.Length > 1 && v[0] == '0') ||
            prerelease.Any(v => !Identifier(v) || Numeric(v) && v.Length > 1 && v[0] == '0'))
            throw new FormatException("Expected major.minor.patch with valid prerelease identifiers.");
        return new(value, core, prerelease);
    }
    static int Number(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;
        for (int i = 0; i < 3; i++)
        {
            int compared = Number(core[i], other.core[i]);
            if (compared != 0)
                return compared;
        }
        if (!IsPrerelease || !other.IsPrerelease)
            return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        for (int i = 0; i < Math.Min(prerelease.Length, other.prerelease.Length); i++)
        {
            string a = prerelease[i], b = other.prerelease[i];
            bool an = Numeric(a), bn = Numeric(b);
            int compared = an && bn ? Number(a, b) : an != bn ? an ? -1 : 1 : string.CompareOrdinal(a, b);
            if (compared != 0)
                return compared;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }
}
