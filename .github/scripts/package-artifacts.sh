#!/usr/bin/env bash
set -euo pipefail

info=$(find raw-export-archives -type f -name 'build-info.json' -print -quit)
version=$(jq -er '.version' "$info")
release_dir="$RUNNER_TEMP/release-artifacts"
mac_work_dir="$RUNNER_TEMP/macos-work"
mkdir -p "$release_dir" "$mac_work_dir"

archives=()
while IFS= read -r -d '' archive; do
  archives+=("$archive")
done < <(find raw-export-archives -type f -name '*.zip' -print0)

mac_archive=''
for archive in "${archives[@]}"; do
  archive_name=$(basename "$archive")
  if [ "$archive_name" = 'macOS.zip' ]; then
    mac_archive="$archive"
  else
    case "$archive_name" in
      *Windows*) platform='Windows' ;;
      *Linux*) platform='Linux' ;;
      *) echo "Unexpected export archive: $archive_name" >&2; exit 1 ;;
    esac
    cp "$archive" "$release_dir/Tempora-v$version-$platform.zip"
  fi
done

if [ -z "$mac_archive" ]; then
  echo 'macOS export archive not found.' >&2
  exit 1
fi

ditto -x -k "$mac_archive" "$mac_work_dir"
app=$(find "$mac_work_dir" -type d -name '*.app' -print -quit)
if [ -z "$app" ]; then
  echo 'No .app bundle found in macOS archive.' >&2
  exit 1
fi

xattr -cr "$app"
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict --verbose=4 "$app"
signature=$(codesign -dv --verbose=4 "$app" 2>&1)
if printf '%s\n' "$signature" | grep -Eq '\(adhoc,runtime\)|Runtime Version='; then
  echo 'macOS app is still signed with hardened runtime.' >&2
  exit 1
fi

mac_zip="$release_dir/Tempora-v$version-macOS.zip"
(
  cd "$(dirname "$app")"
  ditto -c -k --keepParent "$(basename "$app")" "$mac_zip"
)

for platform in Windows Linux macOS; do
  test -s "$release_dir/Tempora-v$version-$platform.zip"
done
cp "$info" "$release_dir/build-info.json"
echo "directory=$release_dir" >> "$GITHUB_OUTPUT"
