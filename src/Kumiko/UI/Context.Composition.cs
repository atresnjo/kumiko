namespace Kumiko.UI;

public sealed partial class Context
{
    ChildScope? composition;
    readonly Stack<Dictionary<int, int>> ordinalPool = new();

    // Identity has no layout/semantic node. Descendants retain normal form/modal ancestry.
    internal sealed class ChildScope : IDisposable
    {
        readonly Context context;
        readonly ChildScope? parent;
        readonly int[] layout;
        readonly int retentionDepth;
        internal readonly ulong Id;
        internal readonly string Path;
        /// <summary>Unkeyed child ordinals per layout parent; pooled because every frame rebuilds every child.</summary>
        internal readonly Dictionary<int, int> Ordinals;
        bool disposed;

        internal ChildScope(Context context, NodeKey key, bool visible)
        {
            this.context = context;
            parent = context.composition;
            Ordinals = context.ordinalPool.Count > 0 ? context.ordinalPool.Pop() : [];
            layout = context.stack.ToArray();
            retentionDepth = context.retentionStack.Count;
            Id = Mix(context.IdentityParent(), Mix(Hash("Kumiko.Child"), Hash(key.Value!)));
            Path = (parent?.Path ?? "") + $"view[{Quote(key.Value)}]/";
            context.BeginRetention(Id, visible);
            context.composition = this;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            context.CheckThread();
            var balanced = context.composition == this && context.stack.SequenceEqual(layout) &&
                context.retentionStack.Count == retentionDepth + 1;
            context.composition = parent;
            context.stack.Clear();
            context.stack.AddRange(layout);
            context.retentionStack.RemoveRange(retentionDepth, context.retentionStack.Count - retentionDepth);
            disposed = true;
            Ordinals.Clear();
            context.ordinalPool.Push(Ordinals);
            if (!balanced)
                throw new InvalidOperationException($"Child {Path} left an unbalanced layout or retention scope.");
        }
    }

    internal ChildScope BeginChild(NodeKey key, bool visible = true)
    {
        CheckThread();
        if (string.IsNullOrEmpty(key.Value))
            throw new ArgumentException("A child view requires a nonempty stable key.", nameof(key));
        return new(this, key, visible);
    }

    ulong IdentityParent()
    {
        var parent = stack.Count > 0 ? Tree[stack[^1]].Id : 0x123456789abcdef;
        return composition is { } owner ? Mix(parent, owner.Id) : parent;
    }

    string? CompositionSegment(int parent)
    {
        if (composition is null)
            return null;
        var preceding = parent >= 0 ? Tree[parent].CompositionPath : null;
        return preceding is not null && composition.Path.StartsWith(preceding, StringComparison.Ordinal)
            ? composition.Path[preceding.Length..] : composition.Path;
    }
}
