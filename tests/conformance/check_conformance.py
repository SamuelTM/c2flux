#!/usr/bin/env python3
"""
Compares what a scanner saw (a c2flux-bench --dump file) with what the test
tree generator created (its manifest), entry by entry, and reports every
difference by category. See ROADMAP.md, phase 2, conformance suite.

Rules every scanner must meet (exit code 1 otherwise):
  * no extra entries, no size, mtime or kind differences, and every directory
    the sum of its children;
  * every manifest file and directory present, except as allowed below;
  * symbolic links never followed: if listed, 0 bytes and no children;
  * --hardlinks each: every link listed; once: exactly one path per file
    (counted once, as on disk);
  * --permissions enforced: an unreadable folder's contents are absent;
    ignored (reads the volume directly): they are present.

The dump lists entries relative to the scanned root. When the scan root is not
the tree itself (MFT scanners always scan a whole volume), --subtree selects
the tree's folder inside the dump (e.g. "tree" for T:\\tree).

Usage:
    python check_conformance.py --manifest M.json --dump D.txt [--subtree tree]
                                [--hardlinks each|once] [--permissions enforced|ignored]
                                [--scanner NAME] [--json-out F] [--summary-out F]
"""

import argparse
import json
import sys

# .NET DateTime ticks of the Unix epoch.
EPOCH_TICKS = 621355968000000000
TICKS_PER_SECOND = 10_000_000


def load_dump(path, subtree):
    prefix = "/" + subtree.strip("/") if subtree else ""
    entries = {}
    outside = 0
    with open(path, encoding="utf-8-sig") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if not line:
                continue
            # Split from both ends: file names may contain "|" (POSIX).
            kind, rest = line.split("|", 1)
            entry_path, size, ticks = rest.rsplit("|", 2)
            if prefix:
                if not entry_path.startswith(prefix + "/"):
                    outside += 1
                    continue
                entry_path = entry_path[len(prefix):]
            entries[entry_path.lstrip("/")] = {
                "kind": "directory" if kind == "D" else "file",
                "size": int(size),
                "ticks": int(ticks),
            }
    return entries, outside


def ticks_to_unix(ticks):
    return (ticks - EPOCH_TICKS) / TICKS_PER_SECOND


def check(manifest, dump):
    expected = {entry["path"]: entry for entry in manifest["entries"]}
    findings = {
        "missing": [],              # in the manifest, not in the scan
        "missing_unreadable": [],   # same, but inside an unreadable folder
        "extra": [],                # in the scan, not in the manifest
        "size": [],                 # file size differs
        "mtime": [],                # file last write time differs (> 2 s)
        "kind": [],                 # directory vs file mismatch
        "symlinks": [],             # how each symbolic link appears
        "unreadable_visible": [],   # contents of unreadable folders that were seen
        "directory_size": [],       # directory size != sum of its children in the scan
    }

    for path, entry in expected.items():
        seen = dump.get(path)
        kind = entry["kind"]

        if kind == "symlink":
            findings["symlinks"].append({
                "path": path,
                "target_kind": entry.get("target_kind"),
                "seen_as": None if seen is None else seen["kind"],
                "seen_size": None if seen is None else seen["size"],
                "followed": any(other.startswith(path + "/") for other in dump),
            })
            continue

        if seen is None:
            key = "missing_unreadable" if entry.get("inside_unreadable") else "missing"
            findings[key].append({"path": path, "kind": kind})
            continue

        if entry.get("inside_unreadable"):
            findings["unreadable_visible"].append({"path": path, "kind": kind})

        if seen["kind"] != kind:
            findings["kind"].append({"path": path, "expected": kind, "seen": seen["kind"]})
            continue

        if kind == "file":
            if seen["size"] != entry["size"]:
                findings["size"].append({"path": path, "expected": entry["size"], "seen": seen["size"]})
            seen_unix = ticks_to_unix(seen["ticks"])
            if abs(seen_unix - entry["mtime_utc"]) > 2:
                findings["mtime"].append({"path": path, "expected": entry["mtime_utc"], "seen": round(seen_unix, 3)})

    for path, seen in dump.items():
        if path and path not in expected:
            findings["extra"].append({"path": path, "kind": seen["kind"], "size": seen["size"]})

    # Internal consistency: a directory's size is the sum of its direct children.
    children = {}
    for path, seen in dump.items():
        parent = path.rsplit("/", 1)[0] if "/" in path else ""
        children.setdefault(parent, []).append(seen["size"])
    for path, seen in dump.items():
        if seen["kind"] == "directory":
            total = sum(children.get(path, []))
            if total != seen["size"]:
                findings["directory_size"].append({"path": path, "size": seen["size"], "children_total": total})

    return findings


