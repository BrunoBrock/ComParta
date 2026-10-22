using ComParta.Core.Ports;

namespace ComParta.Engine.MonoTorrent;

/// <summary>Shared files that are files on this computer's disks: a content reference is the full path.</summary>
internal sealed class FileContent : IContentSource
{
    public bool Exists(string contentRef) => File.Exists(contentRef);

    public long Length(string contentRef) => new FileInfo(contentRef).Length;

    public string NameOf(string contentRef) => Path.GetFileName(contentRef);

    // Others may still read, write or delete the file meanwhile; a change shows when it is checked.
    public Stream OpenRead(string contentRef) =>
        new FileStream(contentRef, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
}
