#!/usr/bin/env python3
"""Writes fixed chart data for chart captures, in <output-dir>:

- chart-tree.json: a scan result (ScanResultFileService JSON)
- chart-storage-history.json: StorageHistoryRecord list for StorageHistoryChart
- chart-scan-comparison.json: ScanHistoryComparisonResult for the growth overview

The WinForms charts (captured on Windows by c2flux-shots --charts) and the
Avalonia charts (rendered headless on any OS) load the same tree, so their
images can be compared directly. See ROADMAP.md, phase 5.2.

Paths use the separator of the OS that renders them, because the charts split
paths with System.IO.Path. Everything else (names, sizes, dates) is identical,
including the root's name.

Usage: generate_chart_fixture.py <output-dir> [--root T:\\]
"""

import argparse
import json
import os

EXTENSIONS = [".jpg", ".mp4", ".pdf", ".docx", ".zip", ".log", ".dll", ".txt"]


class Lcg:
    """Small deterministic generator, same numbers on every Python version."""

    def __init__(self, seed):
        self.state = seed

    def next(self, bound):
        self.state = (self.state * 1103515245 + 12345) % (2 ** 31)
        return self.state % bound


def build(root, sep):
    rng = Lcg(42)
    files = []
    ids = [0]

    def entry(name, path, size=0, directory=False):
        ids[0] += 1
        node = {
            "$id": str(ids[0]),
            "Name": name,
            "FullPath": path,
            "SizeBytes": size,
            "IsDirectory": directory,
            "LastWriteTimeUtc": "2026-01-01T00:00:00Z",
            "Children": [],
        }
        if not directory:
            files.append(node)
        return node

    def join(parent, name):
        return parent + name if parent.endswith(sep) else parent + sep + name

    def directory(name, parent_path, children):
        path = join(parent_path, name)
        node = entry(name, path, directory=True)
        for make in children:
            node["Children"].append(make(path))
        node["Children"].sort(key=lambda child: -child["SizeBytes"])
        node["SizeBytes"] = sum(child["SizeBytes"] for child in node["Children"])
        return node

    def file(name, size):
        return lambda parent_path: entry(name, join(parent_path, name), size)

    def random_files(count, max_kib):
        made = []
        for index in range(count):
            extension = EXTENSIONS[rng.next(len(EXTENSIONS))]
            size = (rng.next(max_kib) + 1) * 1024 + rng.next(1024)
            made.append(file("file-%02d%s" % (index, extension), size))
        return made

    def group(index):
        # Groups shrink so the charts show big, medium and tiny slices.
        subfolders = [
            (lambda sub: lambda parent_path: directory(
                "sub-%d" % sub, parent_path, random_files(6 + rng.next(10), 4096 >> index)))(sub)
            for sub in range(4 + index)
        ]
        return lambda parent_path: directory(
            "group-%03d" % index, parent_path, subfolders + random_files(3, 2048 >> index))

    top = [
        lambda parent_path: directory("tree", parent_path, [group(index) for index in range(4)]),
        lambda parent_path: directory("media", parent_path, [
            file("holiday.mp4", 96 * 1024 * 1024),
            file("concert.mp4", 41 * 1024 * 1024),
        ]),
        file("pagefile.sys", 24 * 1024 * 1024),
        file("hiberfil.sys", 9 * 1024 * 1024),
        file("notes.txt", 2560),
        file("empty.txt", 0),
    ]

    # Same name on every OS: the sunburst and treemap derive colors from it.
    root_node = entry("fixture", root, directory=True)
    for make in top:
        root_node["Children"].append(make(root))
    root_node["Children"].sort(key=lambda child: -child["SizeBytes"])
    root_node["SizeBytes"] = sum(child["SizeBytes"] for child in root_node["Children"])
    # The scanners fill the root's AllFiles with every file of the tree.
    root_node["AllFiles"] = [{"$ref": node["$id"]} for node in files]
    return root_node


