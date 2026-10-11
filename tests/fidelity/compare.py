"""Compares the Avalonia captures (ChartCaptures, $C2FLUX_CHART_OUT) with the
WinForms references and writes a Markdown table.

    python compare.py <captures-dir> [--out report.md]

Controls alone match docs/fidelity/charts/<name>.png from (0, 0); windows
match docs/fidelity/reference/<name>.png from (8, 31), the client area
inside the Windows frame. Captures without a reference are listed apart.
"""

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from png_tool import diff  # noqa: E402

DOCS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "docs", "fidelity")


def reference_for(name):
    chart = os.path.join(DOCS, "charts", name + ".png")
    if os.path.exists(chart):
        return chart, (0, 0)
    window = os.path.join(DOCS, "reference", name + ".png")
    if os.path.exists(window):
        return window, (8, 31)
    return None, None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("captures")
    parser.add_argument("--out")
    args = parser.parse_args()

    rows, missing = [], []
    for file in sorted(os.listdir(args.captures)):
        if not file.endswith(".png"):
            continue
        name = file[:-4]
        reference, offset = reference_for(name)
        if reference is None:
            missing.append(name)
            continue
        mean, strong = diff(reference, os.path.join(args.captures, file), offset)
        rows.append((name, mean, strong))

    lines = ["| Capture | Mean diff | Pixels > 40 |", "|---|---:|---:|"]
    lines += ["| %s | %.1f | %.1f %% |" % row for row in rows]
    if missing:
        lines += ["", "Without a reference: " + ", ".join(missing)]
    report = "\n".join(lines) + "\n"
    print(report)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(report)


if __name__ == "__main__":
    main()
