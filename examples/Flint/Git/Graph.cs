using System.Collections.Immutable;

namespace Flint.Git;

public static class Graph
{
    public static ImmutableArray<GraphRow> Layout(ImmutableArray<Commit> commits)
    {
        var lanes = new List<string?>();
        var colors = new List<int>();
        var rows = ImmutableArray.CreateBuilder<GraphRow>();
        var nextColor = 0;
        int Free()
        {
            var index = lanes.IndexOf(null);
            if (index >= 0)
                return index;
            lanes.Add(null);
            colors.Add(0);
            return lanes.Count - 1;
        }
        foreach (var commit in commits)
        {
            var top = ImmutableArray.CreateBuilder<GraphSegment>();
            var col = lanes.IndexOf(commit.Sha);
            for (var j = 0; j < lanes.Count; j++)
            {
                if (lanes[j] is not { } sha)
                    continue;
                top.Add(new(j, sha == commit.Sha ? col : j, colors[j]));
            }
            if (col < 0)
            {
                col = Free();
                lanes[col] = commit.Sha;
                colors[col] = nextColor++;
            }
            foreach (var segment in top)
                if (segment.Other == col && segment.Lane != col)
                    lanes[segment.Lane] = null;
            var color = colors[col];
            var from = new Dictionary<int, int>();
            lanes[col] = commit.Parents.IsEmpty ? null : commit.Parents[0];
            for (var p = 1; p < commit.Parents.Length; p++)
            {
                var k = lanes.IndexOf(commit.Parents[p]);
                if (k < 0)
                {
                    k = Free();
                    lanes[k] = commit.Parents[p];
                    colors[k] = nextColor++;
                }
                if (k != col)
                    from[k] = col;
            }
            var bottom = ImmutableArray.CreateBuilder<GraphSegment>();
            var width = col + 1;
            foreach (var segment in top)
                width = Math.Max(width, segment.Lane + 1);
            for (var j = 0; j < lanes.Count; j++)
            {
                if (lanes[j] is null)
                    continue;
                bottom.Add(new(j, from.GetValueOrDefault(j, j), colors[j]));
                width = Math.Max(width, j + 1);
            }
            while (lanes.Count > 0 && lanes[^1] is null)
            {
                lanes.RemoveAt(lanes.Count - 1);
                colors.RemoveAt(colors.Count - 1);
            }
            rows.Add(new(col, color, width, top.ToImmutable(), bottom.ToImmutable(), commit.Parents.Length > 1));
        }
        return rows.ToImmutable();
    }
}
