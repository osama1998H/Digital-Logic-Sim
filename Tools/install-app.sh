#!/bin/bash
# Builds the macOS app with Unity in batch mode and installs it to /Applications (keeping one backup).
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.0.85f1/Unity.app/Contents/MacOS/Unity}"
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

if [ ! -x "$UNITY" ]; then
	echo "Unity not found or not executable: $UNITY"
	exit 1
fi

BUILT="Builds/macOS/Digital-Logic-Sim.app"
mkdir -p Builds
# A stale log or app from an earlier run must never be mistaken for this build.
rm -f "$LOG"
rm -rf "$BUILT"
"$UNITY" -batchmode -quit -projectPath "$ROOT" -executeMethod MacBuild.Build -logFile "$LOG"
UNITY_EXIT=$?
[ -f "$LOG" ] && grep -E '^MacBuild:' "$LOG" | tail -1
if [ "$UNITY_EXIT" -ne 0 ] || [ ! -f "$LOG" ] || ! grep -q '^MacBuild: Succeeded' "$LOG" || [ ! -d "$BUILT" ]; then
	[ -f "$LOG" ] && grep -E 'error CS' "$LOG" | sort -u
	echo "Build failed (Unity exit code $UNITY_EXIT), see $LOG. /Applications was not touched."
	exit 1
fi

BACKUP="Builds/previous/Digital-Logic-Sim.app"
if [ -d "$APP" ]; then
	mkdir -p Builds/previous || exit 1
	rm -rf "$BACKUP" || { echo "Could not remove old backup; /Applications was not touched."; exit 1; }
	mv "$APP" "$BACKUP" || { echo "Could not back up the current app; /Applications was not touched."; exit 1; }
fi
if ! ditto "$BUILT" "$APP"; then
	rm -rf "$APP"
	if [ -d "$BACKUP" ]; then
		if ditto "$BACKUP" "$APP"; then
			echo "Install failed; the previous app was restored to $APP"
		else
			echo "Install failed; previous app is in $ROOT/$BACKUP"
		fi
	else
		echo "Install failed."
	fi
	exit 1
fi
echo "Installed $APP"
if pgrep -f "$APP" >/dev/null; then
	echo "Quit the app (Cmd+Q) and reopen it to use the new version."
fi
