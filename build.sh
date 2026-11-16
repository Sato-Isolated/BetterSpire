#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"
exec > >(tee "$ROOT/build.log") 2>&1
trap 'echo "BUILD FAILED. No new release is certified. See build.log." >&2' ERR
DEST="$ROOT/dist/BetterSpire2Lite"
rm -f "$DEST/BetterSpire2Lite.dll" "$DEST/BetterSpire2Lite.json" "$DEST/BetterSpire2Lite.zip" "$DEST/BUILD-SUCCESS.txt"
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
echo '1/13 Running C# simulator and overhead HUD tests...'
dotnet run --project tests/Guardian.Core.Tests.csproj --configuration Release
echo '2/13 Running C# journal tests...'
dotnet run --project tests/Journal.Core.Tests.csproj --configuration Release
echo '3/13 Running C# run damage meter tests...'
dotnet run --project tests/DamageMeter.Core.Tests.csproj --configuration Release
echo '4/13 Running C# refresh/cache/checkpoint regression tests...'
dotnet run --project tests/Performance.Core.Tests.csproj --configuration Release
echo '5/13 Running C# HUD dependency, navigation and geometry tests...'
dotnet run --project tests/Hud.Core.Tests.csproj --configuration Release
echo '6/13 Running C# poison provenance and async isolation tests...'
dotnet run --project tests/Poison.Core.Tests.csproj --configuration Release
echo '7/13 Running source-linked game-boundary tests...'
dotnet run --project tests/GameBoundary.Core.Tests.csproj --configuration Release
echo '8/13 Checking native journal accounting and damage chronology...'
dotnet run --project tests/NativeJournal.Core.Tests.csproj --configuration Release
echo '9/13 Checking isolated previews and forecast coverage...'
dotnet run --project tests/NativePreview.Core.Tests.csproj --configuration Release
echo '10/13 Checking readonly native hooks and combat identity fences...'
dotnet run --project tests/NativeHooks.Core.Tests.csproj --configuration Release
echo '11/13 Building mod...'
dotnet build BetterSpire2Lite.csproj --configuration Release --nologo
echo '12/13 Checking v0.113 metadata contract and patch targets...'
dotnet run --project tests/GameApiCompatibility.Tests.csproj --configuration Release
echo '13/13 Checking native damage source provenance...'
dotnet run --project tests/DamageSourceNative.Tests.csproj --configuration Release
OUT="$ROOT/bin/Release/net9.0"
test -s "$OUT/BetterSpire2Lite.dll"
test -s "$OUT/BetterSpire2Lite.json"
mkdir -p "$DEST"
cp "$OUT/BetterSpire2Lite.dll" "$OUT/BetterSpire2Lite.json" "$DEST/"
printf 'Build timestamp: %s\nC# core/overhead/journal/damage-meter/performance/HUD/poison/game-boundary/native-journal/preview-isolation/native-hooks/v0.113-API-IL-contract/native-damage-source tests and mod compilation: passed.\nIn-game validation: NOT performed.\n' "$(date -u +%FT%TZ)" > "$DEST/BUILD-SUCCESS.txt"
echo "SUCCESS: $DEST"
echo 'Install only the DLL and JSON manifest. Never copy references/ into the game.'
