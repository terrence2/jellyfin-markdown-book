#!/usr/bin/env bash
# Edit -> build -> deploy -> restart loop for the docker-based dev instance.
set -euo pipefail

cd "$(dirname "$0")"

PLUGIN_DIR=".dev/config/plugins/MarkdownBook"
CONTAINER="${1:-jf12}"

nix-shell -p dotnet-sdk_10 --run \
    "dotnet publish src/Jellyfin.Plugin.MarkdownBook/Jellyfin.Plugin.MarkdownBook.csproj -c Release -o out/Jellyfin.Plugin.MarkdownBook"

mkdir -p "$PLUGIN_DIR"
cp out/Jellyfin.Plugin.MarkdownBook/*.dll out/Jellyfin.Plugin.MarkdownBook/*.deps.json "$PLUGIN_DIR/"

if docker ps -a --format '{{.Names}}' | grep -qx "$CONTAINER"; then
    docker restart "$CONTAINER"
    echo "Restarted $CONTAINER. Logs: docker logs -f $CONTAINER"
else
    echo "Docker container '$CONTAINER' not found — see README for the 'docker run' line."
fi