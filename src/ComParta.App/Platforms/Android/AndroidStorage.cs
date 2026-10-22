using System.Collections.Concurrent;
using Android.Content;
using Android.Provider;
using ComParta.Shell;
using Microsoft.Win32.SafeHandles;
using Ports = ComParta.Core.Ports;
using AndroidUri = Android.Net.Uri;

namespace ComParta.App;

/// <summary>
/// What the person shares from a phone (epic 7 story 2, AD-20): items the system picker handed over,
/// kept as the reference it gave (<c>content://…</c>) with a read permission that lasts across
/// restarts, and read with the phone's own content resolver. Everything else, such as the app's own
/// received files, is an ordinary file and goes to <see cref="LocalFileSystem"/>.
/// </summary>
public sealed class AndroidStorage : Ports.IFileSystem, Ports.IContentSource
{
	private const string FolderType = "vnd.android.document/directory";

	private readonly LocalFileSystem _disk = new();
	// The reference of each file found in a picked folder, by the folder and its place in it.
	private readonly ConcurrentDictionary<(string Folder, string Relative), string> _found = new();

	private static ContentResolver Resolver => Android.App.Application.Context.ContentResolver!;

	private static bool IsPicked(string item) => item.StartsWith("content://", StringComparison.Ordinal);

	public bool FileExists(string path) => IsPicked(path) ? Exists(path) : _disk.FileExists(path);

	public bool Exists(string contentRef)
	{
		if (!IsPicked(contentRef))
			return _disk.FileExists(contentRef);
		try
		{
			using var cursor = Resolver.Query(AndroidUri.Parse(contentRef)!, [DocumentsContract.Document.ColumnMimeType], null, null, null);
			return cursor is not null && cursor.MoveToFirst() && cursor.GetString(0) != FolderType;
		}
		catch (Java.Lang.Exception)
		{
			return false;
		}
	}

	public long GetReadableFileSize(string path) => IsPicked(path) ? Length(path) : _disk.GetReadableFileSize(path);

	public long Length(string contentRef)
	{
		if (!IsPicked(contentRef))
			return _disk.GetReadableFileSize(contentRef);
		var size = Ask(() =>
		{
			using var cursor = Resolver.Query(AndroidUri.Parse(contentRef)!, [SizeColumn], null, null, null);
			return cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0) ? cursor.GetLong(0) : -1;
		});
		if (size >= 0)
			return size;
		// Some apps do not tell the size; the open file does.
		using var stream = OpenRead(contentRef);
		return stream.Length;
	}

	public string FullName(string item) => IsPicked(item) ? item : Path.TrimEndingDirectorySeparator(Path.GetFullPath(item));

	public string NameOf(string item)
	{
		if (!IsPicked(item))
			return Path.GetFileName(Path.TrimEndingDirectorySeparator(item));
		var uri = AndroidUri.Parse(item)!;
		// A picked folder is named by the folder at its top.
		if (DocumentsContract.IsTreeUri(uri))
			uri = DocumentsContract.BuildDocumentUriUsingTree(uri, DocumentsContract.GetTreeDocumentId(uri))!;
		return Ask(() =>
		{
			using var cursor = Resolver.Query(uri, [DisplayNameColumn], null, null, null);
			return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) ?? "" : "";
		});
	}

	public Stream OpenRead(string path)
	{
		if (!IsPicked(path))
			return _disk.OpenRead(path);
		// A file the engine can read anywhere in, not a stream that only goes forward.
		var descriptor = Ask(() => Resolver.OpenFileDescriptor(AndroidUri.Parse(path)!, "r"))
			?? throw new IOException("The item cannot be read.");
		var stream = new FileStream(new SafeFileHandle((IntPtr)descriptor.DetachFd(), ownsHandle: true), FileAccess.Read);
		descriptor.Dispose();
		if (stream.CanSeek)
			return stream;
		stream.Dispose();
		throw new IOException("The item can only be read from start to end.");
	}

	public bool FolderExists(string path) =>
		IsPicked(path) ? DocumentsContract.IsTreeUri(AndroidUri.Parse(path)!) : _disk.FolderExists(path);

	public IReadOnlyList<string> ListFiles(string folder)
	{
		if (!IsPicked(folder))
			return _disk.ListFiles(folder);
		var tree = AndroidUri.Parse(folder)!;
		var files = new List<string>();
		Ask(() =>
		{
			Walk(folder, tree, DocumentsContract.GetTreeDocumentId(tree)!, "", files);
			return true;
		});
		return [.. files.Order(StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>Every file in the folder and the folders inside it, leaving out hidden ones as on a computer.</summary>
	private void Walk(string folder, AndroidUri tree, string documentId, string prefix, List<string> files)
	{
		var children = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, documentId)!;
		string[] columns = [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType];
		using var cursor = Resolver.Query(children, columns, null, null, null);
		while (cursor is not null && cursor.MoveToNext())
		{
			var id = cursor.GetString(0);
			var name = cursor.GetString(1);
			if (id is null || string.IsNullOrEmpty(name) || name.StartsWith('.'))
				continue;
			var relative = prefix + name;
			if (cursor.GetString(2) == FolderType)
			{
				Walk(folder, tree, id, relative + "/", files);
				continue;
			}
			_found[(folder, relative)] = DocumentsContract.BuildDocumentUriUsingTree(tree, id)!.ToString()!;
			files.Add(relative);
		}
	}

	public string? Inside(string folder, string relative)
	{
		if (!IsPicked(folder))
		{
			var path = Path.GetFullPath(Path.Combine(folder, relative));
			return path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
		}
		return _found.GetValueOrDefault((folder, relative));
	}

	public byte[] ReadSmallFile(string path, int maxBytes)
	{
		if (!IsPicked(path))
			return _disk.ReadSmallFile(path, maxBytes);
		using var stream = OpenRead(path);
		if (stream.Length > maxBytes)
			throw new IOException("The file is larger than allowed.");
		var content = new byte[stream.Length];
		stream.ReadExactly(content);
		return content;
	}

	public void DeleteFile(string path) => _disk.DeleteFile(path);

	public void WriteNewFile(string path, byte[] content) => _disk.WriteNewFile(path, content);

	// OpenableColumns' names, as every app that hands over items answers them.
	private const string DisplayNameColumn = "_display_name";
	private const string SizeColumn = "_size";

	/// <summary>
	/// Asks the phone; a permission taken back or an item gone becomes the errors core knows, so the
	/// person is told the file cannot be read.
	/// </summary>
	private static T Ask<T>(Func<T> ask)
	{
		try
		{
			return ask();
		}
		catch (Java.Lang.SecurityException e)
		{
			throw new UnauthorizedAccessException("No permission to read the item.", e);
		}
		catch (Java.Lang.Exception e)
		{
			throw new IOException($"The item cannot be read ({e.GetType().Name}).", e);
		}
	}
}
