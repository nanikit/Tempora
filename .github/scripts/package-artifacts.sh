#!/usr/bin/env bash
set -euo pipefail

package_artifacts() {
  local archive_dir=$1 version release_dir
  version=$(jq -er '.version' build-info.json)
  release_dir=$(mktemp -d "$RUNNER_TEMP/release-artifacts.XXXXXX")

  cp "$archive_dir/Windows.zip" "$release_dir/Tempora-v$version-Windows.zip"
  cp "$archive_dir/LinuxX11.zip" "$release_dir/Tempora-v$version-Linux.zip"
  sign_macos_archive "$archive_dir/macOS.zip" "$release_dir/Tempora-v$version-macOS.zip"

  for platform in Windows Linux macOS; do
    test -s "$release_dir/Tempora-v$version-$platform.zip"
  done
  cp build-info.json "$release_dir/build-info.json"
  echo "directory=$release_dir" >> "$GITHUB_OUTPUT"
}

sign_macos_archive() {
  local work_dir app rcodesign
  work_dir=$(mktemp -d "$RUNNER_TEMP/macos-work.XXXXXX")
  install_rcodesign "$work_dir"
  rcodesign="$work_dir/rcodesign"

  unzip -q "$1" -d "$work_dir/app"
  app=$(find "$work_dir/app" -type d -name '*.app' -print -quit)
  if [ -z "$app" ]; then
    echo 'No .app bundle found in macOS archive.' >&2
    exit 1
  fi

  printf '%s\n' '<plist version="1.0"><dict/></plist>' > "$work_dir/entitlements.plist"
  # 0.29.0 replaces existing flags, then removes LINKER_SIGNED itself.
  # --shallow preserves the .NET libraries in Resources, as Apple's codesign does.
  "$rcodesign" sign --shallow --code-signature-flags linker-signed \
    --runtime-version 0.0.0 --entitlements-xml-file "$work_dir/entitlements.plist" "$app"
  check_signature_settings "$rcodesign" "$app/Contents/MacOS/Tempora"

  (
    cd "$(dirname "$app")"
    zip -qry "$2" "$(basename "$app")"
  )
}

install_rcodesign() {
  local archive='apple-codesign-0.29.0-x86_64-unknown-linux-musl'
  local digest='dbe85cedd8ee4217b64e9a0e4c2aef92ab8bcaaa41f20bde99781ff02e600002'
  curl --fail --location --silent --show-error \
    "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign%2F0.29.0/$archive.tar.gz" \
    --output "$1/rcodesign.tar.gz"
  printf '%s  %s\n' "$digest" "$1/rcodesign.tar.gz" | sha256sum -c
  tar -xzf "$1/rcodesign.tar.gz" -C "$1" --strip-components=1
}

check_signature_settings() {
  local signature
  signature=$("$1" print-signature-info "$2")
  # rcodesign verify rejects valid ad-hoc signatures in 0.29.0.
  # Check both slices of the universal executable without claiming bundle verification.
  if [ "$(grep -c 'flags: CodeSignatureFlags(ADHOC)$' <<< "$signature")" -ne 2 ] ||
     [ "$(grep -c 'runtime_version: 0[.]0[.]0$' <<< "$signature")" -ne 2 ]; then
    printf '%s\n' "$signature" >&2
    echo 'Expected ad-hoc signing without hardened runtime for both macOS architectures.' >&2
    exit 1
  fi
}

package_artifacts "${1:?Export archive directory is required.}"
