using AssetRipper.GUI.Avalonia;
using AssetRipper.Il2CppInspector.Plugins;

namespace AssetRipper.GUI.Free;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		PluginAssemblyLoader.Initialize();
		AvaloniaApplicationLauncher.Launch(args);
	}
}
