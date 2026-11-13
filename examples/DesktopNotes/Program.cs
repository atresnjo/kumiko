using DesktopNotes;
using Kumiko;
using Kumiko.Runtime;

using var program = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(new MemoryNoteStore()));
Dev.Run("Desktop notes", 560, 380, program, args);
