using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Metadata;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Audio;
using AssetRipper.Export.Modules.Models;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Scripts;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.GUI.Localizations;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.Numerics;
using AssetRipper.Processing.Prefabs;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Classes.ClassID_128;
using AssetRipper.SourceGenerated.Classes.ClassID_156;
using AssetRipper.SourceGenerated.Classes.ClassID_189;
using AssetRipper.SourceGenerated.Classes.ClassID_2;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_25;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_329;
using AssetRipper.SourceGenerated.Classes.ClassID_33;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.SourceGenerated.Classes.ClassID_82;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.SubMesh;
using AssetRipper.Yaml;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NAudio.Vorbis;
using NAudio.Wave;
using SharpGLTF.Scenes;

namespace AssetRipper.GUI.Avalonia.Services;

internal sealed record InfoRow(string Key, string Value, object? ReferenceTarget = null);

internal sealed record DecodedImageResult(
	WriteableBitmap[] Layers,
	int Width,
	int Height,
	DirectBitmap DirectBitmap);

internal sealed record DecodedAudioResult(
	float[] Samples,
	int SampleRate,
	int Channels,
	byte[] EncodedData,
	string EncodedExtension,
	byte[]? WavData)
{
	public double Duration => Channels > 0 && SampleRate > 0
		? (double)Samples.Length / Channels / SampleRate
		: 0.0;
}

internal sealed record ModelGeometry(
	Vector3[] Positions,
	Vector3[] Normals,
	Vector4[]? Colors,
	int[] TriangleIndices,
	Vector3 Center,
	float Radius);

internal sealed record SearchHit(IUnityObjectBase Asset, AssetCollection Collection);

internal static class AssetContentService
{
	#region Capabilities

	public static bool HasImageData(IUnityObjectBase asset) => asset switch
	{
		IImageTexture texture => texture.CheckAssetIntegrity(),
		SpriteInformationObject spriteInfo => spriteInfo.Texture.CheckAssetIntegrity(),
		ISprite sprite => SpriteConverter.Supported(sprite),
		ITerrainData terrainData => terrainData.Heightmap.Heights.Count > 0,
		_ => false,
	};

	public static bool HasAudioData(IUnityObjectBase asset) => asset is IAudioClip;

	public static bool HasModelData(IUnityObjectBase asset) => asset is IMesh;

	public static bool HasTextData(IUnityObjectBase asset) =>
		asset is IShader or IMonoScript or ITextAsset { Script_C49.IsEmpty: false };

	public static bool HasFontData(IUnityObjectBase asset) =>
		asset is IFont font && font.FontData is { Length: >= 4 };

	public static bool HasVideoData(IUnityObjectBase asset) =>
		asset is IVideoClip clip && clip.CheckIntegrity();

	public static bool HasBinaryData(IUnityObjectBase asset) =>
		asset is RawDataObject { RawData.Length: > 0 }
		|| HasFontData(asset)
		|| HasVideoData(asset);

	#endregion

	#region Image

	public static DecodedImageResult? DecodeImage(IUnityObjectBase asset)
	{
		DirectBitmap bitmap = asset switch
		{
			IImageTexture texture => TextureConverter.TryConvertToBitmap(texture, out DirectBitmap b) ? b : DirectBitmap.Empty,
			SpriteInformationObject spriteInfo => TextureConverter.TryConvertToBitmap(spriteInfo.Texture, out DirectBitmap b) ? b : DirectBitmap.Empty,
			ISprite sprite => SpriteConverter.TryConvertToBitmap(sprite, out DirectBitmap b) ? b : DirectBitmap.Empty,
			ITerrainData terrainData => TerrainHeatmap.GetBitmap(terrainData),
			_ => DirectBitmap.Empty,
		};

		if (bitmap.IsEmpty)
		{
			return null;
		}

		int depth = Math.Max(1, bitmap.Depth);
		WriteableBitmap[] layers = new WriteableBitmap[depth];
		for (int layer = 0; layer < depth; layer++)
		{
			DirectBitmap layerBitmap = depth == 1 ? bitmap : bitmap.GetLayer(layer);
			layers[layer] = ConvertDirectBitmapToWriteableBitmap(layerBitmap);
		}

		return new DecodedImageResult(layers, bitmap.Width, bitmap.Height, bitmap);
	}

