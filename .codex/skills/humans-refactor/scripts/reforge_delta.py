#!/usr/bin/env python3
"""Summarize Reforge before/after JSON for commit messages."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


def load_json(path: Path) -> dict[str, Any]:
    data = path.read_bytes()
    for encoding in ("utf-8-sig", "utf-8", "utf-16", "utf-16-le"):
        try:
            return json.loads(data.decode(encoding))
        except Exception:
            continue
    raise ValueError(f"Could not decode JSON: {path}")


def group(data: dict[str, Any], name: str) -> dict[str, Any] | None:
    for item in data.get("groups", []):
        if item.get("name") == name:
            return item
    return None


def metric_delta(before: dict[str, Any], after: dict[str, Any], key: str) -> int:
    return int(after.get(key, 0)) - int(before.get(key, 0))


def line(label: str, before: dict[str, Any], after: dict[str, Any]) -> str:
    total_before = int(before.get("total", 0))
    total_after = int(after.get("total", 0))
    total_delta = total_after - total_before
    surface_delta = metric_delta(before, after, "surfaceTotal")
    internal_delta = metric_delta(before, after, "internalComplexityTotal")
    return (
        f"- {label}: {total_before} -> {total_after} ({total_delta:+}); "
        f"surface {surface_delta:+}, internal {internal_delta:+}"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--before-all", required=True, type=Path)
    parser.add_argument("--after-all", required=True, type=Path)
    parser.add_argument("--before-section", type=Path)
    parser.add_argument("--after-section", type=Path)
    parser.add_argument("--section", required=True)
    args = parser.parse_args()

    before_all = load_json(args.before_all)
    after_all = load_json(args.after_all)

    if args.before_section and args.after_section:
        before_section_root = load_json(args.before_section)
        after_section_root = load_json(args.after_section)
    else:
        before_section_root = before_all
        after_section_root = after_all

    before_section = group(before_section_root, args.section)
    after_section = group(after_section_root, args.section)
    if before_section is None or after_section is None:
        raise ValueError(f"Section not found in Reforge output: {args.section}")

    print("Reforge:")
    print(line(args.section, before_section, after_section))
    print(line("Overall", before_all, after_all))

    suspicious = after_all.get("suspiciousImprovements") or []
    if suspicious:
        print(f"- Suspicious improvements: {len(suspicious)}")

    baseline = after_all.get("baseline")
    if baseline:
        verdict = baseline.get("verdict")
        improvement = baseline.get("improvement")
        print(f"- Baseline verdict: {verdict} (improvement={improvement})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
