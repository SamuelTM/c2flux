#!/usr/bin/env python3
"""Pixel tools for comparing the Avalonia UI with the WinForms references
(docs/fidelity/). Standard library only.

  png_tool.py diff  <reference.png> <port.png> [--offset X,Y]
      Mean and share of strongly different pixels (largest channel, 0-255),
      the port placed at X,Y inside the reference.
  png_tool.py scan  <file.png> row|column <index> [<from> <to>]
      Where the color changes along one row or column.
  png_tool.py pixel <file.png> <x,y> [<x,y> ...]
  png_tool.py zoom  <file.png> <x> <y> <width> <height> <factor> <out.png>
"""

import argparse
import struct
import sys
import zlib


def load(path):
    """Returns (width, height, rows) with rows[y][x] = (r, g, b)."""
    data = open(path, "rb").read()
    position, idat = 8, b""
    width = height = color_type = None

    while position < len(data):
        (length,) = struct.unpack(">I", data[position:position + 4])
        kind = data[position + 4:position + 8]
        chunk = data[position + 8:position + 8 + length]
        position += 12 + length

        if kind == b"IHDR":
            width, height, depth, color_type = struct.unpack(">IIBB", chunk[:10])
            if depth != 8 or color_type not in (2, 6):
                sys.exit("%s: only 8-bit RGB or RGBA PNGs are supported" % path)
        elif kind == b"IDAT":
            idat += chunk

    bpp = 3 if color_type == 2 else 4
    stride = width * bpp
    raw = zlib.decompress(idat)
    rows, previous, index = [], bytearray(stride), 0

    for _ in range(height):
        kind = raw[index]
        line = bytearray(raw[index + 1:index + 1 + stride])
        index += 1 + stride

        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = previous[x]
            c = previous[x - bpp] if x >= bpp else 0

            if kind == 1:
                line[x] = (line[x] + a) & 255
            elif kind == 2:
                line[x] = (line[x] + b) & 255
            elif kind == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif kind == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255

        rows.append([tuple(line[x * bpp:x * bpp + 3]) for x in range(width)])
        previous = line

    return width, height, rows


def save(path, rows):
    height, width = len(rows), len(rows[0])
    raw = b"".join(b"\x00" + bytes(value for pixel in row for value in pixel) for row in rows)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)))
        handle.write(chunk(b"IDAT", zlib.compress(raw)))
        handle.write(chunk(b"IEND", b""))


def diff(reference, port, offset):
    _, _, a = load(reference)
    width, height, b = load(port)
    ox, oy = offset
    total = strong = 0

    for y in range(height):
        for x in range(width):
            d = max(abs(i - j) for i, j in zip(a[y + oy][x + ox], b[y][x]))
            total += d
            strong += d > 40

    count = width * height
    print("mean %.1f, >40: %.1f%%" % (total / count, 100 * strong / count))


def scan(path, axis, index, start, end):
    width, height, rows = load(path)
    end = end if end is not None else (width if axis == "row" else height)
    previous = None

    for i in range(start, end):
        pixel = rows[index][i] if axis == "row" else rows[i][index]
        if pixel != previous:
            print(i, pixel)
            previous = pixel


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    p = commands.add_parser("diff")
    p.add_argument("reference")
    p.add_argument("port")
    p.add_argument("--offset", default="0,0")

    p = commands.add_parser("scan")
    p.add_argument("file")
    p.add_argument("axis", choices=["row", "column"])
    p.add_argument("index", type=int)
    p.add_argument("start", type=int, nargs="?", default=0)
    p.add_argument("end", type=int, nargs="?")

    p = commands.add_parser("pixel")
    p.add_argument("file")
    p.add_argument("points", nargs="+")

    p = commands.add_parser("zoom")
    p.add_argument("file")
    p.add_argument("x", type=int)
    p.add_argument("y", type=int)
    p.add_argument("width", type=int)
    p.add_argument("height", type=int)
    p.add_argument("factor", type=int)
    p.add_argument("out")

    args = parser.parse_args()

    if args.command == "diff":
        diff(args.reference, args.port, tuple(int(v) for v in args.offset.split(",")))
    elif args.command == "scan":
        scan(args.file, args.axis, args.index, args.start, args.end)
    elif args.command == "pixel":
        _, _, rows = load(args.file)
        for point in args.points:
            x, y = (int(v) for v in point.split(","))
            print(point, rows[y][x])
    else:
        _, _, rows = load(args.file)
        k = args.factor
        out = []
        for y in range(args.y, args.y + args.height):
            line = [pixel for pixel in rows[y][args.x:args.x + args.width] for _ in range(k)]
            out.extend([line] * k)
        save(args.out, out)


if __name__ == "__main__":
    main()
