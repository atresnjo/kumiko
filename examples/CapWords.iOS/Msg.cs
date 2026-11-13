internal abstract partial record CapWordsMessage
{
    public sealed record Loaded(System.Collections.Immutable.ImmutableArray<Word> Words) : CapWordsMessage;
    public sealed record LoadFailed(string Error) : CapWordsMessage;
    public sealed record Open(string Id) : CapWordsMessage;
    public sealed record Gallery : CapWordsMessage;
    public sealed record Back : CapWordsMessage;
    public sealed record Notice(string Text) : CapWordsMessage;
    public sealed record DismissNotice : CapWordsMessage;
    public sealed record Navigate(Kumiko.Runtime.HistoryRequest Request) : CapWordsMessage;
}
