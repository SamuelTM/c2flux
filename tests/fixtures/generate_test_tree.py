#!/usr/bin/env python3
"""
Generates a reproducible synthetic directory tree for testing and benchmarking
c2flux scanners on Windows, macOS and Linux.

The tree covers the cases scanners usually get wrong: size boundaries, very wide
and very deep directories, long and Unicode names, hardlinks, symlinks (including
dangling and looping ones), duplicate content, sparse files, unreadable entries
and empty directories.

Alongside the tree, a JSON manifest describes every entry and the expected
totals, both for a scanner that can read everything and for one that skips
unreadable directories. Features the current OS or filesystem cannot create
are listed under "skipped_features" instead of failing.

Usage:
    python generate_test_tree.py ROOT [--profile small|medium|large] [--seed N]
                                      [--manifest PATH] [--force]
    python generate_test_tree.py ROOT --clean

Only directories created by this script (marked with MARKER_NAME) are ever
overwritten or deleted.
"""

import argparse
import datetime
import json
import os
import random
import shutil
import stat
import subprocess
import sys
import unicodedata

MARKER_NAME = ".c2flux-test-tree"
MARKER_CONTENT = b"c2flux synthetic test tree\n"
MANIFEST_VERSION = 1

# Fixed base time so mtimes are identical across runs and machines.
BASE_MTIME = int(datetime.datetime(2020, 1, 1, tzinfo=datetime.timezone.utc).timestamp())

PROFILES = {
    #          wide files, deep levels, bulk dirs, files per bulk dir
    "small":  {"wide": 500,    "deep": 30,  "bulk_dirs": 20,   "bulk_files": 10},
    "medium": {"wide": 5000,   "deep": 60,  "bulk_dirs": 200,  "bulk_files": 50},
    "large":  {"wide": 50000,  "deep": 100, "bulk_dirs": 2000, "bulk_files": 100},
}

SIZE_BOUNDARIES = [0, 1, 511, 512, 513, 4095, 4096, 4097, 65535, 65536, 65537, 1048576 + 7]

SPARSE_SIZE = 64 * 1024 * 1024

# Relative paths whose permissions are removed. Kept constant so --clean can
# restore them even if the manifest is gone.
LOCKED_DIRECTORY = "unreadable/locked-dir"
LOCKED_FILE = "unreadable/locked-file.bin"

IS_WINDOWS = os.name == "nt"