def violations(manifest, findings, hardlinks, permissions):
    """The findings that break the rules for this scanner, as readable lines."""
    problems = []
    for key in ("extra", "size", "mtime", "kind", "directory_size"):
        for item in findings[key]:
            problems.append("{}: {}".format(key, item))

    link_groups = {}
    for entry in manifest["entries"]:
        if entry.get("link_group"):
            link_groups.setdefault(entry["link_group"], []).append(entry["path"])
    missing = {item["path"] for item in findings["missing"]}

    for group, paths in link_groups.items():
        present = [path for path in paths if path not in missing]
        if hardlinks == "once":
            if len(present) != 1:
                problems.append("hardlinks: group {} should appear once, found {}".format(group, present))
            missing -= set(paths)
        elif len(present) != len(paths):
            problems.append("hardlinks: group {} missing {}".format(group, sorted(set(paths) - set(present))))
            missing -= set(paths)

    for path in sorted(missing):
        problems.append("missing: {}".format(path))

    if permissions == "enforced":
        for item in findings["unreadable_visible"]:
            problems.append("unreadable folder content visible: {}".format(item["path"]))
    else:
        for item in findings["missing_unreadable"]:
            problems.append("unreadable folder content missing (scanner ignores permissions): {}".format(item["path"]))

    for link in findings["symlinks"]:
        if link["followed"] or (link["seen_size"] or 0) != 0:
            problems.append("symbolic link followed: {}".format(link))

    return problems


def markdown(scanner, findings, outside, dump_count, problems):
    lines = ["### Conformance: {}".format(scanner), ""]
    lines.append("Entries in scan: {:,} (outside the tree: {:,})".format(dump_count, outside))
    lines.append("")
    lines.append("| Check | Count | Examples |")
    lines.append("|---|---|---|")
    for key, items in findings.items():
        if key == "symlinks":
            continue
        examples = ", ".join("`{}`".format(item["path"]) for item in items[:3])
        lines.append("| {} | {} | {} |".format(key, len(items), examples))
    lines.append("")
    lines.append("**Result: {}**".format("FAILED ({} problems)".format(len(problems)) if problems else "passed"))
    lines.append("")
    for problem in problems[:20]:
        lines.append("- " + problem)
    if problems:
        lines.append("")
    lines.append("Symbolic links:")
    lines.append("")
    for link in findings["symlinks"]:
        lines.append("- `{}` (to {}): {}".format(
            link["path"], link["target_kind"],
            "not listed" if link["seen_as"] is None else "{} of {} bytes".format(link["seen_as"], link["seen_size"])))
    return "\n".join(lines) + "\n\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--dump", required=True)
    parser.add_argument("--subtree", default="")
    parser.add_argument("--scanner", default="scanner")
    parser.add_argument("--hardlinks", choices=["each", "once"], default="each")
    parser.add_argument("--permissions", choices=["enforced", "ignored"], default="enforced")
    parser.add_argument("--json-out")
    parser.add_argument("--summary-out")
    arguments = parser.parse_args()

    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")

    with open(arguments.manifest, encoding="utf-8") as handle:
        manifest = json.load(handle)
    dump, outside = load_dump(arguments.dump, arguments.subtree)
    findings = check(manifest, dump)
    problems = violations(manifest, findings, arguments.hardlinks, arguments.permissions)

    report = {
        "scanner": arguments.scanner,
        "platform": manifest.get("platform"),
        "skipped_features": manifest.get("skipped_features"),
        "entries_in_scan": len(dump),
        "entries_outside_tree": outside,
        "hardlinks": arguments.hardlinks,
        "permissions": arguments.permissions,
        "problems": problems,
        "counts": {key: len(items) for key, items in findings.items()},
        "findings": findings,
    }
    text = markdown(arguments.scanner, findings, outside, len(dump), problems)

    if arguments.json_out:
        with open(arguments.json_out, "w", encoding="utf-8") as handle:
            json.dump(report, handle, ensure_ascii=False, indent=2)
    if arguments.summary_out:
        with open(arguments.summary_out, "a", encoding="utf-8") as handle:
            handle.write(text)
    print(text)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
