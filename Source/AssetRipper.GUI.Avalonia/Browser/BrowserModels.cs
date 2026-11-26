using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Localizations;
using AssetRipper.Import.GamePlugins;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_189;

namespace AssetRipper.GUI.Avalonia.Browser;

internal enum BrowserItemKind
{
	MountedArchive,
	MountedEntry,
	MountedAsset,
	Bundle,
	Collection,
	Scene,
	Asset,
	ResourceFile,
	FailedFile,
	ScenesGroup,
	SearchGroup,
	PathFolder,
}

internal sealed class BrowserItem
{
	public required string Name { get; init; }
	public required BrowserItemKind Kind { get; init; }
	public required IconKind Icon { get; init; }
	public required bool IsContainer { get; init; }
	public required object Target { get; init; }
	public string Meta { get; init; } = "";
	public string? ClassName { get; init; }
	public string? SecondaryPath { get; init; }
	public IReadOnlyList<string> CompanionNames { get; init; } = [];

	public string DisplayPathSegment => Name;

	public static IconKind ClassifyAssetIcon(IUnityObjectBase asset)
	{
		if (asset is SpriteInformationObject)
		{
			return IconKind.Sprite;
		}
		// Class IDs also cover objects read directly from a type tree without generated interfaces.
		IconKind icon = ClassifyAssetIcon(asset.ClassID);
		return icon == IconKind.File && asset is IImageTexture ? IconKind.Texture : icon;
	}

