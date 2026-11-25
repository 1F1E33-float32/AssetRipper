using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;

namespace AssetRipper.GUI.Avalonia.Theme;

internal enum IconKind
{
	// UI / Titlebar / Controls
	WindowMinimize,
	WindowMaximize,
	WindowRestore,
	WindowClose,
	Home,
	ChevronLeft,
	ChevronRight,
	FolderOpen,
	FilePlus,
	Export,
	Dataset,
	Import,
	Sidebar,
	Settings,
	Check,
	Alert,
	Spinner,
	Copy,
	Play,
	Pause,
	ZoomIn,
	ZoomOut,
	Search,

	// Semantic file and asset types
	Folder,
	Archive,
	Image,
	Model,
	Audio,
	Video,
	Script,
	Text,
	Config,
	Code,
	Application,
	Library,
	Font,
	Document,
	Data,
	File,

	// Unity object glyphs have matching SVG resources in Assets/Icons/Unity.
	GameObject,
	Transform,
	RectTransform,
	Mesh,
	MeshRenderer,
	SkinnedMeshRenderer,
	Material,
	Shader,
	Texture,
	Cubemap,
	TextureArray,
	RenderTexture,
	Sprite,
	SpriteAtlas,
	Camera,
	Light,
	LightProbe,
	ReflectionProbe,
	AnimationClip,
	Animator,
	Avatar,
	ParticleSystem,
	TrailRenderer,
	LineRenderer,
	AudioSource,
	AudioMixer,
	Collider,
	Rigidbody,
	Joint,
	Terrain,
	NavMesh,
	Canvas,
	MonoBehaviour,
	AssetBundle,
	Scene,
	LODGroup,
	Prefab,
	PhysicsMaterial,
	Component,
}

/// <summary>
/// Shared vector glyphs for controls, files, and Unity objects.
/// </summary>
internal static class Icons
{
	public static readonly ISolidColorBrush FolderColor = new SolidColorBrush(Color.Parse("#58A6F6"));
	public static readonly ISolidColorBrush ArchiveColor = new SolidColorBrush(Color.Parse("#E5B567"));
	public static readonly ISolidColorBrush ImageColor = new SolidColorBrush(Color.Parse("#7ECF9C"));
	public static readonly ISolidColorBrush ModelColor = new SolidColorBrush(Color.Parse("#82B9E8"));
	public static readonly ISolidColorBrush AudioColor = new SolidColorBrush(Color.Parse("#E995BD"));
	public static readonly ISolidColorBrush VideoColor = new SolidColorBrush(Color.Parse("#B6A0EC"));
	public static readonly ISolidColorBrush ScriptColor = new SolidColorBrush(Color.Parse("#F0BF7A"));
	public static readonly ISolidColorBrush TextColor = new SolidColorBrush(Color.Parse("#B7C4D5"));
	public static readonly ISolidColorBrush ConfigColor = new SolidColorBrush(Color.Parse("#9EAFC5"));
	public static readonly ISolidColorBrush CodeColor = new SolidColorBrush(Color.Parse("#70C9C5"));
	public static readonly ISolidColorBrush ApplicationColor = new SolidColorBrush(Color.Parse("#79B6F2"));
	public static readonly ISolidColorBrush LibraryColor = new SolidColorBrush(Color.Parse("#A3A6D9"));
	public static readonly ISolidColorBrush FontColor = new SolidColorBrush(Color.Parse("#DAADD5"));
	public static readonly ISolidColorBrush DocumentColor = new SolidColorBrush(Color.Parse("#E8948E"));
	public static readonly ISolidColorBrush DataColor = new SolidColorBrush(Color.Parse("#C2B08C"));
	public static readonly ISolidColorBrush FileColor = new SolidColorBrush(Color.Parse("#A1A1AA"));

	private static readonly Dictionary<IconKind, Geometry> StrokeGeometries = new();
	private static readonly Dictionary<IconKind, Geometry> FillGeometries = new();

