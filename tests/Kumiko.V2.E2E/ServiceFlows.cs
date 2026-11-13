using System.Collections.Concurrent;
using Kumiko;
using Kumiko.Hosting;
using Kumiko.Runtime;
using Kumiko.UI;
using Microsoft.Extensions.DependencyInjection;

static class ServiceFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        try
        {
            using var missing = StorageApp.CreateProgram();
            throw new Exception("Missing dependency was accepted");
        }
        catch (InvalidOperationException error)
        {
            check(error.Message.Contains(nameof(IStore), StringComparison.Ordinal), "startup names missing effect services");
        }
        var store = new MemoryStore();
        var services = new KumikoServices().Add<IStore>(store);
        using var program = StorageApp.CreateProgram(StorageApp.Init, services);
        using var tester = new Tester(program, new(Width: 360, Height: 200, DevMode: true));
        tester.Context.ReduceMotion = true;
        program.DevMode = true;
        void Idle()
        {
            tester.Frame();
            if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Storage command did not finish");
            tester.Settle();
        }
        tester.Get(Role.Button, "Save first").Click();
        program.Send(new StorageMsg.Edit("second"));
        tester.Frame();
        program.Send(new StorageMsg.Edit("latest"));
        Idle();
        check(store.Saves.SequenceEqual(["latest"]) && tester.HasText("Saved: latest"), "mapped debounced saves use the injected service once and display the result");
        check(store.Watchers == 1, "a service subscription starts once through same-frame rebuilds");
        store.Publish("external");
        tester.Settle();
        check(tester.HasText("External: external"), "service subscription results reach the UI-thread model");
        program.Send(new StorageMsg.Watch(false));
        tester.Settle();
        store.Publish("ignored");
        tester.Settle();
        check(store.Watchers == 0 && program.Model.External == "external", "removing a subscription disposes its service handle and rejects stale delivery");
        var session = Path.Combine(output, "services-session.jsonl");
        program.Save(session);
        tester.Screenshot(Path.Combine(output, "services.png"));
        File.WriteAllText(Path.Combine(output, "services.txt"), tester.Dump());
        var replayStore = new MemoryStore();
        using var replay = StorageApp.CreateProgram(StorageApp.Init, new KumikoServices().Add<IStore>(replayStore));
        replay.Replay(session);
        using var replayTester = new Tester(replay, new(Width: 360, Height: 200));
        replayTester.Settle();
        check(replay.Model == program.Model && replayStore.Saves.IsEmpty && replayStore.Watchers == 0, "replay reconstructs storage UI without executing effects or subscriptions");
        replayTester.Screenshot(Path.Combine(output, "services-replay.png"));
        using var restarted = StorageApp.CreateProgram(StorageApp.Init, new KumikoServices().Add<IStore>(store));
        using var restartTester = new Tester(restarted, new(Width: 360, Height: 200));
        restartTester.Get(Role.Button, "Load").Click();
        if (!SpinWait.SpinUntil(() => restarted.PendingCommands == 0, TimeSpan.FromSeconds(5)))
            throw new TimeoutException();
        restartTester.Settle();
        check(restartTester.HasText("Saved: latest"), "a new program loads the previously saved data through its service");
        var collection = new ServiceCollection();
        collection.AddSingleton<IStore>(store);
        collection.AddKumiko(new AppOptions());
        using var provider = collection.BuildServiceProvider();
        using var hosted = StorageApp.CreateProgram(StorageApp.Init, provider.GetRequiredService<KumikoServices>());
        using var hostTester = new Tester(hosted, new(Width: 360, Height: 200));
        hostTester.Get(Role.Button, "Load").Click();
        if (!SpinWait.SpinUntil(() => hosted.PendingCommands == 0, TimeSpan.FromSeconds(5)))
            throw new TimeoutException();
        hostTester.Settle();
        check(hostTester.HasText("Saved: latest"), "Generic Host services reach commands without constructing a native window");
    }
}

internal interface IStore
{
    Task<string> Save(string text, CancellationToken token);
    Task<string> Load(CancellationToken token);
    IDisposable Watch(Action<string> receive);
}

internal sealed class MemoryStore : IStore
{
    public ConcurrentQueue<string> Saves { get; } = new();
    string saved = "";
    Action<string>? receive;
    public int Watchers
    {
        get; private set;
    }
    public Task<string> Save(string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        saved = text;
        Saves.Enqueue(text);
        return Task.FromResult(text);
    }
    public Task<string> Load(CancellationToken token) => Task.FromResult(saved);
    public void Publish(string text) => receive?.Invoke(text);
    public IDisposable Watch(Action<string> callback)
    {
        receive += callback;
        Watchers++;
        return new Handle(() => { receive -= callback; Watchers--; });
    }
    sealed class Handle(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

internal sealed record StorageModel(string Saved = "", string External = "", bool Watching = true);
internal abstract partial record StorageMsg
{
    public sealed record Edit(string Text) : StorageMsg;
    public sealed record Saved(string Text) : StorageMsg;
    public sealed record External(string Text) : StorageMsg;
    public sealed record Watch(bool Enabled) : StorageMsg;
    public sealed record Load : StorageMsg;
    public sealed record Opaque : StorageMsg;
}
internal sealed partial class StorageApp : IKumiko<StorageModel, StorageMsg>
{
    public static Kumiko.Runtime.NavigationRoot<StorageMsg> Root(StorageModel model) => Kumiko.Runtime.NavigationRoot<StorageMsg>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new StorageView(model).Map<StorageMsg>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static StorageModel Init => new();
    public static (StorageModel, Cmd<StorageMsg>) Update(StorageModel model, StorageMsg message) => message switch
    {
        StorageMsg.Edit edit => (model, Save(edit.Text).Map<StorageMsg>(static text => new StorageMsg.Saved(text))),
        StorageMsg.Saved saved => (model with { Saved = saved.Text }, Cmd<StorageMsg>.None),
        StorageMsg.External external => (model with { External = external.Text }, Cmd<StorageMsg>.None),
        StorageMsg.Watch watch => (model with { Watching = watch.Enabled }, Cmd<StorageMsg>.None),
        StorageMsg.Opaque => (model, DiEffects.Load<IOpaqueStore>()),
        StorageMsg.Load => (model, Cmd<StorageMsg>.Run<IStore>("load", async (store, token) => new StorageMsg.Saved(await store.Load(token)), static e => new StorageMsg.Saved(e.Message))),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };
    static Cmd<string> Save(string text) => Cmd<string>.Debounce<IStore>("save", TimeSpan.FromMilliseconds(150),
        (store, token) => store.Save(text, token), static e => e.Message);
    public static IEnumerable<Sub<StorageMsg>> Subscriptions(StorageModel model) => model.Watching
        ? [Sub<StorageMsg>.From<IStore>("watch", static (store, send) => store.Watch(text => send(new StorageMsg.External(text))))] : [];
}
internal sealed class StorageView(StorageModel model) : Kumiko.Runtime.Screen<StorageModel, StorageMsg>(model)
{
    protected override void Render()
    {
        using var page = C.Div(key: "storage").SizeFull().Padding(16).Gap(12);
        Ui.Button("Save first", new StorageMsg.Edit("first"), key: "save");
        Ui.Button("Load", new StorageMsg.Load(), key: "load");
        C.Text("Saved: " + Model.Saved, key: "saved");
        C.Text("External: " + Model.External, key: "external");
    }
}
