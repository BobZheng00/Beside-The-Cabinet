# Runs the app in a throwaway, muted profile (Steam ignored), plays dev actions and screenshots the window.
# Never touches the real profile, nor Godot's own log (the run's is <Out>\profile-<Tag>\godot.log). A hidden run is a
# test copy (--offscreen) and runs beside the player's own copies; without -Hidden, quit any running copy first (the
# player's app keeps to one copy at a time, and a second one quits at once).
#
#   tools\dev\shoot.ps1 -Script "hard@3,moment:greet@8" -Shots 5,10 -Tag solve
#   tools\dev\shoot.ps1 -Exe builds\BesideTheCabinet\BesideTheCabinet.exe -Shots 6            # the standalone build
#   tools\dev\shoot.ps1 -Model C:\some\character.glb -Settings ', "Companion": "character"'
#
# -Script   dev actions "action@seconds,..." (see README.md here); times count from app start, about 2 s after launch
# -Shots    seconds after launch at which to capture, as on a quiet PC: when the script's clock starts later than 2 s in
#           (a busy PC), every shot moves on with it, staying as far after the actions as it was meant to. A shot whose window isn't there yet, or not drawn yet (one flat
#           colour), waits for it, up to -Wait seconds, and says how long it waited (not when the app has quit); a shot
#           taken late, after an earlier one waited, says when it was taken. The app is stopped after the last one, so
#           an action timed later never runs (the run warns of it)
# -Wait     the longest a shot waits for its window, and the -Window one, to show and be drawn (default 10)
# -Bg       colour of the backdrop put behind the transparent window, or "none" for the bare desktop
# -Settings extra JSON members for settings.json, starting with a comma (', "SetUp": false' for a first run)
# -Model    file(s) copied into the profile's characters folder (wildcards allowed)
# -KeyFrom  a profile folder whose AI key the run uses (companion\key.bin, copied still encrypted, removed after): a test
#           on the player's own provider, only with their leave, as it is billed to them. Nothing else of theirs is
#           copied (not reminders.bin), so texts still go only where the script sends them
# -Window   also capture the app's own window with this title (a settings window, a dialog) at each shot, from the
#           window itself (PrintWindow), so nothing else on screen is ever captured
# -Hidden   run the app on a Windows desktop of its own (tools\dev\hidden.cs), off every monitor there too: nothing of it
#           ever reaches the player's screen, taskbar, tray or focus (no console, no window, not for a moment), so it is
#           safe while they are busy (in a game, say). The app pictures its own windows there, as Windows draws nothing
#           on a desktop no one looks at: transparent parts come out black, and a window's title bar isn't in its
#           picture. Anything a run showed the player after all is said in a "seen by the player:" line
# -Desk     also put every one of the app's windows (the cabinet, memo sheets…) together where they are relative to each
#           other, on a light backdrop, as <Tag>-<n>-desk.png: how they sit on the desktop, without showing anything
# -Godot    the console build of Godot to run (default: the _console.exe beside GODOT, or the one private\local.cmd names)
# Output: <Out>\<Tag>-<n>.png (and <Tag>-<n>-window.png), plus the app's log and errors.
param([string]$Script = "", [double[]]$Shots = @(6), [string]$Tag = "shot", [string]$Bg = "#F2EFF7", [string]$Size = "M",
      [int]$X = 1200, [int]$Y = 150, [string]$Settings = "", [string]$Model = "", [string]$KeyFrom = "", [string]$Exe = "", [string]$Window = "", [switch]$Hidden, [switch]$Desk, [double]$Wait = 10,
      [string]$Out = (Join-Path $env:TEMP "insert-coin-shots"),
      [string]$Godot = "")

