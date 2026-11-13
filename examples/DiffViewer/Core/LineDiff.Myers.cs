namespace DiffViewer.Core;

public static partial class LineDiff
{
    sealed partial class Session
    {
        // git bails out of one split after max(sqrt(diagonals), 256) edit steps; a fixed 256 keeps nearly the same
        // output on noisy inputs at a fraction of the cost.
        const int MaxCost = 256;
        const int MaxEqualLimit = 1024;
        const int ScanWindow = 100;
        const int KeepDiscardRun = 4;

        readonly Stack<Box> boxes = new();
        int[] reducedOld = [];
        int[] reducedNew = [];
        int[] indexOld = [];
        int[] indexNew = [];
        byte[] discard = [];
        int[] nextZero = [];
        int[] nextOne = [];
        int[] kvd = [];
        int[] countOld = [];
        int[] countNew = [];
        int[] stampOld = [];
        int[] stampNew = [];

        /// <summary>Myers O(ND) on a region, after discarding lines as xdiff's xdl_cleanup_records does.</summary>
        void Classic(int a0, int a1, int b0, int b1)
        {
            if (work > budget)
            {
                MarkOld(a0, a1);
                MarkNew(b0, b1);
                return;
            }
            EnsureClassicBuffers(Math.Max(a1 - a0, b1 - b0));
            generation++;
            var stamp = generation;
            for (var i = a0; i < a1; i++)
            {
                Count(countOld, stampOld, a[i], stamp);
            }
            for (var j = b0; j < b1; j++)
            {
                Count(countNew, stampNew, b[j], stamp);
            }
            var na = Reduce(a, a0, a1, countNew, stampNew, stamp, changedOld, reducedOld, indexOld);
            var nb = Reduce(b, b0, b1, countOld, stampOld, stamp, changedNew, reducedNew, indexNew);
            work += 4L * (a1 - a0 + b1 - b0);
            Myers(na, nb);
        }

        void EnsureClassicBuffers(int length)
        {
            if (countOld.Length == 0)
            {
                countOld = new int[classCount];
                countNew = new int[classCount];
                stampOld = new int[classCount];
                stampNew = new int[classCount];
            }
            if (reducedOld.Length < length)
            {
                var size = Math.Max(length, Math.Min(Math.Max(n, m), reducedOld.Length * 2));
                reducedOld = new int[size];
                reducedNew = new int[size];
                indexOld = new int[size];
                indexNew = new int[size];
                discard = new byte[size];
                nextZero = new int[size];
                nextOne = new int[size];
            }
        }

        static void Count(int[] counts, int[] stamps, int c, int stamp)
        {
            if (stamps[c] != stamp)
            {
                stamps[c] = stamp;
                counts[c] = 0;
            }
            counts[c]++;
        }

        /// <summary>
        /// Drops lines without a match on the other side, and frequent lines inside runs of such lines, marking them
        /// changed. Returns the number of kept lines written to <paramref name="reduced"/>.
        /// </summary>
        int Reduce(int[] lines, int start, int end, int[] otherCounts, int[] otherStamps, int stamp, bool[] changed,
            int[] reduced, int[] index)
        {
            var length = end - start;
            var limit = Math.Min(BogoSqrt(length), MaxEqualLimit);
            var dis = discard;
            for (var i = 0; i < length; i++)
            {
                var c = lines[start + i];
                var matches = otherStamps[c] == stamp ? otherCounts[c] : 0;
                dis[i] = matches == 0 ? (byte)0 : matches >= limit ? (byte)2 : (byte)1;
            }
            var zero = length;
            var one = length;
            for (var i = length - 1; i >= 0; i--)
            {
                nextZero[i] = zero;
                nextOne[i] = one;
                if (dis[i] == 0)
                {
                    zero = i;
                }
                else if (dis[i] == 1)
                {
                    one = i;
                }
            }
            var lastZero = -1;
            var lastOne = -1;
            var kept = 0;
            for (var i = 0; i < length; i++)
            {
                var d = dis[i];
                var keep = d == 1 || d == 2 && !DiscardMultimatch(dis, i, length, lastZero, lastOne);
                if (keep)
                {
                    reduced[kept] = lines[start + i];
                    index[kept] = start + i;
                    kept++;
                }
                else
                {
                    changed[start + i] = true;
                }
                if (d == 0)
                {
                    lastZero = i;
                }
                else if (d == 1)
                {
                    lastOne = i;
                }
            }
            return kept;
        }

