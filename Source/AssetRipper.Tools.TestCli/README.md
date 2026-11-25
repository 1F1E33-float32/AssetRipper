# testcli

Batch-verifies serialized assets in game archives. After building the GUI, `testcli.exe` (`testcli` on Linux/macOS) is placed beside the main executable and shares `plugins/` and `runtimes/`.

```powershell
.\testcli.exe --list-games
.\testcli.exe --input "C:\Game\Data" --game ABC_CN_Windows_CBT1
.\testcli.exe --input "C:\Game\Data" --game ABC_CN_Windows_CBT1 --threads 8
```

`--input` accepts a directory or a single file; directory traversal is defined by the selected game plugin. `--game` takes a plugin ID listed by `--list-games`.

- Defaults to 10 worker threads, processing one disk archive per task; entries within an archive are opened sequentially, read in full, and deserialized object by object.
- `--threads N` (or `-j N`) sets concurrent archive tasks (must be a positive integer; `1` runs serially). Single-archive inputs run a single task.
- Per-archive statistics are aggregated independently and JSONL records are written as completed; deferred script resolution synchronizes access to the shared assembly cache.
- Interactive consoles render a single shared progress bar (refreshed every 125 ms, cycling secondary counters in narrow terminals) and initialization stage. Redirected output prints only the final summary and report paths. Only Warning and Error messages are written to `.log` files under `log/`; Info, Debug, and Verbose messages are discarded.
- Unknown assets, unreadable assets, logged errors, and MonoBehaviour parsing failures fail the check; scanning continues after individual failures.
- Objects with a complete plugin source type tree are also parsed across all source fields and verified against expected byte counts; source parse failures are reported as errors even when the compatibility model succeeds.
- Script references and deferred MonoBehaviour payloads are resolved once all entries in the same archive are loaded. Missing cross-archive MonoScript references are recorded as `unresolved-script`.
- Collections and decompression caches are released after each archive completes so memory scales primarily with active archive tasks.
- `--skip-scripts` skips deferred script parsing (`skipped-script`) while still checking remaining objects.
- Pressing Ctrl+C preserves already written item records and writes the summary with the completed fraction.

Three files with a shared timestamped prefix are written to `log/` beside the executable: `.log` (warnings, errors, and their exception stacks), `.jsonl` (per-item status with archive, entry, PathID, and ClassID), and `.summary.json` (aggregate counts). Entry status `loaded` only indicates that the entry was loaded; final verdicts come from individual `asset` records and the summary.

Exit codes: `0` when all enabled checks pass, `1` when parse failures, unknown assets, or unresolved scripts occur, `2` on invalid arguments or initialization errors, and `130` when cancelled. With `--skip-scripts`, exit code `0` reflects only the executed checks.

To extract `Read ... but expected ... for asset type ...` mismatches from a log using Python 3:

```powershell
python Source/AssetRipper.Tools.TestCli/extract_read_mismatches.py "log/testcli.log" --output-prefix "artifacts/diagnostics/read-mismatches"
```

The script streams the log and writes matching lines to `.log`, structured rows (with line number, PathID, and size delta) to `.csv`, and grouped summaries to `.json`. Omitting `--output-prefix` writes `<log-stem>.read-mismatches.*` beside the input log.
