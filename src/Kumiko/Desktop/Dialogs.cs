using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

public sealed record OpenDialogOptions(
    string Title = "Open",
    string Directory = "",
    bool Files = true,
    bool Directories = false,
    bool Multiple = false,
    string[]? Extensions = null,
    bool ShowHidden = false
);
public sealed record SaveDialogOptions(
    string Title = "Save",
    string Directory = "",
    string Name = "",
    string[]? Extensions = null,
    bool ShowHidden = false
);
public sealed record MessageOptions(string Title, string Detail = "", string[]? Buttons = null, bool Warning = false);
public static unsafe class Dialogs
{
    public static string[] Open(OpenDialogOptions? options = null)
    {
        var o = options ?? new();
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var panel = O.O(O.Class("NSOpenPanel"), "openPanel");
            O.V(panel, "setTitle:", O.TempString(o.Title));
            O.V(panel, "setCanChooseFiles:", (byte)(o.Files ? 1 : 0));
            O.V(panel, "setCanChooseDirectories:", (byte)(o.Directories ? 1 : 0));
            O.V(panel, "setAllowsMultipleSelection:", (byte)(o.Multiple ? 1 : 0));
            O.V(panel, "setShowsHiddenFiles:", (byte)(o.ShowHidden ? 1 : 0));
            Configure(panel, o.Directory, o.Extensions);
            if (O.N(panel, "runModal") != 1)
                return [];
            var urls = O.O(panel, "URLs");
            int count = (int)O.N(urls, "count");
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = O.Text(O.O(O.O(urls, "objectAtIndex:", (nuint)i), "path"));
            return result;
        }

