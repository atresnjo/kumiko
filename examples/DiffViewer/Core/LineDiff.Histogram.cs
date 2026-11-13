namespace DiffViewer.Core;

public static partial class LineDiff
{
    sealed partial class Session
    {
        // Lines occurring more often than this in a region never anchor it (git and JGit use the same limit).
        const int MaxChain = 64;

        readonly Stack<Region> regions = new();
        int[] chainHead = [];
        int[] chainCount = [];
        int[] chainStamp = [];
        int[] chainNext = [];
        int generation;

        void Histogram()
        {
            chainHead = new int[classCount];
            chainCount = new int[classCount];
            chainStamp = new int[classCount];
            chainNext = new int[n];
            regions.Push(new Region(0, n, 0, m));
            while (regions.TryPop(out var region))
            {
                Diff(region.A0, region.A1, region.B0, region.B1);
            }
        }

        void Diff(int a0, int a1, int b0, int b1)
        {
            var start = a0;
            while (a0 < a1 && b0 < b1 && a[a0] == b[b0])
            {
                a0++;
                b0++;
            }
            while (a0 < a1 && b0 < b1 && a[a1 - 1] == b[b1 - 1])
            {
                a1--;
                b1--;
            }
            work += a0 - start;
            if (a0 == a1 || b0 == b1 || work > budget)
            {
                MarkOld(a0, a1);
                MarkNew(b0, b1);
                return;
            }
            token.ThrowIfCancellationRequested();
            var lcs = FindLcs(a0, a1, b0, b1, out var hasCommon);
            if (lcs.A1 > lcs.A0)
            {
                regions.Push(new Region(lcs.A1, a1, lcs.B1, b1));
                regions.Push(new Region(a0, lcs.A0, b0, lcs.B0));
            }
            else if (hasCommon)
            {
                Classic(a0, a1, b0, b1);
            }
            else
            {
                MarkOld(a0, a1);
                MarkNew(b0, b1);
            }
        }

        /// <summary>
        /// Finds the longest common run whose rarest line occurs least often in the old region, as git's
        /// xhistogram does. Returns an empty region when no line occurs at most <see cref="MaxChain"/> times.
        /// </summary>
        Region FindLcs(int a0, int a1, int b0, int b1, out bool hasCommon)
        {
            generation++;
            var stamp = generation;
            var head = chainHead;
            var count = chainCount;
            var marks = chainStamp;
            var next = chainNext;
            for (var i = a1 - 1; i >= a0; i--)
            {
                var c = a[i];
                if (marks[c] != stamp)
                {
                    marks[c] = stamp;
                    count[c] = 1;
                    next[i] = -1;
                }
                else
                {
                    count[c]++;
                    next[i] = head[c];
                }
                head[c] = i;
            }
            work += (a1 - a0) + (b1 - b0);
            hasCommon = false;
            var best = new Region(0, 0, 0, 0);
            var bestCount = MaxChain + 1;
            var j = b0;
            while (j < b1)
            {
                var jNext = j + 1;
                var c = b[j];
                if (marks[c] == stamp)
                {
                    hasCommon = true;
                    if (count[c] <= bestCount)
                    {
                        var p = head[c];
                        while (p >= 0)
                        {
                            var as0 = p;
                            var bs0 = j;
                            var ae = p + 1;
                            var be = j + 1;
                            var rc = count[c];
                            while (as0 > a0 && bs0 > b0 && a[as0 - 1] == b[bs0 - 1])
                            {
                                as0--;
                                bs0--;
                                if (rc > 1)
                                {
                                    rc = Math.Min(rc, count[a[as0]]);
                                }
                            }
                            while (ae < a1 && be < b1 && a[ae] == b[be])
                            {
                                if (rc > 1)
                                {
                                    rc = Math.Min(rc, count[a[ae]]);
                                }
                                ae++;
                                be++;
                            }
                            work += ae - as0 + 1;
                            if (jNext < be)
                            {
                                jNext = be;
                            }
                            if (best.A1 - best.A0 < ae - as0 || rc < bestCount)
                            {
                                best = new Region(as0, ae, bs0, be);
                                bestCount = rc;
                            }
                            var np = next[p];
                            while (np >= 0 && np < ae)
                            {
                                np = next[np];
                            }
                            p = np;
                        }
                    }
                }
                j = jNext;
                if (work > budget)
                {
                    break;
                }
            }
            return bestCount <= MaxChain ? best : new Region(0, 0, 0, 0);
        }
    }

    readonly record struct Region(int A0, int A1, int B0, int B1);
}
