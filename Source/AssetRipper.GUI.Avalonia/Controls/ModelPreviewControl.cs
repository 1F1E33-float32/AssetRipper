using System.Numerics;
using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>
/// Interactive 3D mesh inspector with orbit, pan, zoom, vertex colors, wireframe, and fit controls,
/// ported directly from vnlens-gui/src/workspace/model_view.rs.
/// </summary>
internal sealed class ModelPreviewControl : Grid
{
	private readonly TextBlock _statsText;
	private readonly ModelCanvas _canvas;
	private readonly VnlensButton _colorsBtn;
	private readonly VnlensButton _wireframeBtn;
	private readonly VnlensButton _fitBtn;

	public ModelPreviewControl()
	{
		RowDefinitions = new RowDefinitions("Auto,*,40");

		_statsText = new TextBlock
		{
			Margin = new Thickness(12, 4),
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Foreground = Palette.Secondary,
		};
		Grid.SetRow(_statsText, 0);
		Children.Add(_statsText);

		_canvas = new ModelCanvas();
		Grid.SetRow(_canvas, 1);
		Children.Add(_canvas);

		// Bottom 40px control bar (matching model_view.rs lines 208-253)
		StackPanel bar = new()
		{
			Height = 40,
			Margin = new Thickness(12, 0),
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 8,
		};
		Grid.SetRow(bar, 2);

		_colorsBtn = new VnlensButton(label: "Colors", active: false, enabled: true, ghost: false);
		GlassSurface.SetTooltip(_colorsBtn, "Show native vertex colors");
		_colorsBtn.Clicked += () =>
		{
			_canvas.VertexColors = !_canvas.VertexColors;
			_colorsBtn.IsActive = _canvas.VertexColors;
		};

		_wireframeBtn = new VnlensButton(label: "Wireframe", active: false, enabled: true, ghost: false);
		GlassSurface.SetTooltip(_wireframeBtn, "Switch shaded and wireframe rendering");
		_wireframeBtn.Clicked += () =>
		{
			_canvas.Wireframe = !_canvas.Wireframe;
			_wireframeBtn.SetLabel(_canvas.Wireframe ? "Solid" : "Wireframe");
		};

		_fitBtn = new VnlensButton(label: "Fit", active: false, enabled: true, ghost: false);
		GlassSurface.SetTooltip(_fitBtn, "Reset camera · drag to orbit · right-drag to pan · scroll to zoom");
		_fitBtn.Clicked += () => _canvas.ResetCamera();

		bar.Children.Add(_colorsBtn);
		bar.Children.Add(_wireframeBtn);
		bar.Children.Add(_fitBtn);
		Children.Add(bar);
	}

	public void SetModel(ModelGeometry? geometry)
	{
		if (geometry is null)
		{
			_statsText.Text = "";
			_canvas.SetGeometry(null);
			return;
		}
		int triangles = geometry.TriangleIndices.Length / 3;
		_statsText.Text = $"{geometry.Positions.Length:N0} vertices · {triangles:N0} triangles";
		_colorsBtn.IsActionEnabled = geometry.Colors is not null;
		_colorsBtn.IsActive = false;
		_wireframeBtn.SetLabel("Wireframe");
		_canvas.SetGeometry(geometry);
	}

	private sealed class ModelCanvas : Control
	{
		private ModelGeometry? _geometry;
		private WriteableBitmap? _framebuffer;
		private float[] _zBuffer = [];
		private Vector3[] _screenPositions = [];
		private Vector3[] _viewNormals = [];
		private bool[] _visible = [];

		private float _yaw = 0.6f;
		private float _pitch = 0.35f;
		private float _distance = 2.8f;
		private Vector2 _pan;
		private bool _wireframe;
		private bool _vertexColors;

		private bool _dragging;
		private bool _dragPan;
		private Point _dragStart;
		private float _startYaw;
		private float _startPitch;
		private Vector2 _startPan;

		public bool Wireframe
		{
			get => _wireframe;
			set { _wireframe = value; Redraw(); }
		}

		public bool VertexColors
		{
			get => _vertexColors;
			set { _vertexColors = value; Redraw(); }
		}

		public ModelCanvas()
		{
			ClipToBounds = true;
		}

		public void SetGeometry(ModelGeometry? geometry)
		{
			_geometry = geometry;
			_wireframe = false;
			_vertexColors = false;
			ResetCamera();
		}

		public void ResetCamera()
		{
			_yaw = 0.6f;
			_pitch = 0.35f;
			_distance = 2.8f;
			_pan = Vector2.Zero;
			Redraw();
		}

