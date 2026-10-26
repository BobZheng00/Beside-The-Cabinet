# Imports the game's art, sounds and other resources again when any file of the project changed since the last import
# (a pull, a branch switch, an edited picture), so a run from source shows what's in the files, not Godot's old copy in
# .godot\imported: only the editor imports by itself, and a run with --path uses whatever is there. run.cmd, demo.cmd,
# firstrun.cmd, update.cmd, smoke.ps1, shoot.ps1 and clip.ps1 call it before they start the app.
# It tells by every file's path, size and time (not .godot or models), noted in .godot\import-stamp.txt after each
# import: unchanged, it takes a fraction of a second; changed, the import takes about 4 s (more for new art).
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\import.ps1
#
# -Godot  the console build of Godot (default: the _console.exe beside GODOT, or the one private\local.cmd names)
# Exit code: 0 up to date, 1 the import failed (nothing noted, so the next run tries again).
param([string]$Godot = "")

$repo = Split-Path (Split-Path $PSScriptRoot)
$game = Join-Path $repo "src\InsertCoin.Game"
$stamp = Join-Path $game ".godot\import-stamp.txt"

function Get-Fingerprint {
  $skip = @(".godot", "models", "bin", "obj", ".vs")                     # Godot's own, her models (no art), builds
  $lines = foreach ($dir in Get-ChildItem $game -Force | Where-Object { $skip -notcontains $_.Name }) {
    $files = if ($dir.PSIsContainer) { Get-ChildItem $dir.FullName -Recurse -File -Force } else { $dir }
    foreach ($f in $files) { "$($f.FullName.Substring($game.Length))|$($f.Length)|$($f.LastWriteTimeUtc.Ticks)" }
  }
  $bytes = [Text.Encoding]::UTF8.GetBytes((($lines | Sort-Object) -join "`n"))
  [BitConverter]::ToString([Security.Cryptography.MD5]::Create().ComputeHash($bytes)).Replace("-", "")
}

$now = Get-Fingerprint
$noted = if (Test-Path $stamp) { (Get-Content $stamp -Raw).Trim() } else { "" }
if ($now -eq $noted -and (Test-Path (Join-Path $game ".godot\imported"))) { exit 0 }

if ($Godot -eq "") {
  $local = Join-Path $repo "private\local.cmd"
  $editor = if ($env:GODOT) { $env:GODOT } elseif (Test-Path $local) { (Select-String -Path $local -Pattern 'GODOT=([^"\r\n]+)').Matches[0].Groups[1].Value } else { "" }
  if ($editor -eq "") { Write-Error "Set GODOT to your Godot 4.7.2 .NET editor's exe, or put set `"GODOT=...`" in private\local.cmd"; exit 1 }
  $Godot = if ($editor -match '_console\.exe$') { $editor } else { $editor -replace '\.exe$', '_console.exe' }
}
if (-not (Test-Path $Godot)) { Write-Error "No Godot at $Godot to import with: the run may show old art"; exit 1 }
"Importing the art and sounds that changed..."
$t0 = Get-Date
& $Godot --headless --path $game --import 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "Godot's import failed (exit $LASTEXITCODE): the run may show old art"; exit 1 }
Get-Fingerprint | Out-File -Encoding ascii $stamp                          # after the import: it may add .import or .uid files
"Imported in $([math]::Round(((Get-Date) - $t0).TotalSeconds, 1)) s."
