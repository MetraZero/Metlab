#!/usr/bin/env python3
# v1.0.0
# GitHub Releaseで生成したZIPのSHA-256をVPM一覧へ安全に反映するスクリプト。

from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--world", required=True)
    parser.add_argument("--avatar", required=True)
    args = parser.parse_args()

    listing_path = Path(__file__).resolve().parents[1] / "Website" / "index.json"
    listing = json.loads(listing_path.read_text(encoding="utf-8-sig"))
    checksums = {
        "com.metlab.worlds": args.world,
        "com.metlab.avatars": args.avatar,
    }

    for package_name, checksum in checksums.items():
        if len(checksum) != 64 or any(character not in "0123456789abcdef" for character in checksum.lower()):
            raise ValueError(f"Invalid SHA-256 for {package_name}: {checksum}")
        versions = listing["packages"][package_name]["versions"]
        if args.version not in versions:
            raise KeyError(f"Version {args.version} is missing for {package_name}")
        versions[args.version]["zipSHA256"] = checksum.lower()

    listing_path.write_text(
        json.dumps(listing, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
