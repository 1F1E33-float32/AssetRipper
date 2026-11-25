using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace AssetRipper.Il2CppInspector.Plugins;

/// <summary>Loads flat plugin assemblies with shared host dependencies and RID-specific native libraries.</summary>
public static class PluginAssemblyLoader
{
	private static readonly AssemblyLoadContext Context = AssemblyLoadContext.GetLoadContext(typeof(PluginAssemblyLoader).Assembly)!;

	static PluginAssemblyLoader()
	{
		Context.Resolving += ResolveManaged;
		Context.ResolvingUnmanagedDll += (_, name) => ResolveNative(name);
	}

	/// <summary>Installs dependency resolution before the host initializes native UI libraries.</summary>
	public static void Initialize() { }

	public static IEnumerable<Assembly> Load(string prefix)
	{
		HashSet<Assembly> assemblies =
		[
			.. Context.Assemblies.Where(a => !a.IsDynamic && a.GetName().Name?.StartsWith(prefix, StringComparison.Ordinal) == true),
		];
		string directory = Path.Combine(AppContext.BaseDirectory, "plugins");
		if (Directory.Exists(directory))
		{
			foreach (string path in Directory.EnumerateFiles(directory, prefix + "*.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
			{
				AssemblyName name = AssemblyName.GetAssemblyName(path);
				assemblies.Add(
					assemblies.FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name))
						?? Context.LoadFromAssemblyPath(Path.GetFullPath(path))
				);
			}
		}
		return assemblies;
	}

	private static Assembly? ResolveManaged(AssemblyLoadContext context, AssemblyName name)
	{
		if (string.IsNullOrEmpty(name.Name) || !string.IsNullOrEmpty(name.CultureName))
			return null;
		string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
		if (!File.Exists(path))
			path = Path.Combine(AppContext.BaseDirectory, "plugins", name.Name + ".dll");
		return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
	}

	private static nint ResolveNative(string name)
	{
		string? os =
			OperatingSystem.IsWindows() ? "win"
			: OperatingSystem.IsLinux() ? "linux"
			: OperatingSystem.IsMacOS() ? "osx"
			: null;
		string? arch = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => null,
		};
		if (os is null || arch is null)
			return 0;
		string extension =
			os == "win" ? ".dll"
			: os == "osx" ? ".dylib"
			: ".so";
		string directory = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native");
		foreach (string candidate in new[] { name, name + extension, "lib" + name, "lib" + name + extension })
		{
			string path = Path.Combine(directory, candidate);
			if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
				return handle;
		}
		return 0;
	}
}
