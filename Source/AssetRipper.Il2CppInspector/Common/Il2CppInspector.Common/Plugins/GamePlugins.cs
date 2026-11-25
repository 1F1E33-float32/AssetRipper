using System.Reflection;

namespace AssetRipper.Il2CppInspector.Plugins;

public static class GamePlugins
{
	private static readonly Lazy<IReadOnlyList<GamePlugin>> Plugins = new(Discover);
	public static IReadOnlyList<GamePlugin> Installed => Plugins.Value;

	public static GamePlugin Get(string gameId)
	{
		string[] parts = gameId.Split('_');
		if (parts.Length != 4 || parts.Any(string.IsNullOrWhiteSpace))
		{
			throw new ArgumentException($"Available games: {string.Join(", ", Installed.Select(p => p.GameId))}");
		}

		return Installed.SingleOrDefault(p => string.Equals(p.GameId, gameId, StringComparison.OrdinalIgnoreCase))
			?? throw new NotSupportedException(
				$"No installed plugin supports {gameId}. Available games: {string.Join(", ", Installed.Select(p => p.GameId))}"
			);
	}

	internal static GamePlugin? Select(string? gameId, ReadOnlySpan<byte> metadata, Stream? binary = null)
	{
		if (gameId != null)
		{
			GamePlugin selected = Get(gameId);
			if (!Matches(selected, binary, metadata))
			{
				throw new InvalidDataException($"Metadata does not match game plugin {selected.GameId}.");
			}

			return selected;
		}

		List<GamePlugin> matches = [];
		foreach (GamePlugin plugin in Installed)
		{
			if (Matches(plugin, binary, metadata))
			{
				matches.Add(plugin);
			}
		}

		return matches.Count switch
		{
			0 => null,
			1 => matches[0],
			_ => throw new InvalidOperationException(
				$"Multiple game plugins match this metadata ({string.Join(", ", matches.Select(static p => p.GameId))}); set LoadOptions.Game."
			),
		};
	}

	private static bool Matches(GamePlugin plugin, Stream? binary, ReadOnlySpan<byte> metadata)
	{
		if (binary is null)
		{
			return plugin.Matches(metadata);
		}
		long position = binary.Position;
		try
		{
			binary.Position = 0;
			return plugin.Matches(binary, metadata);
		}
		finally
		{
			binary.Position = position;
		}
	}

	private static IReadOnlyList<GamePlugin> Discover()
	{
		IEnumerable<Assembly> assemblies = PluginAssemblyLoader.Load("AssetRipper.Il2CppInspector.Plugin.");

		GamePlugin[] result =
		[
			.. assemblies
				.SelectMany(a => a.GetExportedTypes())
				.Where(t => !t.IsAbstract && typeof(GamePlugin).IsAssignableFrom(t))
				.Select(t =>
					(GamePlugin)(Activator.CreateInstance(t) ?? throw new InvalidOperationException($"Cannot construct IL2CPP plugin {t.FullName}."))
				)
				.OrderBy(p => p.GameId, StringComparer.Ordinal),
		];
		if (result.GroupBy(p => p.GameId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
		{
			throw new InvalidOperationException("Duplicate game plugin IDs are installed.");
		}

		return result;
	}
}
