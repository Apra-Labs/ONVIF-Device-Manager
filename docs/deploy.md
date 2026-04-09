# ODM — Local Test Deploy

Work folder: `C:\akhil\git\ONVIF-Device-Manager`

## One-time setup (run once from an elevated shell)

Register two scheduled tasks — one to kill ODM, one to launch it elevated. Both require the creating shell to be elevated (run as Administrator):

```
schtasks /Create /TN "ODM-kill" /TR "taskkill /F /IM odm.exe /T" /SC ONSTART /RL HIGHEST /F
schtasks /Create /TN "ODM-dev" /TR "C:\akhil\git\ONVIF-Device-Manager\build\ODM.exe" /SC ONSTART /RL HIGHEST /F
```

## Deploy steps — run ALL steps every time, no exceptions

### Step 1 — Increment version (MANDATORY — do this before every deploy)

Edit `odm\~cfg\AssemblyInfo.global.cs` — bump the 4th part on all three lines:

```
[assembly: AssemblyVersion("2.2.252.X")]
[assembly: AssemblyFileVersion("2.2.252.X")]
[assembly: System.Reflection.AssemblyInformationalVersion("2.2.252.X-dev")]
```

Commit the version bump before building.

### Step 2 — Kill the running process

ODM.exe holds locks on its DLLs while running — kill it before building or the updated files will not copy.

```
powershell -Command "schtasks /Run /TN 'ODM-kill'; Start-Sleep 2"
```

### Step 3 — Build

```
powershell -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\akhil\git\ONVIF-Device-Manager\odm.sln' /p:Configuration=Release /p:Platform=x64 /v:minimal"
```

Build must complete with 0 errors before continuing.

### Step 4 — Package (MANDATORY — copies exe + DLLs + assets to build\)

`package.bat` copies the output from `bin\Release\x64\` to `build\`. Without this step the running binary is NOT updated.

```
powershell -Command "& 'C:\akhil\git\ONVIF-Device-Manager\deploy.ps1'"
```

(`deploy.ps1` wraps the package.bat logic using `Copy-Item /Y` — it prints the timestamp of `build\odm.exe` on success.)

### Step 5 — Launch

```
powershell -Command "schtasks /Run /TN 'ODM-dev'"
```

### Step 6 — Verify

Check the window title shows the new version (e.g. `v2.2.252.4`).

---

## One-liner (steps 2–5 combined)

```powershell
powershell -Command "schtasks /Run /TN 'ODM-kill'; Start-Sleep 2; & 'C:\akhil\git\ONVIF-Device-Manager\deploy.ps1'; schtasks /Run /TN 'ODM-dev'"
```

Run this **after** bumping the version (Step 1) and **after** building (Step 3).

---

## Common mistakes to avoid

- **Skipping package step** — build output goes to `bin\Release\x64\`, NOT `build\`. The scheduled task runs from `build\`. Always run deploy.ps1 after building.
- **Forgetting the version bump** — the window title shows the version; without bumping it is impossible to tell which build is running.
- **Killing after building** — kill BEFORE building, not after, or the copy will fail on locked DLLs.
