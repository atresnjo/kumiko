using Kumiko;
using Kumiko.Runtime;
using SharedWindows;

// Memory by default. Disk persistence requires an explicitly selected file.
IWorkspaceStore store = Dev.Option(args, "--document") is { } path
    ? new FileWorkspaceStore(Path.GetFullPath(path)) : new MemoryWorkspaceStore();
using var program = WorkspaceApp.CreateProgram(new KumikoServices().Add<IWorkspaceStore>(store));
if (Dev.Option(args, "--document") is not null)
    program.Send(new WorkspaceMessage.Load());
Dev.Run("Shared document", 620, 380, program, args);
