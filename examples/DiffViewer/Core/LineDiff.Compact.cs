namespace DiffViewer.Core;

public static partial class LineDiff
{
    sealed partial class Session
    {
        const int MaxIndent = 200;
        const int MaxBlanks = 20;
        const int MaxSliding = 100;
        const int StartOfFilePenalty = 1;
        const int EndOfFilePenalty = 21;
        const int TotalBlankWeight = -30;
        const int PostBlankWeight = 6;
        const int RelativeIndentPenalty = -4;
        const int RelativeIndentWithBlankPenalty = 10;
        const int RelativeOutdentPenalty = 24;
        const int RelativeOutdentWithBlankPenalty = 17;
        const int RelativeDedentPenalty = 23;
        const int RelativeDedentWithBlankPenalty = 17;
        const int IndentWeight = 60;

        /// <summary>
        /// git's xdl_change_compact: slide each group of changed lines over equal neighbours, merge groups that touch,
        /// align with a change in the other file when possible, and otherwise pick the shift the indent heuristic
        /// scores best.
        /// </summary>
        void Compact(bool[] changed, int[] classes, IReadOnlyList<string> lines, int length, bool[] other, int otherLength)
        {
            var g = new Group(0, 0);
            var go = new Group(0, 0);
            Init(changed, length, ref g);
            Init(other, otherLength, ref go);
            var groups = 0;
            while (true)
            {
                if (g.End != g.Start)
                {
                    groups++;
                    if ((groups & 1023) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                    }
                    Slide(changed, classes, lines, length, other, otherLength, ref g, ref go);
                }
                if (!Next(changed, length, ref g))
                {
                    break;
                }
                Sync(Next(other, otherLength, ref go));
            }
        }

        static void Slide(bool[] changed, int[] classes, IReadOnlyList<string> lines, int length, bool[] other, int otherLength,
            ref Group g, ref Group go)
        {
            int size;
            int earliestEnd;
            int endMatchingOther;
            do
            {
                size = g.End - g.Start;
                endMatchingOther = -1;
                while (SlideUp(changed, classes, ref g))
                {
                    Sync(Previous(other, ref go));
                }
                earliestEnd = g.End;
                if (go.End > go.Start)
                {
                    endMatchingOther = g.End;
                }
                while (SlideDown(changed, classes, length, ref g))
                {
                    Sync(Next(other, otherLength, ref go));
                    if (go.End > go.Start)
                    {
                        endMatchingOther = g.End;
                    }
                }
            }
            while (size != g.End - g.Start);
            if (g.End == earliestEnd)
            {
                return;
            }
            if (endMatchingOther != -1)
            {
                while (go.End == go.Start)
                {
                    Sync(SlideUp(changed, classes, ref g));
                    Sync(Previous(other, ref go));
                }
                return;
            }
            var shift = Math.Max(earliestEnd, Math.Max(g.End - size - 1, g.End - MaxSliding));
            var bestShift = -1;
            var bestIndent = 0;
            var bestPenalty = 0;
            for (; shift <= g.End; shift++)
            {
                var indent = 0;
                var penalty = 0;
                Score(Measure(lines, length, shift), ref indent, ref penalty);
                Score(Measure(lines, length, shift - size), ref indent, ref penalty);
                var compare = IndentWeight * Math.Sign(indent - bestIndent) + (penalty - bestPenalty);
                if (bestShift == -1 || compare <= 0)
                {
                    bestIndent = indent;
                    bestPenalty = penalty;
                    bestShift = shift;
                }
            }
            while (g.End > bestShift)
            {
                Sync(SlideUp(changed, classes, ref g));
                Sync(Previous(other, ref go));
            }
        }

        static void Sync(bool moved)
        {
            if (!moved)
            {
                throw new InvalidOperationException("Diff groups lost synchronisation while compacting.");
            }
        }

        static void Init(bool[] changed, int length, ref Group g)
        {
            g = new Group(0, 0);
            while (g.End < length && changed[g.End])
            {
                g.End++;
            }
        }

        static bool Next(bool[] changed, int length, ref Group g)
        {
            if (g.End == length)
            {
                return false;
            }
            g.Start = g.End + 1;
            g.End = g.Start;
            while (g.End < length && changed[g.End])
            {
                g.End++;
            }
            return true;
        }

