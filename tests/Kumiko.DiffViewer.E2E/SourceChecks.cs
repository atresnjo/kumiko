using DiffViewer.Core;
using DiffViewer.Sources;

namespace DiffViewer.E2E;

/// <summary>
/// Working-tree, staged, revision and folder comparisons against isolated temporary repositories. Line counts are
/// cross-checked with Git's own numstat; the user's repositories and Git configuration are never read.
/// </summary>
internal static class SourceChecks
{
    public static void Run(Harness h)
    {
        using var repo = new Repository("sources");
        repo.Write(".gitignore", "dist/\n");
        repo.Write("src/app.ts", string.Join('\n', Enumerable.Range(1, 60).Select(i => $"export const value{i} = {i};")) + "\n");
        repo.Write("src/remove-me.ts", "export const gone = true;\n");
        repo.Write("src/old-name.ts", string.Join('\n', Enumerable.Range(1, 30).Select(i => $"// line {i}")) + "\n");
        repo.Write("README.md", "# Fixture\n\nIntro.\n");
        File.WriteAllBytes(Path.Combine(repo.Root, "logo.bin"), [0, 1, 2, 3, 0, 255]);
        repo.Write("crlf.ts", "const a = 1;\r\nconst b = 2;\r\n");
        repo.Commit("initial");

        repo.Write("src/app.ts", string.Join('\n', Enumerable.Range(1, 60).Select(i => i switch
        {
            10 => "export const value10 = 100; // changed",
            40 => "export const value40 = 400; // changed",
            _ => $"export const value{i} = {i};"
        }).Concat(["export const extra1 = 1;", "export const extra2 = 2;"])) + "\n");
        File.Delete(Path.Combine(repo.Root, "src/remove-me.ts"));
        repo.Git("mv", "src/old-name.ts", "src/new-name.ts");
        repo.Write("README.md", "# Fixture\n\nIntro, staged.\n");
        repo.Git("add", "README.md");
        repo.Write("src/new view.tsx", "export const View = () => <div className=\"x\">{1}</div>;\n");
        repo.Write("src/ünïcode.ts", "export const café = \"crème\";\n");
        repo.Write("dist/out.js", "ignored();\n");
        File.WriteAllBytes(Path.Combine(repo.Root, "logo.bin"), [0, 1, 2, 3, 0, 254, 9]);
        repo.Write("crlf.ts", "const a = 1;\r\nconst b = 3;\r\n");
        File.WriteAllBytes(Path.Combine(repo.Root, "latin1.ts"), [0x2F, 0x2F, 0x20, 0xE9, 0x0A]);

        var working = Load(new(SourceKind.WorkingTree, repo.Root, "HEAD"));
        var paths = working.Files.Select(f => f.Path).ToArray();
        string[] expected = ["README.md", "crlf.ts", "latin1.ts", "logo.bin", "src/app.ts", "src/new view.tsx", "src/new-name.ts", "src/remove-me.ts", "src/ünïcode.ts"];
        h.Check(paths.SequenceEqual(expected), $"the working tree shows staged, unstaged, untracked, deleted and renamed files ({string.Join(", ", paths)})");
        h.Check(!paths.Any(p => p.StartsWith("dist/", StringComparison.Ordinal)), "ignored files stay out of the working-tree diff");
        var file = (string path) => working.Files.Single(f => f.Path == path);
        h.Check(file("src/new-name.ts") is { Status: FileStatus.Renamed, OldPath: "src/old-name.ts", Hunks.Length: 0 }, "a pure rename has no hunks and names its old path");
        h.Check(file("src/remove-me.ts") is { Status: FileStatus.Deleted, Deletions: 1, Additions: 0 }, "a deleted file shows its removed lines");
        h.Check(file("logo.bin").Binary && file("logo.bin").Lines.IsEmpty, "binary files are detected and never decoded as text");
        h.Check(file("latin1.ts").NewLines is [var latin] && latin.Contains('�'), "invalid UTF-8 decodes with replacement characters");
        h.Check(file("crlf.ts") is { Additions: 1, Deletions: 1 } crlf && !crlf.NewLines.Any(l => l.Contains('\r')), "CRLF endings neither count as changes nor show");
        h.Check(file("src/new view.tsx").Language == Highlight.Language.Tsx && file("src/new view.tsx").NewTokens.Kinds.Contains(Highlight.TokenKind.Tag),
            "paths with spaces load, and TSX is highlighted");
        var numstat = NumStat(repo.Git("diff", "--numstat", "-M", "HEAD"));
        foreach (var (path, added, removed) in numstat)
            h.Check(file(path) is var f && f.Additions == added && f.Deletions == removed, $"{path} matches git numstat (+{added} -{removed})");
        var app = file("src/app.ts");
        h.Check(app.Hunks.Length == 3 && app.Hunks[0].OldStart == 6 && app.Hunks[0].OldCount == 7, "separate edits form separate hunks with three context lines");

        var staged = Load(new(SourceKind.Staged, repo.Root, "HEAD"));
        h.Check(staged.Files.Select(f => f.Path).SequenceEqual(["README.md", "src/new-name.ts"]), "staged changes show only the index");

        repo.Commit("second");
        var revisions = Load(new(SourceKind.Revisions, repo.Root, "HEAD~1", "HEAD"));
        h.Check(revisions.Files.Length == working.Files.Length && revisions.Files.Single(f => f.Path == "src/app.ts").Additions == app.Additions,
            "a commit range matches the working tree it was committed from");
        h.Check(Load(new(SourceKind.WorkingTree, repo.Root, "HEAD")).Files.IsEmpty, "a clean working tree shows no changes");

        using var left = new Repository("left");
        using var right = new Repository("right");
        left.Write("a.ts", "const a = 1;\nconst b = 2;\n");
        left.Write("only-left.ts", "x;\n");
        right.Write("a.ts", "const a = 1;\nconst b = 3;\n");
        right.Write("nested/only-right.ts", "y;\n");
        var folders = Load(new(SourceKind.Paths, left.Root, null, right.Root));
        h.Check(folders.Files.Select(f => (f.Path, f.Status)).SequenceEqual([("a.ts", FileStatus.Modified), ("nested/only-right.ts", FileStatus.Added), ("only-left.ts", FileStatus.Deleted)]),
            "two folders compare by relative path, without their .git folders");
        var single = Load(new(SourceKind.Paths, Path.Combine(left.Root, "a.ts"), null, Path.Combine(right.Root, "a.ts")));
        h.Check(single.Files is [{ Additions: 1, Deletions: 1 }], "two files compare directly");
        h.Check(Arguments.Parse(["main..feature"], "/tmp") is { Kind: SourceKind.Revisions, Base: "main", Target: "feature" } &&
            Arguments.Parse(["--staged"], "/tmp").Kind == SourceKind.Staged &&
            Arguments.Parse(["--compare", "a", "b"], "/tmp") is { Kind: SourceKind.Paths, Path: "/tmp/a", Target: "/tmp/b" },
            "command-line arguments select the source");
        ShownLinesOnly(h);
    }

