#!/usr/bin/env bash
set -euo pipefail

plugin_root=$(cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(cd -- "$plugin_root/../.." && pwd)
output="$repo_root/artifacts/jellyfin-mp2"
image=${MP2_JELLYFIN_IMAGE:-jellyfin/jellyfin@sha256:357724bf0ae27a672c7cbaa899db2d9abeb13dbd8657ccce750258a4c059d037}
sdk=${MP2_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}
mkdir -p "$output/fixture" "$output/nuget"

# A stopped container gives us the stock assets, unaffected by production bind mounts.
fixture_container=$(docker create "$image")
trap 'docker rm "$fixture_container" >/dev/null' EXIT
docker cp "$fixture_container:/jellyfin/jellyfin-web/main.jellyfin.bundle.js" "$output/fixture/main.jellyfin.bundle.js"
docker rm "$fixture_container" >/dev/null
trap - EXIT

docker run --rm --cpus 2 --memory 2g --network host \
    --user "$(id -u):$(id -g)" \
    -e DOTNET_CLI_HOME=/tmp/mp2-dotnet \
    -e NUGET_PACKAGES=/repo/artifacts/jellyfin-mp2/nuget \
    -e MP2_BUNDLE_PATH=/repo/artifacts/jellyfin-mp2/fixture/main.jellyfin.bundle.js \
    -v "$repo_root:/repo" -w /repo "$sdk" \
    dotnet test plugins/Jellyfin.Mp2Compatibility.Tests/Jellyfin.Mp2Compatibility.Tests.csproj -c Release

python3 "$plugin_root/package.py"
