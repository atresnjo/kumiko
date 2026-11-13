using System.Collections.Immutable;
using Kumiko.Runtime;

namespace Flint;

public sealed partial class FlintApp
{
    /// <summary>
    /// The original's startup order: the active repository first, then the rest of the active workspace, then other
    /// workspaces once the UI is idle. Every repository loads independently with its own generation.
    /// </summary>
    static (FlintModel, Cmd<Msg>) Start(FlintModel model)
    {
        var commands = ImmutableArray.CreateBuilder<Cmd<Msg>>();
        var active = model.Workspace.Active;
        var seen = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (active is not null && seen.Add(active))
        {
            (model, var first) = Load(model, active, 4000, TimeSpan.Zero, "warm:");
            commands.Add(first);
        }
        foreach (var path in model.Workspace.Repositories)
            if (seen.Add(path))
            {
                (model, var load) = Load(model, path, 4000, TimeSpan.FromMilliseconds(50), "warm:");
                commands.Add(load);
            }
        foreach (var path in model.Workspaces.SelectMany(w => w.Repositories))
            if (seen.Add(path))
            {
                (model, var load) = Load(model, path, 4000, TimeSpan.FromMilliseconds(1500), "warm:");
                commands.Add(load);
            }
        commands.Add(Discover(TimeSpan.FromMilliseconds(300)));
        commands.Add(Accounts());
        return (model, Cmd<Msg>.Batch([.. commands]));
    }
}