    /// <summary>
    /// A long file with two small edits, one inside a block comment opened far above it: the document keeps only the
    /// shown lines' text and colours, yet those still come from the whole file.
    /// </summary>
    static void ShownLinesOnly(Harness h)
    {
        using var repo = new Repository("shown");
        string Line(int i, bool edited) => i switch
        {
            1 => "/* opened far above the first hunk",
            1500 => "   closed here */",
            1490 when edited => "   edited inside the comment",
            2500 when edited => "export const value2500 = \"changed\";",
            _ => i < 1500 ? $"   comment line {i}" : $"export const value{i} = {i};"
        };
        string Text(bool edited) => string.Join('\n', Enumerable.Range(1, 3000).Select(i => Line(i, edited))) + "\n";
        repo.Write("long.ts", Text(false));
        repo.Commit("initial");
        repo.Write("long.ts", Text(true));
        var file = Load(new(SourceKind.WorkingTree, repo.Root, "HEAD")).Files.Single();
        var disk = Text(true).Split('\n');
        var shown = file.Lines.Where(l => l.Kind != LineKind.Removed).Select(l => l.New).ToArray();
        h.Check(shown.All(n => file.NewLines[n] == disk[n]) && file.Hunks[0].NewStart == 1486 && file.NewLines.Length == 3000,
            "shown lines keep their text and line numbers");
        var comment = file.NewTokens;
        int edited = 1489;
        h.Check(comment.LineStarts[edited + 1] > comment.LineStarts[edited] && comment.Kinds[comment.LineStarts[edited]] == Highlight.TokenKind.Comment,
            "a line inside a block comment opened above its hunk still colours as comment");
        long kept = file.OldLines.Sum(l => (long)l.Length) + file.NewLines.Sum(l => (long)l.Length);
        long whole = 2L * disk.Sum(l => (long)l.Length);
        int tokens = file.OldTokens.Kinds.Length + file.NewTokens.Kinds.Length;
        h.Check(kept < whole / 50 && tokens < 100, $"a loaded file keeps only its shown lines' text and colours ({kept} of {whole} characters, {tokens} tokens)");
    }

    static DiffDocument Load(DiffRequest request) => Loader.Load(request, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Text files from git's numstat; binary lines ("-") and renames are skipped.</summary>
    static IEnumerable<(string Path, int Added, int Removed)> NumStat(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts[0] == "-" || parts[2].Contains("=>", StringComparison.Ordinal))
                continue;
            yield return (parts[2].Trim('"'), int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }
}