		protected override void OnSizeChanged(SizeChangedEventArgs e)
		{
			base.OnSizeChanged(e);
			Redraw();
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
		{
			base.OnPointerWheelChanged(e);
			if (_geometry is null)
			{
				return;
			}
			_distance = Math.Clamp(_distance * MathF.Exp((float)(-e.Delta.Y * 0.14)), 0.15f, 200f);
			Redraw();
			e.Handled = true;
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (_geometry is null)
			{
				return;
			}
			PointerPointProperties props = e.GetCurrentPoint(this).Properties;
			if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed && !props.IsMiddleButtonPressed)
			{
				return;
			}
			_dragging = true;
			_dragPan = !props.IsLeftButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
			_dragStart = e.GetPosition(this);
			_startYaw = _yaw;
			_startPitch = _pitch;
			_startPan = _pan;
			e.Pointer.Capture(this);
			e.Handled = true;
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (!_dragging)
			{
				return;
			}
			Point pos = e.GetPosition(this);
			double dx = pos.X - _dragStart.X;
			double dy = pos.Y - _dragStart.Y;
			if (_dragPan)
			{
				float h = MathF.Max(1f, (float)Bounds.Height);
				float scale = _distance * 2f * MathF.Tan(MathF.PI / 8f) / h;
				_pan = new Vector2(_startPan.X + (float)dx * scale, _startPan.Y - (float)dy * scale);
			}
			else
			{
				_yaw = _startYaw + (float)dx * 0.008f;
				_pitch = Math.Clamp(_startPitch + (float)dy * 0.008f, -1.55f, 1.55f);
			}
			Redraw();
			e.Handled = true;
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (_dragging)
			{
				_dragging = false;
				e.Pointer.Capture(null);
				e.Handled = true;
			}
		}

		private void Redraw()
		{
			RenderSoftware();
			InvalidateVisual();
		}

