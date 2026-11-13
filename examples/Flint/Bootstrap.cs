using Flint.Services;

namespace Flint;

/// <summary>Prepare a deterministic offscreen initial model before creating any UI or recording.</summary>
static class Bootstrap
{
    public static FlintModel Repository(FlintModel model, IGitService git, string path)
    {
        try
        {
            var snapshot = git.Load(path, 4000, CancellationToken.None).GetAwaiter().GetResult();
            var tab = new RepoTab(snapshot);
            if (snapshot.Commits.Length > 0)
            {
                var detail = git.Detail(snapshot.Path, snapshot.Commits[0].Sha, CancellationToken.None).GetAwaiter().GetResult();
                var patch = git.Patch(snapshot.Path, detail, null, false, CancellationToken.None).GetAwaiter().GetResult();
                tab = tab with
                {
                    Sha = detail.Sha,
                    Detail = detail,
                    Patch = patch,
                    Rows = Diff.Parse(patch)
                };
            }
            var workspace = model.Workspace with
            {
                Repositories = [snapshot.Path],
                Active = snapshot.Path
            };
            return model with
            {
                Workspaces = model.Workspaces.Replace(model.Workspace, workspace),
                Repositories = model.Repositories.SetItem(snapshot.Path, tab),
                Loads = model.Generations.SetItem(snapshot.Path, 0)
            };
        }
        catch (Exception error) when (error is IOException or ArgumentException)
        {
            return model with
            {
                Error = error.Message
            };
        }
    }
}
