#!/usr/bin/env python3
"""Add (or replace) one plugin version in the Jellyfin catalog manifest.json.

The manifest is this fork's plugin repository: add
https://raw.githubusercontent.com/Seekeer/Jellyfinepisodelimitplugin/main/manifest.json
in Jellyfin under Dashboard > Plugins > Repositories.

Example:
  python3 tools/manifest_add_version.py --guid a1b2c3d4-... --version 2.0.1.0 \
      --target-abi 12.2.0.0 --source-url https://.../plugin.zip --checksum <md5> \
      --changelog "..."
"""
import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--manifest", default="manifest.json")
    parser.add_argument("--guid", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--target-abi", required=True)
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--checksum", required=True, help="MD5 of the zip, lowercase hex")
    parser.add_argument("--changelog", default="")
    args = parser.parse_args()

    path = Path(args.manifest)
    plugins = json.loads(path.read_text(encoding="utf-8"))
    plugin = next((p for p in plugins if p.get("guid") == args.guid), None)
    if plugin is None:
        print(f"Plugin {args.guid} is not in {path}; add its entry first.", file=sys.stderr)
        return 1

    entry = {
        "version": args.version,
        "changelog": args.changelog,
        "targetAbi": args.target_abi,
        "sourceUrl": args.source_url,
        "checksum": args.checksum.lower(),
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    versions = [v for v in plugin.get("versions", []) if v.get("version") != args.version]
    versions.insert(0, entry)
    versions.sort(key=lambda v: tuple(int(x) for x in v["version"].split(".")), reverse=True)
    plugin["versions"] = versions

    path.write_text(json.dumps(plugins, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{plugin['name']} {args.version} -> {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
