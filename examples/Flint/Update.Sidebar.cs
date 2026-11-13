namespace Flint;

public sealed partial class FlintApp
{
    static RepoTab CollapseSidebar(RepoTab tab)
    {
        var items = SideTree.Items(tab.Snapshot, System.Collections.Immutable.ImmutableHashSet<string>.Empty, "");
        return tab with
        {
            Filter = "",
            Collapsed = tab.Folders.Union(items.Where(item => !item.Leaf).Select(item => item.Id)),
            RevealSidebar = false
        };
    }
    static RepoTab FocusCurrentBranch(RepoTab tab)
    {
        var current = tab.Snapshot.Refs.FirstOrDefault(r => r.Kind == "branch" && r.Current);
        if (current is null)
            return tab;
        var collapsed = tab.Folders.Remove("section:Branches");
        for (var slash = current.Name.IndexOf('/'); slash >= 0; slash = current.Name.IndexOf('/', slash + 1))
            collapsed = collapsed.Remove("folder:heads/" + current.Name[..slash]);
        return tab with
        {
            Filter = "",
            Collapsed = collapsed,
            SideSelected = "branch:" + current.Name,
            RevealSidebar = true
        };
    }
}
