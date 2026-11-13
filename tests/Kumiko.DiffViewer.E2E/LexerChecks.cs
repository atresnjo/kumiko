using System.Text;
using DiffViewer.Highlight;

namespace DiffViewer.E2E;

/// <summary>Highlighting failure scenarios from ACCEPTANCE.md, checked on the tokens the viewer would paint.</summary>
public static partial class LexerChecks
{
    /// <summary>One tokenized text with lookups by line and substring.</summary>
    sealed class Lexed
    {
        public readonly string[] Lines;
        public readonly Highlighting Tokens;

        public Lexed(Language language, string text)
        {
            Lines = LexerSamples.Lines(text);
            Tokens = Highlighter.Tokenize(language, Lines);
        }

        public int Column(int line, string needle, int occurrence)
        {
            var at = -1;
            for (var n = 0; n <= occurrence; n++)
            {
                at = Lines[line].IndexOf(needle, at + 1, StringComparison.Ordinal);
                if (at < 0)
                    throw new InvalidOperationException($"'{needle}' #{occurrence} is not on line {line}: {Lines[line]}");
            }
            return at;
        }

        public int TokenAt(int line, int column)
        {
            for (var t = Tokens.LineStarts[line]; t < Tokens.LineStarts[line + 1]; t++)
            {
                if (column >= Tokens.Starts[t] && column < Tokens.Starts[t] + Tokens.Lengths[t])
                    return t;
            }
            return -1;
        }

        public TokenKind Kind(int line, string needle, int occurrence = 0)
        {
            var t = TokenAt(line, Column(line, needle, occurrence));
            return t < 0 ? TokenKind.Plain : Tokens.Kinds[t];
        }

        /// <summary>The whole token under the first character of needle, or "" over plain text.</summary>
        public string Text(int line, string needle, int occurrence = 0)
        {
            var t = TokenAt(line, Column(line, needle, occurrence));
            return t < 0 ? "" : Lines[line].Substring(Tokens.Starts[t], Tokens.Lengths[t]);
        }

        public string Render(int line) => LexerChecks.Render(Tokens, Lines, line);

        public bool Any(TokenKind kind)
        {
            foreach (var k in Tokens.Kinds)
            {
                if (k == kind)
                    return true;
            }
            return false;
        }
    }

