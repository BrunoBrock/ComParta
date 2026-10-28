using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ComParta.Core.Ports;

namespace ComParta.Shell;

/// <summary>
/// Hands a received file to Windows' attachment services (AD-13), as browsers do with downloads:
/// Windows writes the "came from another computer" mark on it and the installed antivirus looks at
/// it. The other person's address is never given to Windows; the file is said to come from the
/// internet, which is where another person's computer is.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AttachmentGuard : IReceivedFileGuard
{
    // Names ComParta to Windows, so what Windows keeps per program stays ComParta's own.
    private static readonly Guid ClientGuid = new("6f1c2b7e-3d4a-4c8e-9b57-2e0d5a9c41f3");
    // A source that is not an address could be read as a place on this computer, and then no mark is written.
    private const string Internet = "about:internet";

    // What Save answers when the file was refused (the answers browsers treat as blocked).
    private const int SecurityProblem = unchecked((int)0x800C000E);
    private const int Failed = unchecked((int)0x80004005);
    private const int VirusInfected = unchecked((int)0x800700E1);
    private const int VirusDeleted = unchecked((int)0x800700E2);

    public ReceivedFileVerdict Check(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
            return ReceivedFileVerdict.Passed;
        // Handed a whole folder: each file inside it is looked at, and all of them carry the mark.
        if (Directory.Exists(fullPath))
        {
            var folder = ReceivedFileVerdict.Passed;
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                if (CheckFile(file) == ReceivedFileVerdict.Blocked)
                    folder = ReceivedFileVerdict.Blocked;
            }
            return folder;
        }
        return CheckFile(fullPath);
    }

    private static ReceivedFileVerdict CheckFile(string fullPath)
    {
        var verdict = ReceivedFileVerdict.Passed;
        // The attachment services want a single-threaded apartment; core calls from a pool thread.
        var thread = new Thread(() => verdict = CheckHere(fullPath)) { IsBackground = true, Name = "Received file check" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return verdict;
    }

    private static ReceivedFileVerdict CheckHere(string fullPath)
    {
        int saved;
        IAttachmentExecute? attachment = null;
        try
        {
            attachment = (IAttachmentExecute)new AttachmentServices();
            var client = ClientGuid;
            attachment.SetClientGuid(ref client);
            attachment.SetClientTitle("ComParta");
            attachment.SetLocalPath(fullPath);
            attachment.SetSource(Internet);
            saved = attachment.Save();
        }
        catch (Exception)
        {
            // Windows' check could not run at all: the file stays unmarked, as before this check
            // existed, rather than being refused for a fault that is not the file's.
            return ReceivedFileVerdict.Passed;
        }
        finally
        {
            if (attachment is not null)
                Marshal.FinalReleaseComObject(attachment);
        }

        if (saved is SecurityProblem or Failed or VirusInfected or VirusDeleted)
            return ReceivedFileVerdict.Blocked;
        // The antivirus may take the file away without saying so.
        if (!File.Exists(fullPath))
            return ReceivedFileVerdict.Blocked;
        // Any other answer leaves the file as it was before this check existed.
        return ReceivedFileVerdict.Passed;
    }

    [ComImport, Guid("4125dd96-e03a-4103-8f70-e0597d803b9c")]
    private class AttachmentServices;

    /// <summary>Windows' IAttachmentExecute, in its declared order (shobjidl_core.h).</summary>
    [ComImport, Guid("73db1241-1e85-4581-8e4f-a81e1d0f8c57"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAttachmentExecute
    {
        [PreserveSig] int SetClientTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        [PreserveSig] int SetClientGuid(ref Guid guid);
        [PreserveSig] int SetLocalPath([MarshalAs(UnmanagedType.LPWStr)] string localPath);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        [PreserveSig] int SetSource([MarshalAs(UnmanagedType.LPWStr)] string source);
        [PreserveSig] int SetReferrer([MarshalAs(UnmanagedType.LPWStr)] string referrer);
        [PreserveSig] int CheckPolicy();
        [PreserveSig] int Prompt(IntPtr hwnd, int prompt, out int action);
        [PreserveSig] int Save();
        // Declared only to keep the order; ComParta never runs a received file.
        [PreserveSig] int Execute(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string verb, IntPtr process);
        [PreserveSig] int SaveWithUI(IntPtr hwnd);
        [PreserveSig] int ClearClientState();
    }
}
