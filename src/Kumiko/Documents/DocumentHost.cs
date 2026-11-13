using Kumiko.Runtime;

namespace Kumiko.Documents;

/// <summary>Host-only shell wiring. The immutable workspace owns every quit decision; this adapter only dispatches.</summary>
public static class DocumentHost
{
    /// <summary>Prevents native quit from closing documents individually. Dispatches one coordinated quit message.
    /// Once Update removes all document windows, the existing window runtime requests quit again and it proceeds.</summary>
    public static IDisposable BindQuit<TModel, TMessage, T>(App app, Program<TModel, TMessage> program,
        Func<TModel, DocumentWorkspace<T>> workspace, TMessage requestQuit) where TModel : notnull where TMessage : notnull where T : notnull
    {
        app.CheckThread();
        return new QuitBinding<TModel, TMessage, T>(app, program, workspace, requestQuit);
    }
    sealed class QuitBinding<TModel, TMessage, T> : IDisposable where TModel : notnull where TMessage : notnull where T : notnull
    {
        readonly App app;
        readonly Program<TModel, TMessage> program;
        readonly Func<TModel, DocumentWorkspace<T>> workspace;
        readonly TMessage request;
        bool disposed;
        internal QuitBinding(App app, Program<TModel, TMessage> program, Func<TModel, DocumentWorkspace<T>> workspace, TMessage request)
        {
            this.app = app;
            this.program = program;
            this.workspace = workspace;
            this.request = request;
            app.BeforeQuit += BeforeQuit;
            app.Disposing += Dispose;
            program.Disposing += Dispose;
        }
        void BeforeQuit(CancelableEvent quit)
        {
            if (disposed || workspace(program.Model).Documents.Count == 0)
                return;
            quit.PreventDefault();
            program.Send(request);
        }
        public void Dispose()
        {
            app.CheckThread();
            if (disposed)
                return;
            disposed = true;
            app.BeforeQuit -= BeforeQuit;
            app.Disposing -= Dispose;
            program.Disposing -= Dispose;
        }
    }
}