        /// <summary>xdl_clean_mmatch: discard a frequent line only when it sits among lines without matches.</summary>
        bool DiscardMultimatch(byte[] dis, int i, int length, int lastZero, int lastOne)
        {
            var s = Math.Max(0, i - ScanWindow);
            var e = Math.Min(length - 1, i + ScanWindow);
            if (lastZero < s || lastZero < lastOne || nextZero[i] > e || nextZero[i] > nextOne[i])
            {
                return false;
            }
            var before = 0;
            var beforeMulti = 1;
            for (var r = 1; i - r >= s; r++)
            {
                var d = dis[i - r];
                if (d == 0)
                {
                    before++;
                }
                else if (d == 2)
                {
                    beforeMulti++;
                }
                else
                {
                    break;
                }
            }
            var after = 0;
            var afterMulti = 1;
            for (var r = 1; i + r <= e; r++)
            {
                var d = dis[i + r];
                if (d == 0)
                {
                    after++;
                }
                else if (d == 2)
                {
                    afterMulti++;
                }
                else
                {
                    break;
                }
            }
            work += 2 * ScanWindow;
            var discarded = before + after;
            var multi = beforeMulti + afterMulti;
            return multi * KeepDiscardRun < multi + discarded;
        }

        static int BogoSqrt(int value)
        {
            var result = 1;
            for (var v = value; v > 0; v >>= 2)
            {
                result <<= 1;
            }
            return result;
        }

        void Myers(int na, int nb)
        {
            var diagonals = na + nb + 3;
            if (kvd.Length < 2 * diagonals)
            {
                kvd = new int[2 * diagonals];
            }
            boxes.Push(new Box(0, na, 0, nb, false));
            while (boxes.TryPop(out var box))
            {
                var off1 = box.Off1;
                var lim1 = box.Lim1;
                var off2 = box.Off2;
                var lim2 = box.Lim2;
                while (off1 < lim1 && off2 < lim2 && reducedOld[off1] == reducedNew[off2])
                {
                    off1++;
                    off2++;
                }
                while (off1 < lim1 && off2 < lim2 && reducedOld[lim1 - 1] == reducedNew[lim2 - 1])
                {
                    lim1--;
                    lim2--;
                }
                if (off1 == lim1 || off2 == lim2 || work > budget)
                {
                    MarkReduced(off1, lim1, off2, lim2);
                    continue;
                }
                var split = Split(off1, lim1, off2, lim2, box.NeedMin, nb + 1, diagonals + nb + 1);
                boxes.Push(new Box(split.I1, lim1, split.I2, lim2, split.MinHi));
                boxes.Push(new Box(off1, split.I1, off2, split.I2, split.MinLo));
            }
        }

        void MarkReduced(int off1, int lim1, int off2, int lim2)
        {
            for (var i = off1; i < lim1; i++)
            {
                changedOld[indexOld[i]] = true;
            }
            for (var j = off2; j < lim2; j++)
            {
                changedNew[indexNew[j]] = true;
            }
        }

