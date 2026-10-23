#!/bin/bash
# Creates missing Unity .meta files for untracked files under Assets/ and their parent folders.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT" || exit 1
newguid() { uuidgen | tr -d '-' | tr 'A-Z' 'a-z'; }
make_meta() {
	local path="$1"
	[ -e "$path.meta" ] && return
	if [ -d "$path" ]; then
		printf 'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' "$(newguid)" > "$path.meta"
	else
		printf 'fileFormatVersion: 2\nguid: %s' "$(newguid)" > "$path.meta"
	fi
	echo "meta: $path.meta"
}
git ls-files --others --exclude-standard Assets | grep -v '\.meta$' | while IFS= read -r f; do
	make_meta "$f"
	d="$(dirname "$f")"
	while [ "$d" != "Assets" ] && [ "$d" != "." ]; do
		make_meta "$d"
		d="$(dirname "$d")"
	done
done
