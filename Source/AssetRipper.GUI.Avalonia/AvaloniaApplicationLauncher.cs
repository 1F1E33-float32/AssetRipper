using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Localizations;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using Avalonia;

namespace AssetRipper.GUI.Avalonia;

public static class AvaloniaApplicationLauncher
{
	public static void Launch(string[] args)
	{
		SetupLogging();
		Localization.LoadLanguage(GameFileLoader.Settings.LanguageCode);

		// Allow passing file/folder paths on command line to open directly
		App.StartupPaths = args.Where(a => File.Exists(a) || Directory.Exists(a)).ToArray();

		AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
	}

	private static void SetupLogging()
	{
		try
		{
			string logPath = Path.Join(LocalFileSystem.ExecutingDirectory, "log", $"AssetRipper_{DateTime.Now:yyyyMMdd_HHmmss}.log");
			RotateLogs(logPath);
			Logger.Add(new FileLogger(logPath));
		}
		catch
		{
			// Ignore if executing directory is read-only
		}

		Logger.Add(ActivityLogService.Instance);
		Logger.LogSystemInformation("AssetRipper");
	}

	private static void RotateLogs(string path)
	{
		const int MaxLogFiles = 5;
		string? directory = Path.GetDirectoryName(path);
		if (directory is null || !Directory.Exists(directory))
		{
			return;
		}

		FileInfo[] logFiles = new DirectoryInfo(directory).GetFiles("AssetRipper_*.log").OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();

		for (int i = 0; i <= logFiles.Length - MaxLogFiles; i++)
		{
			try
			{
				logFiles[i].Delete();
			}
			catch (IOException)
			{
				// Ignore locked log files
			}
		}
	}
}
