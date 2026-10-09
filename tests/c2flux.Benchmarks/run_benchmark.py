#!/usr/bin/env python3
"""
Compares the scanners of two c2flux builds (baseline and candidate) on the same
machine, in the same job, by running c2flux-bench in a fresh process per
measurement and alternating the two versions.

For every target (scanner + path):
  * runs warm-up rounds (discarded: they fill the OS file system cache),
  * then N measured rounds, alternating the order (B,C then C,B ...) so that
    neither version always benefits from running second,
  * compares medians of elapsed time and peak working set against thresholds,
  * on static targets, also requires identical tree fingerprints.

Without --candidate-app it only measures the baseline (to record reference
numbers).

Usage:
    python run_benchmark.py --bench PATH --baseline-app DIR [--candidate-app DIR]
                            --target SCANNER=PATH[:static] [--target ...]
                            [--runs 5] [--warmup 1]
                            [--max-slowdown 0.10] [--max-memory-increase 0.15]
                            [--reset-dir DIR ...] [--json-out FILE] [--summary-out FILE]

Exit code is 1 when any target regressed or failed only in the candidate.
"""

import argparse
import json
import os
import shutil
import statistics
import subprocess
import sys
import time

STATIC_SUFFIX = ":static"


class Target:
    def __init__(self, spec):
        if "=" not in spec:
            raise argparse.ArgumentTypeError("target must look like SCANNER=PATH[:static], got " + spec)
        scanner, path = spec.split("=", 1)
        self.static = path.endswith(STATIC_SUFFIX)
        if self.static:
            path = path[:-len(STATIC_SUFFIX)]
        self.scanner = scanner
        self.path = path

    @property
    def label(self):
        return "{} {}".format(self.scanner, self.path)


def run_once(arguments, app_dir, target):
    for directory in arguments.reset_dir:
        shutil.rmtree(directory, ignore_errors=True)

    command = bench_command(arguments.bench) + ["--app", app_dir, "--scanner", target.scanner, "--path", target.path]
    started = time.monotonic()
    completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                               encoding="utf-8", errors="replace", timeout=arguments.timeout)
    wall_seconds = time.monotonic() - started

    result = None
    for line in reversed(completed.stdout.splitlines()):
        line = line.strip()
        if line.startswith("{"):
            try:
                result = json.loads(line)
                break
            except json.JSONDecodeError:
                pass

    if result is None:
        result = {
            "status": "error",
            "error": "c2flux-bench exited with {} without a result".format(completed.returncode),
            "error_detail": (completed.stderr or completed.stdout)[-4000:],
        }

    result["process_wall_seconds"] = wall_seconds
    return result


def bench_command(bench):
    if bench.endswith(".py"):
        return [sys.executable, bench]
    if bench.endswith(".dll"):
        return ["dotnet", bench]
    return [bench]


def median_of(results, key):
    values = [result[key] for result in results if result.get(key) is not None]
    return statistics.median(values) if values else None


def spread_of(results, key):
    """Relative spread (max - min) / median: a rough noise indicator."""
    values = [result[key] for result in results if result.get(key) is not None]
    if len(values) < 2:
        return None
    middle = statistics.median(values)
    return (max(values) - min(values)) / middle if middle else None


def summarize_version(results):
    ok = [result for result in results if result.get("status") == "ok"]
    statuses = sorted({result.get("status") for result in results})
    summary = {
        "status": statuses[0] if len(statuses) == 1 else "mixed",
        "runs": len(results),
        "ok_runs": len(ok),
        "errors": sorted({result.get("error") for result in results if result.get("error")}),
    }
    if ok:
        summary.update({
            "elapsed_ms": median_of(ok, "elapsed_ms"),
            "elapsed_spread": spread_of(ok, "elapsed_ms"),
            "peak_working_set_bytes": median_of(ok, "peak_working_set_bytes"),
            "allocated_bytes": median_of(ok, "allocated_bytes"),
            "retained_bytes": median_of(ok, "retained_bytes"),
            "digests": sorted({result["tree"]["digest"] for result in ok}),
            "tree": ok[-1]["tree"],
            "app_version": ok[-1].get("app_version"),
        })
    return summary


