# Records a short clip of the app in a throwaway, muted profile (Steam ignored), for judging motion: Godot's movie
# writer renders every frame at a fixed rate (game time, so dev actions land on the frame they name), off screen.
# Never touches the real profile. Only one copy of the app runs at a time, so quit any running one first.
#
#   tools\dev\clip.ps1 -Script "moment:cheer@3" -Seconds 6 -Tag cheer
#   tools\dev\clip.ps1 -Script "moment:cheer@3" -Seconds 6 -Tag cheer -Crop "150:220:80:135" -Scale 3   # her upper body, three times the size
#
# -Script   dev actions "action@seconds,..." (see README.md here); times count from app start
# -Seconds  how long to record
# -From     seconds to leave out at the start (the app settling) in the GIF
# -Crop     "w:h:x:y" of the window to keep (default: all of her; "150:220:80:135" is her upper body; "" the whole window)
# -Bg       the desktop colour behind her
# -Settings extra JSON members for settings.json, starting with a comma
# -Characters a folder copied in as the profile's characters folder (a model and its voice, say)
# Output: <Out>\<Tag>.gif and <Tag>.mp4 (the frames are deleted), plus the app's log.
param([string]$Script = "", [double]$Seconds = 6, [double]$From = 2, [string]$Tag = "clip", [string]$Settings = "", [string]$Characters = "",
      [string]$Crop = "180:460:65:130", [double]$Scale = 1, [int]$Fps = 30, [string]$Bg = "#F2EFF7",
      [string]$Out = (Join-Path $env:TEMP "insert-coin-clips"),
      [string]$Godot = "",
      [string]$Ffmpeg = "ffmpeg")

$repo = Split-Path (Split-Path $PSScriptRoot)
$local = Join-Path $repo "private\local.cmd"
if ($Godot -eq "" -and -not $Exe) {
  $editor = if ($env:GODOT) { $env:GODOT } elseif (Test-Path $local) { (Select-String -Path $local -Pattern 'GODOT=([^"\r\n]+)').Matches[0].Groups[1].Value } else { "" }
  if ($editor -eq "") { throw "Set GODOT to your Godot 4.7.2 .NET editor's exe, or put set `"GODOT=...`" in private\local.cmd" }
  $Godot = $editor -replace '\.exe$', '_console.exe'
}
New-Item -ItemType Directory $Out -Force | Out-Null
$profile = Join-Path $Out "profile-$Tag"
if (Test-Path $profile) { Remove-Item $profile -Recurse -Force }
New-Item -ItemType Directory $profile | Out-Null
if ($Characters -ne "") { Copy-Item $Characters (Join-Path $profile "characters") -Recurse }
"{ `"SetUp`": true, `"Initials`": `"BOB`", `"Muted`": true, `"AlwaysOnTop`": false, `"WindowX`": -4000, `"WindowY`": 100 $Settings }" |
  Out-File -Encoding ascii (Join-Path $profile "settings.json")

$frames = Join-Path $Out "frames-$Tag"
if (Test-Path $frames) { Remove-Item $frames -Recurse -Force }
New-Item -ItemType Directory $frames | Out-Null
$userArgs = "--data=`"$profile`" --no-steam --offscreen"
if ($Script -ne "") { $userArgs += " --script=$Script" }
$log = Join-Path $Out "run-$Tag.log"; $err = Join-Path $Out "run-$Tag.err"
$count = [int]($Seconds * $Fps)
# One test run at a time on this PC: smoke.ps1, shoot.ps1 and clip.ps1 take the same lock, so parallel agents take turns
# (smoke.ps1 already holds it when it calls this; the lock lets the same thread in again).
$runLock = New-Object System.Threading.Mutex($false, "Local\InsertCoinSmoke")
try { if (-not $runLock.WaitOne([TimeSpan]::FromMinutes(30))) { throw "Another test run held the lock for 30 minutes" } }
catch [System.Threading.AbandonedMutexException] { }                 # a killed run's lock: ours now
$p = Start-Process $Godot -ArgumentList "--path `"$repo\src\InsertCoin.Game`" --write-movie `"$frames\f.png`" --fixed-fps $Fps --quit-after $count -- $userArgs" `
  -PassThru -WindowStyle Minimized -RedirectStandardOutput $log -RedirectStandardError $err
$p.WaitForExit()
$runLock.ReleaseMutex(); $runLock.Dispose()

$first = [int]($From * $Fps)
# the window is transparent: she is laid over a desktop colour, as she is usually seen
$filter = "split[a][b];[a]format=rgb24,drawbox=x=0:y=0:w=iw:h=ih:color=$($Bg.Replace('#', '0x')):t=fill[bg];[bg][b]overlay"
if ($Crop -ne "") { $w, $h, $x, $y = $Crop.Split(':'); $filter += ",crop=${w}:${h}:${x}:${y}" }
$filter += ",scale=iw*${Scale}:-1:flags=lanczos"
& $Ffmpeg -y -loglevel error -framerate $Fps -start_number $first -i "$frames\f%08d.png" -filter_complex "$filter,format=yuv420p" -c:v libx264 -crf 18 (Join-Path $Out "$Tag.mp4")
& $Ffmpeg -y -loglevel error -framerate $Fps -start_number $first -i "$frames\f%08d.png" `
  -filter_complex "$filter,split[c][d];[c]palettegen=stats_mode=diff[p];[d][p]paletteuse=dither=sierra2_4a" (Join-Path $Out "$Tag.gif")
Remove-Item $frames -Recurse -Force
Get-Content $err -ErrorAction SilentlyContinue | Select-String -Pattern "ERROR|Exception" | Select-Object -First 15
"$(Join-Path $Out "$Tag.gif")  ($([int](($Seconds - $From) * $Fps)) frames)"