	private static WriteableBitmap ConvertDirectBitmapToWriteableBitmap(DirectBitmap bitmap)
	{
		// Fast path: DirectBitmap<ColorBGRA32> or generic fallback via PNG stream
		if (bitmap.PixelSize == 4 && bitmap.GetType().IsGenericType)
		{
			Type pixelType = bitmap.GetType().GetGenericArguments()[0];
			if (pixelType.Name == "ColorBGRA32")
			{
				WriteableBitmap wb = new(
					new PixelSize(bitmap.Width, bitmap.Height),
					new global::Avalonia.Vector(96, 96),
					PixelFormat.Bgra8888,
					AlphaFormat.Unpremul);
				using ILockedFramebuffer fb = wb.Lock();
				ReadOnlySpan<byte> src = bitmap.Bits;
				int rowBytes = bitmap.Width * 4;
				unsafe
				{
					byte* dst = (byte*)fb.Address;
					for (int y = 0; y < bitmap.Height; y++)
					{
						src.Slice(y * rowBytes, rowBytes).CopyTo(new Span<byte>(dst + y * fb.RowBytes, rowBytes));
					}
				}
				return wb;
			}
		}

		// Reliable universal path via in-memory PNG encoding into Avalonia Bitmap -> WriteableBitmap
		using MemoryStream ms = new();
		bitmap.SaveAsPng(ms);
		ms.Position = 0;
		return WriteableBitmap.Decode(ms);
	}

	public static byte[] GetRawImageData(IUnityObjectBase asset) => asset switch
	{
		ITexture2D texture => texture.GetImageData(),
		SpriteInformationObject spriteInfo => spriteInfo.Texture.GetImageData(),
		ISprite sprite => sprite.TryGetTexture()?.GetImageData() ?? [],
		ITerrainData terrainData => MemoryMarshal.AsBytes(terrainData.Heightmap.Heights.GetSpan()).ToArray(),
		_ => [],
	};

	#endregion

	#region Audio

