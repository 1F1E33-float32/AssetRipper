using AssetRipper.Assets;
using AssetRipper.GUI.Avalonia.Browser;
using AssetRipper.GUI.Avalonia.Controls;
using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Localizations;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AssetRipper.GUI.Avalonia.Views;

internal enum InspectorMode
{
	Preview,
	Info,
	Yaml,
	Json,
	Hex,
	Dependencies,
}

/// <summary>Right-hand inspector panel backed by <see cref="AssetContentService"/>.</summary>
internal sealed class InspectorView : Grid, IDisposable
{
	private readonly Border _headerIconSlot;
	private readonly TextBlock _headerNameText;
	private readonly Border _badgeBorder;
	private readonly TextBlock _badgeText;
	private readonly TextBlock _headerMetaText;
	private readonly StackPanel _modeStrip;
	private readonly Border _modeBar;
	private readonly Border _contentHost;

	private readonly ImagePreviewControl _imageView = new();
	private readonly AudioPreviewControl _audioView = new();
	private readonly ModelPreviewControl _modelView = new();
	private readonly CodeHexViewControl _codeHexView = new();

	private BrowserItem? _currentItem;
	private InspectorMode _currentMode = InspectorMode.Preview;
	private CancellationTokenSource? _loadCts;

	public BrowserItem? CurrentItem => _currentItem;
	public DecodedImageResult? CurrentDecodedImage { get; private set; }
	public DecodedAudioResult? CurrentDecodedAudio { get; private set; }

	public event Action<object>? ReferenceClicked;

	public async Task CopySelectionAsync()
	{
		if (
			ReferenceEquals(_contentHost.Child, _codeHexView)
			&& _codeHexView.SelectedTextForCopy is { } text
			&& TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
		)
		{
			await clipboard.SetTextAsync(text);
		}
	}