	public static IconKind ClassifyAssetIcon(int classID) =>
		(ClassIDType)classID switch
		{
			ClassIDType.GameObject => IconKind.GameObject,
			ClassIDType.Transform => IconKind.Transform,
			ClassIDType.RectTransform => IconKind.RectTransform,
			ClassIDType.Component or ClassIDType.Behaviour => IconKind.Component,
			ClassIDType.Mesh or ClassIDType.MeshFilter => IconKind.Mesh,
			ClassIDType.Renderer or ClassIDType.MeshRenderer or ClassIDType.BillboardRenderer or ClassIDType.BillboardAsset => IconKind.MeshRenderer,
			ClassIDType.SkinnedMeshRenderer or ClassIDType.ClothRenderer or ClassIDType.Cloth or ClassIDType.SkinnedCloth =>
				IconKind.SkinnedMeshRenderer,
			ClassIDType.Material or ClassIDType.ProceduralMaterial => IconKind.Material,
			ClassIDType.Shader or ClassIDType.ComputeShader or ClassIDType.RayTracingShader or ClassIDType.ShaderVariantCollection => IconKind.Shader,
			ClassIDType.ShaderInclude or ClassIDType.TextAsset => IconKind.Text,
			ClassIDType.Texture or ClassIDType.Texture2D or ClassIDType.ImageTexture or ClassIDType.SparseTexture or ClassIDType.ProceduralTexture =>
				IconKind.Texture,
			ClassIDType.Cubemap or ClassIDType.CubemapArray => IconKind.Cubemap,
			ClassIDType.Texture3D or ClassIDType.Texture2DArray => IconKind.TextureArray,
			ClassIDType.RenderTexture or ClassIDType.CustomRenderTexture => IconKind.RenderTexture,
			ClassIDType.Sprite or ClassIDType.SpriteRenderer or ClassIDType.SpriteMask or ClassIDType.SpriteShapeRenderer => IconKind.Sprite,
			ClassIDType.SpriteAtlas or ClassIDType.SpriteAtlasAsset or ClassIDType.CachedSpriteAtlas => IconKind.SpriteAtlas,
			ClassIDType.Camera or ClassIDType.ScriptableCamera => IconKind.Camera,
			ClassIDType.Light or ClassIDType.Flare or ClassIDType.LensFlare or ClassIDType.Projector or ClassIDType.Halo => IconKind.Light,
			ClassIDType.LightProbeGroup or ClassIDType.LightProbes_197 or ClassIDType.LightProbes_258 or ClassIDType.LightProbeProxyVolume =>
				IconKind.LightProbe,
			ClassIDType.ReflectionProbe => IconKind.ReflectionProbe,
			ClassIDType.AnimationClip or ClassIDType.PreviewAnimationClip or ClassIDType.Animation or ClassIDType.Motion => IconKind.AnimationClip,
			ClassIDType.Animator
			or ClassIDType.AnimatorController
			or ClassIDType.RuntimeAnimatorController
			or ClassIDType.AnimatorOverrideController
			or ClassIDType.AnimatorState
			or ClassIDType.AnimatorStateMachine
			or ClassIDType.AnimatorStateTransition
			or ClassIDType.AnimatorTransition
			or ClassIDType.AnimatorTransitionBase
			or ClassIDType.BlendTree
			or ClassIDType.PlayableDirector => IconKind.Animator,
			ClassIDType.Avatar or ClassIDType.AvatarMask_319 or ClassIDType.AvatarMask_1011 or ClassIDType.AvatarSkeletonMask => IconKind.Avatar,
			ClassIDType.ParticleSystem
			or ClassIDType.ParticleSystemRenderer
			or ClassIDType.ParticleSystemForceField
			or ClassIDType.ParticleEmitter
			or ClassIDType.ParticleRenderer
			or ClassIDType.ParticleAnimator
			or ClassIDType.MeshParticleEmitter
			or ClassIDType.EllipsoidParticleEmitter
			or ClassIDType.VisualEffect
			or ClassIDType.VisualEffectAsset
			or ClassIDType.VisualEffectResource
			or ClassIDType.VFXRenderer => IconKind.ParticleSystem,
			ClassIDType.TrailRenderer => IconKind.TrailRenderer,
			ClassIDType.LineRenderer => IconKind.LineRenderer,
			ClassIDType.AudioClip or ClassIDType.AudioResource => IconKind.Audio,
			ClassIDType.AudioSource or ClassIDType.AudioListener or ClassIDType.AudioReverbZone => IconKind.AudioSource,
			ClassIDType.AudioMixer
			or ClassIDType.AudioMixerController
			or ClassIDType.AudioMixerGroup
			or ClassIDType.AudioMixerGroupController
			or ClassIDType.AudioMixerSnapshot
			or ClassIDType.AudioMixerSnapshotController
			or ClassIDType.AudioMixerEffectController
			or ClassIDType.AudioFilter
			or ClassIDType.AudioReverbFilter
			or ClassIDType.AudioHighPassFilter
			or ClassIDType.AudioLowPassFilter
			or ClassIDType.AudioChorusFilter
			or ClassIDType.AudioEchoFilter
			or ClassIDType.AudioDistortionFilter => IconKind.AudioMixer,
			ClassIDType.Collider
			or ClassIDType.Collider2D
			or ClassIDType.BoxCollider
			or ClassIDType.SphereCollider
			or ClassIDType.CapsuleCollider
			or ClassIDType.MeshCollider
			or ClassIDType.TerrainCollider
			or ClassIDType.WheelCollider
			or ClassIDType.CharacterController
			or ClassIDType.BoxCollider2D
			or ClassIDType.CircleCollider2D
			or ClassIDType.CapsuleCollider2D
			or ClassIDType.PolygonCollider2D
			or ClassIDType.EdgeCollider2D
			or ClassIDType.CompositeCollider2D
			or ClassIDType.TilemapCollider2D
			or ClassIDType.CustomCollider2D => IconKind.Collider,
			ClassIDType.Rigidbody
			or ClassIDType.Rigidbody2D
			or ClassIDType.ArticulationBody
			or ClassIDType.ConstantForce
			or ClassIDType.ConstantForce2D => IconKind.Rigidbody,
			ClassIDType.Joint
			or ClassIDType.Joint2D
			or ClassIDType.HingeJoint
			or ClassIDType.FixedJoint
			or ClassIDType.SpringJoint
			or ClassIDType.CharacterJoint
			or ClassIDType.ConfigurableJoint
			or ClassIDType.AnchoredJoint2D
			or ClassIDType.SpringJoint2D
			or ClassIDType.DistanceJoint2D
			or ClassIDType.HingeJoint2D
			or ClassIDType.SliderJoint2D
			or ClassIDType.WheelJoint2D
			or ClassIDType.RelativeJoint2D
			or ClassIDType.FixedJoint2D
			or ClassIDType.FrictionJoint2D
			or ClassIDType.TargetJoint2D => IconKind.Joint,
			ClassIDType.PhysicsMaterial or ClassIDType.PhysicsMaterial2D => IconKind.PhysicsMaterial,
			ClassIDType.Terrain or ClassIDType.TerrainData or ClassIDType.TerrainLayer or ClassIDType.Tree or ClassIDType.SpeedTreeWindAsset =>
				IconKind.Terrain,
			ClassIDType.NavMeshData_194
			or ClassIDType.NavMeshData_238
			or ClassIDType.NavMeshAgent
			or ClassIDType.NavMeshObstacle
			or ClassIDType.OffMeshLink => IconKind.NavMesh,
			ClassIDType.Canvas or ClassIDType.CanvasRenderer or ClassIDType.CanvasGroup or ClassIDType.UIRenderer or ClassIDType.PanelRenderer =>
				IconKind.Canvas,
			ClassIDType.MonoBehaviour => IconKind.MonoBehaviour,
			ClassIDType.MonoScript => IconKind.Script,
			ClassIDType.Font or ClassIDType.TextMesh or ClassIDType.GUIText => IconKind.Font,
			ClassIDType.VideoClip_327
			or ClassIDType.VideoClip_329
			or ClassIDType.VideoPlayer
			or ClassIDType.MovieTexture
			or ClassIDType.WebCamTexture => IconKind.Video,
			ClassIDType.AssetBundle or ClassIDType.AssetBundleManifest => IconKind.AssetBundle,
			ClassIDType.SceneAsset or ClassIDType.SceneRoots => IconKind.Scene,
			ClassIDType.Prefab or ClassIDType.PrefabInstance => IconKind.Prefab,
			ClassIDType.LODGroup or ClassIDType.SortingGroup => IconKind.LODGroup,
			ClassIDType.RenderSettings
			or ClassIDType.LightmapSettings
			or ClassIDType.LightingSettings
			or ClassIDType.LightmapParameters
			or ClassIDType.OcclusionCullingSettings
			or ClassIDType.NavMeshSettings
			or ClassIDType.GraphicsSettings
			or ClassIDType.QualitySettings
			or ClassIDType.PhysicsManager
			or ClassIDType.Physics2DSettings
			or ClassIDType.AudioManager => IconKind.Config,
			_ => IconKind.File,
		};