    /// <summary>Renders a line as "text:Kind" pieces; untokenized words show as "word:Plain".</summary>
    public static string Render(Highlighting tokens, IReadOnlyList<string> lines, int line)
    {
        var text = lines[line];
        var builder = new StringBuilder();
        var column = 0;
        void Plain(int end)
        {
            foreach (var word in text[column..end].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                builder.Append(word).Append(":Plain ");
        }
        for (var t = tokens.LineStarts[line]; t < tokens.LineStarts[line + 1]; t++)
        {
            Plain(tokens.Starts[t]);
            builder.Append(text.AsSpan(tokens.Starts[t], tokens.Lengths[t]).Trim()).Append(':').Append(tokens.Kinds[t]).Append(' ');
            column = tokens.Starts[t] + tokens.Lengths[t];
        }
        Plain(text.Length);
        return builder.ToString().TrimEnd();
    }

    public static void Run(Action<bool, string> check, Action<string, double> timing)
    {
        void Is(Lexed lexed, int line, string needle, TokenKind kind, string what, int occurrence = 0)
        {
            var actual = lexed.Kind(line, needle, occurrence);
            check(actual == kind, $"lexer {what}: '{needle}' is {actual}, expected {kind} | {lexed.Render(line)}");
        }
        void Token(Lexed lexed, int line, string needle, TokenKind kind, string what)
        {
            var text = lexed.Text(line, needle);
            var actual = lexed.Kind(line, needle);
            check(text == needle && actual == kind, $"lexer {what}: token '{text}' {actual}, expected '{needle}' {kind} | {lexed.Render(line)}");
        }

        var comment = new Lexed(Language.TypeScript, """
            const a = 1; /* opens here
              const hidden = "still comment";
            ends */ let b = a / 2;
            """);
        Is(comment, 0, "opens", TokenKind.Comment, "block comment starts");
        Is(comment, 1, "const", TokenKind.Comment, "block comment keeps keywords inert on the next line");
        Is(comment, 1, "\"still", TokenKind.Comment, "block comment keeps strings inert");
        Is(comment, 2, "ends */", TokenKind.Comment, "block comment ends at */");
        Is(comment, 2, "let", TokenKind.Keyword, "code resumes after the block comment");
        Is(comment, 2, "/ 2", TokenKind.Operator, "division after a closed block comment");

        var template = new Lexed(Language.TypeScript, """
            const html = `<ul>${items.map((item) => `<li title="${item.name}">\n
              ${item.count > 1 ? `${item.count}x` : ""}</li>`).join("")} and {braces}
            </ul>`; const after = { type: 1 };
            """);
        Is(template, 0, "<ul>", TokenKind.String, "template text");
        Is(template, 0, "${", TokenKind.Interpolation, "interpolation opener");
        Is(template, 0, "map", TokenKind.Function, "call inside interpolation");
        Is(template, 0, "<li", TokenKind.String, "nested template text inside interpolation");
        Is(template, 0, "name", TokenKind.Property, "property inside nested interpolation");
        Is(template, 0, "}", TokenKind.Interpolation, "nested interpolation closer");
        Is(template, 0, "\\n", TokenKind.Escape, "escape in nested template");
        Is(template, 1, "count", TokenKind.Property, "nested template keeps its interpolation on the next line");
        Is(template, 1, "x`", TokenKind.String, "third-level template text");
        Is(template, 1, "</li>", TokenKind.String, "nested template continues after its interpolation");
        Is(template, 1, "join", TokenKind.Function, "outer interpolation resumes after the inner template closes");
        Is(template, 1, "and {braces}", TokenKind.String, "outer template text with plain braces");
        Is(template, 2, "</ul>`", TokenKind.String, "outer template ends on the third line");
        Is(template, 2, "const", TokenKind.Keyword, "code resumes after the template");
        Is(template, 2, "type", TokenKind.Property, "object key after the template");

        var regex = new Lexed(Language.JavaScript, """
            const r = a / b / c;
            x = /a\/b/g.test(s);
            const re = /[/\]]+/, half = (a + b) / 2, idx = arr[0] / 2;
            if (/^\d+$/.test(v)) return v / 2;
            function f() { return /x/.source; }
            let y = (count) / total /* note */ / 3;
            const z = obj.size / 4; const w = 10 / 5 / -1;
            """);
        Is(regex, 0, "/", TokenKind.Operator, "division after an identifier");
        Is(regex, 0, "/", TokenKind.Operator, "second division", 1);
        Is(regex, 0, "b", TokenKind.Plain, "operand between divisions");
        Token(regex, 1, "/a\\/b/g", TokenKind.Regex, "regex with escaped slash and flags");
        Is(regex, 1, "test", TokenKind.Function, "method on a regex literal");
        Token(regex, 2, "/[/\\]]+/", TokenKind.Regex, "regex with a class containing / and ]");
        Is(regex, 2, "/ 2", TokenKind.Operator, "division after a parenthesis");
        Is(regex, 2, "/ 2", TokenKind.Operator, "division after a bracket", 1);
        Token(regex, 3, "/^\\d+$/", TokenKind.Regex, "regex after an opening parenthesis");
        Is(regex, 3, "/ 2", TokenKind.Operator, "division after return value");
        Token(regex, 4, "/x/", TokenKind.Regex, "regex after return");
        Is(regex, 5, "/ total", TokenKind.Operator, "division after a parenthesised value");
        Is(regex, 5, "/ 3", TokenKind.Operator, "division after a comment keeps the previous token");
        Is(regex, 6, "/ 4", TokenKind.Operator, "division after a property");
        Is(regex, 6, "/ -1", TokenKind.Operator, "division after a number");
        Is(regex, 6, "const", TokenKind.Keyword, "keyword after divisions", 1);

        var properties = new Lexed(Language.TypeScript, """
            const v = obj.default + x.type + y?.class + z.new.delete();
            const o = { type: 1, default: 2, class: 3, if: 4, get: () => 5, async: true };
            switch (k) { case 1: break; default: return; }
            import type { A } from "a";
            const { type, as } = props; let from = 1, of = 2;
            interface Shape { type: string; readonly extends?: number; }
            class Store { delete(key) { return this.get(key); } static new() {} }
            """);
        Is(properties, 0, "default", TokenKind.Property, "keyword spelling after a dot");
        Is(properties, 0, "type", TokenKind.Property, "contextual keyword after a dot");
        Is(properties, 0, "class", TokenKind.Property, "keyword after optional chaining");
        Is(properties, 0, "new", TokenKind.Property, "control keyword after a dot");
        Is(properties, 0, "delete", TokenKind.Function, "keyword-named method call");
        Is(properties, 1, "type", TokenKind.Property, "object key type");
        Is(properties, 1, "default", TokenKind.Property, "object key default");
        Is(properties, 1, "class", TokenKind.Property, "object key class");
        Is(properties, 1, "if", TokenKind.Property, "object key if");
        Is(properties, 1, "get", TokenKind.Property, "object key get");
        Is(properties, 1, "async", TokenKind.Property, "object key async");
        Is(properties, 2, "default", TokenKind.Keyword, "switch default label stays a keyword");
        Is(properties, 2, "case", TokenKind.Control, "switch case");
        Is(properties, 3, "type", TokenKind.Keyword, "import type");
        Is(properties, 3, "from", TokenKind.Keyword, "import from");
        Is(properties, 3, "A", TokenKind.Type, "imported type name");
        Is(properties, 4, "type", TokenKind.Plain, "destructured binding named type");
        Is(properties, 4, "as", TokenKind.Plain, "destructured binding named as");
        Is(properties, 4, "from", TokenKind.Plain, "variable named from");
        Is(properties, 4, "of", TokenKind.Plain, "variable named of");
        Is(properties, 5, "type", TokenKind.Property, "interface member named type");
        Is(properties, 5, "extends", TokenKind.Property, "optional interface member named extends");
        Is(properties, 5, "readonly", TokenKind.Keyword, "readonly modifier");
        Is(properties, 6, "delete", TokenKind.Function, "method named delete");
        Is(properties, 6, "return", TokenKind.Control, "body of a keyword-named method");
        Is(properties, 6, "new", TokenKind.Function, "static method named new");

        ModernSyntax(Is, Token);
        Jsx(Is, Token, check);
        CSharp(Is, Token, check, timing);
        Invariants(check);
        Timing(check, timing);
        Languages(check);
    }

    delegate void IsCheck(Lexed lexed, int line, string needle, TokenKind kind, string what, int occurrence = 0);

    delegate void TokenCheck(Lexed lexed, int line, string needle, TokenKind kind, string what);

    static void ModernSyntax(IsCheck Is, TokenCheck Token)
    {
        var modern = new Lexed(Language.TypeScript, """
            const n = 1_000_000 + 0xFF_FF + 10n + 0b1010 + 0o17 + .5 + 1.5e-3 + 2E+10; // tail
            function identity<T extends object>(value: T): T { return value; }
            const pair = useState<string | null>(null), map = new Map<string, Array<number>>();
            const f = async <T,>(x: T): Promise<T> => x?.y ?? z; flags ??= 1; m >>>= 2; p **= 3;
            @Component({ selector: "app", standalone: true })
            class Foo<T> extends Base<T> implements Bar { @Input() name?: string; #secret = 1; }
            const s = "a\nb\u{1F600}c\x41"; const ok = a < b && c > d; const spread = [...xs];
            const text = "unterminated
            const resumed = 'line\
            continued'; let after = 1;
            """);
        Token(modern, 0, "1_000_000", TokenKind.Number, "numeric separators");
        Token(modern, 0, "0xFF_FF", TokenKind.Number, "hex with separator");
        Token(modern, 0, "10n", TokenKind.Number, "bigint");
        Token(modern, 0, "0b1010", TokenKind.Number, "binary");
        Token(modern, 0, "0o17", TokenKind.Number, "octal");
        Token(modern, 0, "1.5e-3", TokenKind.Number, "exponent");
        Token(modern, 0, ".5", TokenKind.Number, "leading-dot number");
        Token(modern, 0, "2E+10", TokenKind.Number, "signed exponent");
        Token(modern, 0, "// tail", TokenKind.Comment, "comment placed after the numbers");
        Is(modern, 1, "identity", TokenKind.Function, "generic function name");
        Is(modern, 1, "T", TokenKind.Type, "type parameter");
        Is(modern, 1, "extends", TokenKind.Keyword, "constraint keyword");
        Is(modern, 1, "object", TokenKind.Type, "built-in type");
        Is(modern, 1, "value", TokenKind.Plain, "parameter");
        Is(modern, 1, "T)", TokenKind.Type, "parameter type");
        Is(modern, 1, "T {", TokenKind.Type, "return type");
        Is(modern, 1, "return", TokenKind.Control, "body after the generic signature");
        Is(modern, 2, "useState", TokenKind.Function, "generic call");
        Is(modern, 2, "string", TokenKind.Type, "type argument");
        Is(modern, 2, "null", TokenKind.Constant, "null in type arguments");
        Is(modern, 2, "null)", TokenKind.Constant, "null argument");
        Is(modern, 2, "Map", TokenKind.Type, "constructed generic");
        Is(modern, 2, "Array", TokenKind.Type, "nested type argument");
        Is(modern, 2, ">>()", TokenKind.Punctuation, "closing nested type arguments");
        Is(modern, 3, "async", TokenKind.Keyword, "async generic arrow");
        Is(modern, 3, "T,", TokenKind.Type, "arrow type parameter");
        Is(modern, 3, "Promise", TokenKind.Type, "arrow return type");
        Is(modern, 3, "=>", TokenKind.Operator, "arrow");
        Is(modern, 3, "?.", TokenKind.Operator, "optional chaining");
        Is(modern, 3, "y ??", TokenKind.Property, "property after optional chaining");
        Is(modern, 3, "??", TokenKind.Operator, "nullish coalescing");
        Token(modern, 3, "??=", TokenKind.Operator, "nullish assignment");
        Token(modern, 3, ">>>=", TokenKind.Operator, "unsigned shift assignment");
        Token(modern, 3, "**=", TokenKind.Operator, "power assignment");
        Token(modern, 4, "@Component", TokenKind.Decorator, "class decorator");
        Is(modern, 4, "selector", TokenKind.Property, "decorator argument key");
        Is(modern, 4, "\"app\"", TokenKind.String, "decorator argument string");
        Is(modern, 4, "true", TokenKind.Constant, "decorator argument constant");
        Is(modern, 5, "Foo", TokenKind.Type, "class name");
        Is(modern, 5, "implements", TokenKind.Keyword, "implements");
        Token(modern, 5, "@Input", TokenKind.Decorator, "member decorator");
        Is(modern, 5, "name", TokenKind.Property, "optional class field");
        Is(modern, 5, "string", TokenKind.Type, "field type");
        Is(modern, 5, "#secret", TokenKind.Property, "private field");
        Is(modern, 6, "\\n", TokenKind.Escape, "newline escape");
        Token(modern, 6, "\\u{1F600}", TokenKind.Escape, "code point escape");
        Token(modern, 6, "\\x41", TokenKind.Escape, "hex escape");
        Is(modern, 6, "<", TokenKind.Operator, "less-than comparison");
        Is(modern, 6, ">", TokenKind.Operator, "greater-than comparison");
        Is(modern, 6, "...", TokenKind.Operator, "spread");
        Is(modern, 7, "unterminated", TokenKind.String, "unterminated string");
        Is(modern, 8, "const", TokenKind.Keyword, "unterminated string ends at the line end");
        Is(modern, 9, "continued'", TokenKind.String, "backslash continues a string");
        Is(modern, 9, "let", TokenKind.Keyword, "code after a continued string");
    }

    static void Jsx(IsCheck Is, TokenCheck Token, Action<bool, string> check)
    {
        var view = new Lexed(Language.Tsx, """
            const view = <div className="first
              second" onClick={() => go(1 / 2)} {...rest}>
              Hello {name} &amp; it's <b>bold</b> // not a comment
              <Child.Item value={`t${x}`} data-id='a' />
              <>{items.map((i) => <li key={i}>{i}</li>)}</>
            </div>;
            const after = a / b;
            const id = <T,>(x: T) => x, gen = <T extends unknown>(v: T) => v;
            const z = 1;
            """);
        Is(view, 0, "<", TokenKind.Punctuation, "JSX opening bracket");
        Is(view, 0, "div", TokenKind.Tag, "JSX tag name");
        Is(view, 0, "className", TokenKind.Attribute, "JSX attribute");
        Is(view, 0, "\"first", TokenKind.String, "JSX attribute string start");
        Is(view, 1, "second\"", TokenKind.String, "JSX attribute string continues on the next line");
        Is(view, 1, "onClick", TokenKind.Attribute, "attribute after a multi-line string");
        Is(view, 1, "go", TokenKind.Function, "call inside an attribute expression");
        Is(view, 1, "/", TokenKind.Operator, "division inside an attribute expression");
        Is(view, 1, "...", TokenKind.Operator, "spread attribute");
        Is(view, 2, "Hello", TokenKind.Plain, "JSX text");
        Is(view, 2, "name", TokenKind.Plain, "identifier in a child expression");
        Is(view, 2, "&amp;", TokenKind.Escape, "HTML entity");
        Is(view, 2, "it's", TokenKind.Plain, "apostrophe in text is not a string");
        Is(view, 2, "b>", TokenKind.Tag, "inline child tag");
        Is(view, 2, "// not", TokenKind.Plain, "slashes in text are not comments");
        Is(view, 3, "Child.Item", TokenKind.Tag, "member tag");
        Is(view, 3, "`t$", TokenKind.String, "template in attribute expression");
        Is(view, 3, "data-id", TokenKind.Attribute, "dashed attribute");
        Is(view, 3, "'a'", TokenKind.String, "single-quoted attribute");
        Is(view, 3, "/>", TokenKind.Punctuation, "self-closing tag");
        Is(view, 4, "map", TokenKind.Function, "expression inside a fragment");
        Is(view, 4, "li", TokenKind.Tag, "element in an arrow inside JSX");
        Is(view, 4, "</>", TokenKind.Punctuation, "fragment close");
        Is(view, 5, "div", TokenKind.Tag, "closing tag");
        Is(view, 6, "const", TokenKind.Keyword, "script resumes after JSX");
        Is(view, 6, "/", TokenKind.Operator, "division after JSX");
        Is(view, 7, "T", TokenKind.Type, "TSX generic arrow parameter");
        Is(view, 7, "=>", TokenKind.Operator, "TSX generic arrow");
        Is(view, 7, "extends", TokenKind.Keyword, "TSX constrained generic arrow");
        Is(view, 8, "const", TokenKind.Keyword, "code after TSX generic arrows");

        var tsxTypes = new Lexed(Language.Tsx, """
            type Fn = <T>(x: T) => T;
            interface Callable { <T>(x: T): T; }
            const pick = <Select<Option> value={x} onPick={(o) => o.id / 2} />;
            const last = <i>{n}</i>;
            """);
        Is(tsxTypes, 0, "T", TokenKind.Type, "TSX type alias generic is not JSX");
        Is(tsxTypes, 0, "=>", TokenKind.Operator, "TSX function type arrow");
        Is(tsxTypes, 1, "T", TokenKind.Type, "TSX call signature generic is not JSX");
        Is(tsxTypes, 2, "Select", TokenKind.Tag, "generic TSX element");
        Is(tsxTypes, 2, "Option", TokenKind.Type, "TSX element type argument");
        Is(tsxTypes, 2, "value", TokenKind.Attribute, "attribute after element type arguments");
        Is(tsxTypes, 2, "/ 2", TokenKind.Operator, "division in an attribute after type arguments");
        Is(tsxTypes, 3, "i", TokenKind.Tag, "JSX after TSX type syntax");

        var script = new Lexed(Language.TypeScript, """
            const x = <T>(v: T) => v;
            const n = <number>value, ok = a < b;
            let s = "str"; // <div>
            """);
        check(!script.Any(TokenKind.Tag) && !script.Any(TokenKind.Attribute), "lexer .ts never produces JSX tags for <T>");
        Is(script, 0, "T", TokenKind.Type, ".ts generic arrow parameter");
        Is(script, 1, "number", TokenKind.Type, ".ts type assertion");
        Is(script, 1, "<", TokenKind.Operator, ".ts comparison", 1);
        Is(script, 2, "\"str\"", TokenKind.String, "string after .ts generics");
        Token(script, 2, "// <div>", TokenKind.Comment, "comment after .ts generics");

        var js = new Lexed(Language.Jsx, """
            export default function App() {
              return <main title='x'>{count > 1 && <Badge n={count} />}</main>;
            }
            """);
        Is(js, 1, "main", TokenKind.Tag, "JSX in a .jsx return");
        Is(js, 1, "Badge", TokenKind.Tag, "conditional JSX element");
        Is(js, 1, ">", TokenKind.Operator, "comparison inside a JSX expression", 1);
        Is(js, 2, "}", TokenKind.Punctuation, "function closes after JSX");

        var json = new Lexed(Language.Json, """
            { "name": "kumiko", "n": -1.5e3, "ok": true, "v": null, "esc": "a\"b" }
            """);
        Is(json, 0, "\"name\"", TokenKind.Property, "JSON key");
        Is(json, 0, "\"kumiko\"", TokenKind.String, "JSON string value");
        Is(json, 0, "-1.5e3", TokenKind.Number, "JSON number");
        Is(json, 0, "true", TokenKind.Constant, "JSON true");
        Is(json, 0, "null", TokenKind.Constant, "JSON null");
        Is(json, 0, "\\\"", TokenKind.Escape, "JSON escape");

        var plain = Highlighter.Tokenize(Language.Plain, ["const x = 1;"]);
        check(plain.Kinds.Length == 0 && plain.Lines == 1, "lexer plain text has no tokens");
    }
}
