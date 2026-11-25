using System.Diagnostics;

namespace AssetRipper.Tools.TestCli;

/// <summary>All archive workers update counters; only the timer renders the shared console line.</summary>
internal sealed class ScanProgress : IDisposable
{
	private readonly object gate = new();
	private readonly ScanLog log;
	private readonly Stopwatch elapsed = Stopwatch.StartNew();
	private readonly Timer? timer;
	private bool interactive = !Console.IsOutputRedirected;
	private bool stopped;
	private string stage = "Mounting archives";
	private int totalArchives;
	private int completedArchives;
	private int activeArchives;
	private int failedArchives;
	private long entries;
	private long assets;
	private long failedAssets;
	private int previousWidth;

	public ScanProgress(ScanLog scanLog)
	{
		log = scanLog;
		if (interactive)
		{
			timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(125));
		}
	}

	public void SetArchiveCount(int count)
	{
		lock (gate)
		{
			totalArchives = count;
		}
	}

	public void SetStage(string value)
	{
		lock (gate)
		{
			stage = value;
		}
	}

	public void StartArchive()
	{
		lock (gate)
		{
			activeArchives++;
		}
	}

	public void FinishArchive(bool completed, bool failed)
	{
		lock (gate)
		{
			activeArchives--;
			if (completed)
			{
				completedArchives++;
			}
			if (failed)
			{
				failedArchives++;
			}
		}
	}

	public void FinishEntry() => Interlocked.Increment(ref entries);

	public void FinishAsset(bool failed)
	{
		Interlocked.Increment(ref assets);
		if (failed)
		{
			Interlocked.Increment(ref failedAssets);
		}
	}

	private string FormatLine(int width = int.MaxValue)
	{
		int barWidth = width < 120 ? 10 : 20;
		double fraction = totalArchives == 0 ? 0 : (double)completedArchives / totalArchives;
		int filled = (int)(fraction * barWidth);
		string bar = new string('#', filled) + new string('-', barWidth - filled);
		string total = totalArchives == 0 ? "?" : totalArchives.ToString();
		if (width < 120)
		{
			string detail = (elapsed.ElapsedMilliseconds / 3000 % 3) switch
			{
				0 => $"entries:{Interlocked.Read(ref entries)}",
				1 => $"assets:{Interlocked.Read(ref assets)}",
				_ => $"failed:{failedArchives}/{Interlocked.Read(ref failedAssets)}",
			};
			return $"{stage} [{bar}] {fraction * 100, 3:F0}% {completedArchives}/{total}"
				+ $" | run:{activeArchives} err:{log.Errors} warn:{log.Warnings} | {detail}";
		}
		string line =
			$"{stage} [{bar}] {fraction * 100, 3:F0}% {completedArchives}/{total} packs"
			+ $" | active:{activeArchives} err:{log.Errors} warn:{log.Warnings}";
		string counts = $" | entries:{Interlocked.Read(ref entries)} assets:{Interlocked.Read(ref assets)}";
		string failures = $" | failed:{failedArchives} packs/{Interlocked.Read(ref failedAssets)} assets";
		// Alternate secondary counters when needed while keeping pack progress
		// and error counts visible. The final summary always includes all counters.
		if (line.Length + counts.Length + failures.Length > width)
		{
			return line + ((elapsed.ElapsedMilliseconds / 3000 & 1) == 0 ? counts : failures);
		}
		return line + counts + failures + $" | {elapsed.Elapsed:hh\\:mm\\:ss}";
	}

	private void Refresh()
	{
		lock (gate)
		{
			if (stopped || !interactive)
			{
				return;
			}
			try
			{
				// Leave the last column free to prevent terminal autowrap. Clamp the erased
				// area too, since a resized window can be narrower than the previous frame.
				int width = Console.WindowWidth - 1;
				if (width <= 0)
				{
					return;
				}
				string line = FormatLine(width);
				if (line.Length > width)
				{
					line = line[..width];
				}
				Console.Write("\r" + line.PadRight(Math.Min(width, Math.Max(line.Length, previousWidth))));
				previousWidth = line.Length;
			}
			catch (IOException)
			{
				// Losing a terminal must not abort parsing from a ThreadPool callback.
				interactive = false;
			}
		}
	}

	public void Complete(ScanSummary summary, string reportPrefix)
	{
		lock (gate)
		{
			stopped = true;
			elapsed.Stop();
			stage =
				summary.Cancelled ? "Cancelled"
				: summary.SetupFailed ? "Stopped"
				: summary.HasFailures ? "Finished with failures"
				: "Finished";
			if (interactive && previousWidth > 0)
			{
				Console.Write("\r" + new string(' ', Math.Min(previousWidth, Math.Max(0, Console.WindowWidth - 1))) + "\r");
			}
			Console.WriteLine(FormatLine());
			Console.WriteLine($"Reports: {reportPrefix}.summary.json / .jsonl / .log");
		}
	}

	public void Dispose()
	{
		lock (gate)
		{
			timer?.Dispose();
			if (!stopped && interactive && previousWidth > 0)
			{
				Console.WriteLine();
			}
			stopped = true;
		}
	}
}
