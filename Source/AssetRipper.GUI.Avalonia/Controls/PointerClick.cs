using Avalonia.Controls;
using Avalonia.Input;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>Activates on release inside the pressed surface.</summary>
internal static class PointerClick
{
	public static void Bind(Control control, Action<PointerReleasedEventArgs> click)
	{
		bool pressed = false;
		control.PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
			{
				pressed = true;
				e.Pointer.Capture(control);
				e.Handled = true;
			}
		};
		control.PointerReleased += (_, e) =>
		{
			if (!pressed || e.InitialPressMouseButton != MouseButton.Left)
			{
				return;
			}
			pressed = false;
			e.Pointer.Capture(null);
			e.Handled = true;
			if (new global::Avalonia.Rect(control.Bounds.Size).Contains(e.GetPosition(control)))
			{
				click(e);
			}
		};
		control.PointerCaptureLost += (_, _) => pressed = false;
	}
}