	public static ISolidColorBrush GetSemanticBrush(IconKind kind) =>
		kind switch
		{
			IconKind.Folder => FolderColor,
			IconKind.Archive => ArchiveColor,
			IconKind.Image => ImageColor,
			IconKind.Model => ModelColor,
			IconKind.Audio => AudioColor,
			IconKind.Video => VideoColor,
			IconKind.Script => ScriptColor,
			IconKind.Text => TextColor,
			IconKind.Config => ConfigColor,
			IconKind.Code => CodeColor,
			IconKind.Application => ApplicationColor,
			IconKind.Library => LibraryColor,
			IconKind.Font => FontColor,
			IconKind.Document => DocumentColor,
			IconKind.Data => DataColor,
			IconKind.Alert => Palette.Danger,
			IconKind.Check => Palette.Success,
			IconKind.GameObject or IconKind.Prefab or IconKind.Scene => ApplicationColor,
			IconKind.Transform or IconKind.RectTransform or IconKind.Shader or IconKind.NavMesh => CodeColor,
			IconKind.Mesh or IconKind.MeshRenderer or IconKind.SkinnedMeshRenderer or IconKind.LODGroup => ModelColor,
			IconKind.Material or IconKind.ReflectionProbe or IconKind.Animator or IconKind.AnimationClip or IconKind.Avatar => VideoColor,
			IconKind.Texture
			or IconKind.TextureArray
			or IconKind.Cubemap
			or IconKind.RenderTexture
			or IconKind.Sprite
			or IconKind.SpriteAtlas
			or IconKind.Terrain => ImageColor,
			IconKind.Camera or IconKind.Canvas => ApplicationColor,
			IconKind.Light or IconKind.LightProbe or IconKind.ParticleSystem or IconKind.TrailRenderer or IconKind.LineRenderer => ArchiveColor,
			IconKind.AudioSource or IconKind.AudioMixer => AudioColor,
			IconKind.Collider or IconKind.Rigidbody or IconKind.Joint or IconKind.PhysicsMaterial => DocumentColor,
			IconKind.MonoBehaviour => ScriptColor,
			IconKind.AssetBundle => ArchiveColor,
			IconKind.Component => LibraryColor,
			_ => FileColor,
		};

	public static Control Glyph(IconKind kind, double size, IBrush brush)
	{
		(double viewBox, double strokeWidth, bool filled) = GetMetrics(kind);
		Canvas canvas = new() { Width = viewBox, Height = viewBox };

		if (filled)
		{
			Geometry fillGeom = GetOrCreateFillGeometry(kind);
			canvas.Children.Add(
				new global::Avalonia.Controls.Shapes.Path
				{
					Data = fillGeom,
					Fill = brush,
					Stroke = brush,
					StrokeThickness = strokeWidth,
					StrokeLineCap = PenLineCap.Round,
					StrokeJoin = PenLineJoin.Round,
				}
			);
		}
		else
		{
			Geometry strokeGeom = GetOrCreateStrokeGeometry(kind);
			canvas.Children.Add(
				new global::Avalonia.Controls.Shapes.Path
				{
					Data = strokeGeom,
					Stroke = brush,
					StrokeThickness = strokeWidth,
					StrokeLineCap = PenLineCap.Round,
					StrokeJoin = PenLineJoin.Round,
				}
			);

			if (kind == IconKind.Settings)
			{
				// Settings gear teeth require a heavier 2.5px stroke.
				canvas.Children.Add(
					new global::Avalonia.Controls.Shapes.Path
					{
						Data = Geometry.Parse(
							"M10 2v2m0 12v2M2 10h2m12 0h2M4.34 4.34l1.42 1.42m8.48 8.48 1.42 1.42M4.34 15.66l1.42-1.42m8.48-8.48 1.42-1.42"
						),
						Stroke = brush,
						StrokeThickness = 2.5,
						StrokeLineCap = PenLineCap.Round,
						StrokeJoin = PenLineJoin.Round,
					}
				);
			}
		}

		return new Viewbox
		{
			Width = size,
			Height = size,
			Stretch = Stretch.Uniform,
			Child = canvas,
			IsHitTestVisible = false,
		};
	}

