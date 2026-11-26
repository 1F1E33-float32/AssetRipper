using AssetRipper.GUI.Avalonia.Controls;
using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Localizations;
using AssetRipper.Import.Logging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Views;

internal sealed class ActivityLogView : Grid
{
	private readonly Queue<ActivityLogEntry> _entries = new();
	private readonly InlineCollection _lines = new();
	private readonly SelectableTextBlock _text;
	private readonly ScrollViewer _scroll;
	private readonly TextBlock _summary;
	private readonly TextBlock _progressLabel;
	private readonly ProgressBar _progressBar;
	private readonly VnlensButton _cancelProgress;
	private readonly VnlensButton _showOutput;
	private string? _outputPath;
	private bool _scrollToTail;
	private Vector? _pendingOffset;
	private int _textLength;
	private long _lastSequence = -1;
	private bool _selecting;

	public event Action<string>? ShowOutputRequested;
	public event Action? CancelProgressRequested;

	public ActivityLogView()
	{
		Height = 340;
		Focusable = true;
		RowDefinitions = new RowDefinitions("Auto,*,Auto");

		_summary = new TextBlock
		{
			FontFamily = Palette.MonoFont,
			FontSize = 11,
			Foreground = Palette.Secondary,
			Margin = new Thickness(6, 4, 6, 8),
		};
		_progressLabel = new TextBlock
		{
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Foreground = Palette.Accent,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(6, 0, 6, 4),
			IsVisible = false,
		};
		_progressBar = new ProgressBar
		{
			Minimum = 0,
			Maximum = 1,
			Height = 4,
			Foreground = Palette.Accent,
			Margin = new Thickness(6, 0, 6, 8),
			IsVisible = false,
		};
		_cancelProgress = new VnlensButton(label: Localization.CancelPathIndex, ghost: true, height: 24)
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			IsVisible = false,
		};
		_cancelProgress.Clicked += () => CancelProgressRequested?.Invoke();
		StackPanel header = new();
		header.Children.Add(_summary);
		header.Children.Add(_progressLabel);
		header.Children.Add(_progressBar);
		header.Children.Add(_cancelProgress);
		Children.Add(header);

		_text = new SelectableTextBlock
		{
			Focusable = true,
			FontFamily = Palette.MonoFont,
			FontSize = 11,
			Foreground = Palette.Text,
			TextWrapping = TextWrapping.Wrap,
			Padding = new Thickness(6, 2),
			Inlines = _lines,
		};
		_text.AddHandler(
			KeyDownEvent,
			(_, e) =>
			{
				if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
				{
					if (e.Key == Key.A)
					{
						_text.SelectAll();
						e.Handled = true;
					}
					else if (e.Key == Key.C)
					{
						_text.Copy();
						e.Handled = true;
					}
				}
			},
			RoutingStrategies.Tunnel
		);
		_text.AddHandler(
			PointerPressedEvent,
			(_, e) =>
			{
				_selecting = e.GetCurrentPoint(_text).Properties.IsLeftButtonPressed;
			},
			RoutingStrategies.Tunnel
		);
		_text.AddHandler(PointerReleasedEvent, (_, _) => _selecting = false, RoutingStrategies.Bubble, handledEventsToo: true);
		_text.PointerCaptureLost += (_, _) => _selecting = false;

		MenuItem copy = new() { Header = "Copy" };
		copy.Click += (_, _) => _text.Copy();
		MenuItem selectAll = new() { Header = "Select all" };
		selectAll.Click += (_, _) =>
		{
			_text.Focus();
			_text.SelectAll();
		};
		ContextMenu menu = new() { ItemsSource = new[] { copy, selectAll } };
		menu.Opening += (_, _) => copy.IsEnabled = _text.CanCopy;
		_text.ContextMenu = menu;

