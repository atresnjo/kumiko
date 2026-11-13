using System.Collections.Concurrent;
using Kumiko.Runtime;
using Kumiko.UI;
using Launcher;

sealed class Session : IDisposable
{
    readonly ConcurrentQueue<Action> queue = new();
    bool disposed;
    readonly AutoResetEvent wake = new(false);
    public Program<LauncherModel, LauncherMsg> Program
    {
        get;
    }
    public Tester Tester
    {
        get;
    }
    public FixtureLauncherDesktop Desktop
    {
        get;
    }
    public LauncherController Host
    {
        get;
    }
    public int Deliveries
    {
        get; private set;
    }
    public void DrainCallbacks()
    {
        while (queue.TryDequeue(out var action))
        {
            Deliveries++;
            action();
        }
    }
    public LauncherModel Model => Program.Model;
    public Session(FixtureLauncherDesktop? desktop = null, Theme? theme = null)
    {
        Desktop = desktop ?? new();
        Program = LauncherApp.CreateProgram();
        Tester = new(Program, new(680, 520, 1, theme, true, new(_ => null)));
        Tester.Context.ReduceMotion = true;
        Host = new(Program, Desktop, action =>
        {
            if (Volatile.Read(ref disposed))
                return;
            queue.Enqueue(action);
            try
            {
                wake.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        Host.Start();
        Pump();
    }
    public void Pump()
    {
        for (var i = 0; i < 16; i++)
        {
            DrainCallbacks();
            Tester.Settle();
            if (queue.IsEmpty)
                return;
        }
        throw new InvalidOperationException("Fixture failed to settle.");
    }
    public void Wait(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            Pump();
            if (predicate())
                return;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !wake.WaitOne(remaining))
                throw new TimeoutException("Launcher did not deliver its asynchronous result.");
        }
    }
    public void Key(Key key, Modifiers modifiers = Modifiers.None)
    {
        Tester.Key(key, modifiers);
        Pump();
    }
    public void Click(string name)
    {
        Tester.Get(Role.Button, name).Click();
        Pump();
    }
    public void Query(string query)
    {
        Tester.Get(Role.TextField, "Search applications").Fill(query);
        Pump();
    }
    public void Scene(string output, string name)
    {
        Pump();
        Tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), Tester.Dump());
        Tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
    }
    public void Dispose()
    {
        if (disposed)
            return;
        Volatile.Write(ref disposed, true);
        Host.Dispose();
        Tester.Dispose();
        Program.Dispose();
        wake.Dispose();
    }
}
