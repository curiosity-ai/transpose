#!/usr/bin/env python3
"""Rank the hot frames in a speedscope file produced by `dotnet-trace convert`.

dotnet-trace emits `evented` speedscope profiles (paired open/close records per frame), which the
speedscope UI understands but which cannot be summed by hand. This reconstructs each sample's stack
and reports two rankings:

  * allocation/CPU SITES  — the innermost managed frame of each sample (where the cost is incurred)
  * INCLUSIVE             — every frame a sample passed through (where the cost is attributable)

For an allocation trace (`--providers Microsoft-Windows-DotNETRuntime:0x1:5`) each sample is one
GCAllocationTick, i.e. ~100 KB allocated, so counts convert directly to megabytes. For a CPU trace
(SampleProfiler) the counts are samples, so read them as relative shares.

Usage:
    analyze-trace.py <file.speedscope.json> [--top N] [--filter SUBSTRING]
"""

import argparse
import collections
import json
import sys

# Pseudo-frames dotnet-trace inserts that carry no attribution of their own.
PSEUDO = {"CPU_TIME", "UNMANAGED_CODE_TIME", "(Non-Activities)", "Threads"}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("path", help="the .speedscope.json produced by `dotnet-trace convert`")
    ap.add_argument("--top", type=int, default=30, help="how many frames to list per ranking")
    ap.add_argument("--filter", default=None,
                    help="only show inclusive frames whose name contains this substring "
                         "(e.g. Transpose.Translator)")
    args = ap.parse_args()

    with open(args.path) as fh:
        doc = json.load(fh)

    names = [f["name"] for f in doc["shared"]["frames"]]
    sites = collections.Counter()
    inclusive = collections.Counter()
    total = 0

    for profile in doc["profiles"]:
        stack: list[int] = []
        previous = None
        for event in profile.get("events", ()):
            if event["type"] == "O":
                stack.append(event["frame"])
                previous = "O"
            else:
                # A close immediately after an open means `stack` is a complete sample.
                if previous == "O" and stack:
                    total += 1
                    real = [f for f in stack if names[f] not in PSEUDO]
                    if real:
                        sites[real[-1]] += 1
                        for frame in set(real):
                            inclusive[frame] += 1
                if stack:
                    stack.pop()
                previous = "C"

    if total == 0:
        print("No samples found — is this an `evented` speedscope file from dotnet-trace?", file=sys.stderr)
        return 1

    print(f"{total} samples  (~{total * 100 / 1024:.0f} MB if this is an allocation trace)\n")

    def show(counter: collections.Counter, title: str, name_filter: str | None = None) -> None:
        print(f"== {title} ==")
        shown = 0
        for frame, count in counter.most_common():
            if name_filter and name_filter not in names[frame]:
                continue
            print(f"{count * 100 / total:6.2f}%  {count * 100 / 1024:8.1f}MB  {names[frame][:130]}")
            shown += 1
            if shown >= args.top:
                break
        print()

    show(sites, "SITES (innermost managed frame)")
    show(inclusive, "INCLUSIVE (cost beneath the frame)", args.filter)
    return 0


if __name__ == "__main__":
    sys.exit(main())