		private void RenderSoftware()
		{
			if (_geometry is null || Bounds.Width < 4 || Bounds.Height < 4)
			{
				return;
			}

			// Render at crisp resolution capped for fluid 60fps interaction
			int w = Math.Clamp((int)Bounds.Width, 16, 1280);
			int h = Math.Clamp((int)Bounds.Height, 16, 960);

			if (_framebuffer is null || _framebuffer.PixelSize.Width != w || _framebuffer.PixelSize.Height != h)
			{
				_framebuffer?.Dispose();
				_framebuffer = new WriteableBitmap(new PixelSize(w, h), new global::Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
				_zBuffer = new float[w * h];
			}

			Array.Fill(_zBuffer, float.PositiveInfinity);

			Vector3[] positions = _geometry.Positions;
			Vector3[] normals = _geometry.Normals;
			Vector4[]? colors = _vertexColors ? _geometry.Colors : null;
			int[] indices = _geometry.TriangleIndices;
			int vCount = positions.Length;

			if (_screenPositions.Length < vCount)
			{
				_screenPositions = new Vector3[vCount];
				_viewNormals = new Vector3[vCount];
				_visible = new bool[vCount];
			}

			// Build camera matrix (normalized by model radius)
			float invRadius = 1f / _geometry.Radius;
			Vector3 center = _geometry.Center;

			Matrix4x4 rotY = Matrix4x4.CreateRotationY(_yaw);
			Matrix4x4 rotX = Matrix4x4.CreateRotationX(_pitch);
			Matrix4x4 rot = rotY * rotX;

			float fov = MathF.PI / 4f;
			float focal = (h * 0.5f) / MathF.Tan(fov * 0.5f);
			float halfW = w * 0.5f;
			float halfH = h * 0.5f;

			for (int i = 0; i < vCount; i++)
			{
				Vector3 p = (positions[i] - center) * invRadius;
				Vector3 v = Vector3.Transform(p, rot);
				v.X += _pan.X;
				v.Y += _pan.Y;
				v.Z -= _distance;

				float depth = -v.Z;
				if (depth < 0.05f)
				{
					_visible[i] = false;
					continue;
				}
				_visible[i] = true;
				float invZ = focal / depth;
				_screenPositions[i] = new Vector3(halfW + v.X * invZ, halfH - v.Y * invZ, depth);
				_viewNormals[i] = Vector3.TransformNormal(normals[i], rot);
			}

			Vector3 keyLight = Vector3.Normalize(new Vector3(0.45f, 0.72f, 0.65f));
			Vector3 fillLight = Vector3.Normalize(new Vector3(-0.6f, -0.25f, 0.4f));

			using ILockedFramebuffer fb = _framebuffer.Lock();
			unsafe
			{
				uint* pixels = (uint*)fb.Address;
				int stride = fb.RowBytes / 4;

				// Clear to PANEL background (#242426)
				uint clearColor = unchecked(0xFF242426u);
				for (int y = 0; y < h; y++)
				{
					uint* row = pixels + y * stride;
					for (int x = 0; x < w; x++)
					{
						row[x] = clearColor;
					}
				}

				int triCount = indices.Length / 3;
				uint wireColor = unchecked(0xFF9DCAFFu);

				for (int t = 0; t < triCount; t++)
				{
					int ia = indices[t * 3];
					int ib = indices[t * 3 + 1];
					int ic = indices[t * 3 + 2];
					if (!_visible[ia] || !_visible[ib] || !_visible[ic])
					{
						continue;
					}

					Vector3 a = _screenPositions[ia];
					Vector3 b = _screenPositions[ib];
					Vector3 c = _screenPositions[ic];

					if (_wireframe)
					{
						DrawLine(pixels, stride, w, h, a, b, wireColor);
						DrawLine(pixels, stride, w, h, b, c, wireColor);
						DrawLine(pixels, stride, w, h, c, a, wireColor);
						continue;
					}

					float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
					if (MathF.Abs(area) < 0.01f)
					{
						continue;
					}

					int minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), 0, w - 1);
					int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, w - 1);
					int minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), 0, h - 1);
					int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, h - 1);

					// Flat-interpolated smooth normal per triangle for crisp performance
					Vector3 n = _viewNormals[ia] + _viewNormals[ib] + _viewNormals[ic];
					float nLen = n.Length();
					n = nLen > 1e-5f ? n / nLen : Vector3.UnitZ;
					if (n.Z < 0)
					{
						n = -n;
					}

					float key = MathF.Max(0f, Vector3.Dot(n, keyLight));
					float fill = MathF.Max(0f, Vector3.Dot(n, fillLight)) * 0.35f;
					float hemi = 0.22f + 0.14f * (n.Y * 0.5f + 0.5f);
					float light = MathF.Min(1.0f, hemi + key * 0.68f + fill);

					float baseR = 0.78f, baseG = 0.81f, baseB = 0.86f;
					if (colors is not null)
					{
						Vector4 vc = (colors[ia] + colors[ib] + colors[ic]) * (1f / 3f);
						baseR = Math.Clamp(vc.X, 0f, 1f);
						baseG = Math.Clamp(vc.Y, 0f, 1f);
						baseB = Math.Clamp(vc.Z, 0f, 1f);
					}

					uint pr = (uint)Math.Clamp((int)(baseR * light * 255f), 0, 255);
					uint pg = (uint)Math.Clamp((int)(baseG * light * 255f), 0, 255);
					uint pb = (uint)Math.Clamp((int)(baseB * light * 255f), 0, 255);
					uint shaded = unchecked(0xFF000000u | (pr << 16) | (pg << 8) | pb);

					float invArea = 1f / area;
					for (int y = minY; y <= maxY; y++)
					{
						float py = y + 0.5f;
						uint* row = pixels + y * stride;
						int zRow = y * w;
						for (int x = minX; x <= maxX; x++)
						{
							float px = x + 0.5f;
							float w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * invArea;
							float w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * invArea;
							float w2 = 1f - w0 - w1;
							if (w0 < 0 || w1 < 0 || w2 < 0)
							{
								continue;
							}
							float z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
							int zi = zRow + x;
							if (z < _zBuffer[zi])
							{
								_zBuffer[zi] = z;
								row[x] = shaded;
							}
						}
					}
				}
			}
		}

		private unsafe void DrawLine(uint* pixels, int stride, int w, int h, Vector3 p0, Vector3 p1, uint color)
		{
			int x0 = (int)MathF.Round(p0.X);
			int y0 = (int)MathF.Round(p0.Y);
			int x1 = (int)MathF.Round(p1.X);
			int y1 = (int)MathF.Round(p1.Y);

			int dx = Math.Abs(x1 - x0);
			int sx = x0 < x1 ? 1 : -1;
			int dy = -Math.Abs(y1 - y0);
			int sy = y0 < y1 ? 1 : -1;
			int err = dx + dy;
			int steps = Math.Min(dx - dy + 1, 2500);

			for (int s = 0; s < steps; s++)
			{
				if ((uint)x0 < (uint)w && (uint)y0 < (uint)h)
				{
					pixels[y0 * stride + x0] = color;
				}
				if (x0 == x1 && y0 == y1)
				{
					break;
				}
				int e2 = 2 * err;
				if (e2 >= dy) { err += dy; x0 += sx; }
				if (e2 <= dx) { err += dx; y0 += sy; }
			}
		}

		public override void Render(DrawingContext context)
		{
			if (_framebuffer is not null)
			{
				context.DrawImage(
					_framebuffer,
					new Rect(0, 0, _framebuffer.PixelSize.Width, _framebuffer.PixelSize.Height),
					new Rect(0, 0, Bounds.Width, Bounds.Height));
			}
		}
	}
}
