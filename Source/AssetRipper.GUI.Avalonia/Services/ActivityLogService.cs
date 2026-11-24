using AssetRipper.Import.Logging;

namespace AssetRipper.GUI.Avalonia.Services;

internal sealed record ActivityError(long Id, string Message);
internal sealed record ActivityLogEntry(LogType Type, LogCategory Category, string Message);

internal sealed class ActivityLogService : ILogger
{
	public static ActivityLogService Instance { get; } = new();

	private readonly object _sync = new();
	private readonly List<ActivityError> _errors = new();
	private readonly List<ActivityLogEntry> _recentLogs = new();
	private long _nextErrorId = 1;
	private string _status = "Ready";
	private string? _lastWarning;
	private int _activeJobs;
	private string? _activeJobLabel;

	public event Action? Changed;

	public bool IsBusy
	{
		get { lock (_sync) return _activeJobs > 0; }
	}

	public string Status
	{
		get { lock (_sync) return _status; }
	}

	public string? ActiveJobLabel
	{
		get { lock (_sync) return _activeJobLabel; }
	}

	public string? LastWarning
	{
		get { lock (_sync) return _lastWarning; }
	}

	public bool HasErrors
	{
		get { lock (_sync) return _errors.Count > 0; }
	}

	public IReadOnlyList<ActivityError> GetErrors()
	{
		lock (_sync)
		{
			return _errors.ToArray();
		}
	}

	public IReadOnlyList<ActivityLogEntry> GetRecentLogs()
	{
		lock (_sync)
		{
			return _recentLogs.ToArray();
		}
	}

	public IDisposable BeginTask(string label)
	{
		lock (_sync)
		{
			_activeJobs++;
			_activeJobLabel = label;
			_status = label;
		}
		Notify();
		return new TaskScope(this, label);
	}

	private void EndTask(string label)
	{
		lock (_sync)
		{
			_activeJobs = Math.Max(0, _activeJobs - 1);
			if (_activeJobs == 0)
			{
				_activeJobLabel = null;
				if (_status == label)
				{
					_status = "Ready";
				}
			}
		}
		Notify();
	}

	public void SetStatus(string status)
	{
		lock (_sync)
		{
			_status = status;
		}
		Notify();
	}

	public void ReportError(string message)
	{
		lock (_sync)
		{
			_errors.Add(new ActivityError(_nextErrorId++, message));
			if (_errors.Count > 50)
			{
				_errors.RemoveAt(0);
			}
		}
		Notify();
	}

	public void DismissError(long id)
	{
		lock (_sync)
		{
			int idx = _errors.FindIndex(e => e.Id == id);
			if (idx >= 0)
			{
				_errors.RemoveAt(idx);
			}
		}
		Notify();
	}

	public void ClearWarnings()
	{
		lock (_sync)
		{
			_lastWarning = null;
		}
		Notify();
	}

	public void Log(LogType type, LogCategory category, string message)
	{
		string trimmed = message.Trim();
		if (string.IsNullOrEmpty(trimmed))
		{
			return;
		}

		lock (_sync)
		{
			_recentLogs.Add(new ActivityLogEntry(type, category, trimmed));
			if (_recentLogs.Count > 120)
			{
				_recentLogs.RemoveAt(0);
			}

			if (type == LogType.Error)
			{
				string prefix = category == LogCategory.None ? "" : $"[{category}] ";
				_errors.Add(new ActivityError(_nextErrorId++, prefix + trimmed));
				if (_errors.Count > 50)
				{
					_errors.RemoveAt(0);
				}
			}
			else if (type == LogType.Warning)
			{
				_lastWarning = trimmed;
			}
			else if (type == LogType.Info && _activeJobs > 0)
			{
				_status = trimmed.Length > 120 ? trimmed[..120] + "…" : trimmed;
			}
		}
		Notify();
	}

	public void BlankLine(int numLines)
	{
	}

	private void Notify()
	{
		Changed?.Invoke();
	}

	private sealed class TaskScope(ActivityLogService owner, string label) : IDisposable
	{
		private int _disposed;
		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				owner.EndTask(label);
			}
		}
	}
}
