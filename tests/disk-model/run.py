#!/usr/bin/env python3
"""Compares the duplicate analysis of two versions on a real block device.

Usage (root): run.py <device> <tree> <baseline tool> <current tool> <work dir>

For each version, with the page cache dropped: blktrace records the reads the
disk under the device really gets, and from them comes what a hard disk would
spend, seeking and transferring. The device is a dm-delay target: set to 8 ms
per request it also gives a real time under latency, which is the same for
every request and so says nothing about seeks. Set to 0 it gives the time on
the runner's SSD. dm-delay shows no requests itself: the loop device under it
is what blktrace watches.
"""
import json
import os
import shutil
import subprocess
import sys
import time

SECTOR = 512
SEEK_SECONDS = 0.008
BYTES_PER_SECOND = 150e6


def run(*command, **options):
    return subprocess.run(command, check=True, text=True, capture_output=True, **options).stdout


def set_delay(device, backing, milliseconds):
    sectors = run("blockdev", "--getsz", backing).strip()
    name = os.path.basename(device)
    run("dmsetup", "suspend", name)
    run("dmsetup", "load", name, "--table", f"0 {sectors} delay {backing} 0 {milliseconds}")
    run("dmsetup", "resume", name)


def drop_caches():
    subprocess.run(["sync"], check=True)
    with open("/proc/sys/vm/drop_caches", "w") as file:
        file.write("3")


def measure(tool, traced, tree, work, label, env):
    drop_caches()
    trace = os.path.join(work, label + ".blktrace")
    os.makedirs(trace, exist_ok=True)
    tracer = subprocess.Popen(["blktrace", "-d", traced, "-D", trace, "-o", "trace"], stdout=subprocess.DEVNULL)
    time.sleep(1)
    groups = os.path.join(work, label + ".groups")
    seconds = float(run(tool, tree, groups, env=env).strip().splitlines()[-1])
    tracer.terminate()
    tracer.wait()
    return {"label": label, "seconds": seconds, "groups": groups, **analyze(trace)}


def analyze(trace):
    lines = run("blkparse", "-q", "-i", "trace", "-D", trace, "-f", "%a %d %S %n\n").splitlines()
    requests = seeks = sectors = distance = 0
    position = None

    for line in lines:
        parts = line.split()

        if len(parts) != 4 or parts[0] != "D" or "R" not in parts[1]:
            continue

        start, count = int(parts[2]), int(parts[3])
        requests += 1
        sectors += count

        if position is not None and start != position:
            seeks += 1
            distance += abs(start - position)

        position = start + count

    bytes_read = sectors * SECTOR
    return {
        "requests": requests,
        "seeks": seeks,
        "seek_gb": distance * SECTOR / 1e9,
        "read_mb": bytes_read / 1e6,
        "model_seconds": seeks * SEEK_SECONDS + bytes_read / BYTES_PER_SECOND,
    }


def row(result):
    return "| {label} | {read_mb:.0f} | {requests} | {seeks} | {seek_gb:.1f} | {model_seconds:.1f} | {seconds:.1f} |".format(**result)


def main(device, tree, baseline, current, work):
    backing = run("dmsetup", "deps", "-o", "devname", os.path.basename(device)).split("(")[-1].strip(" )\n")
    backing = "/dev/" + backing
    home = os.path.join(work, "home")
    env = {**os.environ, "C2FLUX_HOME": home}
    results = []

    for delay, disk in ((8, "HD"), (0, "SSD")):
        set_delay(device, backing, delay)
        shutil.rmtree(home, ignore_errors=True)
        results.append(measure(baseline, backing, tree, work, f"{disk} before", env))
        results.append(measure(current, backing, tree, work, f"{disk} after", env))
        # Stamps need the 2 settled seconds: the tree is older by now.
        results.append(measure(current, backing, tree, work, f"{disk} after, repeated", env))

    print("| Run | Read (MB) | Requests | Seeks | Seek distance (GB) | Modelled HD (s) | Measured (s) |")
    print("|---|---:|---:|---:|---:|---:|---:|")

    for result in results:
        print(row(result))

    by_label = {result["label"]: result for result in results}
    failures = []

    expected = open(by_label["HD before"]["groups"]).read()

    for result in results:
        if open(result["groups"]).read() != expected:
            failures.append(f"{result['label']}: different groups")

    if not expected.strip():
        failures.append("No groups found: the tree tests nothing")

    hd_before, hd_after, hd_again = by_label["HD before"], by_label["HD after"], by_label["HD after, repeated"]

    for name in ("read_mb", "seeks", "model_seconds"):
        if hd_after[name] >= hd_before[name]:
            failures.append(f"HD: {name} did not improve: {hd_before[name]} to {hd_after[name]}")

    # Files that differ midway are read again: only full hashes are remembered.
    if hd_again["read_mb"] >= hd_after["read_mb"]:
        failures.append(f"HD: repeating read {hd_again['read_mb']} MB, first time {hd_after['read_mb']} MB")

    # dm-delay costs the same per request, so these times barely see seeks.
    if hd_after["seconds"] > hd_before["seconds"] * 1.25:
        failures.append(f"HD: slower: {hd_before['seconds']} s to {hd_after['seconds']} s")

    ssd_before, ssd_after = by_label["SSD before"], by_label["SSD after"]

    # Times on a shared runner vary, and this case has little left to gain.
    if ssd_after["seconds"] > ssd_before["seconds"] * 1.25:
        failures.append(f"SSD: slower: {ssd_before['seconds']} s to {ssd_after['seconds']} s")

    with open(os.path.join(work, "results.json"), "w") as file:
        json.dump(results, file, indent=1)

    for failure in failures:
        print("FAILED: " + failure, file=sys.stderr)

    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
