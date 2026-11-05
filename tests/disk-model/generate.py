#!/usr/bin/env python3
"""Writes a deterministic tree of same-size files, in shuffled order, so that
disk order and scan order differ the way they do on a disk in long use."""
import os
import random
import sys

# (files, smallest size, largest size, how many sizes)
KINDS = [
    (3000, 1024, 3 * 1024, 40),
    (800, 64 * 1024, 1024 * 1024, 30),
    (60, 8 * 1024 * 1024, 32 * 1024 * 1024, 6),
]
DUPLICATES = 0.15
DIRECTORIES = 200
STAMP = 8


def main(root):
    rng = random.Random(20261105)
    plans = []

    for count, smallest, largest, sizes in KINDS:
        choices = [rng.randrange(smallest, largest) for _ in range(sizes)]

        for _ in range(count):
            size = rng.choice(choices)

            if rng.random() < DUPLICATES:
                plans.append((size, 0, 0))
            else:
                # Differences at the start, in the middle or at the end.
                where = rng.choice((0, size // 2, size - STAMP))
                plans.append((size, where, rng.getrandbits(48) + 1))

    rng.shuffle(plans)

    for index, (size, where, stamp) in enumerate(plans):
        directory = os.path.join(root, "d%03d" % rng.randrange(DIRECTORIES))
        os.makedirs(directory, exist_ok=True)
        # Everything of one size starts out equal.
        block = random.Random(size).randbytes(1024 * 1024)

        with open(os.path.join(directory, "f%05d.bin" % index), "wb") as file:
            left = size

            while left > 0:
                file.write(block[: min(left, len(block))])
                left -= len(block)

            file.seek(where)
            file.write(stamp.to_bytes(STAMP, "little"))


if __name__ == "__main__":
    main(sys.argv[1])
