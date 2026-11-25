using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.Streams.Smart;

namespace AssetRipper.Import.GamePlugins;

/// <summary>A game format adapter selected explicitly when importing.</summary>
public interface IGameImportPlugin
{
	string Id { get; }
	GameImportSession Mount(IReadOnlyList<string> paths);
}

/// <summary>Owns a mounted game and its lazily opened archives.</summary>
public abstract class GameImportSession : IDisposable
{
	public abstract AssetRipper.Primitives.UnityVersion Version { get; }
	public abstract IReadOnlyList<GameArchive> Archives { get; }
	public abstract IAssemblyManager CreateAssemblyManager();
	public abstract void Dispose();
}

/// <summary>Opening an archive reads its index; entry streams decode only requested blocks.</summary>
public abstract class GameArchive : IDisposable
{
	public abstract string Name { get; }
	public abstract string FilePath { get; }
	public abstract IReadOnlyList<GameArchiveEntry> Entries { get; }
	public abstract Stream OpenEntry(GameArchiveEntry entry);
	public abstract void Dispose();

	public virtual IEnumerable<GameArchiveEntry> GetResources(GameArchiveEntry entry) => [];

	public virtual FileBase ReadEntry(GameArchiveEntry entry)
	{
		using SmartStream stream = SmartStream.Create(OpenEntry(entry));
		return SchemeReader.ReadFile(stream, FilePath, entry.Name);
	}
}

public sealed record GameArchiveEntry(int Index, string Name, long Length, bool IsSerialized);
