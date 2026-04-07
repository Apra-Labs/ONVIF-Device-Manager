$src = 'C:\akhil\git\ONVIF-Device-Manager'
$dst = "$src\build"
$binSrc = "$src\odm\odm.ui.app\bin\Release\x64"

# Copy odm.exe first — this is the critical file; fail loudly if it's missing
Copy-Item "$binSrc\odm.exe" "$dst\odm.exe" -Force -ErrorAction Stop

# Copy remaining files from the build output; skip locked files silently
Get-ChildItem "$binSrc\*" -File | Where-Object { $_.Name -ne 'odm.exe' } | ForEach-Object {
    Copy-Item $_.FullName "$dst\" -Force -ErrorAction SilentlyContinue
}

Copy-Item "$src\libs\ffmpeg-git-a5c1a0c\x64\bin\*.dll" "$dst\" -Force -ErrorAction SilentlyContinue
Copy-Item "$src\odm\odm.ui.views\images\wheel_zoom.cur" "$dst\images\" -Force -ErrorAction SilentlyContinue
Copy-Item "$src\odm\odm.localization\locales\*" "$dst\locales\" -Force -ErrorAction SilentlyContinue
Copy-Item "$src\odm\odm.ui.app\meta\*" "$dst\meta\" -Force -ErrorAction SilentlyContinue

Write-Host "Packaged: $((Get-Item "$dst\odm.exe").LastWriteTime)"
