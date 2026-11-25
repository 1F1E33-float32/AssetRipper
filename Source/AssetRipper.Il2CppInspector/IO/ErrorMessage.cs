namespace AssetRipper.Il2CppInspector.IO;

internal static class ErrorMessage
{
	public static string DescribeCauses(Exception error)
	{
		string text = error.Message;
		for (Exception? inner = error.InnerException; inner is not null; inner = inner.InnerException)
			if (!text.Contains(inner.Message, StringComparison.Ordinal))
				text = $"{text} → {inner.Message}";
		return text;
	}
}
