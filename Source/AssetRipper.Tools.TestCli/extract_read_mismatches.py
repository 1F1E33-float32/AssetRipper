"""Extract asset byte-count mismatches from an AssetRipper log (Python 3)."""

import argparse
import csv
import json
import re
from collections import Counter
from pathlib import Path

PATTERN = re.compile(
    r"Read (?P<read>\d+) but expected (?P<expected>\d+) for asset type "
    r"(?P<type>.+?)(?: \((?P<class_id>-?\d+)\))?\. "
    r"V: (?P<version>\S+) P: (?P<platform>\S+) N: (?P<entry>.*?) "
    r"(?:PathID: (?P<path_id>-?\d+) )?Path: (?P<path>.*)$"
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", type=Path)
    parser.add_argument("--output-prefix", type=Path)
    args = parser.parse_args()
    prefix = args.output_prefix or args.log.with_name(args.log.stem + ".read-mismatches")
    outputs = {extension: Path(str(prefix) + extension) for extension in (".log", ".csv", ".json")}
    if args.log.resolve() in (path.resolve() for path in outputs.values()):
        parser.error("The output prefix would overwrite the input log.")
    prefix.parent.mkdir(parents=True, exist_ok=True)
    groups = {}
    total = 0
    with (
        args.log.open(encoding="utf-8-sig", errors="replace") as source,
        outputs[".log"].open("w", encoding="utf-8", newline="\n") as filtered,
        outputs[".csv"].open("w", encoding="utf-8-sig", newline="") as table,
    ):
        writer = csv.DictWriter(table, fieldnames=["line", *PATTERN.groupindex, "delta"])
        writer.writeheader()
        for number, line in enumerate(source, 1):
            match = PATTERN.search(line.rstrip("\r\n"))
            if match is None:
                continue
            row = match.groupdict()
            for field in ("read", "expected", "class_id", "path_id"):
                row[field] = int(row[field]) if row[field] is not None else None
            row["line"] = number
            row["delta"] = row["expected"] - row["read"]
            writer.writerow(row)
            filtered.write(line.rstrip("\r\n") + "\n")
            total += 1
            key = (row["type"], row["class_id"])
            group = groups.setdefault(key, {"count": 0, "sizes": Counter(), "examples": []})
            group["count"] += 1
            group["sizes"][(row["read"], row["expected"])] += 1
            if len(group["examples"]) < 5:
                group["examples"].append(row)

    report = []
    for (name, class_id), group in sorted(groups.items(), key=lambda item: (-item[1]["count"], item[0][0])):
        report.append(
            {
                "type": name,
                "class_id": class_id,
                "count": group["count"],
                "sizes": [{"read": read, "expected": expected, "delta": expected - read, "count": count} for (read, expected), count in group["sizes"].most_common()],
                "examples": group["examples"],
            }
        )
        print(f"{name} ({class_id}): {group['count']} errors, {len(group['sizes'])} size pairs")
    outputs[".json"].write_text(json.dumps({"total": total, "types": report}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Total: {total} errors, {len(groups)} types")
    for path in outputs.values():
        print(path)


if __name__ == "__main__":
    main()