		_scroll = new ScrollViewer
		{
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			IsScrollChainingEnabled = false,
			Content = _text,
		};
		_scroll.LayoutUpdated += (_, _) =>
		{
			if (_scrollToTail)
			{
				_scrollToTail = false;
				_scroll.ScrollToEnd();
			}
			else if (_pendingOffset is { } offset)
			{
				_pendingOffset = null;
				_scroll.Offset = offset;
			}
		};
		Grid.SetRow(_scroll, 1);
		Children.Add(_scroll);

		_showOutput = new VnlensButton(label: "Show output", ghost: false, height: 26)
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(6, 6, 0, 0),
			IsVisible = false,
		};
		_showOutput.Clicked += () =>
		{
			if (_outputPath is { } path)
			{
				ShowOutputRequested?.Invoke(path);
			}
		};
		Grid.SetRow(_showOutput, 2);
		Children.Add(_showOutput);
	}

	public void Refresh(ActivityLogSnapshot snapshot, string? outputPath)
	{
		_progressLabel.IsVisible = _progressBar.IsVisible = _cancelProgress.IsVisible = snapshot.Progress is not null;
		if (snapshot.Progress is { } progress)
		{
			_progressLabel.Text = progress.Label;
			_progressBar.Value = progress.Fraction;
		}
		_summary.Text =
			$"{snapshot.LineCount} / {ActivityLogService.MaximumLines} lines"
			+ $" · Error {snapshot.ErrorCount} · Warning {snapshot.WarningCount}"
			+ (snapshot.IsBusy ? " · Running" : "");
		_outputPath = outputPath;
		_showOutput.IsVisible = !string.IsNullOrEmpty(outputPath);

		IReadOnlyList<ActivityLogEntry> incoming = snapshot.Entries;
		if (incoming.Count == 0 || _lastSequence == incoming[^1].Sequence)
		{
			return;
		}
		int selectionStart = _text.SelectionStart;
		int selectionEnd = _text.SelectionEnd;
		bool followTail =
			!_selecting
			&& selectionStart == selectionEnd
			&& (_entries.Count == 0 || _scroll.Offset.Y + _scroll.Viewport.Height >= _scroll.Extent.Height - 1);

		int removedLines = 0;
		int removedCharacters = 0;
		foreach (ActivityLogEntry entry in _entries)
		{
			if (entry.Sequence >= incoming[0].Sequence)
			{
				break;
			}
			removedLines++;
			removedCharacters += entry.Text.Length + Environment.NewLine.Length;
		}
		Vector offset = _scroll.Offset;
		if (removedCharacters > 0 && !followTail)
		{
			double removedHeight = _text.TextLayout.HitTestTextPosition(removedCharacters).Top;
			offset = new Vector(offset.X, Math.Max(0, offset.Y - removedHeight));
		}

		// Retain the selection's character positions as the oldest lines leave the bounded history.
		for (int i = 0; i < removedLines; i++)
		{
			_entries.Dequeue();
		}
		_lines.RemoveRange(0, removedLines);
		_textLength -= removedCharacters;
		foreach (ActivityLogEntry entry in incoming)
		{
			if (entry.Sequence > _lastSequence)
			{
				_entries.Enqueue(entry);
				string line = entry.Text + Environment.NewLine;
				_lines.Add(new Run(line) { Foreground = GetLevelBrush(entry.Type) });
				_textLength += line.Length;
			}
		}
		_lastSequence = incoming[^1].Sequence;
		_text.SelectionStart = Math.Clamp(selectionStart - removedCharacters, 0, _textLength);
		_text.SelectionEnd = Math.Clamp(selectionEnd - removedCharacters, 0, _textLength);
		_scrollToTail = followTail;
		_pendingOffset = followTail ? null : offset;
	}

	private static IBrush GetLevelBrush(LogType type) =>
		type switch
		{
			LogType.Error => Palette.Danger,
			LogType.Warning => Palette.Warning,
			LogType.Info => Palette.Text,
			LogType.Debug => Palette.AccentText,
			LogType.Verbose => Palette.Tertiary,
			_ => Palette.Text,
		};
}
