using AssetRipper.Il2CppInspector.Reflection;

namespace AssetRipper.Il2CppInspector.Plugins;

public abstract class GamePlugin
{
	public abstract string GameId { get; }
	public virtual string? StartupMetadataFileName => null;

	public abstract bool Matches(ReadOnlySpan<byte> metadata);

	/// <summary>Matches a borrowed binary stream when metadata is embedded in the image.</summary>
	public virtual bool Matches(Stream binary, ReadOnlySpan<byte> metadata) => Matches(metadata);

	public abstract Il2CppInspector Load(Stream binary, byte[] metadata, byte[]? startupMetadata, LoadOptions options, EventHandler<string>? status);

	public virtual IGameTypeLayouts CreateTypeLayouts(TypeModel model) =>
		throw new NotSupportedException($"{GameId} uses the stock IL2CPP pipeline.");
}
