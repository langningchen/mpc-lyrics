#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# -gt 0 ]]; then
  if [[ ! -f "$1" ]]; then
    echo "Not found: $1" >&2
    exit 1
  fi
  EXE_WIN="$(wslpath -w "$1")"
else
  EXE_WIN="$(powershell.exe -NoLogo -NoProfile -NonInteractive -Command \
    "[IO.Path]::Combine(\$env:LOCALAPPDATA, 'MpcLyricsCSharpBuild', 'run', 'MpcLyrics.exe')")"
  EXE_WIN="${EXE_WIN%$'\r'}"
fi
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass \
  -File "$(wslpath -w "$ROOT/scripts/run-diagnostic.ps1")" \
  -ExePath "$EXE_WIN"
