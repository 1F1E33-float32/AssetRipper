using AssetRipper.Import.GamePlugins;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;

namespace AssetRipper.GamePlugins.ABC;

/// <summary>ABC Windows 0.75.2 archive and encrypted IL2CPP metadata adapter.</summary>
public sealed class AbcGamePlugin : IGameImportPlugin
{
	public string Id => "ABC_CN_Windows_CBT1";

	public GameImportSession Mount(IReadOnlyList<string> paths) => new Session(paths);

	private sealed class Session : GameImportSession
	{
		public override AssetRipper.Primitives.UnityVersion Version => new(2022, 3, 14);
		public override IReadOnlyList<GameArchive> Archives { get; }
		private readonly string? _root;

		public Session(IReadOnlyList<string> paths)
		{
			HashSet<string> archives = new(StringComparer.OrdinalIgnoreCase);
			foreach (string path in paths)
			{
				if (File.Exists(path) && Path.GetExtension(path).Equals(".wmv", StringComparison.OrdinalIgnoreCase))
					archives.Add(Path.GetFullPath(path));
				else if (Directory.Exists(path))
					foreach (string file in Directory.EnumerateFiles(path, "*.wmv", SearchOption.AllDirectories))
						archives.Add(file);
				else
					throw new ArgumentException($"ABC import path is not a directory or .wmv archive: {path}");
			}
			if (archives.Count == 0)
				throw new InvalidDataException("No ABC .wmv archives found in the selected paths.");
			Archives = archives.Order(StringComparer.OrdinalIgnoreCase).Select(p => (GameArchive)new AbcArchive(p)).ToArray();
			DirectoryInfo? directory = new(Path.GetDirectoryName(Archives[0].FilePath)!);
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

		public override IAssemblyManager CreateAssemblyManager()
		{
			if (_root is null)
				return new BaseManager(_ => { });
			string binary = Path.Combine(_root, "GameAssembly.dll");
			string? metadata = Directory.EnumerateFiles(_root, "global-metadata.dat", SearchOption.AllDirectories).FirstOrDefault();
			if (metadata is null)
				throw new FileNotFoundException("ABC global-metadata.dat was not found under the game directory.");
			IL2CppManager manager = new(_ => { }, AbcScriptSerialization.ShouldSerializeField);
			try
			{
				manager.Initialize(binary, metadata, LocalFileSystem.Instance, "ABC_CN_Windows_0.75.2");
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
				archive.Dispose();
		}
	}
}
