using Kumiko.Runtime;

internal sealed partial class CapWordsApp
{
    public static IEnumerable<Sub<CapWordsMessage>> Subscriptions(CapWordsModel model) =>
        model.Words.IsDefaultOrEmpty && model.Error is null
            ? [Sub<CapWordsMessage>.Run("word-artwork", _ => Task.FromResult<CapWordsMessage>(new CapWordsMessage.Loaded(LoadWords())),
                error => new CapWordsMessage.LoadFailed(error.Message))]
            : [];
}
