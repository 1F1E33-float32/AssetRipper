using Avalonia.Controls;

namespace AssetRipper.GUI.Avalonia.Theme;

/// <summary>Runs visual updates on the window's animation clock while the control is attached.</summary>
internal sealed class FrameLoop
{
	private readonly Control _owner;
	private readonly Func<bool> _update;
	private bool _running;
	private bool _pending;

	public FrameLoop(Control owner, Func<bool> update)
	{
		_owner = owner;
		_update = update;
		owner.DetachedFromVisualTree += (_, _) => Stop();
	}

	public void Start()
	{
		_running = true;
		RequestFrame();
	}

	public void Stop() => _running = false;

	private void RequestFrame()
	{
		if (!_pending && TopLevel.GetTopLevel(_owner) is { } window)
		{
			_pending = true;
			window.RequestAnimationFrame(OnFrame);
		}
	}

	private void OnFrame(TimeSpan elapsed)
	{
		_pending = false;
		if (!_running || TopLevel.GetTopLevel(_owner) is null)
		{
			return;
		}
		_running = _update();
		if (_running)
		{
			RequestFrame();
		}
	}
}
