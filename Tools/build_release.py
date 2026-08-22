#!/usr/bin/env python3
# v1.0.0
# MetlabのVPMパッケージを再現可能なZIPとして生成し、バージョンを検証するスクリプト。

from __future__ import annotations

import argparse
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


PACKAGE_NAMES = ("com.metlab.worlds", "com.metlab.avatars")


def build_package(package_dir: Path, output_dir: Path, version: str) -> Path:
    manifest_path = package_dir / "package.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("version") != version:
        raise ValueError(
            f"{manifest_path}: version {manifest.get('version')} does not match {version}"
        )

    output_path = output_dir / f"{manifest['name']}-{version}.zip"
    with ZipFile(output_path, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        for source in sorted(path for path in package_dir.rglob("*") if path.is_file()):
            relative = source.relative_to(package_dir).as_posix()
            info = ZipInfo(relative, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, source.read_bytes())
    return output_path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    repository_root = Path(__file__).resolve().parents[1]
    packages_root = repository_root / "Packages"
    args.output.mkdir(parents=True, exist_ok=True)

    for package_name in PACKAGE_NAMES:
        output = build_package(packages_root / package_name, args.output, args.version)
        print(output)


if __name__ == "__main__":
    main()
