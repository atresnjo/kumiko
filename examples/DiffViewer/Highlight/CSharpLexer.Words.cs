namespace DiffViewer.Highlight;

sealed partial class CSharpLexer
{
    /// <summary>How a word reads in C#, independent of its neighbours.</summary>
    enum Word : byte
    {
        Identifier,
        Keyword,
        Control,
        Constant,
        BuiltinType,
        /// <summary>Introduces a type name: new, class, struct, interface, enum, record, is, as, typeof, sizeof.</summary>
        TypeIntroducer,
        /// <summary>A keyword only in some positions (var, async, get, record, ...); otherwise an identifier.</summary>
        Contextual,
        /// <summary>An accessor keyword (get, set, init, add, remove): a keyword before ';', '{' or '=>'.</summary>
        Accessor
    }

    /// <summary>Every C# keyword is 2–10 lowercase ASCII letters, so most names skip the keyword table.</summary>
    static Word Classify(ReadOnlySpan<char> w) => w.Length is < 2 or > 10 || !char.IsAsciiLetterLower(w[0]) ? Word.Identifier : Keywords(w);

    static Word Keywords(ReadOnlySpan<char> w) => w switch
    {
        "if" or "else" or "for" or "foreach" or "while" or "do" or "switch" or "case" or "default" or "break" or "continue" or
            "return" or "throw" or "try" or "catch" or "finally" or "goto" or "lock" or "when" or "in" or "yield" or "await" or
            "checked" or "unchecked" => Word.Control,
        "true" or "false" or "null" or "this" or "base" => Word.Constant,
        "bool" or "byte" or "sbyte" or "char" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint" or
            "float" or "double" or "decimal" or "object" or "string" or "void" or "dynamic" => Word.BuiltinType,
        "new" or "class" or "struct" or "interface" or "enum" or "is" or "as" or "typeof" or "sizeof" => Word.TypeIntroducer,
        "public" or "private" or "protected" or "internal" or "static" or "sealed" or "abstract" or "virtual" or "override" or
            "readonly" or "const" or "extern" or "unsafe" or "volatile" or "event" or "delegate" or "operator" or "explicit" or
            "implicit" or "params" or "ref" or "out" or "using" or "namespace" or "fixed" or "stackalloc" or "nameof" or "async" or
            "where" or "global" => Word.Keyword,
        "var" or "record" or "partial" or "required" or "file" or "scoped" or "with" or "and" or "or" or "not" => Word.Contextual,
        "get" or "set" or "init" or "add" or "remove" => Word.Accessor,
        _ => Word.Identifier
    };

    /// <summary>Preprocessor directives whose remainder is free text, like a comment.</summary>
    static bool TextDirective(ReadOnlySpan<char> w) => w is "region" or "endregion" or "pragma" or "error" or "warning" or "nullable" or "line";
}
