#!/usr/bin/env bash
# Big Walk Mod Menu - online installer for Linux / Steam Deck.
#
#   bash <(curl -fsSL https://github.com/Giorey01/BigWalkModMenu/releases/latest/download/install.sh)
#
# Downloads the package of this release, checks it, and runs the installer that is inside it.
# Options are passed on to it (--mod a,b  --all-mods  --yes  --dry-run  "/path/to/Big Walk"):
#   curl -fsSL .../install.sh | bash -s -- --mod voice,reshade
#
# @VERSION@ and @SHA256@ are filled in by build.sh: the copy to run is the one attached to a release.

main() {
  local version="@VERSION@" sha256="@SHA256@"
  local url="https://github.com/Giorey01/BigWalkModMenu/releases/download/v$version/BigWalkModMenu-$version.zip"
  local tmp sum

  fail() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

  case "$version" in *@*) fail "This is the template. Run the install.sh attached to a release instead." ;; esac

  tmp="$(mktemp -d)" || fail "Could not create a temporary folder."
  trap 'rm -rf "$tmp"' EXIT

  printf 'Downloading Big Walk Mod Menu %s...\n' "$version"
  if command -v curl >/dev/null 2>&1; then
    curl -fL --retry 2 -sS -o "$tmp/package.zip" "$url" || fail "Download failed: $url"
  elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$tmp/package.zip" "$url" || fail "Download failed: $url"
  else
    fail "curl or wget is needed."
  fi

  command -v sha256sum >/dev/null 2>&1 || fail "The sha256sum command is missing: the download cannot be verified."
  sum="$(sha256sum "$tmp/package.zip" | cut -d' ' -f1)"
  [ "$sum" = "$sha256" ] || fail "The downloaded file is not the expected one (different checksum). Not installing it."

  mkdir "$tmp/package"
  if command -v unzip >/dev/null 2>&1; then
    unzip -q "$tmp/package.zip" -d "$tmp/package"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$tmp/package.zip" "$tmp/package"
  elif command -v bsdtar >/dev/null 2>&1; then
    bsdtar -xf "$tmp/package.zip" -C "$tmp/package"
  else
    fail "unzip, python3 or bsdtar is needed to extract the package."
  fi || fail "Could not extract the package."

  # With "curl | bash" the keyboard is not on standard input: the installer's questions read the terminal.
  if [ -t 0 ]; then
    bash "$tmp/package/install-linux.sh" "$@"
  elif (exec < /dev/tty) 2>/dev/null; then
    bash "$tmp/package/install-linux.sh" "$@" < /dev/tty
  else
    bash "$tmp/package/install-linux.sh" --yes "$@"
  fi
}

main "$@"