def compare(target, baseline, candidate, arguments):
    """Returns (verdict, notes). Verdicts: ok, regression, fixed, broken-in-both,
    unsupported, error, baseline-only."""
    notes = []

    if candidate is None:
        if baseline["status"] == "ok":
            return "baseline-only", notes
        return baseline["status"], baseline["errors"]

    if baseline["status"] == "unsupported" and candidate["status"] == "unsupported":
        return "unsupported", baseline["errors"]

    baseline_ok = baseline["status"] == "ok"
    candidate_ok = candidate["status"] == "ok"

    if not baseline_ok and not candidate_ok:
        return "broken-in-both", candidate["errors"] or baseline["errors"]
    if not candidate_ok:
        return "regression", ["candidate failed: " + "; ".join(candidate["errors"] or [candidate["status"]])]
    if not baseline_ok:
        return "fixed", ["baseline failed: " + "; ".join(baseline["errors"] or [baseline["status"]])]

    verdict = "ok"

    slowdown = ratio(candidate["elapsed_ms"], baseline["elapsed_ms"])
    if slowdown is not None and slowdown > 1 + arguments.max_slowdown:
        verdict = "regression"
        notes.append("{:+.1%} time (limit {:+.0%})".format(slowdown - 1, arguments.max_slowdown))

    memory = ratio(candidate["peak_working_set_bytes"], baseline["peak_working_set_bytes"])
    if memory is not None and memory > 1 + arguments.max_memory_increase:
        verdict = "regression"
        notes.append("{:+.1%} peak memory (limit {:+.0%})".format(memory - 1, arguments.max_memory_increase))

    if target.static:
        if len(baseline["digests"]) > 1:
            notes.append("baseline produced different trees across runs")
        if len(candidate["digests"]) > 1:
            verdict = "regression"
            notes.append("candidate produced different trees across runs")
        elif baseline["digests"] != candidate["digests"]:
            verdict = "regression"
            notes.append("tree differs from baseline: " + tree_difference(baseline["tree"], candidate["tree"]))
    else:
        difference = tree_difference(baseline["tree"], candidate["tree"])
        if difference != "same counts":
            notes.append("live path, informational: " + difference)

    return verdict, notes


def tree_difference(baseline_tree, candidate_tree):
    parts = []
    for key in ("directories", "files", "file_bytes", "root_size_bytes"):
        if baseline_tree.get(key) != candidate_tree.get(key):
            parts.append("{} {} -> {}".format(key, baseline_tree.get(key), candidate_tree.get(key)))
    return ", ".join(parts) if parts else "same counts"


def ratio(candidate_value, baseline_value):
    if candidate_value is None or not baseline_value:
        return None
    return candidate_value / baseline_value


# ----- report ----------------------------------------------------------------

VERDICT_ICONS = {
    "ok": "✅", "fixed": "🟢", "baseline-only": "📏", "unsupported": "⏭️",
    "broken-in-both": "⚠️", "regression": "❌", "error": "❌", "mixed": "❌",
}


def markdown_report(report):
    has_candidate = report["candidate_app"] is not None
    lines = ["## Scanner benchmark", ""]
    lines.append("Runs per version: {} measured + {} warm-up. Medians shown; spread = (max − min) / median."
                 .format(report["runs"], report["warmup"]))
    lines.append("")

    if has_candidate:
        lines.append("| | Scanner | Path | Baseline | Candidate | Δ time | Δ peak mem | Notes |")
        lines.append("|---|---|---|---|---|---|---|---|")
    else:
        lines.append("| | Scanner | Path | Time | Spread | Peak mem | Allocated | Files | Dirs |")
        lines.append("|---|---|---|---|---|---|---|---|---|")

    for item in report["targets"]:
        baseline = item["baseline"]
        candidate = item.get("candidate")
        icon = VERDICT_ICONS.get(item["verdict"], "❔")
        path = item["path"] + (" (static)" if item["static"] else "")

        if has_candidate:
            lines.append("| {} | {} | `{}` | {} | {} | {} | {} | {} |".format(
                icon, item["scanner"], path,
                format_time(baseline), format_time(candidate),
                format_delta(candidate, baseline, "elapsed_ms"),
                format_delta(candidate, baseline, "peak_working_set_bytes"),
                escape("; ".join(item["notes"])) or item["verdict"]))
        else:
            tree = baseline.get("tree") or {}
            lines.append("| {} | {} | `{}` | {} | {} | {} | {} | {} | {} |".format(
                icon, item["scanner"], path, format_time(baseline),
                format_percent(baseline.get("elapsed_spread")),
                format_bytes(baseline.get("peak_working_set_bytes")),
                format_bytes(baseline.get("allocated_bytes")),
                format_count(tree.get("files")), format_count(tree.get("directories"))))

    lines.append("")
    lines.append("Legend: ✅ ok · 🟢 fixed (failed only in baseline) · 📏 baseline only · "
                 "⏭️ unsupported here · ⚠️ fails in both versions · ❌ regression")

    failing = [item for item in report["targets"] if item["verdict"] in ("regression", "error", "mixed")]
    lines.append("")
    lines.append("**Result: {}**".format("FAILED ({} target(s))".format(len(failing)) if failing else "passed"))
    return "\n".join(lines) + "\n"


