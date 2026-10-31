#!/usr/bin/env bash
# Build the plugin and produce an installable zip for a manual server deployment.
#
# Output:      out/MarkdownBook_<JellyfinVersion>.zip
# Zip layout:  MarkdownBook_<JellyfinVersion>/  containing the plugin dll, its
#              deps.json, Markdig.dll and an installation README.
#
# Usage:       ./package.sh [jellyfinVersion]   (defaults to Directory.Build.props)
set -euo pipefail

cd "$(dirname "$0")"

# Default to the version the plugin was built against (keep in sync with Directory.Build.props).
DEFAULT_JELLYFIN_VERSION="$(sed -n 's|^.*>\([0-9]\+\.[0-9]\+\.[0-9]\+\)</JellyfinVersion>.*|\1|p' Directory.Build.props | head -n1)"
JELLYFIN_VERSION="${1:-$DEFAULT_JELLYFIN_VERSION}"
STAGE_NAME="MarkdownBook_${JELLYFIN_VERSION}.0"  # targetAbi convention (e.g. MarkdownBook_12.0.0.0)
PUBLISH_DIR="out/Jellyfin.Plugin.MarkdownBook"

echo "==> Publishing for Jellyfin ${JELLYFIN_VERSION}..."
nix-shell -p dotnet-sdk_10 --run \
    "dotnet publish src/Jellyfin.Plugin.MarkdownBook/Jellyfin.Plugin.MarkdownBook.csproj -c Release -p:JellyfinVersion=${JELLYFIN_VERSION} -o ${PUBLISH_DIR}"

echo "==> Staging ${STAGE_NAME}/..."
rm -rf "out/pkg/${STAGE_NAME}"
mkdir -p "out/pkg/${STAGE_NAME}"

cp "${PUBLISH_DIR}/Jellyfin.Plugin.MarkdownBook.dll" \
   "${PUBLISH_DIR}/Jellyfin.Plugin.MarkdownBook.deps.json" \
   "${PUBLISH_DIR}/Markdig.dll" \
   "out/pkg/${STAGE_NAME}/"

# Permissions as requested: dll 600, deps.json 600, Markdig.dll 700.
chmod 600 "out/pkg/${STAGE_NAME}/Jellyfin.Plugin.MarkdownBook.dll"
chmod 600 "out/pkg/${STAGE_NAME}/Jellyfin.Plugin.MarkdownBook.deps.json"
chmod 700 "out/pkg/${STAGE_NAME}/Markdig.dll"

# README for future-me: the three forgotten install facts.
cat > "out/pkg/${STAGE_NAME}/README.md" <<EOF
# Markdown Books — plugin build for Jellyfin ${JELLYFIN_VERSION}

Markdown (.md) files in Books libraries, rendered in the web client.

## Installing

1. Unzip this folder into the plugin directory so it ends up at
   \`/var/lib/jellyfin/plugins/${STAGE_NAME}/\`:

       sudo unzip ${STAGE_NAME}.zip -d /var/lib/jellyfin/plugins/

2. Give it to the service user:

       sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/${STAGE_NAME}/

3. Restart the server:

       sudo systemctl restart jellyfin

## Reminders

- This build targets Jellyfin ${JELLYFIN_VERSION}. The \`targetAbi\` in this
  folder name is the supported version, not the plugin's own version.
- The web client reader needs the free **File Transformation** plugin
  (manifest: https://www.iamparadox.dev/jellyfin/plugins/manifest.json).
  Without it, .md books still appear in libraries but open via the API only.
- Settings: Dashboard → Plugins → Markdown Books. After a failed install
  attempt (e.g. files copied without \`Markdig.dll\`), Jellyfin persists a
  disabled status in this folder's \`meta.json\` — delete that file and
  restart if the plugin ever refuses to load again.
EOF

chmod 600 "out/pkg/${STAGE_NAME}/README.md"

echo "==> Creating zip..."
# zip/unzip (and their stored unix permissions) are only needed inside one
# short-lived nix-shell, matching how the repo builds everything else.
REPO="$PWD" STAGE_NAME="$STAGE_NAME" nix-shell -p zip unzip --run bash <<'EOS'
    cd "$REPO/out/pkg"
    rm -f "../$STAGE_NAME.zip"
    zip -r "../$STAGE_NAME.zip" "$STAGE_NAME" >/dev/null
    cd "$REPO"
    echo "==> Created out/$STAGE_NAME.zip:"
    unzip -Z1 "out/$STAGE_NAME.zip"
    echo
    unzip -Zl "out/$STAGE_NAME.zip" | grep -E 'Markdown'
EOS