class TreeBuilder:
    def __init__(self, root, profile, seed):
        self.root = os.path.abspath(root)
        self.profile = PROFILES[profile]
        self.profile_name = profile
        self.seed = seed
        self.random = random.Random(seed)
        self.entries = {}
        self.skipped = []
        self.link_groups = {}
        self.file_index = 0
        # One random buffer, sliced at different offsets, keeps generation fast
        # while giving distinct content to distinct files.
        self.content_pool = self.random.randbytes(4 * 1024 * 1024)

    # ----- path helpers -------------------------------------------------

    def native(self, relative_path):
        path = os.path.join(self.root, *relative_path.split("/")) if relative_path else self.root
        if IS_WINDOWS and not path.startswith("\\\\?\\"):
            # Extended-length prefix: allows paths longer than MAX_PATH.
            path = "\\\\?\\" + path
        return path

    def skip(self, feature, reason):
        self.skipped.append({"feature": feature, "reason": reason})

    # ----- primitives ---------------------------------------------------

    def make_directory(self, relative_path):
        parts = relative_path.split("/")
        for depth in range(1, len(parts) + 1):
            partial = "/".join(parts[:depth])
            if partial not in self.entries:
                os.makedirs(self.native(partial), exist_ok=True)
                self.entries[partial] = {"path": partial, "kind": "directory"}

    def content_for(self, size, content_key=None):
        if size == 0:
            return b""
        if content_key is None:
            content_key = self.file_index
        offset = (content_key * 7919) % len(self.content_pool)
        chunks = []
        remaining = size
        while remaining > 0:
            chunk = self.content_pool[offset:offset + remaining]
            chunks.append(chunk)
            remaining -= len(chunk)
            offset = 0
        return b"".join(chunks)

    def write_file(self, relative_path, size, content_key=None, **extra):
        parent = relative_path.rsplit("/", 1)[0] if "/" in relative_path else ""
        if parent:
            self.make_directory(parent)

        path = self.native(relative_path)
        with open(path, "wb") as handle:
            handle.write(self.content_for(size, content_key))

        mtime = BASE_MTIME + self.file_index * 60
        os.utime(path, (mtime, mtime))
        self.file_index += 1

        entry = {"path": relative_path, "kind": "file", "size": size, "mtime_utc": mtime}
        entry.update(extra)
        self.entries[relative_path] = entry
        return entry

    def name_available(self, relative_path):
        # Detects collisions on case-insensitive or normalization-insensitive
        # filesystems (NTFS, APFS) before overwriting an existing entry.
        return not os.path.lexists(self.native(relative_path))

    # ----- sections -----------------------------------------------------

    def build(self):
        os.makedirs(self.root, exist_ok=True)
        with open(os.path.join(self.root, MARKER_NAME), "wb") as handle:
            handle.write(MARKER_CONTENT)
        marker_mtime = BASE_MTIME
        os.utime(os.path.join(self.root, MARKER_NAME), (marker_mtime, marker_mtime))
        self.entries[MARKER_NAME] = {
            "path": MARKER_NAME, "kind": "file", "size": len(MARKER_CONTENT), "mtime_utc": marker_mtime,
        }

        self.build_sizes()
        self.build_wide()
        self.build_deep()
        self.build_long_names()
        self.build_unicode_names()
        self.build_special_names()
        self.build_hardlinks()
        self.build_symlinks()
        self.build_duplicates()
        self.build_sparse()
        self.build_empty_directories()
        self.build_extreme_mtimes()
        self.build_bulk()
        # Last, so nothing else has to write inside a locked directory.
        self.build_unreadable()

    def build_sizes(self):
        for size in SIZE_BOUNDARIES:
            self.write_file("sizes/size-{:08d}.bin".format(size), size)

    def build_wide(self):
        for index in range(self.profile["wide"]):
            self.write_file("wide/file-{:06d}.dat".format(index), self.random.randint(0, 8192))

    def build_deep(self):
        parts = ["deep"]
        for level in range(self.profile["deep"]):
            parts.append("level-{:03d}".format(level))
            self.write_file("/".join(parts + ["leaf.txt"]), 100 + level)

    def build_long_names(self):
        # 255 is the per-component limit on NTFS (UTF-16 units), APFS and ext4 (bytes).
        self.write_file("long-names/" + "a" * 251 + ".txt", 255)
        # 120 two-byte characters: 240 UTF-8 bytes, still below every limit.
        self.write_file("long-names/" + "é" * 120 + ".txt", 240)
        long_directory = "long-names/" + "/".join(["directory-with-a-fairly-long-name-{:02d}".format(i) for i in range(10)])
        self.write_file(long_directory + "/beyond-max-path.txt", 260)

    def build_unicode_names(self):
        names = [
            "café.txt",
            "naïve résumé.txt",
            "日本語のファイル.txt",
            "中文文件.txt",
            "한국어 파일.txt",
            "файл.txt",
            "ملف عربي.txt",
            "קובץ.txt",
            "emoji 🚀📁.txt",
            "Ελληνικά.txt",
            "देवनागरी.txt",
        ]
        for name in names:
            self.write_file("unicode/" + unicodedata.normalize("NFC", name), len(name.encode("utf-8")))

        # Same visible name in decomposed form, with a suffix so it never
        # collides with the NFC file on normalization-insensitive filesystems.
        nfd_name = unicodedata.normalize("NFD", "café") + "-nfd.txt"
        self.write_file("unicode/" + nfd_name, 50, normalization="NFD")

        self.write_file("unicode/pasta São Paulo/北京/arquivo.txt", 77)

    def build_special_names(self):
        portable = [
            "with space.txt",
            " leading-space.txt",
            ".hidden-file",
            "#hash.txt",
            "semi;colon.txt",
            "brackets [1] (2) {3}.txt",
            "percent %20.txt",
            "single'quote.txt",
            "comma,name.txt",
            "ampersand & name.txt",
            "many.dots.tar.gz",
            "no-extension",
            "UPPER.TXT",
        ]
        for name in portable:
            self.write_file("special-names/" + name, 10 + len(name))

        self.make_directory("special-names/.hidden-directory")
        self.write_file("special-names/.hidden-directory/inside.txt", 33)

        if self.name_available("special-names/upper.txt"):
            self.write_file("special-names/upper.txt", 99)
        else:
            self.skip("case-sensitive-names", "filesystem is case-insensitive")

        posix_only = ["colon:name.txt", "question?.txt", "star*.txt", "pipe|name.txt", "back\\slash.txt", "trailing-dot."]
        if IS_WINDOWS:
            self.skip("posix-only-names", "names with :?*|\\ or a trailing dot are invalid on Windows")
        else:
            for name in posix_only:
                self.write_file("special-names/" + name, 10 + len(name))

    def build_hardlinks(self):
        original = "hardlinks/a/original.bin"
        self.write_file(original, 100000, link_group="hardlink-1")
        links = ["hardlinks/b/link-1.bin", "hardlinks/c/link-2.bin"]
        try:
            for link in links:
                self.make_directory(link.rsplit("/", 1)[0])
                os.link(self.native(original), self.native(link))
                entry = dict(self.entries[original])
                entry["path"] = link
                self.entries[link] = entry
        except OSError as error:
            self.skip("hardlinks", str(error))

    def build_symlinks(self):
        self.write_file("symlinks/target.txt", 42)
        self.make_directory("symlinks/target-dir")
        self.write_file("symlinks/target-dir/inside.txt", 43)

        links = [
            ("symlinks/link-to-file", "target.txt", False, "file"),
            ("symlinks/link-to-dir", "target-dir", True, "directory"),
            ("symlinks/link-to-sizes", os.path.join("..", "sizes"), True, "directory"),
            ("symlinks/dangling", "does-not-exist.txt", False, "missing"),
            # Points back to its own parent: a scanner that follows it loops forever.
            ("symlinks/loop", "..", True, "directory"),
        ]
        for relative_path, target, is_directory, target_kind in links:
            try:
                os.symlink(target, self.native(relative_path), target_is_directory=is_directory)
            except (OSError, NotImplementedError) as error:
                self.skip("symlinks", str(error))
                return
            self.entries[relative_path] = {
                "path": relative_path, "kind": "symlink",
                "target": target.replace("\\", "/"), "target_kind": target_kind,
            }

    def build_duplicates(self):
        # Three groups of identical content spread over different directories.
        for group in range(3):
            size = [1024, 65536, 1048576][group]
            for copy in range(3):
                self.write_file(
                    "duplicates/folder-{}/group-{}-copy-{}.bin".format(copy, group, copy),
                    size, content_key=10000 + group, duplicate_group="duplicate-{}".format(group))

        # Same size, different content: catches size-only duplicate detection.
        self.write_file("duplicates/same-size-a.bin", 4096, content_key=20001)
        self.write_file("duplicates/same-size-b.bin", 4096, content_key=20002)

    def build_sparse(self):
        relative_path = "sparse/sparse-64MiB.bin"
        self.make_directory("sparse")
        path = self.native(relative_path)
        open(path, "wb").close()

        if IS_WINDOWS and not mark_sparse_windows(path):
            os.remove(path)
            self.skip("sparse-files", "could not set the sparse flag on this volume")
            return

        with open(path, "r+b") as handle:
            handle.truncate(SPARSE_SIZE)
            handle.seek(SPARSE_SIZE // 2)
            handle.write(b"x")

        mtime = BASE_MTIME + self.file_index * 60
        os.utime(path, (mtime, mtime))
        self.file_index += 1
        self.entries[relative_path] = {
            "path": relative_path, "kind": "file", "size": SPARSE_SIZE, "mtime_utc": mtime, "sparse": True,
        }

    def build_empty_directories(self):
        for relative_path in ["empty/one", "empty/two", "empty/nested/deeper/deepest"]:
            self.make_directory(relative_path)

    def build_extreme_mtimes(self):
        timestamps = {
            "mtimes/unix-epoch.txt": 0,
            "mtimes/before-epoch.txt": -86400 * 365,
            "mtimes/y2038.txt": 2147483648,
            "mtimes/year-2100.txt": 4102444800,
        }
        for relative_path, timestamp in timestamps.items():
            entry = self.write_file(relative_path, 16)
            try:
                os.utime(self.native(relative_path), (timestamp, timestamp))
                entry["mtime_utc"] = timestamp
            except (OSError, OverflowError, ValueError) as error:
                self.skip("mtime:" + relative_path, str(error))

    def build_bulk(self):
        for directory in range(self.profile["bulk_dirs"]):
            group = "bulk/group-{:03d}/dir-{:05d}".format(directory // 100, directory)
            for index in range(self.profile["bulk_files"]):
                # Mostly small files with an occasional large one, like real disks.
                size = int(self.random.lognormvariate(8, 2.5))
                size = min(size, 8 * 1024 * 1024)
                self.write_file("{}/file-{:04d}.dat".format(group, index), size)

    def build_unreadable(self):
        self.write_file(LOCKED_DIRECTORY + "/hidden-1.bin", 1000, inside_unreadable=True)
        self.write_file(LOCKED_DIRECTORY + "/hidden-2.bin", 2000, inside_unreadable=True)
        self.write_file(LOCKED_DIRECTORY + "/sub/hidden-3.bin", 3000, inside_unreadable=True)
        self.entries[LOCKED_DIRECTORY + "/sub"]["inside_unreadable"] = True
        self.entries[LOCKED_DIRECTORY]["unreadable"] = True

        # Listable but not readable: affects content hashing, not sizes.
        self.write_file(LOCKED_FILE, 5000, unreadable=True)

        lock(self.native(LOCKED_DIRECTORY), is_directory=True)
        lock(self.native(LOCKED_FILE), is_directory=False)

        try:
            os.listdir(self.native(LOCKED_DIRECTORY))
        except PermissionError:
            return

        # Running as root (or with backup privileges) ignores the permissions.
        self.skip("unreadable-directories", "permissions are not enforced for the current user")
        for entry in self.entries.values():
            entry.pop("inside_unreadable", None)
            entry.pop("unreadable", None)

    # ----- manifest -----------------------------------------------------

    def manifest(self):
        entries = sorted(self.entries.values(), key=lambda entry: entry["path"])
        return {
            "manifest_version": MANIFEST_VERSION,
            "generated_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "platform": sys.platform,
            "profile": self.profile_name,
            "seed": self.seed,
            "root": self.root,
            "totals": {
                "all": totals(entries, include_unreadable=True),
                "readable_only": totals(entries, include_unreadable=False),
            },
            "skipped_features": self.skipped,
            "entries": entries,
        }


def totals(entries, include_unreadable):
    result = {"directories": 0, "files": 0, "symlinks": 0, "logical_bytes": 0, "logical_bytes_unique": 0}
    seen_link_groups = set()
    for entry in entries:
        if not include_unreadable and entry.get("inside_unreadable"):
            continue
        kind = entry["kind"]
        if kind == "directory":
            result["directories"] += 1
        elif kind == "symlink":
            result["symlinks"] += 1
        else:
            result["files"] += 1
            result["logical_bytes"] += entry["size"]
            group = entry.get("link_group")
            if group is None or group not in seen_link_groups:
                result["logical_bytes_unique"] += entry["size"]
                if group is not None:
                    seen_link_groups.add(group)
    return result


def scan_readable(root):
    """Independent walk of the generated tree, without following symlinks.
    Used to check that the manifest matches what is actually on disk."""
    result = {"directories": 0, "files": 0, "symlinks": 0, "logical_bytes": 0, "logical_bytes_unique": 0}
    seen_inodes = set()
    pending = [root]
    while pending:
        directory = pending.pop()
        try:
            iterator = os.scandir(directory)
        except PermissionError:
            continue
        with iterator:
            for entry in iterator:
                if entry.is_symlink():
                    result["symlinks"] += 1
                elif entry.is_dir(follow_symlinks=False):
                    result["directories"] += 1
                    pending.append(entry.path)
                elif entry.is_file(follow_symlinks=False):
                    # os.lstat, not entry.stat: scandir reports st_ino = 0 on Windows.
                    info = os.lstat(entry.path)
                    result["files"] += 1
                    result["logical_bytes"] += info.st_size
                    key = (info.st_dev, info.st_ino)
                    if info.st_nlink <= 1 or key not in seen_inodes:
                        result["logical_bytes_unique"] += info.st_size
                        seen_inodes.add(key)
    return result


# ----- permissions -----------------------------------------------------------

EVERYONE_SID = "*S-1-1-0"


def lock(path, is_directory):
    if IS_WINDOWS:
        # Explicit deny of "read data / list directory" for Everyone. The owner
        # keeps WRITE_DAC, so unlock() can always remove it again.
        subprocess.run(["icacls", strip_extended_prefix(path), "/deny", EVERYONE_SID + ":(RD)"],
                       check=True, stdout=subprocess.DEVNULL)
    else:
        os.chmod(path, 0 if is_directory else stat.S_IWUSR)


def unlock(path, is_directory):
    if not os.path.lexists(path):
        return
    if IS_WINDOWS:
        subprocess.run(["icacls", strip_extended_prefix(path), "/remove:d", EVERYONE_SID],
                       check=False, stdout=subprocess.DEVNULL)
    else:
        os.chmod(path, stat.S_IRWXU if is_directory else stat.S_IRUSR | stat.S_IWUSR)


def strip_extended_prefix(path):
    return path[4:] if path.startswith("\\\\?\\") else path


def mark_sparse_windows(path):
    import ctypes
    from ctypes import wintypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateFileW.restype = wintypes.HANDLE
    kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                                     wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel32.DeviceIoControl.restype = wintypes.BOOL
    kernel32.DeviceIoControl.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD,
                                         wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD),
                                         wintypes.LPVOID]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]

    generic_read_write = 0xC0000000
    open_existing = 3
    fsctl_set_sparse = 0x000900C4
    invalid_handle = wintypes.HANDLE(-1).value

    handle = kernel32.CreateFileW(path, generic_read_write, 0, None, open_existing, 0, None)
    if handle == invalid_handle:
        return False
    try:
        returned = wintypes.DWORD(0)
        return bool(kernel32.DeviceIoControl(handle, fsctl_set_sparse, None, 0, None, 0,
                                             ctypes.byref(returned), None))
    finally:
        kernel32.CloseHandle(handle)