def format_time(summary):
    if not summary or summary.get("elapsed_ms") is None:
        return summary["status"] if summary else "—"
    text = "{:,.0f} ms".format(summary["elapsed_ms"])
    if summary.get("elapsed_spread") is not None:
        text += " ±{:.0%}".format(summary["elapsed_spread"] / 2)
    return text


def format_delta(candidate, baseline, key):
    if not candidate or not baseline:
        return "—"
    value = ratio(candidate.get(key), baseline.get(key))
    return "—" if value is None else "{:+.1%}".format(value - 1)


def format_bytes(value):
    if value is None:
        return "—"
    for unit in ("B", "KiB", "MiB", "GiB"):
        if abs(value) < 1024 or unit == "GiB":
            return "{:,.1f} {}".format(value, unit) if unit != "B" else "{:,} B".format(int(value))
        value /= 1024.0


def format_percent(value):
    return "—" if value is None else "{:.0%}".format(value)


def format_count(value):
    return "—" if value is None else "{:,}".format(value)


def escape(text):
    return text.replace("|", "\\|")


# ----- entry point -----------------------------------------------------------

def main():
    # The Windows console defaults to a legacy code page (cp1252 on the CI
    # runners) that cannot print the report's symbols.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bench", required=True, help="c2flux-bench executable (.exe, .dll or a .py stand-in)")
    parser.add_argument("--baseline-app", required=True)
    parser.add_argument("--candidate-app")
    parser.add_argument("--target", action="append", type=Target, required=True)
    parser.add_argument("--runs", type=int, default=5)
    parser.add_argument("--warmup", type=int, default=1)
    parser.add_argument("--max-slowdown", type=float, default=0.10)
    parser.add_argument("--max-memory-increase", type=float, default=0.15)
    parser.add_argument("--reset-dir", action="append", default=[],
                        help="directory deleted before every run (e.g. the app's scan cache)")
    parser.add_argument("--timeout", type=int, default=1800, help="seconds per run")
    parser.add_argument("--json-out")
    parser.add_argument("--summary-out")
    arguments = parser.parse_args()

    versions = [("baseline", arguments.baseline_app)]
    if arguments.candidate_app:
        versions.append(("candidate", arguments.candidate_app))

    report = {
        "baseline_app": arguments.baseline_app,
        "candidate_app": arguments.candidate_app,
        "runs": arguments.runs,
        "warmup": arguments.warmup,
        "max_slowdown": arguments.max_slowdown,
        "max_memory_increase": arguments.max_memory_increase,
        "targets": [],
    }

    for target in arguments.target:
        measured = {name: [] for name, _ in versions}
        total_rounds = arguments.warmup + arguments.runs

        for round_index in range(total_rounds):
            order = versions if round_index % 2 == 0 else list(reversed(versions))
            for name, app_dir in order:
                result = run_once(arguments, app_dir, target)
                phase = "warm-up" if round_index < arguments.warmup else "run"
                print("[{}] {} {} {}/{}: {} {}".format(
                    target.label, name, phase, round_index + 1, total_rounds, result.get("status"),
                    "{:,.0f} ms".format(result["elapsed_ms"]) if result.get("elapsed_ms") is not None
                    else result.get("error", "")), flush=True)
                if round_index >= arguments.warmup:
                    measured[name].append(result)
                if result.get("status") == "unsupported":
                    # Support does not change between runs; skip the remaining rounds.
                    measured[name] = [result]
            if all(len(results) == 1 and results[0].get("status") == "unsupported"
                   for results in measured.values()):
                break

        baseline = summarize_version(measured["baseline"])
        candidate = summarize_version(measured["candidate"]) if arguments.candidate_app else None
        verdict, notes = compare(target, baseline, candidate, arguments)

        report["targets"].append({
            "scanner": target.scanner,
            "path": target.path,
            "static": target.static,
            "verdict": verdict,
            "notes": notes,
            "baseline": baseline,
            "candidate": candidate,
            "raw": measured,
        })
        print("[{}] => {} {}".format(target.label, verdict, "; ".join(notes)), flush=True)

    # Files first: a console problem must never cost the measurements.
    markdown = markdown_report(report)

    if arguments.json_out:
        with open(arguments.json_out, "w", encoding="utf-8") as handle:
            json.dump(report, handle, ensure_ascii=False, indent=2)
    if arguments.summary_out:
        with open(arguments.summary_out, "a", encoding="utf-8") as handle:
            handle.write(markdown)

    print()
    print(markdown)

    failed = any(item["verdict"] in ("regression", "error", "mixed") for item in report["targets"])
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
