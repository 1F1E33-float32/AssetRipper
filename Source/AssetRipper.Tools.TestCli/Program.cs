using System.Text;
using System.Text.Json;
using AssetRipper.Il2CppInspector.Plugins;
using AssetRipper.Import.GamePlugins;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;

namespace AssetRipper.Tools.TestCli;

internal static class Program
{
	private const int DefaultThreads = 10;

	public static int Main(string[] args)
	{
		PluginAssemblyLoader.Initialize();
		Console.OutputEncoding = Encoding.UTF8;
		try
		{
			if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
			{
				Console.WriteLine(
					"testcli --input <folder-or-file> --game <plugin-id> [--threads <count>] [--skip-scripts]\n"
						+ "testcli --list-games\n"
						+ $"--threads (-j): concurrent disk archives, default {DefaultThreads}. Entries within each archive are read sequentially.\n"
						+ "The console shows one shared progress bar; only warnings and errors are logged to log/.\n"
						+ "Reads each archive entry and asset. Writes .log and .summary.json reports beside the executable under log/.\n"
						+ "Exit codes: 0 = all checked items passed; 1 = failures/unknown/unresolved; 2 = setup error; 130 = cancelled."
				);
				return 0;
			}
			if (args.Length == 1 && args[0] == "--list-games")
			{
				foreach (IGameImportPlugin plugin in GameImportPlugins.Installed)
					Console.WriteLine(plugin.Id);
				return 0;
			}

			string? input = null;
			string? game = null;
			bool skipScripts = false;
			int threads = DefaultThreads;
			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--input" or "-i" when i + 1 < args.Length:
						input = Path.GetFullPath(args[++i]);
						break;
					case "--game" or "-g" when i + 1 < args.Length:
						game = args[++i];
						break;
					case "--threads" or "-j" when i + 1 < args.Length:
						if (!int.TryParse(args[++i], out threads) || threads <= 0)
						{
							throw new ArgumentException("--threads must be a positive integer.");
						}
						break;
					case "--skip-scripts":
						skipScripts = true;
						break;
					default:
						throw new ArgumentException($"Unknown option or missing value: {args[i]}");
				}
			}
			if (input is null || game is null)
				throw new ArgumentException("Both --input and --game are required.");
			if (!Directory.Exists(input) && !File.Exists(input))
				throw new ArgumentException($"Input does not exist: {input}");
			IGameImportPlugin selected =
				GameImportPlugins.Installed.SingleOrDefault(p => p.Id.Equals(game, StringComparison.OrdinalIgnoreCase))
				?? throw new ArgumentException($"Unknown game: {game}. Use --list-games to list installed plugin IDs.");

			string prefix = Path.Combine(AppContext.BaseDirectory, "log", $"testcli_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Environment.ProcessId}");
			ScanLog scanLog = new(prefix + ".log");
			Logger.Add(scanLog);
			Logger.LogSystemInformation("AssetRipper testcli");
			using ScanProgress progress = new(scanLog);
			using CancellationTokenSource cancellation = new();
			ConsoleCancelEventHandler onCancel = (_, e) =>
			{
				e.Cancel = true;
				cancellation.Cancel();
				progress.SetStage("Cancelling");
			};
			Console.CancelKeyPress += onCancel;
			ScanSummary summary = new() { Threads = threads };
			try
			{
				using GameImportSession session = selected.Mount([input]);
				progress.SetArchiveCount(session.Archives.Count);
				progress.SetStage(skipScripts ? "Preparing" : "Loading scripts");
				using IAssemblyManager assemblies = CreateAssemblyManager(session, skipScripts);
				object assemblyGate = new();
				ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = threads, CancellationToken = cancellation.Token };
				progress.SetStage("Scanning");
				// The work item is a complete disk archive. Its collections and decompression cache
				// belong to one scanner; script metadata is shared explicitly.
				Parallel.ForEach(
					session.Archives,
					parallelOptions,
					archive =>
					{
						using ScanLog.ErrorScope archiveLog = scanLog.BeginArchive();
						ScanSummary archiveSummary = new();
						ArchiveScanner scanner = new(assemblies, assemblyGate, archiveLog, archiveSummary, progress, skipScripts, cancellation.Token);
						bool completed = false;
						progress.StartArchive();
						try
						{
							scanner.Scan(archive, session.Version);
							completed = true;
						}
						finally
						{
							lock (summary)
							{
								summary.AddArchive(archiveSummary);
							}
							progress.FinishArchive(completed, archiveSummary.HasFailures || archiveLog.Errors != 0);
						}
					}
				);
			}
			catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
			{
				summary.Cancelled = true;
				Logger.Warning("Scan cancelled; partial results have been saved.");
			}
			catch (Exception ex)
			{
				summary.SetupFailed = true;
				Logger.Error("Could not complete the scan.", ex);
			}
			finally
			{
				Console.CancelKeyPress -= onCancel;
				summary.Errors = scanLog.Errors;
				summary.Warnings = scanLog.Warnings;
				File.WriteAllText(prefix + ".summary.json", JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
				progress.Complete(summary, prefix);
			}
			return summary.Cancelled ? 130
				: summary.SetupFailed ? 2
				: summary.HasFailures ? 1
				: 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 2;
		}
	}

	private static IAssemblyManager CreateAssemblyManager(GameImportSession session, bool skipScripts)
	{
		if (!skipScripts)
		{
			try
			{
				return session.CreateAssemblyManager();
			}
			catch (Exception ex)
			{
				Logger.Error(
					LogCategory.Import,
					"Script metadata initialization failed. Continuing to check native assets; script failures will be reported.",
					ex
				);
			}
		}
		return new BaseManager(static _ => { });
	}
}

