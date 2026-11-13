namespace Kumiko.Runtime;

public sealed partial record Sub<TMessage>
{
    /// <summary>Asynchronous, cancellable work whose lifetime follows a subscription key.</summary>
    public static Sub<TMessage> Run(string key, Func<CancellationToken, Task<TMessage>> work, Func<Exception, TMessage> onError) => new(key, _ => throw new InvalidOperationException("The Kumiko runtime owns subscription execution."))
    {
        CheckedStart = (send, report) =>
        {
            var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            _ = Task.Run(async () =>
            {
                using var scope = KumikoExecution.Enter("subscription");
                try
                {
                    var message = await work(token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested)
                        send(message);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (KumikoViolationException error) { report(error); }
                catch (Exception error)
                {
                    try
                    {
                        if (!token.IsCancellationRequested)
                            send(onError(error));
                    }
                    catch (Exception failure) { report(failure); }
                }
            });
            return new Lifetime(() =>
            {
                cancellation.Cancel();
                cancellation.Dispose();
            });
        }
    };

    /// <summary>Native appearance notifications delivered as messages without polling.</summary>
    public static Sub<TMessage> Appearance(Func<bool, TMessage> message) => new("kumiko:appearance", send =>
    {
        void Changed(bool dark) => send(message(dark));
        Desktop.Appearance.Changed += Changed;
        return new Lifetime(() => Desktop.Appearance.Changed -= Changed);
    });

    sealed class Lifetime(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
