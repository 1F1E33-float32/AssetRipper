using AssetRipper.Import.Logging;

namespace AssetRipper.GUI.Avalonia.Services;

internal sealed record ActivityLogEntry(long Sequence, LogType Type, LogCategory Category, string Message)
{
	public string Text => Category == LogCategory.None ? $"[{Type}] {Message}" : $"[{Type}] [{Category}] {Message}";
}

internal sealed record ActivityLogSnapshot(
	IReadOnlyList<ActivityLogEntry> Entries,
	int LineCount,
	int ErrorCount,
	int WarningCount,
	string? ActiveJobLabel
)
{
	public bool IsBusy => ActiveJobLabel is not null;
}

internal sealed class ActivityLogService : ILogger
{
	public const int MaximumLines = 1000;
	public static ActivityLogService Instance { get; } = new();

	private readonly object _sync = new();
	private readonly Queue<ActivityLogEntry> _lines = new(MaximumLines);
	private readonly List<TaskScope> _tasks = new();
	private long _nextSequence;
	private int _errorCount;
	private int _warningCount;

	public event Action? Changed;

	public ActivityLogSnapshot GetSnapshot(bool includeEntries)
	{
		lock (_sync)
		{
			return new(includeEntries ? _lines.ToArray() : [], _lines.Count, _errorCount, _warningCount, _tasks.LastOrDefault()?.Label);
		}
	}

	public IDisposable BeginTask(string label)
	{
		TaskScope scope = new(this, label);
		lock (_sync)
		{
			_tasks.Add(scope);
		}
		SetStatus(label);
		return scope;
	}

	private void EndTask(TaskScope scope)
	{
		lock (_sync)
		{
			_tasks.Remove(scope);
		}
		Changed?.Invoke();
	}

	public void SetStatus(string status) => Logger.Info(LogCategory.Avalonia, status);

	public void ReportError(string message, Exception exception) => Logger.Error(LogCategory.Avalonia, message, exception);

	public void Log(LogType type, LogCategory category, string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		lock (_sync)
		{
			// Preserve indentation and severity on every line of an exception or multiline message.
			using StringReader reader = new(message);
			while (reader.ReadLine() is { } line)
			{
				if (_lines.Count == MaximumLines)
				{
					UpdateCount(_lines.Dequeue().Type, -1);
				}
				_lines.Enqueue(new ActivityLogEntry(_nextSequence++, type, category, line));
				UpdateCount(type, 1);
			}
		}
		Changed?.Invoke();
	}

	private void UpdateCount(LogType type, int delta)
	{
		switch (type)
		{
			case LogType.Error:
				_errorCount += delta;
				break;
			case LogType.Warning:
				_warningCount += delta;
				break;
		}
	}

	public void BlankLine(int numLines) { }

	private sealed class TaskScope(ActivityLogService owner, string label) : IDisposable
	{
		public string Label { get; } = label;
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				owner.EndTask(this);
			}
		}
	}
}
