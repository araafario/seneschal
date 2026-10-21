using System.Diagnostics;
using System.Text;

namespace Seneschal.Shell;

/// <summary>
/// The Windows folder picker (the real Explorer dialog). Seneschal runs on the user's own machine, so a dialog started here
/// opens in front of the browser. Returns null when the user cancels or the dialog cannot be shown, so the caller can fall back.
/// </summary>
internal static class NativePicker
{
    public static bool Available => OperatingSystem.IsWindows() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SENESCHAL_NO_SHELL"));

    public static Task<string?> PickFolderAsync(string start, CancellationToken cancel = default) => RunAsync(FolderScript, start, cancel);

    private static async Task<string?> RunAsync(string script, string start, CancellationToken cancel)
    {
        if (!Available) return null;
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-STA", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(arg);
        info.Environment["SENESCHAL_PICK_START"] = start;
        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;
            await using var _ = cancel.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var output = await process.StandardOutput.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);
            var path = output.Trim();
            return process.ExitCode == 0 && path.Length > 0 ? path : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    private const string Common = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        $start = $env:SENESCHAL_PICK_START
        if (-not $start -or -not (Test-Path -LiteralPath $start)) { $start = [Environment]::GetFolderPath('UserProfile') }
        if (Test-Path -LiteralPath $start -PathType Leaf) { $start = Split-Path -Parent $start }
        """;

    // The Explorer folder picker (IFileOpenDialog with FOS_PICKFOLDERS), parented to the window in front so it is not hidden behind the browser
    private const string FolderScript = Common + """

        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        public static class FolderPick {
            [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class Dialog {}
            [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IShellItem {
                void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
                void GetParent(out IShellItem ppsi);
                void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string name);
                void GetAttributes(uint mask, out uint attrs);
                void Compare(IShellItem psi, uint hint, out int order);
            }
            [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            interface IFileDialog {
                [PreserveSig] int Show(IntPtr parent);
                void SetFileTypes(uint count, IntPtr types);
                void SetFileTypeIndex(uint index);
                void GetFileTypeIndex(out uint index);
                void Advise(IntPtr events, out uint cookie);
                void Unadvise(uint cookie);
                void SetOptions(uint options);
                void GetOptions(out uint options);
                void SetDefaultFolder(IShellItem item);
                void SetFolder(IShellItem item);
                void GetFolder(out IShellItem item);
                void GetCurrentSelection(out IShellItem item);
                void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
                void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
                void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
                void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
                void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
                void GetResult(out IShellItem item);
            }
            [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
            static extern void SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, out IShellItem item);
            [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
            public static string Pick(string start) {
                var dialog = (IFileDialog)new Dialog();
                uint options; dialog.GetOptions(out options);
                dialog.SetOptions(options | 0x20 | 0x40 | 0x800); // pick folders, file system only, path must exist
                dialog.SetTitle("Choose a folder");
                var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                IShellItem folder;
                SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out folder);
                dialog.SetFolder(folder);
                if (dialog.Show(GetForegroundWindow()) != 0) return null;
                IShellItem result; dialog.GetResult(out result);
                string path; result.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
                return path;
            }
        }
        '@
        $picked = [FolderPick]::Pick($start)
        if ($picked) { [Console]::Out.Write($picked) } else { exit 1 }
        """;}