	public InspectorView()
	{
		Background = Palette.Panel;
		RowDefinitions = new RowDefinitions("38,Auto,*");

		Border headerBorder = new()
		{
			Height = Palette.InspectorHeaderHeight,
			BorderBrush = Palette.Border,
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(12, 0),
		};

		Grid headerGrid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,Auto,8,Auto"), VerticalAlignment = VerticalAlignment.Center };

		_headerIconSlot = new Border
		{
			Width = 17,
			Height = 17,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(_headerIconSlot, 0);
		headerGrid.Children.Add(_headerIconSlot);

		_headerNameText = new TextBlock
		{
			FontFamily = Palette.UiFont,
			FontSize = 13,
			FontWeight = FontWeight.Medium,
			Foreground = Palette.Text,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(_headerNameText, 2);
		headerGrid.Children.Add(_headerNameText);

		_badgeText = new TextBlock
		{
			FontFamily = Palette.UiFont,
			FontSize = 10,
			FontWeight = FontWeight.SemiBold,
			Foreground = Palette.AccentText,
		};
		_badgeBorder = new Border
		{
			Padding = new Thickness(6, 1),
			CornerRadius = new CornerRadius(4),
			Background = Palette.GhostActive,
			VerticalAlignment = VerticalAlignment.Center,
			IsVisible = false,
			Child = _badgeText,
		};
		Grid.SetColumn(_badgeBorder, 4);
		headerGrid.Children.Add(_badgeBorder);

		_headerMetaText = new TextBlock
		{
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Foreground = Palette.Secondary,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(_headerMetaText, 6);
		headerGrid.Children.Add(_headerMetaText);

		headerBorder.Child = headerGrid;
		Grid.SetRow(headerBorder, 0);
		Children.Add(headerBorder);

		// Compact mode tab strip (Preview / Info / YAML / JSON / Hex / Dependencies)
		_modeStrip = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_modeBar = new Border
		{
			Height = 34,
			Padding = new Thickness(12, 0),
			BorderBrush = Palette.Border,
			BorderThickness = new Thickness(0, 0, 0, 1),
			IsVisible = false,
			Child = _modeStrip,
		};
		Grid.SetRow(_modeBar, 1);
		Children.Add(_modeBar);

		// Main preview content with 54px bottom clearance for the floating status dock
		_contentHost = new Border { Padding = new Thickness(0, 0, 0, 54), ClipToBounds = true };
		Grid.SetRow(_contentHost, 2);
		Children.Add(_contentHost);
	}

	public void Inspect(BrowserItem? item)
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = null;
		_audioView.StopAudio();
		CurrentDecodedImage = null;
		CurrentDecodedAudio = null;

		_currentItem = item;
		if (item is null)
		{
			_headerIconSlot.Child = null;
			_headerNameText.Text = "";
			_badgeBorder.IsVisible = false;
			_headerMetaText.Text = "";
			_modeBar.IsVisible = false;
			_contentHost.Child = null;
			return;
		}

		_headerIconSlot.Child = Icons.FileIcon(item.Icon, false);
		_headerNameText.Text = item.Name;

		if (item.Kind == BrowserItemKind.Asset && item.Target is IUnityObjectBase asset)
		{
			_badgeText.Text = asset.ClassName.ToUpperInvariant();
			_badgeBorder.IsVisible = true;
			_headerMetaText.Text = $"PathID {asset.PathID}";

			bool hasVisualPreview =
				AssetContentService.HasImageData(asset)
				|| AssetContentService.HasAudioData(asset)
				|| AssetContentService.HasModelData(asset)
				|| AssetContentService.HasTextData(asset);

			_currentMode = hasVisualPreview ? InspectorMode.Preview : InspectorMode.Info;
			RebuildAssetModeBar(asset, hasVisualPreview);
			LoadAssetMode(asset, _currentMode);
		}
		else if (item.Kind == BrowserItemKind.ResourceFile && item.Target is ResourceFile resource)
		{
			_badgeText.Text = "RAW";
			_badgeBorder.IsVisible = true;
			long len = resource.Stream.CanSeek ? resource.Stream.Length : 0;
			_headerMetaText.Text = $"Resource · {AssetContentService.FormatBytes(len)}";
			_modeBar.IsVisible = false;
			LoadResourcePreview(resource);
		}
		else if (item.Kind == BrowserItemKind.FailedFile && item.Target is FailedFile failed)
		{
			_badgeText.Text = "ERROR";
			_badgeBorder.IsVisible = true;
			_headerMetaText.Text = "Failed file";
			_modeBar.IsVisible = false;
			_codeHexView.ShowText(failed.StackTrace);
			_contentHost.Child = _codeHexView;
		}
		else
		{
			_badgeBorder.IsVisible = false;
			_headerMetaText.Text = BrowserItem.GetKindLabel(item);
			_modeBar.IsVisible = false;
			_contentHost.Child = null;
		}
	}

	public void ToggleAudioPlayback()
	{
		if (ReferenceEquals(_contentHost.Child, _audioView))
		{
			_audioView.TogglePlayback();
		}
	}

	private void RebuildAssetModeBar(IUnityObjectBase asset, bool hasPreview)
	{
		_modeStrip.Children.Clear();
		_modeBar.IsVisible = true;

		if (hasPreview)
		{
			string previewLabel = asset switch
			{
				_ when AssetContentService.HasImageData(asset) => Localization.AssetTabImage,
				_ when AssetContentService.HasAudioData(asset) => Localization.AssetTabAudio,
				_ when AssetContentService.HasModelData(asset) => Localization.AssetTabModel,
				_ => Localization.AssetTabText,
			};
			AddModePill(previewLabel, InspectorMode.Preview, asset);
		}

		AddModePill(Localization.AssetTabInformation, InspectorMode.Info, asset);
		AddModePill(Localization.Yaml, InspectorMode.Yaml, asset);
		AddModePill(Localization.Json, InspectorMode.Json, asset);

		if (AssetContentService.HasBinaryData(asset) || AssetContentService.HasImageData(asset))
		{
			AddModePill(Localization.AssetTabHex, InspectorMode.Hex, asset);
		}

		AddModePill(Localization.AssetTabDependencies, InspectorMode.Dependencies, asset);
	}

	private void AddModePill(string label, InspectorMode mode, IUnityObjectBase asset)
	{
		VnlensButton btn = new(label: label, active: _currentMode == mode, enabled: true, ghost: true, height: 24);
		btn.Padding = new Thickness(9, 0);
		btn.CornerRadius = new CornerRadius(6);
		btn.Clicked += () =>
		{
			if (_currentMode != mode)
			{
				_currentMode = mode;
				bool hasPreview =
					AssetContentService.HasImageData(asset)
					|| AssetContentService.HasAudioData(asset)
					|| AssetContentService.HasModelData(asset)
					|| AssetContentService.HasTextData(asset);
				RebuildAssetModeBar(asset, hasPreview);
				LoadAssetMode(asset, mode);
			}
		};
		_modeStrip.Children.Add(btn);
	}

	private void LoadAssetMode(IUnityObjectBase asset, InspectorMode mode)
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();
		CancellationToken token = _loadCts.Token;
		_audioView.StopAudio();

		if (mode == InspectorMode.Info)
		{
			List<InfoRow> rows = AssetContentService.GetInformationRows(asset);
			_contentHost.Child = BuildPropertyTable(rows);
			return;
		}

		if (mode == InspectorMode.Dependencies)
		{
			List<InfoRow> rows = AssetContentService.GetDependencyRows(asset);
			_contentHost.Child = rows.Count > 0 ? BuildPropertyTable(rows) : BuildCenteredMessage("No dependencies");
			return;
		}

		// Heavy decoding/serialization runs off the UI thread with spinner feedback
		ShowLoadingState();
		IDisposable taskScope = ActivityLogService.Instance.BeginTask($"Preparing preview… · {asset.GetBestName()}");

		Task.Run(
			() =>
			{
				try
				{
					if (token.IsCancellationRequested)
					{
						return;
					}

					if (mode == InspectorMode.Preview)
					{
						if (AssetContentService.HasImageData(asset))
						{
							DecodedImageResult? img = AssetContentService.DecodeImage(asset);
							Dispatcher.UIThread.Post(() =>
							{
								if (token.IsCancellationRequested)
									return;
								CurrentDecodedImage = img;
								if (img is not null)
								{
									_headerMetaText.Text = $"{img.Width}×{img.Height} · PathID {asset.PathID}";
									_imageView.SetImage(img);
									_contentHost.Child = _imageView;
								}
								else
								{
									_contentHost.Child = BuildCenteredMessage("Image data could not be decoded.");
								}
							});
							return;
						}

						if (asset is IAudioClip clip)
						{
							DecodedAudioResult? audio = AssetContentService.DecodeAudio(clip, out string? error);
							Dispatcher.UIThread.Post(() =>
							{
								if (token.IsCancellationRequested)
									return;
								CurrentDecodedAudio = audio;
								if (audio is not null && audio.Samples.Length > 0)
								{
									_audioView.SetAudio(audio);
									_contentHost.Child = _audioView;
								}
								else if (audio is not null)
								{
									_codeHexView.ShowHex(audio.EncodedData);
									_contentHost.Child = _codeHexView;
								}
								else
								{
									_contentHost.Child = BuildCenteredMessage(error ?? "Audio data could not be decoded.");
								}
							});
							return;
						}

						if (asset is IMesh mesh)
						{
							ModelGeometry? geo = AssetContentService.ExtractMeshGeometry(mesh);
							Dispatcher.UIThread.Post(() =>
							{
								if (token.IsCancellationRequested)
									return;
								if (geo is not null)
								{
									_modelView.SetModel(geo);
									_contentHost.Child = _modelView;
								}
								else
								{
									_contentHost.Child = BuildCenteredMessage("Mesh geometry could not be extracted.");
								}
							});
							return;
						}

						if (AssetContentService.HasTextData(asset))
						{
							string text = AssetContentService.GetTextContent(asset);
							Dispatcher.UIThread.Post(() =>
							{
								if (token.IsCancellationRequested)
									return;
								_codeHexView.ShowText(text);
								_contentHost.Child = _codeHexView;
							});
							return;
						}
					}
					else if (mode == InspectorMode.Yaml)
					{
						string yaml = AssetContentService.GetYamlContent(asset);
						Dispatcher.UIThread.Post(() =>
						{
							if (token.IsCancellationRequested)
								return;
							_codeHexView.ShowText(yaml);
							_contentHost.Child = _codeHexView;
						});
						return;
					}
					else if (mode == InspectorMode.Json)
					{
						string json = AssetContentService.GetJsonContent(asset);
						Dispatcher.UIThread.Post(() =>
						{
							if (token.IsCancellationRequested)
								return;
							_codeHexView.ShowText(json);
							_contentHost.Child = _codeHexView;
						});
						return;
					}
					else if (mode == InspectorMode.Hex)
					{
						(byte[] data, _) = AssetContentService.GetBinaryData(asset);
						Dispatcher.UIThread.Post(() =>
						{
							if (token.IsCancellationRequested)
								return;
							if (data.Length > 0)
							{
								_codeHexView.ShowHex(data);
								_contentHost.Child = _codeHexView;
							}
							else
							{
								_contentHost.Child = BuildCenteredMessage("No raw binary data.");
							}
						});
						return;
					}
				}
				catch (Exception ex)
				{
					ActivityLogService.Instance.ReportError("Preview failed.", ex);
					Dispatcher.UIThread.Post(() =>
					{
						if (!token.IsCancellationRequested)
						{
							_contentHost.Child = BuildCenteredMessage($"Error: {ex.Message}");
						}
					});
				}
				finally
				{
					taskScope.Dispose();
				}
			},
			token
		);
	}

	private void LoadResourcePreview(ResourceFile resource)
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_loadCts = new CancellationTokenSource();
		CancellationToken token = _loadCts.Token;

		ShowLoadingState();
		Task.Run(
			() =>
			{
				byte[] bytes = AssetContentService.ReadResourceBytes(resource);
				Dispatcher.UIThread.Post(() =>
				{
					if (token.IsCancellationRequested)
						return;
					_codeHexView.ShowHex(bytes);
					_contentHost.Child = _codeHexView;
				});
			},
			token
		);
	}