	public static string GetKindLabel(BrowserItem item) =>
		item.Kind switch
		{
			BrowserItemKind.MountedArchive => Localization.Bundle,
			BrowserItemKind.MountedEntry => Localization.Collection,
			BrowserItemKind.Bundle => Localization.Bundle,
			BrowserItemKind.Collection => Localization.Collection,
			BrowserItemKind.Scene => Localization.Scene,
			BrowserItemKind.ResourceFile => Localization.Resources,
			BrowserItemKind.FailedFile => Localization.FailedFiles,
			BrowserItemKind.Asset => item.ClassName ?? "Asset",
			BrowserItemKind.MountedAsset => item.ClassName ?? "Asset",
			_ => "Folder",
		};
}

internal static class BrowserFolderBuilder
{
	public static List<BrowserItem> BuildRoot(GameBundle gameBundle)
	{
		List<BrowserItem> items = BuildBundleItems(gameBundle);
		if (GameFileLoader.MountedGame is { } mounted)
		{
			foreach (GameArchive archive in mounted.Archives)
				items.Add(
					new BrowserItem
					{
						Name = archive.Name,
						Kind = BrowserItemKind.MountedArchive,
						Icon = IconKind.Archive,
						IsContainer = true,
						Target = archive,
						Meta = "Mounted",
					}
				);
		}

		// Add a virtual Scenes folder if the loaded project contains scenes
		List<SceneDefinition> scenes = gameBundle.Scenes.ToList();
		if (scenes.Count > 0)
		{
			scenes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			items.Insert(
				0,
				new BrowserItem
				{
					Name = "Scenes",
					Kind = BrowserItemKind.ScenesGroup,
					Icon = IconKind.Folder,
					IsContainer = true,
					Target = scenes,
					Meta = scenes.Count.ToString(),
				}
			);
		}

		return items;
	}

