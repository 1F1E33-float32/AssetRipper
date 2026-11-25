using AssetRipper.Il2CppInspector.IO;
using AssetRipper.Il2CppInspector.Plugins;
using AssetRipper.Il2CppInspector.Reflection;

namespace AssetRipper.Il2CppInspector;

/// <summary>Loads native IL2CPP images and metadata into a queryable reflection model.</summary>
public static class Il2CppLoader
{
	/// <summary>Loads files, recovering embedded metadata when metadataPath is omitted and discovering auxiliary metadata beside a supplied metadata file.</summary>
	public static Il2CppLoadResult Load(
		string binaryPath,
		string? metadataPath = null,
		LoadOptions? options = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
		binaryPath = Path.GetFullPath(binaryPath);
		metadataPath = metadataPath is null ? null : Path.GetFullPath(metadataPath);
		cancellationToken = cancellationToken.CanBeCanceled ? cancellationToken : options?.CancellationToken ?? default;
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			using Stream binary = File.OpenRead(binaryPath);
			byte[] metadataBytes = metadataPath is null ? [] : File.ReadAllBytes(metadataPath);
			using MemoryStream metadata = new(metadataBytes, writable: false);
			GamePlugin? plugin = GamePlugins.Select(options?.Game, metadataBytes, binary);
			string? startupPath = options?.StartupMetadataPath;
			if (startupPath is null && metadataPath is not null && plugin?.StartupMetadataFileName is { } filename)
			{
				string candidate = SafePath.CombineUnder(Path.GetDirectoryName(metadataPath) ?? "", filename);
				if (File.Exists(candidate))
				{
					startupPath = candidate;
				}
			}

			using Stream? startup = startupPath is null ? null : File.OpenRead(Path.GetFullPath(startupPath));
			return Load(binary, metadata, options, startup, cancellationToken);
		}
		catch (Exception exception)
			when (exception
					is IOException
						or InvalidDataException
						or NotSupportedException
						or ArgumentException
						or InvalidOperationException
						or OverflowException
						or IndexOutOfRangeException
						or KeyNotFoundException
			)
		{
			string message = $"Failed to load IL2CPP binary '{binaryPath}' with metadata '{metadataPath}': {ErrorMessage.DescribeCauses(exception)}";
			throw exception is NotSupportedException ? new NotSupportedException(message, exception) : new InvalidDataException(message, exception);
		}
	}

	/// <summary>Loads borrowed streams, recovering embedded metadata when metadata is omitted. The caller may close the input streams after this method returns.</summary>
	public static Il2CppLoadResult Load(
		Stream binary,
		Stream? metadata = null,
		LoadOptions? options = null,
		Stream? startupMetadata = null,
		CancellationToken cancellationToken = default
	)
	{
		if (!binary.CanRead || !binary.CanSeek)
		{
			throw new ArgumentException("The IL2CPP binary stream must be readable and seekable.", nameof(binary));
		}

		cancellationToken = cancellationToken.CanBeCanceled ? cancellationToken : options?.CancellationToken ?? default;
		if (metadata?.CanSeek == true)
		{
			metadata.Position = 0;
		}

		if (startupMetadata?.CanSeek == true)
		{
			startupMetadata.Position = 0;
		}

		LoadOptions effective = new()
		{
			ImageBase = options?.ImageBase ?? 0,
			Game = options?.Game,
			StartupMetadataPath = options?.StartupMetadataPath,
			CancellationToken = cancellationToken,
			ProgressCallback = Report,
		};
		List<TypeModel> models = [];
		List<IDisposable> resources = [];
		try
		{
			Report(new("Read IL2CPP metadata", 0, 1));
			using MemoryStream metadataBuffer = new();
			metadata?.CopyTo(metadataBuffer);
			byte[] data = metadataBuffer.GetBufferOrCopy();
			GamePlugin? plugin = GamePlugins.Select(effective.Game, data, binary);
			Report(new("Read IL2CPP metadata", 1, 1));
			string? activeStatus = null;
			void CompleteStatus()
			{
				if (activeStatus is null)
				{
					return;
				}
				string completed = activeStatus;
				activeStatus = null;
				Report(new(completed, 1, 1));
			}
			void Status(object? _, string message)
			{
				if (string.Equals(activeStatus, message, StringComparison.Ordinal))
				{
					return;
				}
				CompleteStatus();
				activeStatus = message;
				Report(new(message, 0, 1));
			}
			if (plugin is not null)
			{
				using MemoryStream startupBuffer = new();
				startupMetadata?.CopyTo(startupBuffer);
				binary.Position = 0;
				Il2CppInspector package = plugin.Load(binary, data, startupMetadata is null ? null : startupBuffer.ToArray(), effective, Status);
				Own(package.Metadata);
				Own((IDisposable)package.BinaryImage);
				CompleteStatus();
				models.Add(new(package, Report));
			}
			else
			{
				if (data.Length == 0)
				{
					throw new InvalidDataException("No embedded metadata decoder matches this IL2CPP binary. Supply an external metadata stream.");
				}
				metadataBuffer.Position = 0;
				Metadata decoded = Metadata.FromStream(metadataBuffer, Status, cancellationToken);
				Own(decoded);
				binary.Position = 0;
				IFileFormatStream image =
					FileFormatStream.Load(binary, effective, Status) ?? throw new NotSupportedException("The IL2CPP binary format is unsupported.");
				Own((IDisposable)image);
				foreach (IFileFormatStream architecture in image.Images)
				{
					cancellationToken.ThrowIfCancellationRequested();
					Own((IDisposable)architecture);
					Il2CppBinary native =
						Il2CppBinary.Load(architecture, decoded, Status)
						?? throw new InvalidDataException(
							$"IL2CPP registration structures were not found in {architecture.Format} {architecture.Arch} image."
						);
					CompleteStatus();
					models.Add(new(new(native, decoded), Report));
				}
				CompleteStatus();
			}

			Report(new("Build IL2CPP model", 1, 1));
			return new(models.AsReadOnly(), resources);
		}
		catch
		{
			foreach (IDisposable resource in resources)
			{
				resource.Dispose();
			}

			throw;
		}

		void Own(IDisposable resource)
		{
			if (!resources.Contains(resource))
			{
				resources.Add(resource);
			}
		}

		void Report(OperationProgress progress)
		{
			cancellationToken.ThrowIfCancellationRequested();
			options?.ProgressCallback?.Invoke(progress);
		}
	}

	private static byte[] GetBufferOrCopy(this MemoryStream stream) =>
		stream.TryGetBuffer(out ArraySegment<byte> buffer) && buffer.Offset == 0 && buffer.Count == buffer.Array?.Length
			? buffer.Array
			: stream.ToArray();
}
