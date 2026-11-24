using AssetRipper.Assets.Bundles;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.Processing;

namespace AssetRipper.GUI.Avalonia;

public static class GameFileLoader
{
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
		if (GameData is not null)
		{
			GameData = null;
			GC.Collect();
			Logger.Info(LogCategory.General, "Data was reset.");
		}
	}

	public static void LoadAndProcess(IReadOnlyList<string> paths)
	{
		Reset();
		Settings.LogConfigurationValues();
		GameData = ExportHandler.LoadAndProcess(paths, LocalFileSystem.Instance);
	}

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
