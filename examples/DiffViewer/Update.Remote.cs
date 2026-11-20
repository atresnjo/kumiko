using System.Collections.Immutable;
using System.Globalization;
using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;

namespace DiffViewer;

/// <summary>Pull requests and commits for the palette, and opening one of them.</summary>
internal sealed partial class DiffApp
{
    /// <summary>
    /// Opens the palette. The open project's commits are read again each time; pull requests are searched when
    /// none are cached yet or the cached list is stale.
    /// </summary>
    static (Model, Cmd<Msg>) Palette(Model model, string query)
    {
        var next = Quick(model with
        {
            Finder = null,
            Search = null,
            Help = false,
            Scanning = model.Scanning || !model.Scanned && model.Store is not null
        }, query, 0);
        var project = next.ProjectId;
        var place = next.ProjectPlace;
        var commits = next.Request.Kind is SourceKind.Paths or SourceKind.Demo ? Cmd<Msg>.None
            : Cmd<Msg>.Run("commits", async token => (Msg)new Msg.CommitsFound(project, await Commits.Log(place, token).ConfigureAwait(false)),
                _ => new Msg.CommitsFound(project, []));
        var (searched, pulls) = next.Pulls is { Loading: false } list && (!list.Loaded || list.Stale) ? FetchPulls(next) : (next, Cmd<Msg>.None);
        var hosts = next.SshHome is not { } home ? Cmd<Msg>.None
            : Cmd<Msg>.Run("ssh-hosts", _ => Task.Run(() => (Msg)new Msg.HostsFound(SshConfig.Hosts(home))), _ => new Msg.HostsFound([]));
        return (searched, Cmd<Msg>.Batch(commits, pulls, hosts));
    }

    /// <summary>Searches pull requests of every gh account; the cached list stays listed until the result arrives.</summary>
    static (Model, Cmd<Msg>) FetchPulls(Model model)
    {
        if (model.GitHub is not { } config)
            return (model, Cmd<Msg>.None);
        var generation = model.Pulls.Generation + 1;
        var next = Requery(model with
        {
            Pulls = model.Pulls with
            {
                Generation = generation,
                Loading = true,
                Stale = false
            }
        });
        return (next, Cmd<Msg>.Run("pulls", async token =>
        {
            var (pulls, error) = await GitHub.PullRequests(config, token).ConfigureAwait(false);
            return (Msg)new Msg.PullsFound(generation, pulls, error);
        }, error => new Msg.PullsFound(generation, [], error.Message)));
    }

    /// <summary>Keeps a search's result for the configured time; a failed search keeps the previous list.</summary>
    static (Model, Cmd<Msg>) PullsFound(Model model, Msg.PullsFound found)
    {
        if (found.Generation != model.Pulls.Generation || model.GitHub is not { } config)
            return (model, Cmd<Msg>.None);
        var items = found.Pulls.IsEmpty && found.Error is not null ? model.Pulls.Items : found.Pulls;
        // Background loads of pull requests that left the list, or changed since, are dropped.
        var keys = items.SelectMany(pull => new[] { PrefetchKey(pull, false), PrefetchKey(pull, true) }).ToImmutableHashSet(StringComparer.Ordinal);
        var next = Requery(model with
        {
            Prefetched = model.Prefetched.RemoveRange(model.Prefetched.Keys.Where(key => !keys.Contains(key))),
            Pulls = model.Pulls with
            {
                Items = items,
                Loading = false,
                Loaded = true,
                Stale = false,
                Error = found.Error
            }
        });
        var generation = found.Generation;
        var age = config.CacheFor;
        return (next, Cmd<Msg>.Run("pulls:expire", async token =>
        {
            await Task.Delay(age, token).ConfigureAwait(false);
            return (Msg)new Msg.PullsExpired(generation);
        }, _ => new Msg.PullsExpired(generation)));
    }

    /// <summary>An expired list is searched again at once while the palette shows it, otherwise when it next opens.</summary>
    static (Model, Cmd<Msg>) Expired(Model model, Msg.PullsExpired expired)
    {
        if (expired.Generation != model.Pulls.Generation || model.Pulls.Loading)
            return (model, Cmd<Msg>.None);
        var next = model with
        {
            Pulls = model.Pulls with
            {
                Stale = true
            }
        };
        return next.Quick is null ? (next, Cmd<Msg>.None) : FetchPulls(next);
    }

