using Flint.Git;

namespace Flint.Services;

public sealed partial class GitService
{
    public async Task<string> RemoteUrl(string path, string remote, CancellationToken token)
    {
        ValidateRevision(remote);
        return (await Git(path, token, "remote", "get-url", "--", remote).ConfigureAwait(false)).TrimEnd('\r', '\n');
    }
    async Task<string> ConfigureRemote(string path, GitRequest request, CancellationToken token)
    {
        ValidateRevision(request.Value);
        switch (request.Action)
        {
            case GitAction.AddRemote:
                await ValidateRemoteName(path, request.Value, token).ConfigureAwait(false);
                ValidateUrl(request.Body);
                await Git(path, token, "remote", "add", "--", request.Value, request.Body).ConfigureAwait(false);
                return "Added remote " + request.Value;
            case GitAction.SetRemoteUrl:
                ValidateUrl(request.Body);
                await Git(path, token, "remote", "set-url", "--", request.Value, request.Body).ConfigureAwait(false);
                return "Updated URL for " + request.Value;
            case GitAction.RenameRemote:
                await ValidateRemoteName(path, request.Body, token).ConfigureAwait(false);
                await Git(path, token, "remote", "rename", "--", request.Value, request.Body).ConfigureAwait(false);
                return "Renamed " + request.Value + " to " + request.Body;
            case GitAction.RemoveRemote:
                await Git(path, token, "remote", "remove", "--", request.Value).ConfigureAwait(false);
                return "Removed remote " + request.Value;
            case GitAction.SetUpstream:
                ValidateRevision(request.Body);
                await Git(path, token, "show-ref", "--verify", "--", "refs/remotes/" + request.Body).ConfigureAwait(false);
                await Git(path, token, "branch", "--set-upstream-to=refs/remotes/" + request.Body, "--", request.Value).ConfigureAwait(false);
                return request.Value + " now tracks " + request.Body;
            case GitAction.RemoveUpstream:
                await Git(path, token, "branch", "--unset-upstream", "--", request.Value).ConfigureAwait(false);
                return "Removed upstream for " + request.Value;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }
    async Task ValidateRemoteName(string path, string name, CancellationToken token)
    {
        ValidateRevision(name);
        await Git(path, token, "check-ref-format", "refs/remotes/" + name + "/branch").ConfigureAwait(false);
    }
    static void ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url[0] == '-' || url.Any(char.IsControl))
            throw new ArgumentException("Enter a remote URL or repository path.");
    }
}
