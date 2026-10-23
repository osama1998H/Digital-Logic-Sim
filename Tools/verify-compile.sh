#!/bin/bash
# Compiles the DLS assembly with dotnet (no Unity needed), using the Unity-generated DLS.csproj as a template.
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
if [ ! -f DLS.csproj ]; then
	echo "DLS.csproj not found. Open the project in Unity once so it generates the csproj files." >&2
	exit 1
fi
OUT="$ROOT/Tools/.verify"
mkdir -p "$OUT"
GLOB="<ItemGroup><Compile Include=\"$ROOT/Assets/Scripts/**/*.cs\" Exclude=\"$ROOT/Assets/Scripts/Seb/**;$ROOT/Assets/Scripts/Description/**\" /></ItemGroup>"
grep -v '<Compile Include=' DLS.csproj \
	| sed -E "s#<ProjectReference Include=\"([^\"]+)\"#<ProjectReference Include=\"$ROOT/\1\"#" \
	| sed "s#</Project>#$GLOB</Project>#" > "$OUT/VerifyDLS.csproj"
set +e
LOG="$(dotnet build "$OUT/VerifyDLS.csproj" -nologo -v q 2>&1)"
CODE=$?
echo "$LOG" | grep -E 'error CS' | sort -u
echo "$LOG" | grep -E 'Build succeeded|Build FAILED|Error\(s\)'
exit $CODE