internal sealed class ScanLog(string filePath) : ILogger
{
	private readonly FileLogger fileLogger = new(filePath);
	private long errors;
	private long warnings;
	private readonly AsyncLocal<ErrorScope?> currentArchive = new();
	public long Errors => Interlocked.Read(ref errors);
	public long Warnings => Interlocked.Read(ref warnings);

	public ErrorScope BeginArchive()
	{
		ErrorScope scope = new(currentArchive, currentArchive.Value);
		currentArchive.Value = scope;
		return scope;
	}

	public void Log(LogType type, LogCategory category, string message)
	{
		if (type is not (LogType.Warning or LogType.Error))
		{
			return;
		}
		fileLogger.Log(type, category, message);
		if (type == LogType.Error)
		{
			Interlocked.Increment(ref errors);
			currentArchive.Value?.RecordError();
		}
		else
		{
			Interlocked.Increment(ref warnings);
		}
	}

	public void BlankLine(int numLines) { }

	// Logger invokes sinks on the originating execution context, even though its output
	// is locked globally. Only errors from this archive affect its asset/entry verdicts.
	internal sealed class ErrorScope(AsyncLocal<ErrorScope?> current, ErrorScope? previous) : IDisposable
	{
		private long errors;
		public long Errors => Interlocked.Read(ref errors);

		public void RecordError() => Interlocked.Increment(ref errors);

		public void Dispose() => current.Value = previous;
	}
}

internal sealed class ScanSummary
{
	public int Threads { get; init; }
	public long Archives { get; set; }
	public long Entries { get; set; }
	public long Resources { get; set; }
	public long Assets { get; set; }
	public long PassedAssets { get; set; }
	public long FailedAssets { get; set; }
	public long UnknownAssets { get; set; }
	public long UnresolvedScripts { get; set; }
	public long SkippedScripts { get; set; }
	public long FailedEntries { get; set; }
	public long FailedArchives { get; set; }
	public long Errors { get; set; }
	public long Warnings { get; set; }
	public bool Cancelled { get; set; }
	public bool SetupFailed { get; set; }
	public bool HasFailures =>
		Errors != 0 || FailedAssets != 0 || UnknownAssets != 0 || UnresolvedScripts != 0 || FailedEntries != 0 || FailedArchives != 0;

	public void AddArchive(ScanSummary archive)
	{
		Archives += archive.Archives;
		Entries += archive.Entries;
		Resources += archive.Resources;
		Assets += archive.Assets;
		PassedAssets += archive.PassedAssets;
		FailedAssets += archive.FailedAssets;
		UnknownAssets += archive.UnknownAssets;
		UnresolvedScripts += archive.UnresolvedScripts;
		SkippedScripts += archive.SkippedScripts;
		FailedEntries += archive.FailedEntries;
		FailedArchives += archive.FailedArchives;
	}
}
