using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Platforms;
using AssetRipper.IO.Files;
using AssetRipper.Il2CppInspector;
using AssetRipper.Il2CppInspector.Reflection;
using AsmResolver.DotNet;

namespace AssetRipper.Import.Structure.Assembly.Managers;

/// <summary>Adapts decoded IL2CPP types to the managed model used by Unity field serialization.</summary>
public sealed class IL2CppManager(Action<string> requestAssemblyCallback) : BaseManager(requestAssemblyCallback)
{
	public override ScriptingBackend ScriptingBackend => ScriptingBackend.IL2Cpp;

	public override void Initialize(PlatformGameStructure gameStructure)
	{
		string? binary = gameStructure.Il2CppGameAssemblyPath;
		string? metadata = gameStructure.Il2CppMetaDataPath;
		if (string.IsNullOrWhiteSpace(binary) || string.IsNullOrWhiteSpace(metadata))
			throw new ArgumentException("The IL2CPP game assembly and global metadata paths are required.", nameof(gameStructure));

		if (gameStructure.Version is { } version)
			Logger.Info(LogCategory.Import, $"During IL2CPP initialization, found Unity version: {version}");
		Initialize(binary, metadata, gameStructure.FileSystem);
	}

	public void Initialize(string binaryPath, string metadataPath, FileSystem fileSystem, string? game = null)
	{
		using Stream binary = fileSystem.File.OpenRead(binaryPath);
		using Stream metadata = fileSystem.File.OpenRead(metadataPath);
		Logger.SendStatusChange("loading_step_parse_il2cpp_metadata");
		string? lastMessage = null;
		using Il2CppLoadResult loaded = Il2CppLoader.Load(binary, metadata, new LoadOptions { Game = game, ProgressCallback = Report });
		TypeModel model = loaded.Models.FirstOrDefault() ?? throw new InvalidDataException("The IL2CPP binary contains no supported images.");
		if (loaded.Models.Count > 1)
			Logger.Info(LogCategory.Import, $"Using {model.Package.BinaryImage.Arch} metadata from the multi-architecture IL2CPP binary.");
		foreach (AssemblyDefinition assembly in new Il2CppTypeModelConverter(model, Report).Convert())
			Add(assembly);

		void Report(OperationProgress progress)
		{
			string message = progress.Detail ?? progress.Label;
			if (message != lastMessage)
			{
				Logger.Info(LogCategory.Import, message);
				lastMessage = message;
			}
		}
	}
}
