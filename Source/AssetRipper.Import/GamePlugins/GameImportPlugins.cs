using AssetRipper.Il2CppInspector.Plugins;

namespace AssetRipper.Import.GamePlugins;

/// <summary>Discovers the installed game archive adapters from the host's plugins directory.</summary>
public static class GameImportPlugins
{
	private static readonly Lazy<IReadOnlyList<IGameImportPlugin>> Plugins = new(Discover);
	public static IReadOnlyList<IGameImportPlugin> Installed => Plugins.Value;

	private static IReadOnlyList<IGameImportPlugin> Discover()
	{
		IGameImportPlugin[] result = PluginAssemblyLoader
			.Load("AssetRipper.GamePlugins.")
			.SelectMany(a => a.GetExportedTypes())
			.Where(t => !t.IsAbstract && typeof(IGameImportPlugin).IsAssignableFrom(t))
			.Select(t => (IGameImportPlugin)Activator.CreateInstance(t)!)
			.OrderBy(p => p.Id, StringComparer.Ordinal)
			.ToArray();
		if (result.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
			throw new InvalidDataException("Duplicate game import plugin IDs are installed.");
		return Array.AsReadOnly(result);
	}
}
