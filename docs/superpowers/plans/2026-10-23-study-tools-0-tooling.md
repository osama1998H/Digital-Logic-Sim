# Plan 0: Build, Compile-Check and Install Tooling

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three repo scripts that every other plan's verification relies on: a fast compile check of the Unity code, Unity `.meta` generation, and building and installing the app into `/Applications`.

**Architecture:** Shell scripts in `Tools/`. They reuse the Unity-generated `DLS.csproj` (gitignored, present locally) and the existing `MacBuild.Build` editor method (`Assets/Editor/MacBuild.cs`).

**Tech Stack:** zsh, dotnet SDK (`/usr/local/share/dotnet/dotnet`), Unity 6000.0.85f1 at `/Applications/Unity/Hub/Editor/6000.0.85f1/Unity.app`.

**Spec:** `docs/superpowers/plans/2026-10-23-study-tools-roadmap.md`

## Global Constraints

See the roadmap. Branch: `feature/study-tools-tooling`.

## Review Focus

- **Unity Editor open on the project:** batch-mode Unity refuses to start. `install-app.sh` must detect this and print a clear message rather than hang.
- **App running in `/Applications`:** replacing the bundle under a running app must not kill it. The script tells the user to quit and reopen.
- **`DLS.csproj` missing** (fresh clone, Unity never opened): `verify-compile.sh` must say "open the project in Unity once to generate DLS.csproj" and exit 1.
- **New `.cs` file not yet in `DLS.csproj`:** the compile check must glob all `Assets/Scripts/**/*.cs` (minus `Seb/**` and `Description/**`, which are separate assemblies) so it doesn't depend on Unity having refreshed the csproj.

---

### Task 1: `Tools/verify-compile.sh`

**Files:**
- Create: `Tools/verify-compile.sh` (executable)
- Modify: `.gitignore`: add `Tools/.verify/`

**Interfaces:**
- Produces: `Tools/verify-compile.sh`. Exit 0 and prints `Build succeeded.` / `0 Error(s)` when the DLS assembly compiles. Otherwise exit 1 with the `error CS` lines.

- [ ] **Step 1: Write the script.** Behaviour:
  1. `cd` to the repo root (the script's dir `/..`).
  2. Fail with the message above if `DLS.csproj` is absent.
  3. Generate `Tools/.verify/VerifyDLS.csproj` from `DLS.csproj` by removing every `<Compile Include=` line, rewriting `<ProjectReference Include="X">` to the absolute `$ROOT/X`, and inserting before `</Project>`: `<ItemGroup><Compile Include="$ROOT/Assets/Scripts/**/*.cs" Exclude="$ROOT/Assets/Scripts/Seb/**;$ROOT/Assets/Scripts/Description/**" /></ItemGroup>`.
  4. Run `dotnet build Tools/.verify/VerifyDLS.csproj -nologo -v q`, print the `error CS` lines plus the summary, and propagate the exit code.
- [ ] **Step 2: Verify it catches an error.** Temporarily add `Assets/Scripts/Game/Tmp.cs` containing `class X { void F() { int a = "s"; } }`. Run `Tools/verify-compile.sh`. Expected: exit 1 and `error CS0029`. Delete the file.
- [ ] **Step 3: Verify the clean tree.** Run `Tools/verify-compile.sh`. Expected: exit 0, `0 Error(s)`.
- [ ] **Step 4: Commit** (`Add compile-check script for the Unity code`).

### Task 2: `Tools/make-metas.sh`

**Files:**
- Create: `Tools/make-metas.sh` (executable)

**Interfaces:**
- Produces: `Tools/make-metas.sh`. For every untracked, non-ignored path under `Assets/` (from `git ls-files --others --exclude-standard Assets`) and each of its parent folders, it creates a missing `.meta`. Files get `fileFormatVersion: 2\nguid: <32 lowercase hex>` with no trailing newline. Folders get the folder template (`folderAsset: yes` + `DefaultImporter` block, matching `Assets/Scripts/Game/Layout.meta`). GUIDs come from `uuidgen | tr -d '-' | tr 'A-Z' 'a-z'`. Each created path is printed.

- [ ] **Step 1: Write the script.**
- [ ] **Step 2: Verify.** Create `Assets/Scripts/Game/TmpDir/Tmp.cs`, run the script. Expected: it prints two `meta:` lines (the file and `TmpDir.meta`), and the folder meta contains `folderAsset: yes`. Delete both files and the folder.
- [ ] **Step 3: Commit** (`Add Unity meta generation script`).

### Task 3: `Tools/install-app.sh`

**Files:**
- Create: `Tools/install-app.sh` (executable)
- Modify: `CHANGELOG.md`: under "Development", add a bullet listing the three scripts.

**Interfaces:**
- Produces: `Tools/install-app.sh`. It builds `Builds/macOS/Digital-Logic-Sim.app` with `Unity -batchmode -quit -projectPath . -executeMethod MacBuild.Build -logFile Builds/unity-build.log`, prints the `MacBuild:` line, and exits 1 on failure (also printing `error CS` lines from the log). On success:
  - It moves any existing `/Applications/Digital-Logic-Sim.app` to `Builds/previous/Digital-Logic-Sim.app`, replacing any older backup.
  - It copies the new build with `ditto`.
  - If `pgrep -f "/Applications/Digital-Logic-Sim.app"` finds a running instance, it prints "Quit the app (Cmd+Q) and reopen it to use the new version."

- [ ] **Step 1: Write the script.** Before building, if `pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectPath.*Digital-Logic-Sim"` or a Unity Editor process holding this project is found (check `Temp/UnityLockfile` exists *and* `lsof` shows it open), print "Close the Unity Editor first (it locks the project)" and exit 1.
- [ ] **Step 2: Verify.** Run it. Expected: `MacBuild: Succeeded (0 errors)`. Then `ls /Applications/Digital-Logic-Sim.app/Contents/Resources/Data/Managed/DLS.dll` exists, with a modification time from today.
- [ ] **Step 3: Commit** (`Add build-and-install script for the macOS app`), then merge into `main` and push (see roadmap Global Constraints).