	public static Control FileIcon(IconKind kind, bool selectedOnAccent, double size = 17.0)
	{
		IBrush brush = selectedOnAccent ? Palette.White : GetSemanticBrush(kind);
		return Glyph(kind, size, brush);
	}

	private static (double ViewBox, double StrokeWidth, bool Filled) GetMetrics(IconKind kind) =>
		kind switch
		{
			IconKind.WindowMinimize or IconKind.WindowMaximize or IconKind.WindowRestore or IconKind.WindowClose => (12.0, 1.0, false),
			IconKind.Model => (24.0, 1.7, false),
			IconKind.Dataset => (24.0, 1.8, false),
			IconKind.Spinner => (20.0, 2.0, false),
			IconKind.Play or IconKind.Pause => (20.0, 1.5, true),
			_ => (20.0, 1.5, false),
		};

	private static Geometry GetOrCreateFillGeometry(IconKind kind)
	{
		if (!FillGeometries.TryGetValue(kind, out Geometry? geometry))
		{
			string data = kind switch
			{
				IconKind.Play => "M6.5 4.5v11l9-5.5z",
				IconKind.Pause => "M6 4.5h2.5v11H6zM11.5 4.5H14v11h-2.5z",
				_ => "",
			};
			geometry = Geometry.Parse(data);
			FillGeometries[kind] = geometry;
		}
		return geometry;
	}

