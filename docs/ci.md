# CI Workflow

Workflow file: `.github/workflows/odm.yml`

Triggers: push and pull request to `development` branch.  
Runner: `windows-2022`.

---

## Build Order

Steps run in this sequence — order matters because later steps depend on earlier outputs:

| Step | What it does |
|------|-------------|
| Checkout | Full history (`fetch-depth: 0`) — needed for `git rev-parse --short HEAD` in version patching |
| Setup VS Dev Environment | Installs Visual Studio 2022 build tools via `seanmiddleditch/gha-setup-vsdevenv@v4` |
| Install .NET Framework targeting packs | Copies .NET 4.0 and 4.5 reference assemblies via NuGet — required because `windows-2022` runners don't include them |
| Set build version | Patches all AssemblyInfo files and the vdproj — see below |
| Build application | `msbuild odm.sln /p:Configuration=Release /p:Platform=x64 /m` — full solution, parallel |
| Restore tests | `dotnet restore odm/odm.tests/odm.tests.csproj` |
| Build tests | `msbuild odm.tests.csproj /p:Configuration=Debug /p:Platform="Any CPU"` |
| Run tests | `vstest.console.exe` — runs offline tests only; integration tests skip via `Assert.Inconclusive` |
| Collect artifacts | `package.bat` — stages `build/` directory |
| Verify required DLLs | PowerShell check that all required binaries are present in `build/` |
| Upload zip artifact | `actions/upload-artifact@v4` — uploads `build/` as `odm-build-zip` |
| Strip PDB files | Removes `*.pdb` from MSI source dirs (not from zip) |
| DisableOutOfProcBuild | Runs `DisableOutOfProcBuild.exe` — required for vdproj builds |
| Build installer | `devenv odm.sln /Build "Release|x64" /Project odm.setup` |
| Upload installer | `actions/upload-artifact@v4` — uploads MSI as `odm-installer` |

---

## Version Patching

The "Set build version" step is the single source of truth for all version numbers in a given CI build. No version commits are made to source control by CI.

**Variables computed:**

```
$run     = github.run_number          (e.g. 42)
$hash    = git rev-parse --short HEAD  (e.g. b7ee223)
$ver4    = "3.0.$run.0"               (e.g. 3.0.42.0)
$verMsi  = "3.0.$run"                  (e.g. 3.0.42)
$verInfo = "3.0.$run+$hash"           (e.g. 3.0.42+b7ee223)
```

**Files patched (regex replacement, in-memory, then written):**

- `odm/~cfg/AssemblyInfo.global.cs`, `onvif/~cfg/AssemblyInfo.global.cs`, `utils/~cfg/AssemblyInfo.global.cs`
  - `AssemblyVersion` → `$ver4`
  - `AssemblyFileVersion` → `$ver4`
  - `AssemblyInformationalVersion` → `$verInfo`

- `odm/~cfg/AssemblyInfo.global.fs`, `onvif/~cfg/AssemblyInfo.global.fs`, `utils/~cfg/AssemblyInfo.global.fs`
  - `AssemblyVersion` → `$ver4`
  - `AssemblyFileVersion` → `$ver4`

- `odm.setup/odm.setup.vdproj`
  - `ProductVersion` → `$verMsi`
  - `ProductCode` → fresh `[guid]::NewGuid()` (rotated per build)

---

## Artifact Structure

### `odm-build-zip` (zip of `build/`)

All runtime files including PDBs. Intended for local test deploys and debugging.

Required files (verified by CI, build fails if missing):
- `build/odm.exe`
- `build/odm.player.net.dll`
- `build/odm.player.host.exe`
- `build/odm.player.media.dll`
- `build/avcodec-61.dll`
- `build/avformat-61.dll`
- `build/avutil-59.dll`
- `build/swscale-8.dll`
- `build/swresample-5.dll`

### `odm-installer` (`odm.setup/Release/odm.setup.msi`)

PDB-stripped MSI. Triggers a major upgrade on any machine with a prior ODM install (`2.x` or any earlier `3.0.x` build).

---

## Why the Installer Is Built Separately

Visual Studio Deployment Projects (`.vdproj`) cannot be built by MSBuild in out-of-process mode. Attempting `msbuild /Project odm.setup` without `DisableOutOfProcBuild` either fails or produces a zero-byte MSI silently.

The workflow works around this by:
1. Building all other projects with parallel MSBuild (`/m`)
2. Calling `DisableOutOfProcBuild.exe` (ships with Visual Studio)
3. Building the installer with `devenv /Build /Project odm.setup` (in-process)

Building the full solution in step 1 (rather than just `odm.ui.app`) ensures native C++/CLI projects (`live555.vcxproj`, `odm.player.lib.vcxproj`, `odm.player.net.vcxproj`) are always compiled — this was the root cause of issue #1 where selective project builds could silently skip them.

---

## .NET Framework Reference Assemblies

`windows-2022` GitHub Actions runners include .NET 4.6+ reference assemblies but not 4.0 or 4.5. The workflow installs them via NuGet:

```
Microsoft.NETFramework.ReferenceAssemblies.net40
Microsoft.NETFramework.ReferenceAssemblies.net45
```

Copies are installed to the standard reference assembly path (`C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0` etc.) so MSBuild resolves them transparently.

---

## Closed Issues

| Issue | What was fixed |
|-------|---------------|
| #1 | Full solution build ensures native player projects are never skipped |
| #12 | Unified CI versioning — all AssemblyInfo + vdproj patched in one step |
| #13 | Per-CI-build ProductCode GUID rotation enables clean major upgrades |
| #15 | `build/` as single staging area for both zip and MSI inputs |