$repo = Split-Path (Split-Path $PSScriptRoot)
$local = Join-Path $repo "private\local.cmd"
if ($Godot -eq "" -and -not $Exe) {
  $editor = if ($env:GODOT) { $env:GODOT } elseif (Test-Path $local) { (Select-String -Path $local -Pattern 'GODOT=([^"\r\n]+)').Matches[0].Groups[1].Value } else { "" }
  if ($editor -eq "") { throw "Set GODOT to your Godot 4.7.2 .NET editor's exe, or put set `"GODOT=...`" in private\local.cmd" }
  $Godot = $editor -replace '\.exe$', '_console.exe'
}
New-Item -ItemType Directory $Out -Force | Out-Null
# The app is stopped once the last shot is taken, so an action timed after it never runs (#82: it looked like a stall)
$lastShot = ($Shots | Measure-Object -Maximum).Maximum
$times = @($Script -split ',' | Where-Object { $_ -match '@([\d.]+)$' } | ForEach-Object { [double]$Matches[1] })
$lastAction = ($times + 0 | Measure-Object -Maximum).Maximum
# Through powershell -File, -Shots 6,9,11 arrives as one number, 6911: a run would hold the test lock for nothing. Refuse a
# lone shot more than a minute after the last action (the smoke scenarios leave at most 12 s); several shots can't be that
if ($Shots.Count -eq 1 -and $lastShot -gt $lastAction + 2 + 60) {
  throw "A shot at ${lastShot}s is over a minute after the script's last action: through powershell -File, -Shots 6,9,11 arrives as one number (6911). Call shoot.ps1 directly in PowerShell, or for a long wait add a later action that only logs, such as trayitems@$([int]$lastShot - 3)"
}
$late = @($Script -split ',' | Where-Object { $_ -match '@([\d.]+)$' -and [double]$Matches[1] + 2 -ge $lastShot })
if ($late) { "warning: $($late -join ', ') would run after the last shot at ${lastShot}s (script times count from about 2 s after launch), when the app is stopped: add a later shot" }
$profile = Join-Path $Out "profile-$Tag"
if (Test-Path $profile) { Get-ChildItem $profile -File | Remove-Item -Force } else { New-Item -ItemType Directory $profile | Out-Null }
if (Test-Path (Join-Path $profile "companion")) { Remove-Item (Join-Path $profile "companion") -Recurse -Force }   # her memory starts fresh too
# so do the skills, the vault and the notes folder dev actions seed (skillseed, skillmake, notesseed): a run never finds an earlier run's
foreach ($seeded in @((Join-Path $profile "skills"), (Join-Path $profile "vault"), "$profile-files")) {
  if (Test-Path $seeded) { Remove-Item $seeded -Recurse -Force }
}
if ($KeyFrom -ne "") {
  New-Item -ItemType Directory (Join-Path $profile "companion") -Force | Out-Null
  Copy-Item (Join-Path $KeyFrom "companion\key.bin") (Join-Path $profile "companion") -Force
}
if ($Model -ne "") {
  New-Item -ItemType Directory (Join-Path $profile "characters") -Force | Out-Null
  Copy-Item $Model (Join-Path $profile "characters") -Force
}
if ($Hidden) { $X = -4000; $Y = 100; $Bg = "none" }
$onTop = if ($Hidden) { "false" } else { "true" }
$seed = "{ `"SetUp`": true, `"Initials`": `"BOB`", `"Muted`": true, `"AlwaysOnTop`": $onTop, `"Size`": `"$Size`", `"WindowX`": $X, `"WindowY`": $Y $Settings }"
# Settings the app can't read would start it on its defaults, not on what the run asked for: stop instead
. (Join-Path $PSScriptRoot "seed.ps1")
Assert-Seed $seed $Settings "shoot.ps1"
$seed | Out-File -Encoding ascii (Join-Path $profile "settings.json")

Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "flat.ps1")
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Shoot { [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  public static IntPtr Find(uint pid, string title) { IntPtr found = IntPtr.Zero;
    EnumWindows((h, p) => { uint owner; GetWindowThreadProcessId(h, out owner); var t = new System.Text.StringBuilder(256); GetWindowText(h, t, 256);
      if (owner == pid && IsWindowVisible(h) && t.ToString().Contains(title)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found; }
  public static System.Collections.Generic.List<IntPtr> All(uint pid) { var all = new System.Collections.Generic.List<IntPtr>();
    EnumWindows((h, p) => { uint owner; GetWindowThreadProcessId(h, out owner); if (owner == pid && IsWindowVisible(h)) all.Add(h); return true; }, IntPtr.Zero);
    return all; } }
"@
[Shoot]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null       # true pixels on every monitor
if (-not ("Backstage" -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot "hidden.cs") }

$form = $null
if ($Bg -ne "none") {
  $form = New-Object System.Windows.Forms.Form
  $form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.ShowInTaskbar = $false; $form.TopMost = $true   # over whatever else is on screen
  $form.BackColor = [System.Drawing.ColorTranslator]::FromHtml($Bg)
  $form.Location = New-Object System.Drawing.Point(($X - 40), ($Y - 40)); $form.Size = New-Object System.Drawing.Size(760, 920)
  $form.Show(); [System.Windows.Forms.Application]::DoEvents()
}

$userArgs = "--data=`"$profile`" --no-steam --test-run"                # hidden or not: settings it can't read leave it muted
$shotDir = Join-Path $profile "shots"                                  # hidden, the app pictures its windows here when asked
if ($Hidden) {
  if (Test-Path $shotDir) { Remove-Item $shotDir -Recurse -Force }
  New-Item -ItemType Directory $shotDir | Out-Null
  $userArgs += " --offscreen --shots=`"$shotDir`""
}
if ($Script -ne "") { $userArgs += " --script=$Script" }
$log = Join-Path $Out "run-$Tag.log"; $err = Join-Path $Out "run-$Tag.err"
# One test run at a time on this PC: smoke.ps1, shoot.ps1 and clip.ps1 take the same lock, so parallel agents take turns
# (smoke.ps1 already holds it when it calls this; the lock lets the same thread in again).
$runLock = New-Object System.Threading.Mutex($false, "Local\InsertCoinSmoke")
try { if (-not $runLock.WaitOne([TimeSpan]::FromMinutes(30))) { throw "Another test run held the lock for 30 minutes" } }
catch [System.Threading.AbandonedMutexException] { }                 # a killed run's lock: ours now
$watch = $null
$ownLog = "--log-file `"$(Join-Path $profile 'godot.log')`""          # Godot's log in the profile: the player's own is left alone
if ($Hidden) {
  $command = if ($Exe -ne "") { "`"$(Resolve-Path $Exe)`" $ownLog -- $userArgs" } else { "`"$Godot`" --path `"$repo\src\InsertCoin.Game`" $ownLog -- $userArgs" }
  $p = [Backstage]::Start($command, $log, $err)
  $watch = New-Object Unseen $p.Id
} elseif ($Exe -ne "") {
  $p = Start-Process (Resolve-Path $Exe) -ArgumentList "$ownLog -- $userArgs" -PassThru
} else {
  $p = Start-Process $Godot -ArgumentList "--path `"$repo\src\InsertCoin.Game`" $ownLog -- $userArgs" -PassThru -WindowStyle Minimized `
    -RedirectStandardOutput $log -RedirectStandardError $err
}

# The cabinet's own window, by its title (the project's name; " (DEBUG)" added in a run from the editor): Windows' idea of
# the main window can be another of the app's (a ticket, once one shows).
$appName = (Select-String -Path "$repo\src\InsertCoin.Game\project.godot" -Pattern '^config/name="([^"]+)"').Matches[0].Groups[1].Value
function Cabinet-Window($app) {
  foreach ($h in [Shoot]::All([uint32]$app.Id)) {
    $t = New-Object System.Text.StringBuilder 256; [Shoot]::GetWindowText($h, $t, 256) | Out-Null
    if ($t.ToString() -eq $appName -or $t.ToString() -eq "$appName (DEBUG)") { return $h }
  }
  return $app.MainWindowHandle
}

# A window's picture from the window itself (PrintWindow), so nothing else on screen is ever captured, and where it is.
function Print-Window($h) {
  $r = New-Object Shoot+RECT; [Shoot]::GetWindowRect($h, [ref]$r) | Out-Null
  if ($r.R -le $r.L) { return $null }
  $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [Shoot]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  [pscustomobject]@{ L = $r.L; T = $r.T; Picture = $bmp }
}

# Hidden, the app's windows as it pictures them itself (TestShots), the one on top first, each put on black as PrintWindow
# leaves a window's transparent parts. None when the app doesn't answer.
function App-Windows([int]$n) {
  $want = Join-Path $shotDir "$n.want"
  New-Item -ItemType File $want -Force | Out-Null
  $until = (Get-Date).AddSeconds(15)
  while (Test-Path $want) {
    if ((Get-Date) -gt $until) { Remove-Item $want -ErrorAction SilentlyContinue; return @() }
    Start-Sleep -Milliseconds 50
  }
  $order = [Backstage]::Windows()
  $wins = foreach ($line in Get-Content -Encoding UTF8 (Join-Path $shotDir "$n.txt")) {
    $f = $line -split "`t"
    $own = [System.Drawing.Image]::FromFile($f[6]); $bmp = New-Object System.Drawing.Bitmap $own.Width, $own.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::Black); $g.DrawImage($own, 0, 0, $own.Width, $own.Height)
    $g.Dispose(); $own.Dispose()
    [pscustomobject]@{ Main = $f[0] -eq "0"; L = [int]$f[2]; T = [int]$f[3]; Picture = $bmp; Title = $f[7]; Z = [array]::IndexOf($order, [IntPtr][long]$f[1]) }
  }
  @($wins | Sort-Object Z)
}

# One shot's pictures: the app's process, its cabinet, the window -Window names and, hidden, every window of the app.
function Take([int]$n) {
  # run from source, the console wrapper starts the real process as its child
  $app = if ($Exe -ne "") { $p.Refresh(); if ($p.HasExited) { $null } else { $p } } else {
    $child = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | Where-Object { $_.Name -like "Godot*" } | Select-Object -First 1
    if ($child) { Get-Process -Id $child.ProcessId } else { $null }
  }
  $shot = [pscustomobject]@{ App = $app; Cabinet = $null; One = $null; Wins = @() }
  if (-not $app) { return $shot }
  if ($Hidden) {
    $shot.Wins = @(App-Windows $n)
    $shot.Cabinet = $shot.Wins | Where-Object { $_.Main } | Select-Object -First 1 | ForEach-Object { $_.Picture }
    if ($Window -ne "") { $shot.One = $shot.Wins | Where-Object { $_.Title.Contains($Window) } | Select-Object -First 1 | ForEach-Object { $_.Picture } }
  } else {
    $main = Cabinet-Window $app
    $r = New-Object Shoot+RECT; [Shoot]::GetWindowRect($main, [ref]$r) | Out-Null
    if ($r.R -gt $r.L) {
      $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [System.Drawing.Graphics]::FromImage($bmp)
      $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose(); $shot.Cabinet = $bmp
    }
    if ($Window -ne "") { $h2 = [Shoot]::Find([uint32]$app.Id, $Window); if ($h2 -ne [IntPtr]::Zero) { $shot.One = (Print-Window $h2).Picture } }
  }
  $shot
}

# What a shot still lacks: the app, its window, the named window, or either drawn (one flat colour is nothing drawn yet).
function Lacking($shot) {
  if (-not $shot.App) { return "the app" }
  if (-not $shot.Cabinet) { return "its window" }
  if (Test-Flat $shot.Cabinet) { return "its window to be drawn" }
  if ($Window -ne "" -and -not $shot.One) { return "the window titled '$Window'" }
  if ($Window -ne "" -and (Test-Flat $shot.One)) { return "the window titled '$Window' to be drawn" }
  return $null
}

function Drop($shot) {
  if ($Hidden) { foreach ($one in $shot.Wins) { $one.Picture.Dispose() } }
  else { foreach ($one in @($shot.Cabinet, $shot.One)) { if ($one) { $one.Dispose() } } }
}

$t0 = Get-Date; $i = 0
# Shots are timed from launch as on a quiet PC, where the script's clock starts about $lead s in. A busy PC that takes
# longer to get there moves them on with it (the app marks when its clock starts), so each lands as far after the
# actions as it was meant to and the app isn't stopped before the last of them (#108, #116).
$lead = 2
$started = Join-Path $profile "script-started"
if ($Script -ne "") {
  while (-not (Test-Path $started) -and -not $p.HasExited -and ((Get-Date) - $t0).TotalSeconds -lt 60) { Start-Sleep -Milliseconds 50; [System.Windows.Forms.Application]::DoEvents() }
  if (Test-Path $started) {
    $behind = ((Get-Item $started).LastWriteTime.AddSeconds(-$lead) - $t0).TotalSeconds
    if ($behind -gt 0) { $t0 = $t0.AddSeconds($behind) }
    if ($behind -ge 0.5) { "the script's clock started $("{0:0.0}" -f ($behind + $lead))s after launch: shots moved $("{0:0.0}" -f $behind)s on with it" }
  }
}
foreach ($s in $Shots) {
  while (((Get-Date) - $t0).TotalSeconds -lt $s) { Start-Sleep -Milliseconds 50; [System.Windows.Forms.Application]::DoEvents() }
  # A busy PC runs a few seconds behind: what isn't there or drawn yet is waited for, up to -Wait seconds, then taken as
  # it is. A shot with nothing lacking is taken at its second, as ever; an app that has quit isn't waited for.
  $from = Get-Date; $waitedFor = $null
  while ($true) {
    $shot = Take $i; $lacking = Lacking $shot
    if (-not $lacking -or $p.HasExited -or ((Get-Date) - $from).TotalSeconds -ge $Wait) { break }
    if (-not $waitedFor) { $waitedFor = $lacking }
    Drop $shot; Start-Sleep -Milliseconds 250; [System.Windows.Forms.Application]::DoEvents()
  }
  $waited = "{0:0.0}" -f ((Get-Date) - $from).TotalSeconds
  $waitNote = if (-not $waitedFor) { "" } elseif ($lacking) { "; waited ${waited}s for $waitedFor, still lacking $lacking" } else { "; waited ${waited}s for $waitedFor" }
  $taken = ((Get-Date) - $t0).TotalSeconds
  $at = if ($taken - $s -ge 0.5) { "${s}s, taken at $("{0:0.0}" -f $taken)s" } else { "${s}s" }   # late after an earlier shot waited
  $app = $shot.App; $wins = $shot.Wins
  if (-not $app) { "no app process at $at (waited ${waited}s)$(if (-not $Hidden) { ' (is another copy running?)' })"; continue }
  if (-not $shot.Cabinet) { "no window yet at $at (waited ${waited}s)"; Drop $shot; continue }
  $file = Join-Path $Out ("{0}-{1}.png" -f $Tag, $i)
  $shot.Cabinet.Save($file)
  "$file  at ${at}: $($shot.Cabinet.Width)x$($shot.Cabinet.Height), working set $([int]($app.WorkingSet64 / 1MB)) MB, cpu $([math]::Round($app.TotalProcessorTime.TotalSeconds, 2))s$waitNote"
  if ($Window -ne "") {
    if (-not $shot.One) { "  no window titled '$Window' at $at (waited ${waited}s)" } else {
      $file2 = Join-Path $Out ("{0}-{1}-window.png" -f $Tag, $i); $shot.One.Save($file2); "  $file2"
    }
  }
  if ($Desk) {
    if (-not $Hidden) { $wins = @([Shoot]::All([uint32]$app.Id) | ForEach-Object { Print-Window $_ } | Where-Object { $_ }) }
    $minX = ($wins | ForEach-Object { $_.L } | Measure-Object -Minimum).Minimum; $minY = ($wins | ForEach-Object { $_.T } | Measure-Object -Minimum).Minimum
    $maxX = ($wins | ForEach-Object { $_.L + $_.Picture.Width } | Measure-Object -Maximum).Maximum; $maxY = ($wins | ForEach-Object { $_.T + $_.Picture.Height } | Measure-Object -Maximum).Maximum
    $deskBmp = New-Object System.Drawing.Bitmap ([int]($maxX - $minX + 40)), ([int]($maxY - $minY + 40)); $dg = [System.Drawing.Graphics]::FromImage($deskBmp)
    $dg.Clear([System.Drawing.ColorTranslator]::FromHtml("#F2EFF7"))
    for ($k = $wins.Count - 1; $k -ge 0; $k--) {                       # bottom first, so those on top are drawn over
      $one = $wins[$k].Picture
      $one.MakeTransparent([System.Drawing.Color]::Black)              # what PrintWindow leaves black is the window's transparent part
      $dg.DrawImage($one, [int]($wins[$k].L - $minX + 20), [int]($wins[$k].T - $minY + 20), $one.Width, $one.Height)
    }
    $file3 = Join-Path $Out ("{0}-{1}-desk.png" -f $Tag, $i); $deskBmp.Save($file3); $dg.Dispose(); $deskBmp.Dispose(); "  $file3 ($($wins.Count) windows)"
  }
  Drop $shot; if (-not $Hidden) { foreach ($one in $wins) { $one.Picture.Dispose() } }   # -Desk's own pictures
  $i++
}

Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
if ($watch) { [Backstage]::EndAll(); $watch.Stop() }
if ($form) { $form.Close() }
$runLock.ReleaseMutex(); $runLock.Dispose()
Start-Sleep -Milliseconds 500
if ($KeyFrom -ne "") { Remove-Item (Join-Path $profile "companion\key.bin") -Force -ErrorAction SilentlyContinue }
if ($Exe -eq "") {
  Get-Content $err -ErrorAction SilentlyContinue | Select-Object -First 25
  Get-Content $log -ErrorAction SilentlyContinue | Select-String -Pattern "ERROR|Exception|Companion '" | Select-Object -First 25
}