def storage_history(root):
    """Eight measurements over ten days of a 500 GB volume filling up."""
    gib = 1024 ** 3
    free = [182, 176, 171, 160, 158, 141, 126, 119]
    return [
        {
            "Path": root,
            "RecordedAtUtc": "2026-01-%02dT%02d:00:00Z" % (1 + day, 9 + day % 3 * 4),
            "SizeBytes": (500 - free_gib) * gib,
            "TotalCapacityBytes": 500 * gib,
            "FreeSpaceBytes": free_gib * gib,
        }
        for day, free_gib in zip([0, 1, 2, 4, 5, 7, 8, 9], free)
    ]


def scan_comparison(root, sep):
    """Two scans of root: one folder grew, one shrank, a few files changed."""
    mib = 1024 ** 2

    def path(*parts):
        return root.rstrip(sep) + sep + sep.join(parts)

    def scan(scan_id, created, size):
        return {
            "ScanId": scan_id, "CreatedUtc": created, "RootPath": root,
            "RootSizeBytes": size, "FileCount": 262, "DirectoryCount": 27,
        }

    def change(parts, before, after):
        return {
            "Path": path(*parts), "ParentPath": path(*parts[:-1]),
            "BaselineSizeBytes": before, "CompareSizeBytes": after, "DeltaBytes": after - before,
        }

    def folder(parts, before, after, new=0, changed=0):
        return {
            "Path": path(*parts), "BaselineSizeBytes": before, "CompareSizeBytes": after,
            "DeltaBytes": after - before, "NewFileCount": new, "ChangedFileCount": changed,
        }

    baseline, compare = 340 * mib, 376 * mib
    return {
        "BaselineScan": scan("baseline", "2026-01-01T09:00:00Z", baseline),
        "CompareScan": scan("compare", "2026-01-10T17:00:00Z", compare),
        "BaselineSizeBytes": baseline, "CompareSizeBytes": compare, "SizeDeltaBytes": compare - baseline,
        "BaselineFileCount": 259, "CompareFileCount": 262,
        "NewFileCount": 3, "DeletedFileCount": 1, "ChangedFileCount": 2,
        "NewFiles": [
            change(["media", "concert.mp4"], 0, 41 * mib),
            change(["tree", "group-001", "file-00.zip"], 0, 3 * mib),
            change(["notes.txt"], 0, 2560),
        ],
        "DeletedFiles": [change(["old.log"], 6 * mib, 0)],
        "ChangedFiles": [
            change(["pagefile.sys"], 16 * mib, 24 * mib),
            change(["tree", "group-000", "sub-1", "file-02.pdf"], 9 * mib, 4 * mib),
        ],
        "FolderGrowth": [
            folder(["media"], 96 * mib, 137 * mib, new=1),
            folder(["tree"], 197 * mib, 189 * mib, new=1, changed=1),
            folder(["tree", "group-001"], 44 * mib, 47 * mib, new=1),
            folder(["tree", "group-000"], 92 * mib, 87 * mib, changed=1),
        ],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output_dir")
    parser.add_argument("--root", default="T:\\" if os.name == "nt" else "/fixture")
    args = parser.parse_args()

    sep = "\\" if "\\" in args.root else "/"
    os.makedirs(args.output_dir, exist_ok=True)
    tree = build(args.root, sep)
    files = {
        "chart-tree.json": tree,
        "chart-storage-history.json": storage_history(args.root),
        "chart-scan-comparison.json": scan_comparison(args.root, sep),
    }

    for name, data in files.items():
        with open(os.path.join(args.output_dir, name), "w", encoding="utf-8") as handle:
            json.dump(data, handle, ensure_ascii=False)

    print("%s: %d files, %d bytes" % (args.output_dir, len(tree["AllFiles"]), tree["SizeBytes"]))


if __name__ == "__main__":
    main()