        /// <summary>xdl_split: meet-in-the-middle search for the middle snake, bailing out past the cost limit.</summary>
        Split Split(int off1, int lim1, int off2, int lim2, bool needMin, int fo, int bo)
        {
            var ha1 = reducedOld;
            var ha2 = reducedNew;
            var kv = kvd;
            var dmin = off1 - lim2;
            var dmax = lim1 - off2;
            var fmid = off1 - off2;
            var bmid = lim1 - lim2;
            var odd = ((fmid - bmid) & 1) != 0;
            var fmin = fmid;
            var fmax = fmid;
            var bmin = bmid;
            var bmax = bmid;
            kv[fo + fmid] = off1;
            kv[bo + bmid] = lim1;
            for (var ec = 1; ; ec++)
            {
                var steps = 0;
                if ((ec & 127) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (fmin > dmin)
                {
                    fmin--;
                    kv[fo + fmin - 1] = -1;
                }
                else
                {
                    fmin++;
                }
                if (fmax < dmax)
                {
                    fmax++;
                    kv[fo + fmax + 1] = -1;
                }
                else
                {
                    fmax--;
                }
                for (var d = fmax; d >= fmin; d -= 2)
                {
                    var i1 = kv[fo + d - 1] >= kv[fo + d + 1] ? kv[fo + d - 1] + 1 : kv[fo + d + 1];
                    var prev = i1;
                    var i2 = i1 - d;
                    while (i1 < lim1 && i2 < lim2 && ha1[i1] == ha2[i2])
                    {
                        i1++;
                        i2++;
                    }
                    steps += i1 - prev + 1;
                    kv[fo + d] = i1;
                    if (odd && bmin <= d && d <= bmax && kv[bo + d] <= i1)
                    {
                        return new Split(i1, i2, true, true);
                    }
                }
                if (bmin > dmin)
                {
                    bmin--;
                    kv[bo + bmin - 1] = int.MaxValue;
                }
                else
                {
                    bmin++;
                }
                if (bmax < dmax)
                {
                    bmax++;
                    kv[bo + bmax + 1] = int.MaxValue;
                }
                else
                {
                    bmax--;
                }
                for (var d = bmax; d >= bmin; d -= 2)
                {
                    var i1 = kv[bo + d - 1] < kv[bo + d + 1] ? kv[bo + d - 1] : kv[bo + d + 1] - 1;
                    var prev = i1;
                    var i2 = i1 - d;
                    while (i1 > off1 && i2 > off2 && ha1[i1 - 1] == ha2[i2 - 1])
                    {
                        i1--;
                        i2--;
                    }
                    steps += prev - i1 + 1;
                    kv[bo + d] = i1;
                    if (!odd && fmin <= d && d <= fmax && i1 <= kv[fo + d])
                    {
                        return new Split(i1, i2, true, true);
                    }
                }
                work += steps;
                var exhausted = work > budget;
                if (needMin && !exhausted)
                {
                    continue;
                }
                if (ec >= MaxCost || exhausted)
                {
                    return FurthestReaching(off1, lim1, off2, lim2, fmin, fmax, bmin, bmax, fo, bo);
                }
            }
        }

        Split FurthestReaching(int off1, int lim1, int off2, int lim2, int fmin, int fmax, int bmin, int bmax, int fo, int bo)
        {
            var kv = kvd;
            var fbest = -1;
            var fbest1 = -1;
            for (var d = fmax; d >= fmin; d -= 2)
            {
                var i1 = Math.Min(kv[fo + d], lim1);
                var i2 = i1 - d;
                if (lim2 < i2)
                {
                    i1 = lim2 + d;
                    i2 = lim2;
                }
                if (fbest < i1 + i2)
                {
                    fbest = i1 + i2;
                    fbest1 = i1;
                }
            }
            var bbest = int.MaxValue;
            var bbest1 = int.MaxValue;
            for (var d = bmax; d >= bmin; d -= 2)
            {
                var i1 = Math.Max(off1, kv[bo + d]);
                var i2 = i1 - d;
                if (i2 < off2)
                {
                    i1 = off2 + d;
                    i2 = off2;
                }
                if (i1 + i2 < bbest)
                {
                    bbest = i1 + i2;
                    bbest1 = i1;
                }
            }
            if (lim1 + lim2 - bbest < fbest - (off1 + off2))
            {
                return new Split(fbest1, fbest - fbest1, true, false);
            }
            return new Split(bbest1, bbest - bbest1, false, true);
        }
    }

    readonly record struct Box(int Off1, int Lim1, int Off2, int Lim2, bool NeedMin);

    readonly record struct Split(int I1, int I2, bool MinLo, bool MinHi);
}