    static Model CommitsFound(Model model, Msg.CommitsFound found) => found.Project != model.ProjectId ? model : Requery(model with
    {
        Commits = found.Commits,
        CommitsOf = found.Project
    });

    /// <summary>
    /// A pull request replaces the diff; the local project stays the palette's current project. One loaded in the
    /// background shows at once. Zen mode reads its file whole, so it always loads.
    /// </summary>
    static Model OpenPull(Model model, PullRequest pull)
    {
        var opened = Open(model, new(SourceKind.PullRequest, model.ProjectPath)
        {
            Pull = pull,
            Host = model.Request.Host
        });
        return opened.Zen is null && model.Prefetched.GetValueOrDefault(PrefetchKey(pull, model.IgnoreWhitespace))?.Document is { } document
            ? Loaded(opened, document) : opened;
    }

    const int PrefetchCount = 5;

    /// <summary>Names a background load: the pull request as last updated, and the whitespace setting it was diffed with.</summary>
    internal static string PrefetchKey(PullRequest pull, bool ignoreWhitespace) =>
        pull.Url + "@" + pull.Updated.UtcTicks.ToString(CultureInfo.InvariantCulture) + (ignoreWhitespace ? ":w" : "");

    /// <summary>
    /// Starts loading the first pull requests in the palette's visible rows that are neither loaded, loading nor
    /// refused, so choosing one shows it without waiting.
    /// </summary>
    static (Model, Cmd<Msg>) Prefetch(Model model)
    {
        if (model.Quick is not { } quick || model.Zen is not null)
            return (model, Cmd<Msg>.None);
        var whitespace = model.IgnoreWhitespace;
        var path = model.ProjectPath;
        var host = model.Request.Host;
        var wanted = quick.Items.Skip(QuickOpen.FirstVisible(quick.Selected, quick.Items.Length)).Take(QuickOpen.Rows)
            .Select(item => item.Message).OfType<Msg.OpenPull>().Take(PrefetchCount).Select(open => open.Pull)
            .Where(pull => !model.Prefetched.ContainsKey(PrefetchKey(pull, whitespace))).ToImmutableArray();
        if (wanted.IsEmpty)
            return (model, Cmd<Msg>.None);
        var loads = wanted.Select(pull =>
        {
            var key = PrefetchKey(pull, whitespace);
            var request = new DiffRequest(SourceKind.PullRequest, path)
            {
                Pull = pull,
                Host = host
            };
            return Cmd<Msg>.Run("prefetch", async token => (Msg)new Msg.PullPrefetched(key, await Loader.Load(request, whitespace, token).ConfigureAwait(false)),
                _ => new Msg.PullPrefetched(key, null));
        }).ToArray();
        return (model with
        {
            Prefetched = wanted.Aggregate(model.Prefetched, (all, pull) => all.SetItem(PrefetchKey(pull, whitespace), new(null, false)))
        }, Cmd<Msg>.Batch(loads));
    }

    /// <summary>Keeps a background load; the pull request opened while it was loading takes it instead of loading twice.</summary>
    static Model Prefetched(Model model, Msg.PullPrefetched done)
    {
        if (!model.Prefetched.ContainsKey(done.Key))
            return model;
        var next = model with
        {
            Prefetched = model.Prefetched.SetItem(done.Key, new(done.Document, done.Document is null))
        };
        return done.Document is { } document && next is { Loading: true, Zen: null, Request.Pull: { } pull } && PrefetchKey(pull, next.IgnoreWhitespace) == done.Key
            ? Loaded(next, document) : next;
    }

    /// <summary>A commit against its first parent, or against the empty tree for a first commit.</summary>
    static Model OpenCommit(Model model, Commit commit) => Open(model, new(SourceKind.Revisions, model.ProjectPath,
        commit.Parent.Length > 0 ? commit.Parent : Loader.EmptyTree, commit.Sha)
    {
        Title = commit.Subject,
        Host = model.Request.Host
    });

    /// <summary>Rebuilds an open palette's rows after its sources change, keeping the highlighted row.</summary>
    static Model Requery(Model model)
    {
        if (model.Quick is not { } quick)
            return model;
        var keep = quick.Selected < quick.Items.Length ? quick.Items[quick.Selected].Id : null;
        return Quick(model, quick.Query, quick.Selected, keep);
    }
}