        if (OperatingSystem.IsWindows())
            return WindowsFile(false, o.Title, o.Directory, "", o.Extensions, o.Multiple, o.Directories && !o.Files);
        return LinuxFile(false, o.Title, o.Directory, "", o.Extensions, o.Multiple, o.Directories && !o.Files);
    }

    public static string? Save(SaveDialogOptions? options = null)
    {
        var o = options ?? new();
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var panel = O.O(O.Class("NSSavePanel"), "savePanel");
            O.V(panel, "setTitle:", O.TempString(o.Title));
            O.V(panel, "setNameFieldStringValue:", O.TempString(o.Name));
            O.V(panel, "setShowsHiddenFiles:", (byte)(o.ShowHidden ? 1 : 0));
            Configure(panel, o.Directory, o.Extensions);
            return O.N(panel, "runModal") == 1 ? O.Text(O.O(O.O(panel, "URL"), "path")) : null;
        }

        return (OperatingSystem.IsWindows() ? WindowsFile(true, o.Title, o.Directory, o.Name, o.Extensions, false, false) : LinuxFile(true, o.Title, o.Directory, o.Name, o.Extensions, false, false)).FirstOrDefault();
    }

    static void Configure(nint panel, string directory, string[]? extensions)
    {
        if (directory.Length > 0)
            O.V(panel, "setDirectoryURL:", O.O(O.Class("NSURL"), "fileURLWithPath:", O.TempString(directory)));
        if (extensions is not { Length: > 0 })
            return;
        var array = O.New("NSMutableArray");
        foreach (var ext in extensions)
            O.V(array, "addObject:", O.TempString(ext.TrimStart('.')));
        O.V(panel, "setAllowedFileTypes:", array);
        O.Release(array);
    }

    public static int Message(MessageOptions options)
    {
        var buttons = options.Buttons is { Length: > 0 } b ? b : ["OK"];
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var alert = O.New("NSAlert");
            try
            {
                O.V(alert, "setMessageText:", O.TempString(options.Title));
                O.V(alert, "setInformativeText:", O.TempString(options.Detail));
                O.V(alert, "setAlertStyle:", (nuint)(options.Warning ? 0 : 1));
                foreach (var button in buttons)
                    O.O(alert, "addButtonWithTitle:", O.TempString(button));
                return (int)O.N(alert, "runModal") - 1000;
            }
            finally
            {
                O.Release(alert);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var config = new TaskDialogConfig
            {
                Size = (uint)sizeof(TaskDialogConfig),
                Flags = 0x1000,
                Title = Marshal.StringToCoTaskMemUni(options.Title),
                Content = Marshal.StringToCoTaskMemUni(options.Detail),
                DefaultButton = 100
            };
            var labels = new List<nint>();
            var nativeButtons = new TaskDialogButton[buttons.Length];
            try
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    var label = Marshal.StringToCoTaskMemUni(buttons[i]);
                    labels.Add(label);
                    nativeButtons[i] = new()
                    {
                        Id = 100 + i,
                        Text = label
                    };
                }

                fixed (TaskDialogButton* p = nativeButtons)
                {
                    config.ButtonCount = (uint)buttons.Length;
                    config.Buttons = (nint)p;
                    int hr = TaskDialogIndirect(ref config, out var selected, out _, out _);
                    Marshal.ThrowExceptionForHR(hr);
                    return selected - 100;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(config.Title);
                Marshal.FreeCoTaskMem(config.Content);
                foreach (var label in labels)
                    Marshal.FreeCoTaskMem(label);
            }
        }

        var dialog = gtk_message_dialog_new(0, 0, options.Warning ? 1 : 0, 0, "%s", options.Title + "\n\n" + options.Detail);
        for (int i = 0; i < buttons.Length; i++)
            gtk_dialog_add_button(dialog, buttons[i], 100 + i);
        int response = gtk_dialog_run(dialog);
        Gtk.gtk_widget_destroy(dialog);
        return response >= 100 ? response - 100 : -1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenFileName
    {
        public uint Size;
        public nint Owner, Instance, Filter, CustomFilter;
        public uint MaxCustomFilter, FilterIndex;
        public nint File;
        public uint MaxFile;
        public nint FileTitle;
        public uint MaxFileTitle;
        public nint InitialDirectory, Title;
        public uint Flags;
        public ushort FileOffset, Extension;
        public nint DefaultExtension, Data, Hook, Template, Reserved;
        public uint Reserved2, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern int GetOpenFileNameW(ref OpenFileName options);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    static extern int GetSaveFileNameW(ref OpenFileName options);
    [StructLayout(LayoutKind.Sequential)]
    struct BrowseInfo
    {
        public nint Owner, Root, Display, Title;
        public uint Flags;
        public nint Callback, Data;
        public int Image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHBrowseForFolderW(ref BrowseInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetPathFromIDListW(nint pidl, char* path);
    static string[] WindowsFile(bool save, string title, string directory, string name, string[]? extensions, bool multiple, bool directories)
    {
        if (directories)
        {
            char* path = stackalloc char[32768];
            var label = Marshal.StringToCoTaskMemUni(title);
            try
            {
                var options = new BrowseInfo
                {
                    Display = (nint)path,
                    Title = label,
                    Flags = 0x41
                };
                var pidl = SHBrowseForFolderW(ref options);
                if (pidl == 0)
                    return [];
                try
                {
                    return SHGetPathFromIDListW(pidl, path) != 0 ? [new string(path)] : [];
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(label);
            }
        }

        var buffer = new char[32768];
        name.AsSpan(0, Math.Min(name.Length, 32766)).CopyTo(buffer);
        var filter = extensions is { Length: > 0 } ? "Files\0" + string.Join(';', extensions.Select(e => "*." + e.TrimStart('.'))) + "\0All files\0*.*\0\0" : "All files\0*.*\0\0";
        nint f = Marshal.StringToCoTaskMemUni(filter), t = Marshal.StringToCoTaskMemUni(title), d = Marshal.StringToCoTaskMemUni(directory);
        try
        {
            fixed (char* p = buffer)
            {
                var options = new OpenFileName
                {
                    Size = (uint)sizeof(OpenFileName),
                    Filter = f,
                    File = (nint)p,
                    MaxFile = (uint)buffer.Length,
                    Title = t,
                    InitialDirectory = d,
                    Flags = 0x80000 | 8 | 0x800 | (save ? 2u : 0x1000u) | (multiple ? 0x200u : 0)
                };
                if ((save ? GetSaveFileNameW(ref options) : GetOpenFileNameW(ref options)) == 0)
                    return [];
                var parts = new List<string>();
                int start = 0;
                while (start < buffer.Length && buffer[start] != '\0')
                {
                    int end = Array.IndexOf(buffer, '\0', start);
                    parts.Add(new(buffer, start, end - start));
                    start = end + 1;
                }

                return parts.Count <= 1 ? parts.ToArray() : parts.Skip(1).Select(p => Path.Combine(parts[0], p)).ToArray();
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(f);
            Marshal.FreeCoTaskMem(t);
            Marshal.FreeCoTaskMem(d);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct TaskDialogButton
    {
        public int Id;
        public nint Text;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct TaskDialogConfig
    {
        public uint Size;
        public nint Owner, Instance;
        public uint Flags, CommonButtons;
        public nint Title, Icon, Instruction, Content;
        public uint ButtonCount;
        public nint Buttons;
        public int DefaultButton;
        public uint RadioCount;
        public nint Radios;
        public int DefaultRadio;
        public nint Verification, ExpandedInfo, ExpandedControl, CollapsedControl, FooterIcon, Footer, Callback, Data;
        public uint Width;
    }

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode)]
    static extern int TaskDialogIndirect(ref TaskDialogConfig config, out int button, out int radio, out int verification);
    [DllImport(Gtk.Library)]
    static extern nint gtk_file_chooser_dialog_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        nint parent,
        int action,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string cancel,
        int cancelId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string accept,
        int acceptId,
        nint end
    );
    [DllImport(Gtk.Library)]
    static extern int gtk_dialog_run(nint dialog);
    [DllImport(Gtk.Library)]
    static extern nint gtk_dialog_add_button(nint dialog, [MarshalAs(UnmanagedType.LPUTF8Str)] string label, int response);
    [DllImport(Gtk.Library)]
    static extern void gtk_file_chooser_set_select_multiple(nint chooser, int multiple);
    [DllImport(Gtk.Library)]
    static extern void gtk_file_chooser_set_current_name(nint chooser, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Gtk.Library)]
    static extern int gtk_file_chooser_set_current_folder(nint chooser, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Gtk.Library)]
    static extern nint gtk_file_chooser_get_filenames(nint chooser);
    [DllImport(Gtk.GLib)]
    static extern void g_slist_free(nint list);
    [DllImport(Gtk.Library)]
    static extern nint gtk_file_filter_new();
    [DllImport(Gtk.Library)]
    static extern void gtk_file_filter_add_pattern(nint filter, [MarshalAs(UnmanagedType.LPUTF8Str)] string pattern);
    [DllImport(Gtk.Library)]
    static extern void gtk_file_chooser_add_filter(nint chooser, nint filter);
    [DllImport(Gtk.Library)]
    static extern nint gtk_message_dialog_new(
        nint parent,
        int flags,
        int kind,
        int buttons,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string format,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text
    );
    static string[] LinuxFile(bool save, string title, string directory, string name, string[]? extensions, bool multiple, bool directories)
    {
        var dialog = gtk_file_chooser_dialog_new(title, 0, save ? 1 : directories ? 2 : 0, "Cancel", -6, save ? "Save" : "Open", -3, 0);
        try
        {
            gtk_file_chooser_set_select_multiple(dialog, multiple ? 1 : 0);
            if (directory.Length > 0)
                gtk_file_chooser_set_current_folder(dialog, directory);
            if (save)
                gtk_file_chooser_set_current_name(dialog, name);
            if (extensions is { Length: > 0 })
            {
                var filter = gtk_file_filter_new();
                foreach (var ext in extensions)
                    gtk_file_filter_add_pattern(filter, "*." + ext.TrimStart('.'));
                gtk_file_chooser_add_filter(dialog, filter);
            }

            if (gtk_dialog_run(dialog) != -3)
                return [];
            var list = gtk_file_chooser_get_filenames(dialog);
            var result = new List<string>();
            for (var p = list; p != 0; p = Marshal.ReadIntPtr(p, IntPtr.Size))
            {
                var value = Marshal.ReadIntPtr(p);
                result.Add(Marshal.PtrToStringUTF8(value) ?? "");
                Gtk.g_free(value);
            }

            g_slist_free(list);
            return result.ToArray();
        }
        finally
        {
            Gtk.gtk_widget_destroy(dialog);
        }
    }
}
