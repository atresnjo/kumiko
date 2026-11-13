using Kumiko.Documents;

namespace Kumiko.Runtime;

/// <summary>Service-resolved effects; request/reply identity stays in the application's model/messages.</summary>
public static class DocumentEffects
{
    public static Cmd<TMessage> Save<T, TMessage>(DocumentRequest<T> request, IDocumentCodec<T> codec,
        Func<DocumentSaved<T>, TMessage> saved, Func<Exception, TMessage> failed) where T : notnull =>
        Cmd<TMessage>.Run<IDocumentStore>("document-save:" + request.Id,
            async (store, token) => saved(await store.SaveAsync(request, codec, token).ConfigureAwait(false)), failed);

    /// <summary>Use only after BeginSave. A caller must deliver cancellation as a message before removing Pending.</summary>
    public static Cmd<TMessage> Autosave<T, TMessage>(DocumentRequest<T> request, IDocumentCodec<T> codec, TimeSpan delay,
        Func<DocumentSaved<T>, TMessage> saved, Func<Exception, TMessage> failed) where T : notnull =>
        Cmd<TMessage>.Debounce<IDocumentStore>("document-autosave:" + request.Id, delay,
            async (store, token) => saved(await store.SaveAsync(request, codec, token).ConfigureAwait(false)), failed);

    public static Cmd<TMessage> Open<T, TMessage>(string path, IDocumentCodec<T> codec,
        Func<DocumentRead<T>, TMessage> opened, Func<Exception, TMessage> failed) where T : notnull =>
        Cmd<TMessage>.Run<IDocumentStore>("document-open",
            async (store, token) => opened(await store.ReadAsync(path, codec, token).ConfigureAwait(false)), failed);
}