        static bool Previous(bool[] changed, ref Group g)
        {
            if (g.Start == 0)
            {
                return false;
            }
            g.End = g.Start - 1;
            g.Start = g.End;
            while (g.Start > 0 && changed[g.Start - 1])
            {
                g.Start--;
            }
            return true;
        }

        static bool SlideDown(bool[] changed, int[] classes, int length, ref Group g)
        {
            if (g.End >= length || classes[g.Start] != classes[g.End])
            {
                return false;
            }
            changed[g.Start] = false;
            g.Start++;
            changed[g.End] = true;
            g.End++;
            while (g.End < length && changed[g.End])
            {
                g.End++;
            }
            return true;
        }

        static bool SlideUp(bool[] changed, int[] classes, ref Group g)
        {
            if (g.Start <= 0 || classes[g.Start - 1] != classes[g.End - 1])
            {
                return false;
            }
            g.Start--;
            changed[g.Start] = true;
            g.End--;
            changed[g.End] = false;
            while (g.Start > 0 && changed[g.Start - 1])
            {
                g.Start--;
            }
            return true;
        }

        static Measurement Measure(IReadOnlyList<string> lines, int length, int split)
        {
            var endOfFile = split >= length;
            var indent = endOfFile ? -1 : Indent(lines[split]);
            var preBlank = 0;
            var preIndent = -1;
            for (var i = split - 1; i >= 0; i--)
            {
                preIndent = Indent(lines[i]);
                if (preIndent != -1)
                {
                    break;
                }
                preBlank++;
                if (preBlank == MaxBlanks)
                {
                    preIndent = 0;
                    break;
                }
            }
            var postBlank = 0;
            var postIndent = -1;
            for (var i = split + 1; i < length; i++)
            {
                postIndent = Indent(lines[i]);
                if (postIndent != -1)
                {
                    break;
                }
                postBlank++;
                if (postBlank == MaxBlanks)
                {
                    postIndent = 0;
                    break;
                }
            }
            return new Measurement(endOfFile, indent, preBlank, preIndent, postBlank, postIndent);
        }

        static void Score(Measurement m, ref int effectiveIndent, ref int penalty)
        {
            if (m.PreIndent == -1 && m.PreBlank == 0)
            {
                penalty += StartOfFilePenalty;
            }
            if (m.EndOfFile)
            {
                penalty += EndOfFilePenalty;
            }
            var postBlank = m.Indent == -1 ? 1 + m.PostBlank : 0;
            var totalBlank = m.PreBlank + postBlank;
            penalty += TotalBlankWeight * totalBlank;
            penalty += PostBlankWeight * postBlank;
            var indent = m.Indent != -1 ? m.Indent : m.PostIndent;
            var anyBlanks = totalBlank != 0;
            effectiveIndent += indent;
            if (indent == -1 || m.PreIndent == -1 || indent == m.PreIndent)
            {
                return;
            }
            if (indent > m.PreIndent)
            {
                penalty += anyBlanks ? RelativeIndentWithBlankPenalty : RelativeIndentPenalty;
            }
            else if (m.PostIndent != -1 && m.PostIndent > indent)
            {
                penalty += anyBlanks ? RelativeOutdentWithBlankPenalty : RelativeOutdentPenalty;
            }
            else
            {
                penalty += anyBlanks ? RelativeDedentWithBlankPenalty : RelativeDedentPenalty;
            }
        }

        /// <summary>Column of the first non-space character (tabs to multiples of 8), or -1 for a blank line.</summary>
        static int Indent(string? line)
        {
            if (line is null)
            {
                return -1;
            }
            var result = 0;
            foreach (var c in line)
            {
                if (!IsSpace(c))
                {
                    return result;
                }
                if (c == ' ')
                {
                    result++;
                }
                else if (c == '\t')
                {
                    result += 8 - result % 8;
                }
                if (result >= MaxIndent)
                {
                    return MaxIndent;
                }
            }
            return -1;
        }
    }

    struct Group(int start, int end)
    {
        public int Start = start;
        public int End = end;
    }

    readonly record struct Measurement(bool EndOfFile, int Indent, int PreBlank, int PreIndent, int PostBlank, int PostIndent);
}
