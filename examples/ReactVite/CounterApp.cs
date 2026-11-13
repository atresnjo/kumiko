using Kumiko.Runtime;

namespace ReactVite;

public enum CounterRoute
{
    Shell
}

/// <summary>The count is native state. React reads it through a stream and changes it through a typed call.</summary>
public sealed record CounterModel(NavigationHistory<CounterRoute> Navigation, int Count = 0, string LastSource = "nobody yet");
public abstract partial record CounterMsg
{
    public sealed record Add(int By, string Source) : CounterMsg;
    public sealed record Reset : CounterMsg;
    public sealed record Navigate(HistoryRequest Request) : CounterMsg;
}

public sealed partial class CounterApp : IKumiko<CounterModel, CounterMsg>
{
    public const int MaxStep = 10;
    public static CounterModel Init => new(NavigationHistory<CounterRoute>.Start(CounterRoute.Shell));
    public static NavigationRoot<CounterMsg> Root(CounterModel model) => NavigationRoot<CounterMsg>.Create(model.Navigation, "app",
        _ => new ShellScreen(new(model.Count, model.LastSource)).Map<CounterMsg>(static message => message),
        static request => new CounterMsg.Navigate(request));

    public static (CounterModel, Cmd<CounterMsg>) Update(CounterModel model, CounterMsg message) => (message switch
    {
        CounterMsg.Add add when add.By is > 0 and <= MaxStep => model with
        {
            Count = model.Count + add.By,
            LastSource = add.Source
        },
        CounterMsg.Add => model,
        CounterMsg.Reset => model with
        {
            Count = 0,
            LastSource = "Kumiko"
        },
        CounterMsg.Navigate => model,
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<CounterMsg>.None);
}
