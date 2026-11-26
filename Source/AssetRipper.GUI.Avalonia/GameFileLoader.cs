using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.GamePlugins;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.Processing;

namespace AssetRipper.GUI.Avalonia;

public static class GameFileLoader
{
	public static IReadOnlyList<IGameImportPlugin> Plugins => GameImportPlugins.Installed;
	public static GameImportSession? MountedGame { get; private set; }
	private static readonly Dictionary<(GameArchive, int), object> LoadedEntries = new();
	private static bool _pluginAssembliesLoaded;
	private static GameData? GameData { get; set; }

	[MemberNotNullWhen(true, nameof(GameData))]
	public static bool IsLoaded => GameData is not null;

	public static GameBundle GameBundle => GameData!.GameBundle;
	public static IAssemblyManager AssemblyManager => GameData!.AssemblyManager;
	public static FullConfiguration Settings { get; } = LoadSettings();

	public static ExportHandler ExportHandler
	{
		private get;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			value.ThrowIfSettingsDontMatch(Settings);
			field = value;
		}
	} = new(Settings);

	public static bool Premium => ExportHandler.GetType() != typeof(ExportHandler);

	public static void Reset()
	{
		MountedGame?.Dispose();
		MountedGame = null;
		LoadedEntries.Clear();
		_pluginAssembliesLoaded = false;
		if (GameData is not null)
		{
			GameData.GameBundle.Dispose();
			GameData.AssemblyManager.Dispose();
			GameData = null;
			GC.Collect();
			Logger.Info(LogCategory.General, "Data was reset.");
		}
	}

	public static void LoadAndProcess(IReadOnlyList<string> paths)
	{
		Reset();
		Settings.LogConfigurationValues();
		if (!string.IsNullOrEmpty(Settings.ImportSettings.Game))
		{
			IGameImportPlugin plugin =
				Plugins.SingleOrDefault(p => p.Id == Settings.ImportSettings.Game)
				?? throw new NotSupportedException($"Unknown game import plugin: {Settings.ImportSettings.Game}");
			MountedGame = plugin.Mount(paths);
			GameData = new(new GameBundle(), MountedGame.Version, new BaseManager(_ => { }), null);
			return;
		}
		GameData = ExportHandler.LoadAndProcess(paths, LocalFileSystem.Instance);
	}

	public static object LoadMountedEntry(GameArchive archive, GameArchiveEntry entry)
	{
		if (LoadedEntries.TryGetValue((archive, entry.Index), out object? existing))
			return existing;
		if (MountedGame is null || GameData is null)
			throw new InvalidOperationException("No game is mounted.");
		if (entry.IsSerialized && !_pluginAssembliesLoaded && !Settings.DisableScriptImport)
		{
			IAssemblyManager manager = MountedGame.CreateAssemblyManager();
			GameData.AssemblyManager.Dispose();
			GameData = GameData with { AssemblyManager = manager };
			_pluginAssembliesLoaded = true;
		}
		FileBase file = archive.ReadEntry(entry);
		object result;
		if (file is SerializedFile serialized)
		{
			foreach (GameArchiveEntry resource in archive.GetResources(entry))
				LoadMountedEntry(archive, resource);
			SerializedAssetCollection collection = GameData.GameBundle.AddCollectionFromSerializedFile(
				serialized,
				new GameAssetFactory(AssemblyManager),
				GameData.ProjectVersion
			);
			collection.FilePath = serialized.FilePath;
			collection.InitializeDependencyList();
			result = collection;
		}
		else if (file is ResourceFile resource)
		{
			GameData.GameBundle.AddResource(resource);
			result = resource;
		}
		else
			throw new NotSupportedException($"Unsupported mounted entry: {entry.Name}");
		LoadedEntries.Add((archive, entry.Index), result);
		return result;
	}

	internal static object? GetLoadedEntry(GameArchive archive, GameArchiveEntry entry) => LoadedEntries.GetValueOrDefault((archive, entry.Index));

	public static async Task<bool> ExportUnityProjectAsync(string path, Func<string, Task<bool>> confirmDeleteAsync)
	{
		if (!IsLoaded || !IsValidExportDirectory(path))
		{
			return false;
		}

		if (IsNonEmptyDirectory(path))
		{
			if (!await confirmDeleteAsync(path))
			{
				Logger.Info(LogCategory.Export, "User declined to delete existing export directory. Aborting export.");
				return false;
			}
			Directory.Delete(path, true);
		}

		Directory.CreateDirectory(path);
		await Task.Run(() => ExportHandler.Export(GameData, path, LocalFileSystem.Instance));
		return true;
	}

	public static async Task<bool> ExportPrimaryContentAsync(string path, Func<string, Task<bool>> confirmDeleteAsync)
	{
		if (!IsLoaded || !IsValidExportDirectory(path))
		{
			return false;
		}

		if (IsNonEmptyDirectory(path))
		{
			if (!await confirmDeleteAsync(path))
			{
				Logger.Info(LogCategory.Export, "User declined to delete existing export directory. Aborting export.");
				return false;
			}
			Directory.Delete(path, true);
		}

		Directory.CreateDirectory(path);
		await Task.Run(() =>
		{
			Logger.Info(LogCategory.Export, "Starting primary content export");
			Logger.Info(LogCategory.Export, $"Attempting to export assets to {path}...");
			Settings.ExportRootPath = path;
			PrimaryContentExporter.CreateDefault(GameData, Settings).Export(GameBundle, Settings, LocalFileSystem.Instance);
			Logger.Info(LogCategory.Export, "Finished exporting primary content.");
		});
		return true;
	}

	private static FullConfiguration LoadSettings()
	{
		FullConfiguration settings = new();
		settings.LoadFromDefaultPath();
		return settings;
	}

	private static bool IsValidExportDirectory(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			Logger.Error(LogCategory.Export, "Export path is empty");
			return false;
		}
		string directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (directoryName is "Desktop" or "Documents" or "Downloads")
		{
			Logger.Error(LogCategory.Export, $"Export path '{path}' is a system directory");
			return false;
		}
		return true;
	}

	private static bool IsNonEmptyDirectory(string path)
	{
		return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
	}
}
