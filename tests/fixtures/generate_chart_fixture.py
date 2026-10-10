#!/usr/bin/env python3
"""Writes a fixed scan result (ScanResultFileService JSON) for chart captures.

The WinForms charts (captured on Windows by c2flux-shots --charts) and the
Avalonia charts (rendered headless on any OS) load the same tree, so their
images can be compared directly. See ROADMAP.md, phase 5.2.

Paths use the separator of the OS that renders them, because the charts split
paths with System.IO.Path. Everything else (names, sizes, dates) is identical,
including the root's name.

Usage: generate_chart_fixture.py <output.json> [--root T:\\]
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    parser.add_argument("--root", default="T:\\" if os.name == "nt" else "/fixture")
    args = parser.parse_args()

    sep = "\\" if "\\" in args.root else "/"
    tree = build(args.root, sep)

    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(tree, handle, ensure_ascii=False)

    print("%s: %d files, %d bytes" % (args.output, len(tree["AllFiles"]), tree["SizeBytes"]))


if __name__ == "__main__":
    main()