	public static List<BrowserItem> BuildChildren(BrowserItem container)
	{
		return container.Kind switch
		{
			BrowserItemKind.MountedArchive when container.Target is GameArchive archive => BuildArchiveItems(archive),
			BrowserItemKind.MountedEntry when container.Target is AssetCollection collection => BuildCollectionItems(collection),
			BrowserItemKind.Bundle when container.Target is Bundle bundle => BuildBundleItems(bundle),
			BrowserItemKind.Collection when container.Target is AssetCollection col => BuildCollectionItems(col),
			BrowserItemKind.Scene when container.Target is SceneDefinition scene => BuildSceneItems(scene),
			BrowserItemKind.ScenesGroup when container.Target is List<SceneDefinition> scenes => BuildScenesGroupItems(scenes),
			BrowserItemKind.SearchGroup when container.Target is List<SearchHit> hits => BuildSearchHitItems(hits),
			BrowserItemKind.PathFolder when container.Target is List<BrowserItem> children => children,
			_ => [],
		};
	}

	private static List<BrowserItem> BuildArchiveItems(GameArchive archive)
	{
		// Plugin relationships come from the archive index; listing must not decode any entries.
		Dictionary<int, GameArchiveEntry[]> companions = archive
			.Entries.Where(entry => entry.IsSerialized)
			.ToDictionary(
				entry => entry.Index,
				entry => archive.GetResources(entry).Where(resource => !resource.IsSerialized).DistinctBy(resource => resource.Index).ToArray()
			);
		HashSet<int> boundResources = companions.Values.SelectMany(resources => resources).Select(resource => resource.Index).ToHashSet();
		List<BrowserItem> items = new();
		foreach (GameArchiveEntry entry in archive.Entries)
		{
			if (boundResources.Contains(entry.Index))
			{
				continue;
			}
			GameArchiveEntry[] resources = companions.GetValueOrDefault(entry.Index) ?? [];
			items.Add(
				new BrowserItem
				{
					Name = entry.Name,
					Kind = BrowserItemKind.MountedEntry,
					Icon = entry.IsSerialized ? IconKind.Library : IconKind.Data,
					IsContainer = entry.IsSerialized,
					Target = (archive, entry),
					Meta = resources.Length > 0 ? $"{resources.Length + 1} files" : AssetContentService.FormatBytes(entry.Length),
					CompanionNames = resources.Select(resource => resource.Name).ToArray(),
				}
			);
		}
		return items;
	}

