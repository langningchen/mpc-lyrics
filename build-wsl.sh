#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

if ! command -v wslpath >/dev/null 2>&1; then
  echo "This script must be run inside WSL." >&2
  exit 1
fi

if command -v powershell.exe >/dev/null 2>&1; then
  POWERSHELL="powershell.exe"
elif [[ -x /mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe ]]; then
  POWERSHELL="/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
else
  echo "Windows PowerShell could not be found through WSL interop." >&2
  exit 1
fi

ROOT_WIN="$(wslpath -w "$ROOT")"
SCRIPT_WIN="$(wslpath -w "$ROOT/scripts/build.ps1")"

"$POWERSHELL" \
  -NoLogo \
  -NoProfile \
  -NonInteractive \
  -ExecutionPolicy Bypass \
  -File "$SCRIPT_WIN" \
  -ProjectRoot "$ROOT_WIN"

if [[ ! -f dist/MpcLyrics.exe ]]; then
  echo "Build finished without dist/MpcLyrics.exe" >&2
  exit 1
fi

echo
echo "Output: $ROOT/dist/MpcLyrics.exe"
echo "Keep the entire dist directory together when moving the program."
