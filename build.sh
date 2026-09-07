#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"
exec > >(tee "$ROOT/build.log") 2>&1
trap 'echo "BUILD FAILED. No new release is certified. See build.log." >&2' ERR
DEST="$ROOT/dist/BetterSpire2Lite"
rm -f "$DEST/BetterSpire2Lite.dll" "$DEST/BetterSpire2Lite.json" "$DEST/BUILD-SUCCESS.txt"
if ! command -v dotnet >/dev/null 2>&1; then
    echo 'Install Microsoft .NET SDK 9 (not just the runtime).' >&2
    exit 1
fi
if ! dotnet --list-sdks | grep -q '^9\.'; then
    echo 'Microsoft .NET SDK 9 is required.' >&2
    exit 1
fi
for ref in sts2.dll GodotSharp.dll 0Harmony.dll; do
    test -f "$ROOT/references/$ref"
done
echo '1/7 Running C# simulator and overhead HUD tests...'
dotnet run --project tests/Guardian.Core.Tests.csproj --configuration Release
echo '2/7 Running C# journal tests...'
dotnet run --project tests/Journal.Core.Tests.csproj --configuration Release
echo '3/7 Running C# run damage meter tests...'
dotnet run --project tests/DamageMeter.Core.Tests.csproj --configuration Release
echo '4/7 Running C# refresh/cache/checkpoint regression tests...'
dotnet run --project tests/Performance.Core.Tests.csproj --configuration Release
echo '5/7 Running C# HUD dependency, navigation and geometry tests...'
dotnet run --project tests/Hud.Core.Tests.csproj --configuration Release
echo '6/7 Running C# poison provenance and async isolation tests...'
dotnet run --project tests/Poison.Core.Tests.csproj --configuration Release
echo '7/7 Building mod...'
dotnet build BetterSpire2Lite.csproj --configuration Release --nologo
OUT="$ROOT/bin/Release/net9.0"
test -s "$OUT/BetterSpire2Lite.dll"
test -s "$OUT/BetterSpire2Lite.json"
mkdir -p "$DEST"
cp "$OUT/BetterSpire2Lite.dll" "$OUT/BetterSpire2Lite.json" "$DEST/"
printf 'Built locally: %s\nC# core/overhead/journal/damage-meter/performance/HUD/poison tests and mod compilation: passed.\nIn-game validation: NOT performed.\n' "$(date -u +%FT%TZ)" > "$DEST/BUILD-SUCCESS.txt"
echo "SUCCESS: $DEST"
echo 'Install only the DLL and JSON manifest. Never copy references/ into the game.'
