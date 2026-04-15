# Installer Pipeline

Delivered in Sprint 4.

---

## Overview

Each CI build produces two artifacts from the same `build/` staging area:

| Artifact | Contents | PDB files |
|---|---|---|
| `odm-build-zip` (zip of `build/`) | `odm.exe`, all DLLs, FFmpeg DLLs, assets | Included (for debugging) |
| `odm-installer` (`odm.setup.msi`) | Same binaries as zip | Stripped before MSI build |

---

## Versioning Scheme

All CI builds use the `3.0.<run_number>` versioning scheme:

| Field | Format | Example |
|-------|--------|---------|
| `AssemblyVersion` / `AssemblyFileVersion` | `3.0.<run>.0` | `3.0.42.0` |
| `AssemblyInformationalVersion` | `3.0.<run>+<git_short_hash>` | `3.0.42+b7ee223` |
| MSI `ProductVersion` | `3.0.<run>` | `3.0.42` |
| MSI `ProductCode` | Fresh GUID per build | `{F3A1B2C4-...}` |

**Why `3.0.x`:** All previous shipped builds used `2.x` versioning. Using `3.0.x` ensures every CI build is strictly greater than any existing installed version, so Windows Installer's major-upgrade comparison (`ProductVersion` must increase) always triggers the automatic uninstall-and-reinstall flow without requiring manual uninstall.

**Why a fresh ProductCode per build:** Windows Installer uses `ProductCode` as the primary identity for upgrade detection. Rotating it on every build ensures that every new CI build is treated as a different product for upgrade purposes, enabling clean major upgrades even when the version number changes are small.

---

## Version Patching (Single Source of Truth)

The CI workflow (`odm.yml` — "Set build version" step) patches all version strings in one PowerShell step before the build runs:

**C# AssemblyInfo files patched:**
- `odm/~cfg/AssemblyInfo.global.cs`
- `onvif/~cfg/AssemblyInfo.global.cs`
- `utils/~cfg/AssemblyInfo.global.cs`

**F# AssemblyInfo files patched:**
- `odm/~cfg/AssemblyInfo.global.fs`
- `onvif/~cfg/AssemblyInfo.global.fs`
- `utils/~cfg/AssemblyInfo.global.fs`

**Installer patched:**
- `odm.setup/odm.setup.vdproj` — `ProductVersion` and `ProductCode` fields

This single step replaced the previous pattern of committing version bumps to source, which caused noise in the git history and created race conditions in CI.

---

## `build/` Staging Area

`package.bat` (run as the "Collect artifacts" CI step) copies all output from `odm/odm.ui.app/bin/x64/Release/` into `build/`. The `build/` directory serves as both:
- The runtime directory for local test deploys (the `ODM-dev` scheduled task runs `build/odm.exe`)
- The source for the zip artifact upload

**Required artifacts verified by CI** (will fail the build if missing):

```
build/odm.exe
build/odm.player.net.dll
build/odm.player.host.exe
build/odm.player.media.dll
build/avcodec-61.dll
build/avformat-61.dll
build/avutil-59.dll
build/swscale-8.dll
build/swresample-5.dll
```

---

## PDB Stripping

PDBs are stripped from the installer inputs but kept in the zip artifact. The "Strip PDB files" CI step removes `*.pdb` from:
- `odm/odm.ui.app/bin/x64/Release/` (MSI source directory)
- `build/` (already-packaged artifacts)

This runs after the zip artifact upload and before the MSI build, so the zip retains PDBs for post-deploy debugging while the MSI shipped to end users does not embed them.

---

## MSI Build Constraints

The Visual Studio Deployment Project (`odm.setup.vdproj`) requires in-process MSBuild — it cannot be built with `/m` parallel builds and will silently produce an empty MSI if attempted. The CI workflow handles this by:

1. Building the full solution with parallel MSBuild: `msbuild odm.sln /m`
2. Building the installer separately with `devenv /Build /Project odm.setup` (which sets `DisableOutOfProcBuild` first)

This separation was the fix for issue #1 — previously, using `/Project odm.ui.app` could silently skip the native C++/CLI player projects (`live555`, `odm.player.lib`, `odm.player.net`), leaving stale or absent `odm.player.net.dll` in the output.
