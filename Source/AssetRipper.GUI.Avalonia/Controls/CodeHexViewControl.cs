using System.Globalization;
using System.Text;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>
/// Virtualized line-numbered text viewer and 12-byte-per-row hex/ASCII viewer
/// matching vnlens-gui/src/workspace/view.rs (22px row height, Cascadia Mono 11px).
/// </summary>
internal sealed class CodeHexViewControl : Grid
{
	private sealed record TextLineRow(int LineNumber, string Text);
	private sealed record HexRow(string Offset, string Hex, string Ascii);

	private readonly ListBox _list;
	private double _gutterWidth = 40;
	private string _rawTextForCopy = "";

	public string RawTextForCopy => _rawTextForCopy;

	public CodeHexViewControl()
	{
		_list = new ListBox
		{
			Background = Palette.Transparent,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(8, 8, 8, 8),
			ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
		};
		ScrollViewer.SetHorizontalScrollBarVisibility(_list, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
		ScrollViewer.SetVerticalScrollBarVisibility(_list, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

		Children.Add(_list);
	}

	public void ShowText(string text)
	{
		_rawTextForCopy = text;
		string[] rawLines = text.Replace("\r\n", "\n").Split('\n');
		int count = rawLines.Length;
		int digits = Math.Max(1, (int)Math.Floor(Math.Log10(Math.Max(1, count))) + 1);
		_gutterWidth = digits * 7.0 + 7.0 + 16.0;

		TextLineRow[] rows = new TextLineRow[count];
		for (int i = 0; i < count; i++)
		{
			rows[i] = new TextLineRow(i + 1, rawLines[i].Replace("\t", "    "));
		}

		double gutter = _gutterWidth;
		_list.ItemTemplate = new FuncDataTemplate<TextLineRow>((row, _) =>
		{
			if (row is null)
			{
				return new Border();
			}
			StackPanel panel = new()
			{
				Orientation = Orientation.Horizontal,
				Height = 22,
				VerticalAlignment = VerticalAlignment.Center,
			};
			panel.Children.Add(new TextBlock
			{
				Text = row.LineNumber.ToString(CultureInfo.InvariantCulture),
				Width = gutter,
				Padding = new Thickness(0, 0, 12, 0),
				TextAlignment = TextAlignment.Right,
				FontFamily = Palette.MonoFont,
				FontSize = 11,
				Foreground = Palette.Tertiary,
				VerticalAlignment = VerticalAlignment.Center,
			});
			panel.Children.Add(new TextBlock
			{
				Text = row.Text,
				FontFamily = Palette.MonoFont,
				FontSize = 11,
				Foreground = Palette.Text,
				VerticalAlignment = VerticalAlignment.Center,
			});
			return panel;
		});

		_list.ItemsSource = rows;
	}

	public void ShowHex(byte[] bytes)
	{
		const int BytesPerRow = 12;
		// Cap display at 2 MB of rows for instant responsiveness while preserving full binary export
		int displayLen = Math.Min(bytes.Length, 2 * 1024 * 1024);
		int rowCount = (displayLen + BytesPerRow - 1) / BytesPerRow;
		HexRow[] rows = new HexRow[rowCount];

		StringBuilder hexBuilder = new(36);
		char[] asciiChars = new char[BytesPerRow];

		for (int r = 0; r < rowCount; r++)
		{
			int start = r * BytesPerRow;
			int len = Math.Min(BytesPerRow, displayLen - start);
			hexBuilder.Clear();
			for (int i = 0; i < len; i++)
			{
				if (i > 0)
				{
					hexBuilder.Append(' ');
				}
				byte b = bytes[start + i];
				hexBuilder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
				asciiChars[i] = (b >= 32 && b <= 126) ? (char)b : '·';
			}
			while (hexBuilder.Length < 35)
			{
				hexBuilder.Append(' ');
			}
			rows[r] = new HexRow(
				start.ToString("X8", CultureInfo.InvariantCulture),
				hexBuilder.ToString(),
				new string(asciiChars, 0, len));
		}

		_rawTextForCopy = string.Join('\n', rows.Take(500).Select(r => $"{r.Offset}  {r.Hex}  {r.Ascii}"));

		_list.ItemTemplate = new FuncDataTemplate<HexRow>((row, _) =>
		{
			if (row is null)
			{
				return new Border();
			}
			StackPanel panel = new()
			{
				Orientation = Orientation.Horizontal,
				Spacing = 16,
				Height = 22,
				VerticalAlignment = VerticalAlignment.Center,
			};
			panel.Children.Add(new TextBlock
			{
				Text = row.Offset,
				FontFamily = Palette.MonoFont,
				FontSize = 11,
				Foreground = Palette.Tertiary,
				VerticalAlignment = VerticalAlignment.Center,
			});
			panel.Children.Add(new TextBlock
			{
				Text = row.Hex,
				FontFamily = Palette.MonoFont,
				FontSize = 11,
				Foreground = Palette.Text,
				VerticalAlignment = VerticalAlignment.Center,
			});
			panel.Children.Add(new TextBlock
			{
				Text = row.Ascii,
				FontFamily = Palette.MonoFont,
				FontSize = 11,
				Foreground = Palette.Secondary,
				VerticalAlignment = VerticalAlignment.Center,
			});
			return panel;
		});

		_list.ItemsSource = rows;
	}
}
