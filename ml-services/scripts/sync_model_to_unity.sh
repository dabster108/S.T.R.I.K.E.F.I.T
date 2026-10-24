#!/usr/bin/env bash
# Copy the exported motion forecaster from ml-services/models/ into the Unity project (Assets/Models/ExpertAI/) and check that
# every copy is byte-identical (SHA-256).
#
#   ml-services/scripts/sync_model_to_unity.sh          copy, then verify
#   ml-services/scripts/sync_model_to_unity.sh --check  verify only (exit 1 if a file differs or is missing)
#
# Only the files themselves are copied. Their .meta files in Unity are left alone, so the GUIDs that ExpertAIConfig
# (Assets/Settings/Defense/L3_HarborTyrant_Expert.asset) points at stay valid. After a copy, open Unity once so it re-imports the
# model, then run the EditMode tests: GoldenVector_ModelOutputMatchesPython checks the Unity run against golden_vector.json.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
src="$here/../models"
dst="$here/../../Assets/Models/ExpertAI"
files=(motion_forecaster.onnx norm.json golden_vector.json)

sha() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

check_only=0
[[ "${1:-}" == "--check" ]] && check_only=1

[[ -d "$dst" ]] || { echo "Unity folder not found: $dst" >&2; exit 1; }

if [[ $check_only -eq 0 ]]; then
  for f in "${files[@]}"; do
    [[ -f "$src/$f" ]] || { echo "missing in ml-services/models: $f (run the pipeline first)" >&2; exit 1; }
    cp "$src/$f" "$dst/$f"
  done
fi

status=0
for f in "${files[@]}"; do
  if [[ ! -f "$src/$f" || ! -f "$dst/$f" ]]; then
    echo "MISSING    $f"; status=1; continue
  fi
  a="$(sha "$src/$f")"; b="$(sha "$dst/$f")"
  if [[ "$a" == "$b" ]]; then echo "identical  $f  $a"; else echo "DIFFERENT  $f  models/ $a  Unity $b"; status=1; fi
done
exit $status
