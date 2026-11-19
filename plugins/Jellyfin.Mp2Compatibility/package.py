"""Package only our assembly and Jellyfin metadata; never bundle host dependencies."""

from datetime import datetime, timezone
from hashlib import sha256, md5
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

plugin_root = Path(__file__).resolve().parent
repo_root = plugin_root.parents[1]
version = "1.0.1.0"
assembly = "Jellyfin.Plugin.Mp2Compatibility.dll"
dll = plugin_root / "bin" / "Release" / "net10.0" / assembly
output = repo_root / "artifacts" / "jellyfin-mp2"
output.mkdir(parents=True, exist_ok=True)
metadata = {
    "guid": "a236529a-ea3c-4f69-93a9-f8b3a68c41dc",
    "name": "MP2 Audio Compatibility",
    "version": version,
    "targetAbi": "12.2.0.0",
    "framework": "net10.0",
    "owner": "Seekeer",
    "category": "General",
    "overview": "Automatic MP2 audio conversion for Jellyfin Web",
    "description": "Removes MP2 from the verified Web 12.2 compatibility profile, allowing AAC audio conversion with video stream copy. Unknown bundles are unchanged. No Web file writes or extra plugins.",
    "changelog": "Catalog release: checksum-guarded Web 12.2 compatibility patch and cache invalidation.",
    "timestamp": datetime.now(timezone.utc).isoformat(),
    "status": "Active",
    "autoUpdate": True,
    "assemblies": [assembly],
}
package = output / f"Jellyfin.Plugin.Mp2Compatibility-{version}.zip"
with ZipFile(package, "w", ZIP_DEFLATED) as archive:
    archive.write(dll, assembly)
    archive.writestr("meta.json", json.dumps(metadata, ensure_ascii=False, indent=2) + "\n")
(output / "SHA256SUMS").write_text(f"{sha256(package.read_bytes()).hexdigest()}  {package.name}\n")
print(package)

(output / "MD5SUMS").write_text(f"{md5(package.read_bytes()).hexdigest()}  {package.name}\n")