	private static Geometry GetOrCreateStrokeGeometry(IconKind kind)
	{
		if (StrokeGeometries.TryGetValue(kind, out Geometry? existing))
		{
			return existing;
		}

		GeometryGroup group = new();
		switch (kind)
		{
			case >= IconKind.GameObject and <= IconKind.Component:
				// These SVGs use only paths, a 20x20 viewBox, and the shared 1.5px rounded stroke.
				// Apply the caller's brush so selection and disabled colors also work for SVG glyphs.
				using (Stream stream = AssetLoader.Open(new Uri($"avares://AssetRipper.GUI.Avalonia/Assets/Icons/Unity/{kind}.svg")))
				{
					XElement svg = XElement.Load(stream);
					foreach (XElement path in svg.Elements(XName.Get("path", "http://www.w3.org/2000/svg")))
					{
						group.Children.Add(
							Geometry.Parse((string?)path.Attribute("d") ?? throw new InvalidDataException($"SVG path is missing in {kind}."))
						);
					}
				}
				break;
			case IconKind.WindowClose:
				group.Children.Add(Geometry.Parse("m1.5 1.5 9 9m-9 0 9-9"));
				break;
			case IconKind.WindowMaximize:
				group.Children.Add(Geometry.Parse("M1.5 1.5h9v9h-9z"));
				break;
			case IconKind.WindowMinimize:
				group.Children.Add(Geometry.Parse("M1 6h10"));
				break;
			case IconKind.WindowRestore:
				group.Children.Add(Geometry.Parse("M1.5 3.5h7v7h-7zM3.5 3V1.5h7v7H9"));
				break;
			case IconKind.Home:
				group.Children.Add(Geometry.Parse("M3 9.5 10 3.5l7 6M5 8v8.5h3.5V12h3v4.5H15V8"));
				break;
			case IconKind.ChevronLeft:
				group.Children.Add(Geometry.Parse("M12.5 4.5 7 10l5.5 5.5"));
				break;
			case IconKind.ChevronRight:
				group.Children.Add(Geometry.Parse("M7.5 4.5 13 10l-5.5 5.5"));
				break;
			case IconKind.FolderOpen:
				group.Children.Add(Geometry.Parse("M2.5 15.5v-11h5l2 2h6V9M2.5 15.5 5 9h13l-2.5 6.5z"));
				break;
			case IconKind.FilePlus:
				group.Children.Add(Geometry.Parse("M5 2.5h6.5L16 7v10.5H5zM11.5 2.5V7H16M10.5 10v5M8 12.5h5"));
				break;
			case IconKind.Export:
				group.Children.Add(Geometry.Parse("M10 12.5V3M6.5 6.5 10 3l3.5 3.5M3.5 11v5.5h13V11"));
				break;
			case IconKind.Import:
				group.Children.Add(Geometry.Parse("M10 3v9M6.5 8.5 10 12l3.5-3.5M3.5 11v5.5h13V11"));
				break;
			case IconKind.Dataset:
				group.Children.Add(new EllipseGeometry(new Rect(4, 2, 16, 6)));
				group.Children.Add(Geometry.Parse("M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"));
				break;
			case IconKind.Sidebar:
				group.Children.Add(new RectangleGeometry(new Rect(2.5, 3.5, 15, 13), 2, 2));
				group.Children.Add(Geometry.Parse("M12 3.5v13"));
				break;
			case IconKind.Settings:
				group.Children.Add(new EllipseGeometry(new Rect(7.5, 7.5, 5, 5)));
				group.Children.Add(new EllipseGeometry(new Rect(4, 4, 12, 12)));
				break;
			case IconKind.Check:
				group.Children.Add(Geometry.Parse("M4.5 10.5 8 14l7.5-8"));
				break;
			case IconKind.Alert:
				group.Children.Add(new EllipseGeometry(new Rect(2.75, 2.75, 14.5, 14.5)));
				group.Children.Add(Geometry.Parse("M10 6v4.5M10 13.5v.25"));
				break;
			case IconKind.Spinner:
				group.Children.Add(Geometry.Parse("M10 2.75A7.25 7.25 0 1 1 2.75 10"));
				break;
			case IconKind.Copy:
				group.Children.Add(new RectangleGeometry(new Rect(7, 7, 10, 10), 1.5, 1.5));
				group.Children.Add(Geometry.Parse("M13 7V4.5A1.5 1.5 0 0 0 11.5 3h-7A1.5 1.5 0 0 0 3 4.5v7A1.5 1.5 0 0 0 4.5 13H7"));
				break;
			case IconKind.ZoomIn:
				group.Children.Add(Geometry.Parse("M10 5v10M5 10h10"));
				break;
			case IconKind.ZoomOut:
				group.Children.Add(Geometry.Parse("M5 10h10"));
				break;
			case IconKind.Search:
				group.Children.Add(new EllipseGeometry(new Rect(3.5, 3.5, 10, 10)));
				group.Children.Add(Geometry.Parse("M12.2 12.2 16.5 16.5"));
				break;
			case IconKind.Folder:
				group.Children.Add(Geometry.Parse("M2 6V4.5h6l2 2H18v10H2zM2 6.5h8"));
				break;
			case IconKind.Archive:
				group.Children.Add(Geometry.Parse("M3 6h14v11H3zM2.5 3h15v3h-15zM9 8h2M9 10.5h2"));
				group.Children.Add(new RectangleGeometry(new Rect(8.5, 13, 3, 2), 0.5, 0.5));
				break;
			case IconKind.Image:
				group.Children.Add(new RectangleGeometry(new Rect(2.5, 3, 15, 14), 2, 2));
				group.Children.Add(new EllipseGeometry(new Rect(5.2, 5.7, 2.6, 2.6)));
				group.Children.Add(Geometry.Parse("m3 14 4-4 3 3 3-5 4 6"));
				break;
			case IconKind.Model:
				group.Children.Add(Geometry.Parse("m12 3 9 5v8l-9 5-9-5V8l9-5Zm0 18v-9m-9-4 9 4 9-4M7.5 5.5l9 5"));
				break;
			case IconKind.Audio:
				group.Children.Add(Geometry.Parse("M8 14V5l8-2v9M8 8l8-2"));
				group.Children.Add(new EllipseGeometry(new Rect(3, 13, 5, 4)));
				group.Children.Add(new EllipseGeometry(new Rect(11, 11, 5, 4)));
				break;
			case IconKind.Video:
				group.Children.Add(new RectangleGeometry(new Rect(2.5, 3, 15, 14), 1.5, 1.5));
				group.Children.Add(Geometry.Parse("M5.5 3v14M14.5 3v14M2.5 7h3M2.5 12h3M14.5 7h3M14.5 12h3"));
				group.Children.Add(Geometry.Parse("m8 7 4 3-4 3z"));
				break;
			case IconKind.Script:
				group.Children.Add(Geometry.Parse("M5 2.5h6.5L16 7v10.5H5zM11.5 2.5V7H16"));
				group.Children.Add(Geometry.Parse("m7 10 2 2-2 2M11 14h2"));
				break;
			case IconKind.Text:
				group.Children.Add(Geometry.Parse("M5 2.5h6.5L16 7v10.5H5zM11.5 2.5V7H16"));
				group.Children.Add(Geometry.Parse("M7.5 10h6M7.5 12.5h6M7.5 15h4"));
				break;
			case IconKind.Config:
				group.Children.Add(Geometry.Parse("M3 5h5m4 0h5M3 10h9m4 0h1M3 15h2m4 0h8"));
				group.Children.Add(new EllipseGeometry(new Rect(8, 3, 4, 4)));
				group.Children.Add(new EllipseGeometry(new Rect(12, 8, 4, 4)));
				group.Children.Add(new EllipseGeometry(new Rect(5, 13, 4, 4)));
				break;
			case IconKind.Code:
				group.Children.Add(Geometry.Parse("m6 5-4 5 4 5m8-10 4 5-4 5M11.5 3.5l-3 13"));
				break;
			case IconKind.Application:
				group.Children.Add(new RectangleGeometry(new Rect(2.5, 3, 15, 14), 2, 2));
				group.Children.Add(Geometry.Parse("M2.5 7h15M5 5h.5M8 5h.5m-.5 5 5 2.5L8 15z"));
				break;
			case IconKind.Library:
				group.Children.Add(new RectangleGeometry(new Rect(5, 5, 10, 10), 1.5, 1.5));
				group.Children.Add(new RectangleGeometry(new Rect(8, 8, 4, 4), 0.5, 0.5));
				group.Children.Add(Geometry.Parse("M8 2v3m4-3v3M8 15v3m4-3v3M2 8h3m-3 4h3M15 8h3m-3 4h3"));
				break;
			case IconKind.Font:
				group.Children.Add(Geometry.Parse("m2.5 16 5-12 5 12M4.5 11h6m3 0c0-3 4-3 4 0v5m0-4h-2a2 2 0 1 0 2 2"));
				break;
			case IconKind.Document:
				group.Children.Add(Geometry.Parse("M5 2.5h6.5L16 7v10.5H5zM11.5 2.5V7H16"));
				group.Children.Add(Geometry.Parse("M7.5 10h6M7.5 12.5h3"));
				group.Children.Add(Geometry.Parse("m11.5 14.5 1.5-1 1.5 1v3l-1.5-1-1.5 1z"));
				break;
			case IconKind.Data:
				group.Children.Add(new EllipseGeometry(new Rect(3.5, 2, 13, 5)));
				group.Children.Add(Geometry.Parse("M3.5 4.5v11c0 3.3 13 3.3 13 0v-11M3.5 10c0 3.3 13 3.3 13 0"));
				break;
			default: // File
				group.Children.Add(Geometry.Parse("M5 2.5h6.5L16 7v10.5H5zM11.5 2.5V7H16"));
				break;
		}

		StrokeGeometries[kind] = group;
		return group;
	}
}
