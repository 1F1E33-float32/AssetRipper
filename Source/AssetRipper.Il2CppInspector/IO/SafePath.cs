namespace AssetRipper.Il2CppInspector.IO;

internal static class SafePath
{
	public static string CombineUnder(string root, string relative)
	{
		string fullRoot = Path.GetFullPath(root);
		string candidate = Path.GetFullPath(Path.Combine(fullRoot, relative));
		string prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (
			!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(candidate, fullRoot, StringComparison.OrdinalIgnoreCase)
		)
			throw new InvalidDataException($"Path escapes root: {relative}");
		return candidate;
	}
}