# ----- cleanup ---------------------------------------------------------------

def remove_tree(root):
    native_root = os.path.abspath(root)
    if IS_WINDOWS:
        native_root = "\\\\?\\" + native_root

    if not os.path.exists(native_root):
        return
    if not os.path.isfile(os.path.join(native_root, MARKER_NAME)):
        if os.listdir(native_root):
            sys.exit("Refusing to delete '{}': it was not created by this script (no {} marker)."
                     .format(root, MARKER_NAME))

    unlock(os.path.join(native_root, *LOCKED_DIRECTORY.split("/")), is_directory=True)
    unlock(os.path.join(native_root, *LOCKED_FILE.split("/")), is_directory=False)

    def on_error(function, path, _):
        os.chmod(path, stat.S_IRWXU)
        function(path)

    if sys.version_info >= (3, 12):
        shutil.rmtree(native_root, onexc=on_error)
    else:
        shutil.rmtree(native_root, onerror=on_error)


# ----- entry point -----------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="directory to create the tree in")
    parser.add_argument("--profile", choices=sorted(PROFILES), default="small")
    parser.add_argument("--seed", type=int, default=20260101)
    parser.add_argument("--manifest", help="where to write the manifest (default: ROOT.manifest.json)")
    parser.add_argument("--force", action="store_true", help="replace an existing tree created by this script")
    parser.add_argument("--clean", action="store_true", help="only delete an existing tree and exit")
    arguments = parser.parse_args()

    if sys.version_info < (3, 9):
        sys.exit("Python 3.9 or newer is required.")

    root = os.path.abspath(arguments.root)

    if arguments.clean:
        remove_tree(root)
        return

    if os.path.exists(root) and os.listdir(root):
        if not arguments.force:
            sys.exit("'{}' is not empty. Use --force to replace a tree created by this script.".format(root))
        remove_tree(root)

    builder = TreeBuilder(root, arguments.profile, arguments.seed)
    builder.build()
    manifest = builder.manifest()

    on_disk = scan_readable(builder.native(""))
    expected = manifest["totals"]["readable_only"]
    if on_disk != expected:
        sys.exit("Generated tree does not match its manifest.\n  expected: {}\n  on disk:  {}"
                 .format(expected, on_disk))

    manifest_path = arguments.manifest or root.rstrip("/\\") + ".manifest.json"
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=2)
        handle.write("\n")

    print("Tree:     {}".format(root))
    print("Manifest: {}".format(manifest_path))
    print("Totals:   {}".format(json.dumps(manifest["totals"]["all"])))
    for skipped in manifest["skipped_features"]:
        print("Skipped:  {} ({})".format(skipped["feature"], skipped["reason"]))


if __name__ == "__main__":
    main()
