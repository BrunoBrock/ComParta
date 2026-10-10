using ComParta.Contracts;
using ComParta.Core;
using ComParta.Core.UseCases;
using ComParta.Engine.MonoTorrent;
using ComParta.Storage.Sqlite;

namespace ComParta.App;

/// <summary>The only place adapters are wired to the core's ports.</summary>
public sealed class AppServices
{
	private readonly AppPaths _paths = new();
	private MonoTorrentTransferEngine? _engine;

	private AppServices()
	{
		Router = Task.Run(StartAsync);
	}

	/// <summary>Completes when the app is ready to answer commands.</summary>
	public Task<BridgeRouter> Router { get; }

	public static AppServices Start() => new();

	private async Task<BridgeRouter> StartAsync()
	{
		try
		{
			Directory.CreateDirectory(_paths.ReceivedRoot);
			var store = await SqliteStore.OpenAsync(_paths.DatabasePath);
			_engine = new MonoTorrentTransferEngine(_paths);
			var shares = new ShareUseCases(store, _engine, new NativeFilePicker(), new LocalFileSystem());
			var transfers = new TransferUseCases(store, _engine, picker: new NativeFilePicker(), files: new LocalFileSystem());
			var router = new BridgeRouter(shares, transfers, new NativeClipboard(), new ReceivedFolderOpener(_paths));

			await _engine.StartAsync(SharingPolicy.Default);
			await shares.StartAsync();
			await transfers.StartAsync();
			return router;
		}
		catch (Exception e)
		{
			Log(e);
			throw;
		}
	}

	public void Stop()
	{
		try
		{
			// Lets the engine save where each transfer got to before the process ends.
			_engine?.StopAsync().Wait(TimeSpan.FromSeconds(5));
		}
		catch (Exception e)
		{
			Log(e);
		}
	}

	/// <summary>Local log file only; never file contents (conventions) and nothing is sent anywhere (AD-9).</summary>
	public void Log(Exception e)
	{
		try
		{
			Directory.CreateDirectory(_paths.LogsRoot);
			File.AppendAllText(Path.Combine(_paths.LogsRoot, "comparta.log"), $"{DateTimeOffset.UtcNow:O} {e}{Environment.NewLine}");
		}
		catch (Exception)
		{
			// Nothing sensible is left to do when even the log cannot be written.
		}
	}
}