	public static DecodedAudioResult? DecodeAudio(IAudioClip clip, out string? error)
	{
		if (!AudioClipDecoder.TryDecode(clip, out byte[]? encodedData, out string? extension, out error))
		{
			return null;
		}

		byte[]? wavData = null;
		float[] samples = [];
		int sampleRate = clip.Frequency > 0 ? clip.Frequency : 44100;
		int channels = clip.Channels > 0 ? clip.Channels : 1;

		try
		{
			if (string.Equals(extension, "ogg", StringComparison.OrdinalIgnoreCase))
			{
				using MemoryStream ms = new(encodedData);
				using VorbisWaveReader vorbis = new(ms);
				sampleRate = vorbis.WaveFormat.SampleRate;
				channels = vorbis.WaveFormat.Channels;
				samples = ReadAllFloatSamples(vorbis);
				wavData = AudioConverter.OggToWav(encodedData);
			}
			else if (string.Equals(extension, "wav", StringComparison.OrdinalIgnoreCase))
			{
				wavData = encodedData;
				using MemoryStream ms = new(encodedData);
				using WaveFileReader wav = new(ms);
				sampleRate = wav.WaveFormat.SampleRate;
				channels = wav.WaveFormat.Channels;
				samples = ReadAllFloatSamples(wav.ToSampleProvider());
			}
			else
			{
				// Try WaveFileReader as fallback for PCM-wrapped formats
				try
				{
					using MemoryStream ms = new(encodedData);
					using WaveFileReader wav = new(ms);
					sampleRate = wav.WaveFormat.SampleRate;
					channels = wav.WaveFormat.Channels;
					samples = ReadAllFloatSamples(wav.ToSampleProvider());
					wavData = encodedData;
				}
				catch
				{
					// Non-WAV/OGG format (e.g. MP3/tracker): still return encoded bytes for export
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warning(LogCategory.Export, $"Audio PCM decode note for '{clip.GetBestName()}': {ex.Message}");
		}

		return new DecodedAudioResult(samples, sampleRate, channels, encodedData, extension, wavData);
	}

	private static float[] ReadAllFloatSamples(ISampleProvider provider)
	{
		List<float> list = new();
		float[] buffer = new float[16384];
		int read;
		// Cap at ~10 minutes of stereo 48kHz (~57M floats) to stay memory-safe
		const int MaxSamples = 60_000_000;
		while ((read = provider.Read(buffer.AsSpan())) > 0)
		{
			for (int i = 0; i < read; i++)
			{
				list.Add(buffer[i]);
			}
			if (list.Count >= MaxSamples)
			{
				break;
			}
		}
		return list.ToArray();
	}

	#endregion

	#region 3D Model

	public static ModelGeometry? ExtractMeshGeometry(IMesh mesh)
	{
		if (!MeshData.TryMakeFromMesh(mesh, out MeshData meshData))
		{
			return null;
		}

		int vertexCount = (int)mesh.VertexData.VertexCount;
		if (vertexCount <= 0)
		{
			return null;
		}

		Vector3[] positions = new Vector3[vertexCount];
		Vector3[] normals = new Vector3[vertexCount];
		bool hasNormals = meshData.Normals is { Length: > 0 };
		bool hasColors = meshData.Colors is { Length: > 0 };
		Vector4[]? colors = hasColors ? new Vector4[vertexCount] : null;

		Vector3 min = new(float.PositiveInfinity);
		Vector3 max = new(float.NegativeInfinity);

		for (int i = 0; i < vertexCount; i++)
		{
			Vector3 p = meshData.TryGetVertexAtIndex((uint)i);
			// Flip X for right-handed preview coordinate system (matching GLTF conversion)
			p = new Vector3(-p.X, p.Y, p.Z);
			positions[i] = p;
			min = Vector3.Min(min, p);
			max = Vector3.Max(max, p);

			if (hasNormals)
			{
				Vector3 n = meshData.TryGetNormalAtIndex((uint)i);
				n = new Vector3(-n.X, n.Y, n.Z);
				float len = n.Length();
				normals[i] = len > 1e-6f ? n / len : Vector3.UnitY;
			}
			else
			{
				normals[i] = Vector3.Zero;
			}

			if (colors is not null)
			{
				colors[i] = meshData.TryGetColorAtIndex((uint)i).Vector;
			}
		}

		List<int> indices = new();
		bool is16Bit = mesh.Is16BitIndices();
		uint[] idxBuf = meshData.ProcessedIndexBuffer;

		for (int s = 0; s < mesh.SubMeshes.Count; s++)
		{
			ISubMesh sub = mesh.SubMeshes[s];
			uint firstIndex = sub.GetFirstIndex(is16Bit);
			uint indexCount = sub.IndexCount;
			MeshTopology topology = sub.GetTopology();

			if (firstIndex >= idxBuf.Length)
			{
				continue;
			}

			uint available = (uint)Math.Min(indexCount, idxBuf.Length - firstIndex);

			switch (topology)
			{
				case MeshTopology.Triangles:
					for (uint i = 0; i + 2 < available; i += 3)
					{
						int a = (int)idxBuf[firstIndex + i + 2];
						int b = (int)idxBuf[firstIndex + i + 1];
						int c = (int)idxBuf[firstIndex + i];
						if (a < vertexCount && b < vertexCount && c < vertexCount)
						{
							indices.Add(a);
							indices.Add(b);
							indices.Add(c);
						}
					}
					break;
				case MeshTopology.TriangleStrip:
					for (uint i = 0; i + 2 < available; i++)
					{
						int a = (int)idxBuf[firstIndex + i + 2];
						int b = (int)idxBuf[firstIndex + i + 1];
						int c = (int)idxBuf[firstIndex + i];
						if (a == b || a == c || b == c || a >= vertexCount || b >= vertexCount || c >= vertexCount)
						{
							continue;
						}
						if ((i & 1) == 1)
						{
							indices.Add(b);
							indices.Add(a);
							indices.Add(c);
						}
						else
						{
							indices.Add(a);
							indices.Add(b);
							indices.Add(c);
						}
					}
					break;
				case MeshTopology.Quads:
					for (uint q = 0; q + 3 < available; q += 4)
					{
						int a = (int)idxBuf[firstIndex + q + 3];
						int b = (int)idxBuf[firstIndex + q + 2];
						int c = (int)idxBuf[firstIndex + q + 1];
						int d = (int)idxBuf[firstIndex + q];
						if (a < vertexCount && b < vertexCount && c < vertexCount && d < vertexCount)
						{
							indices.Add(a); indices.Add(b); indices.Add(c);
							indices.Add(a); indices.Add(c); indices.Add(d);
						}
					}
					break;
			}
		}

		// Compute smooth normals if the mesh did not supply normals
		if (!hasNormals)
		{
			for (int i = 0; i + 2 < indices.Count; i += 3)
			{
				int ia = indices[i];
				int ib = indices[i + 1];
				int ic = indices[i + 2];
				Vector3 fn = Vector3.Cross(positions[ib] - positions[ia], positions[ic] - positions[ia]);
				normals[ia] += fn;
				normals[ib] += fn;
				normals[ic] += fn;
			}
			for (int i = 0; i < normals.Length; i++)
			{
				float len = normals[i].Length();
				normals[i] = len > 1e-6f ? normals[i] / len : Vector3.UnitY;
			}
		}

		Vector3 center = (min + max) * 0.5f;
		float radius = 0f;
		for (int i = 0; i < positions.Length; i++)
		{
			float dist = Vector3.Distance(positions[i], center);
			if (dist > radius)
			{
				radius = dist;
			}
		}
		if (radius < 1e-5f || float.IsNaN(radius) || float.IsInfinity(radius))
		{
			center = Vector3.Zero;
			radius = 1f;
		}

		return new ModelGeometry(positions, normals, colors, indices.ToArray(), center, radius);
	}

	public static bool TryExportGlb(IMesh mesh, Stream output, out string? errorMessage)
	{
		try
		{
			SceneBuilder sceneBuilder = GlbMeshBuilder.Build(mesh);
			return GlbWriter.TryWrite(sceneBuilder, output, out errorMessage);
		}
		catch (Exception ex)
		{
			errorMessage = ex.Message;
			return false;
		}
	}

	#endregion

	#region Text / YAML / JSON / Binary

	public static string GetTextContent(IUnityObjectBase asset) => asset switch
	{
		IShader shader => shader.Has_Script() && !shader.Script.IsEmpty ? shader.Script : DumpShaderDataAsText(shader),
		IMonoScript monoScript => DecompileMonoScript(monoScript),
		ITextAsset textAsset => textAsset.Script_C49,
		_ => "",
	};

	public static string GetTextFileName(IUnityObjectBase asset) => asset switch
	{
		IShader => $"{FileSystem.FixInvalidFileNameCharacters(asset.GetBestName())}.shader",
		IMonoScript monoScript => $"{FileSystem.FixInvalidFileNameCharacters(monoScript.ClassName_R)}.cs",
		ITextAsset textAsset => $"{FileSystem.FixInvalidFileNameCharacters(asset.GetBestName())}.{textAsset.GetBestExtension() ?? "txt"}",
		_ => $"{FileSystem.FixInvalidFileNameCharacters(asset.GetBestName())}.txt",
	};

	private static string DumpShaderDataAsText(IShader shader)
	{
		InvariantStringWriter writer = new();
		DummyShaderTextExporter.ExportShader(shader, writer);
		return writer.ToString();
	}

	private static string DecompileMonoScript(IMonoScript monoScript)
	{
		if (!GameFileLoader.IsLoaded)
		{
			return EmptyScript.GetContent(monoScript);
		}
		IAssemblyManager assemblyManager = GameFileLoader.AssemblyManager;
		if (!monoScript.IsScriptPresents(assemblyManager))
		{
			return EmptyScript.GetContent(monoScript);
		}
		try
		{
			_ = monoScript.GetTypeDefinition(assemblyManager);
			return EmptyScript.GetContent(monoScript);
		}
		catch (Exception ex)
		{
			return $"{Localization.AnErrorOccuredDuringDecompilation}\n\n{ex}";
		}
	}

	public static string GetYamlContent(IUnityObjectBase asset)
	{
		try
		{
			using StringWriter stringWriter = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
			YamlWriter writer = new();
			writer.WriteHead(stringWriter);
			YamlDocument document = new YamlWalker().ExportYamlDocument(asset, ExportIdHandler.GetMainExportID(asset));
			writer.WriteDocument(document);
			writer.WriteTail(stringWriter);
			return stringWriter.ToString();
		}
		catch (Exception ex)
		{
			return ex.ToString();
		}
	}

	public static string GetJsonContent(IUnityObjectBase asset)
	{
		try
		{
			StringWriter stringWriter = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
			asset.WalkStandard(new DefaultJsonWalker(stringWriter));
			return stringWriter.ToString();
		}
		catch (Exception ex)
		{
			return ex.ToString();
		}
	}

	public static (byte[] Data, string Extension) GetBinaryData(IUnityObjectBase asset)
	{
		if (asset is RawDataObject rawData)
		{
			return (rawData.RawData, "dat");
		}
		if (asset is IFont font && font.FontData is { Length: >= 4 } fontBytes)
		{
			string ext = (fontBytes[0], fontBytes[1], fontBytes[2], fontBytes[3]) switch
			{
				(0x4F, 0x54, 0x54, 0x4F) => "otf",
				(0x00, 0x01, 0x00, 0x00) => "ttf",
				(0x74, 0x74, 0x63, 0x66) => "ttc",
				_ => "dat",
			};
			return (fontBytes, ext);
		}
		if (asset is IVideoClip clip && clip.TryGetContent(out byte[]? videoContent))
		{
			clip.TryGetExtensionFromPath(out string? ext);
			return (videoContent, ext ?? "bin");
		}
		if (HasImageData(asset))
		{
			return (GetRawImageData(asset), "bin");
		}
		return ([], "bin");
	}

	public static byte[] ReadResourceBytes(ResourceFile resource)
	{
		if (resource.Stream.CanSeek)
		{
			resource.Stream.Position = 0;
		}
		using MemoryStream ms = new();
		resource.Stream.CopyTo(ms);
		return ms.ToArray();
	}

	#endregion

	#region Information & Dependencies & Search

	public static List<InfoRow> GetInformationRows(IUnityObjectBase asset)
	{
		List<InfoRow> rows = new();

		if (asset.MainAsset is SceneHierarchyObject sceneHierarchy)
		{
			rows.Add(new InfoRow(Localization.Scene, sceneHierarchy.Scene.Name, sceneHierarchy.Scene));
		}

		rows.Add(new InfoRow(Localization.Collection, asset.Collection.Name, asset.Collection));
		rows.Add(new InfoRow(Localization.PathId, asset.PathID.ToString(CultureInfo.InvariantCulture)));
		rows.Add(new InfoRow(Localization.ClassIdTypeNumber, asset.ClassID.ToString(CultureInfo.InvariantCulture)));
		rows.Add(new InfoRow(Localization.ClassIdTypeName, asset.ClassName));

		if (asset.OriginalPath is not null)
		{
			rows.Add(new InfoRow(Localization.OriginalPath, asset.OriginalPath));
		}
		if (asset.OverridePath is not null)
		{
			rows.Add(new InfoRow(Localization.OverridePath, asset.OverridePath));
		}
		if (!string.IsNullOrEmpty(asset.AssetBundleName))
		{
			rows.Add(new InfoRow(Localization.AssetBundleName, asset.AssetBundleName));
		}

		if (asset.MainAsset is not null && asset.MainAsset != asset)
		{
			rows.Add(new InfoRow(Localization.MainAsset, asset.MainAsset.GetBestName(), asset.MainAsset));
		}

		switch (asset)
		{
			case IComponent component:
				if (component.GameObject_C2P is { } go)
				{
					rows.Add(new InfoRow(Localization.GameObject, go.GetBestName(), go));
					if (component is IRenderer && go.TryGetComponent<IMeshFilter>()?.MeshP is { } rendererMesh)
					{
						rows.Add(new InfoRow(Localization.Mesh, rendererMesh.GetBestName(), rendererMesh));
					}
				}
				if (component is IMonoBehaviour mb && mb.ScriptP is { } ms)
				{
					rows.Add(new InfoRow(Localization.Script, ms.GetBestName(), ms));
					AddMonoScriptRows(rows, ms);
				}
				else if (component is IMeshFilter mf && mf.MeshP is { } mesh)
				{
					rows.Add(new InfoRow(Localization.Mesh, mesh.GetBestName(), mesh));
				}
				else if (component is IAudioSource audioSource && audioSource.AudioClipP is { } clip)
				{
					rows.Add(new InfoRow(Localization.AudioClip, clip.GetBestName(), clip));
				}
				break;
			case IGameObject gameObject:
				if (gameObject.TryGetComponent(out ITransform? transform))
				{
					rows.Add(new InfoRow(Localization.Transform, transform.GetBestName(), transform));
				}
				break;
			case IMaterial material:
				if (material.Shader_C21P is { } shader)
				{
					rows.Add(new InfoRow(Localization.Shader, shader.GetBestName(), shader));
				}
				break;
			case SpriteInformationObject spriteInfo:
				rows.Add(new InfoRow(Localization.Texture, spriteInfo.Texture.GetBestName(), spriteInfo.Texture));
				break;
			case IMonoScript monoScript:
				AddMonoScriptRows(rows, monoScript);
				break;
			case ITexture2D texture2D:
				rows.Add(new InfoRow(Localization.Width, texture2D.Width_C28.ToString(CultureInfo.InvariantCulture)));
				rows.Add(new InfoRow(Localization.Height, texture2D.Height_C28.ToString(CultureInfo.InvariantCulture)));
				rows.Add(new InfoRow(Localization.Format, texture2D.Format_C28E.ToString()));
				break;
			case IMesh meshAsset:
				rows.Add(new InfoRow(Localization.VertexCount, meshAsset.VertexData.VertexCount.ToString(CultureInfo.InvariantCulture)));
				rows.Add(new InfoRow(Localization.SubmeshCount, meshAsset.SubMeshes.Count.ToString(CultureInfo.InvariantCulture)));
				break;
			case IAudioClip audioClip:
				rows.Add(new InfoRow(Localization.Channels, audioClip.Channels.ToString(CultureInfo.InvariantCulture)));
				rows.Add(new InfoRow(Localization.Frequency, $"{audioClip.Frequency} Hz"));
				rows.Add(new InfoRow(Localization.Length, $"{audioClip.Length:0.00} s"));
				break;
			case ITerrainData terrainData:
				rows.Add(new InfoRow(Localization.Width, terrainData.Heightmap.GetWidth().ToString(CultureInfo.InvariantCulture)));
				rows.Add(new InfoRow(Localization.Height, terrainData.Heightmap.GetHeight().ToString(CultureInfo.InvariantCulture)));
				break;
		}

		return rows;

		static void AddMonoScriptRows(List<InfoRow> list, IMonoScript monoScript)
		{
			list.Add(new InfoRow(Localization.AssemblyName, monoScript.AssemblyName));
			list.Add(new InfoRow(Localization.Namespace, monoScript.Namespace));
			list.Add(new InfoRow(Localization.ClassName, monoScript.ClassName_R));
		}
	}

	public static List<InfoRow> GetDependencyRows(IUnityObjectBase asset)
	{
		List<InfoRow> rows = new();
		foreach ((string path, PPtr pptr) in asset.FetchDependencies())
		{
			if (pptr.IsNull)
			{
				continue;
			}
			IUnityObjectBase? dependency = asset.Collection.TryGetAsset(pptr);
			if (dependency is null)
			{
				rows.Add(new InfoRow(path, "Missing"));
			}
			else
			{
				rows.Add(new InfoRow(path, $"{dependency.GetBestName()} ({dependency.ClassName})", dependency));
			}
		}
		return rows;
	}

	public static List<SearchHit> SearchAssets(string query, int maxResults = 5000)
	{
		List<SearchHit> results = new();
		if (!GameFileLoader.IsLoaded || string.IsNullOrWhiteSpace(query))
		{
			return results;
		}

		GameBundle bundle = GameFileLoader.GameBundle;
		foreach (AssetCollection collection in bundle.FetchAssetCollections())
		{
			foreach (IUnityObjectBase asset in collection)
			{
				if (asset.GetBestName().Contains(query, StringComparison.OrdinalIgnoreCase)
					|| asset.ClassName.Contains(query, StringComparison.OrdinalIgnoreCase))
				{
					results.Add(new SearchHit(asset, collection));
					if (results.Count >= maxResults)
					{
						return results;
					}
				}
			}
		}
		return results;
	}

	public static string FormatBytes(long size)
	{
		if (size < 1024)
		{
			return $"{size} B";
		}
		if (size < 1024 * 1024)
		{
			return $"{size / 1024.0:0.0} KB";
		}
		if (size < 1024L * 1024 * 1024)
		{
			return $"{size / (1024.0 * 1024.0):0.0} MB";
		}
		return $"{size / (1024.0 * 1024.0 * 1024.0):0.0} GB";
	}

	#endregion

	private sealed class InvariantStringWriter : StringWriter
	{
		public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;
	}
}