	private void ShowLoadingState()
	{
		StackPanel panel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		panel.Children.Add(new VnlensSpinner(14, Palette.Secondary));
		panel.Children.Add(
			new TextBlock
			{
				Text = "Preparing preview…",
				FontFamily = Palette.UiFont,
				FontSize = 12,
				Foreground = Palette.Secondary,
				VerticalAlignment = VerticalAlignment.Center,
			}
		);
		_contentHost.Child = panel;
	}

	private static Control BuildCenteredMessage(string text)
	{
		return new TextBlock
		{
			Text = text,
			FontFamily = Palette.UiFont,
			FontSize = 12,
			Foreground = Palette.Secondary,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(24),
		};
	}

	private Control BuildPropertyTable(List<InfoRow> rows)
	{
		StackPanel stack = new() { Orientation = Orientation.Vertical };

		for (int i = 0; i < rows.Count; i++)
		{
			InfoRow row = rows[i];
			Border rowBorder = new()
			{
				MinHeight = 28,
				Background = (i % 2 == 0) ? Palette.Panel : Palette.PanelAlt,
				BorderBrush = Palette.Border,
				BorderThickness = new Thickness(0, 0, 0, 1),
			};

			Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("180,1,*") };

			TextBlock keyText = new()
			{
				Text = row.Key,
				FontFamily = Palette.UiFont,
				FontSize = 12,
				Foreground = Palette.Secondary,
				Padding = new Thickness(12, 6),
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
			Grid.SetColumn(keyText, 0);
			grid.Children.Add(keyText);

			Border divider = new() { Width = 1, Background = Palette.Border };
			Grid.SetColumn(divider, 1);
			grid.Children.Add(divider);

			if (row.ReferenceTarget is not null)
			{
				object target = row.ReferenceTarget;
				TextBlock linkText = new()
				{
					Text = row.Value,
					FontFamily = Palette.MonoFont,
					FontSize = 12,
					Foreground = Palette.AccentText,
					Padding = new Thickness(12, 6),
					VerticalAlignment = VerticalAlignment.Center,
					TextWrapping = TextWrapping.Wrap,
					Cursor = new Cursor(StandardCursorType.Hand),
				};
				linkText.PointerPressed += (_, e) =>
				{
					if (e.GetCurrentPoint(linkText).Properties.IsLeftButtonPressed)
					{
						e.Handled = true;
						ReferenceClicked?.Invoke(target);
					}
				};
				Grid.SetColumn(linkText, 2);
				grid.Children.Add(linkText);
			}
			else
			{
				SelectableTextBlock valText = new()
				{
					Text = row.Value,
					FontFamily = Palette.MonoFont,
					FontSize = 12,
					Foreground = Palette.Text,
					Padding = new Thickness(12, 6),
					VerticalAlignment = VerticalAlignment.Center,
					TextWrapping = TextWrapping.Wrap,
				};
				Grid.SetColumn(valText, 2);
				grid.Children.Add(valText);
			}

			rowBorder.Child = grid;
			stack.Children.Add(rowBorder);
		}

		return new ScrollViewer
		{
			Content = stack,
			HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
		};
	}

	public void Dispose()
	{
		_loadCts?.Cancel();
		_loadCts?.Dispose();
		_audioView.Dispose();
	}
}
