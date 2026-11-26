using AssetRipper.Import.GamePlugins;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.Primitives;

namespace AssetRipper.GamePlugins.AzurPromilia;

/// <summary>Imports Azur Promilia's concatenated UnityCN bundles and Code Philosophy metadata.</summary>
public sealed class AzurPromiliaGamePlugin : IGameImportPlugin
{
	public string Id => "AzurPromilia_xx_Windows_x.x.x";

	public GameImportSession Mount(IReadOnlyList<string> paths) => new Session(paths);

	private sealed class Session : GameImportSession
	{
		private readonly string? _root;
		public override UnityVersion Version => new(2022, 3, 62);
		public override IReadOnlyList<GameArchive> Archives { get; }

		public Session(IReadOnlyList<string> paths)
		{
			HashSet<string> archives = new(StringComparer.OrdinalIgnoreCase);
			foreach (string path in paths)
			{
				if (File.Exists(path) && Path.GetExtension(path).Equals(".r", StringComparison.OrdinalIgnoreCase))
				{
					if (!IsBundle(path))
					{
						throw new InvalidDataException($"Azur Promilia file is not a UnityFS AssetBundle: {path}");
					}
					archives.Add(Path.GetFullPath(path));
				}
				else if (Directory.Exists(path))
				{
					foreach (string archive in Directory.EnumerateFiles(path, "*.r", SearchOption.AllDirectories))
					{
						// Raw audio, video and streaming payloads share the .r extension with bundles.
						if (IsBundle(archive))
						{
							archives.Add(Path.GetFullPath(archive));
						}
					}
				}
				else
				{
					throw new ArgumentException($"Azur Promilia import path is not a directory or .r archive: {path}");
				}
			}
			if (archives.Count == 0)
			{
				throw new InvalidDataException("No Azur Promilia UnityFS .r archives were found.");
			}
			Archives = archives.Order(StringComparer.OrdinalIgnoreCase).Select(static path => (GameArchive)new AzurPromiliaArchive(path)).ToArray();
			DirectoryInfo? directory = new FileInfo(Archives[0].FilePath).Directory;
			while (directory is not null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "GameAssembly.dll")))
				{
					_root = directory.FullName;
					break;
				}
				directory = directory.Parent;
			}
		}

		private static bool IsBundle(string path)
		{
			using Stream stream = File.OpenRead(path);
			Span<byte> signature = stackalloc byte[8];
			return stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) == signature.Length
				&& signature.SequenceEqual("UnityFS\0"u8);
		}

		public override IAssemblyManager CreateAssemblyManager()
		{
			if (_root is null)
			{
				return new BaseManager(static _ => { });
			}
			string metadata = Path.Combine(_root, "AzurPromilia_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
			if (!File.Exists(metadata))
			{
				throw new FileNotFoundException("Azur Promilia global-metadata.dat was not found.", metadata);
			}
			IL2CppManager manager = new(static _ => { });
			try
			{
				manager.Initialize(Path.Combine(_root, "GameAssembly.dll"), metadata, LocalFileSystem.Instance, "AzurPromilia_xx_Windows_x.x.x");
				return manager;
			}
			catch
			{
				manager.Dispose();
				throw;
			}
		}

		public override void Dispose()
		{
			foreach (GameArchive archive in Archives)
			{
				archive.Dispose();
			}
		}
	}
}
