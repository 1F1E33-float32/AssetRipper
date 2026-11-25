using AssetRipper.Il2CppInspector.Reflection;

namespace AssetRipper.Il2CppInspector;

/// <summary>Owns the loaded models and their backing data. Keep this result alive while querying models or reading native method bodies.</summary>
public sealed class Il2CppLoadResult : IDisposable
{
	private readonly List<IDisposable> _resources;
	private int _disposed;

	internal Il2CppLoadResult(IReadOnlyList<TypeModel> models, List<IDisposable> resources)
	{
		Models = models;
		_resources = resources;
	}

	/// <summary>One complete reflection model for each native architecture in the input binary.</summary>
	public IReadOnlyList<TypeModel> Models { get; }

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			foreach (IDisposable resource in _resources)
			{
				resource.Dispose();
			}
		}
	}
}