	public static List<BrowserItem> BuildBundleItems(Bundle bundle)
	{
		List<BrowserItem> items = new();
		Dictionary<AssetCollection, ResourceFile[]> companions = ResourceBindings.FindCompanions(bundle);
		HashSet<ResourceFile> boundResources = companions.Values.SelectMany(resources => resources).ToHashSet();

		if (bundle.Bundles.Count > 0)
		{
			foreach (Bundle child in bundle.Bundles.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
			{
				int count = child.Bundles.Count + child.Collections.Count + child.Resources.Count + child.FailedFiles.Count;
				items.Add(
					new BrowserItem
					{
						Name = string.IsNullOrEmpty(child.Name) ? "(Unnamed Bundle)" : child.Name,
						Kind = BrowserItemKind.Bundle,
						Icon = IconKind.Archive,
						IsContainer = true,
						Target = child,
						Meta = count.ToString(),
					}
				);
			}
		}

		if (bundle.Collections.Count > 0)
		{
			foreach (AssetCollection col in bundle.Collections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
			{
				if (col.Count == 0 && col is not SerializedAssetCollection)
				{
					continue;
				}
				items.Add(
					new BrowserItem
					{
						Name = string.IsNullOrEmpty(col.Name) ? "(Unnamed Collection)" : col.Name,
						Kind = BrowserItemKind.Collection,
						Icon = IconKind.Library,
						IsContainer = true,
						Target = col,
						Meta = companions.TryGetValue(col, out ResourceFile[]? resources) ? $"{resources.Length + 1} files" : col.Count.ToString(),
						CompanionNames = resources?.Select(resource => resource.Name).ToArray() ?? [],
					}
				);
			}
		}

		if (bundle.Resources.Count > 0)
		{
			foreach (ResourceFile res in bundle.Resources.OrderBy(r => r.NameFixed, StringComparer.OrdinalIgnoreCase))
			{
				if (boundResources.Contains(res))
				{
					continue;
				}
				long size = res.Stream.CanSeek ? res.Stream.Length : 0;
				items.Add(
					new BrowserItem
					{
						Name = res.NameFixed,
						Kind = BrowserItemKind.ResourceFile,
						Icon = IconKind.Data,
						IsContainer = false,
						Target = res,
						Meta = size > 0 ? AssetContentService.FormatBytes(size) : "",
					}
				);
			}
		}

		if (bundle.FailedFiles.Count > 0)
		{
			foreach (FailedFile failed in bundle.FailedFiles.OrderBy(f => f.NameFixed, StringComparer.OrdinalIgnoreCase))
			{
				items.Add(
					new BrowserItem
					{
						Name = failed.NameFixed,
						Kind = BrowserItemKind.FailedFile,
						Icon = IconKind.Alert,
						IsContainer = false,
						Target = failed,
						Meta = "Failed",
					}
				);
			}
		}

		return items;
	}

	public static List<BrowserItem> BuildCollectionItems(AssetCollection collection)
	{
		List<BrowserItem> items = new(collection.Count);
		foreach (IUnityObjectBase asset in collection)
		{
			items.Add(CreateAssetItem(asset));
		}
		items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		return items;
	}

	public static List<BrowserItem> BuildSceneItems(SceneDefinition scene)
	{
		List<BrowserItem> items = new();
		foreach (AssetCollection col in scene.Collections)
		{
			items.Add(
				new BrowserItem
				{
					Name = col.Name,
					Kind = BrowserItemKind.Collection,
					Icon = IconKind.Library,
					IsContainer = true,
					Target = col,
					Meta = col.Count.ToString(),
				}
			);
		}
		return items;
	}

	public static List<BrowserItem> BuildScenesGroupItems(List<SceneDefinition> scenes)
	{
		List<BrowserItem> items = new(scenes.Count);
		foreach (SceneDefinition scene in scenes)
		{
			items.Add(
				new BrowserItem
				{
					Name = scene.Name,
					Kind = BrowserItemKind.Scene,
					Icon = IconKind.Scene,
					IsContainer = true,
					Target = scene,
					Meta = scene.Collections.Count.ToString(),
					SecondaryPath = scene.Path,
				}
			);
		}
		return items;
	}

	public static List<BrowserItem> BuildSearchHitItems(List<SearchHit> hits)
	{
		List<BrowserItem> items = new(hits.Count);
		foreach (SearchHit hit in hits)
		{
			items.Add(CreateAssetItem(hit.Asset, hit.Collection.Name));
		}
		return items;
	}

	public static BrowserItem CreateAssetItem(IUnityObjectBase asset, string? collectionLabel = null, string? displayName = null)
	{
		string name = displayName ?? asset.GetBestName();
		if (string.IsNullOrWhiteSpace(name))
		{
			name = $"{asset.ClassName} #{asset.PathID}";
		}
		return new BrowserItem
		{
			Name = name,
			Kind = BrowserItemKind.Asset,
			Icon = BrowserItem.ClassifyAssetIcon(asset),
			IsContainer = false,
			Target = asset,
			ClassName = asset.ClassName,
			Meta = asset.ClassName,
			SecondaryPath = collectionLabel,
		};
	}
}
