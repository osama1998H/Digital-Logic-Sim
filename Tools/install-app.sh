#!/bin/bash
# Builds the macOS app with Unity in batch mode and installs it to /Applications (keeping one backup).
UNITY="/Applications/Unity/Hub/Editor/6000.0.85f1/Unity.app/Contents/MacOS/Unity"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT" || exit 1
APP="/Applications/Digital-Logic-Sim.app"
LOG="Builds/unity-build.log"

# An open editor holds the project lock; batch mode would fail.
EDITOR_OPEN=0
if pgrep -fl "Unity.app/Contents/MacOS/Unity" | grep -F "$ROOT" | grep -vq -- "-batchmode"; then
	EDITOR_OPEN=1
fi
if [ -f Temp/UnityLockfile ] && lsof Temp/UnityLockfile >/dev/null 2>&1; then
	EDITOR_OPEN=1
fi
if [ "$EDITOR_OPEN" = 1 ]; then
	echo "Close the Unity Editor first (it locks the project)"
	exit 1
fi

mkdir -p Builds
"$UNITY" -batchmode -quit -projectPath "$ROOT" -executeMethod MacBuild.Build -logFile "$LOG"
grep -E '^MacBuild:' "$LOG" | tail -1
if ! grep -q '^MacBuild: Succeeded' "$LOG"; then
	grep -E 'error CS' "$LOG" | sort -u
	echo "Build failed, see $LOG"
	exit 1
fi

if [ -d "$APP" ]; then
	mkdir -p Builds/previous
	rm -rf Builds/previous/Digital-Logic-Sim.app
	mv "$APP" Builds/previous/Digital-Logic-Sim.app
fi
ditto Builds/macOS/Digital-Logic-Sim.app "$APP"
echo "Installed $APP"
if pgrep -f "$APP" >/dev/null; then
	echo "Quit the app (Cmd+Q) and reopen it to use the new version."
fi
