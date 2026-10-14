# Runs the smoke suite: every scenario in tools\dev\smoke\scenarios.json, one after another, through shoot.ps1 -Hidden
# (a throwaway muted profile, Steam ignored, nothing on screen, the real profile never touched), and says PASS, FAIL or
# BLOCKED for each, with the screenshots and log lines that show why.
# One smoke run at a time on this PC: a second run waits for the first. A copy of the app already running (the demo,
# say) blocks the run instead of being stopped: this script never stops anything of the user's.
# Works the same in a worktree: it borrows the main checkout's GODOT setting and models when the worktree has none.
#
#   tools\dev\smoke.ps1                      # every scenario
#   tools\dev\smoke.ps1 -Only boot,chat      # some of them (from bash: -Only "boot,chat")
#   tools\dev\smoke.ps1 -List                # the scenarios and what each one checks
#   tools\dev\smoke.ps1 -Prepare             # only make this checkout ready for test runs (GODOT, models, import, build)
#
# -Only      scenario names, comma-separated
# -NoBuild   skip building the game first (check.ps1 has just built it)
# -Out       where shots, logs and the report go (default: <main checkout>\.qa\smoke\<branch>)
# -Wait      minutes to wait for another smoke run before giving up as BLOCKED (default 20)
# -Port      the mock mind's port (default 8797; the PC's other mock servers use 8765 to 8893)
# -Prepare   in a worktree, before shoot.ps1 or clip.ps1 runs of your own: copies the main checkout's private\local.cmd
#            (where Godot is), links its models, imports the project and builds the game; then stops
# Test runs take turns: shoot.ps1 and clip.ps1 take the same lock (Local\InsertCoinSmoke), so an agent's own run waits
# for a smoke run and the other way round. Copies of the app left over from a killed test run (started --offscreen)
# are stopped; nothing else is.
# A worktree's models folder is a link to the main checkout's: delete a worktree with git worktree remove (or
# `cmd /c rmdir` the link first), never with Remove-Item -Recurse, which can follow the link.
# Output: <Out>\report.md and report.json, <scenario>-<n>.png per shot (-window, -desk as shoot.ps1 makes them),
#         run-<scenario>.log and .err, mind-<scenario>.jsonl for scenarios with the mock mind.
# Exit code: 0 every scenario passed, 1 something failed, 3 blocked (another run, or a copy of the app running).
param([string[]]$Only = @(), [switch]$NoBuild, [string]$Out = "", [double]$Wait = 20, [int]$Port = 8797, [switch]$List,
      [switch]$Prepare)

$repo = Split-Path (Split-Path $PSScriptRoot)
$game = Join-Path $repo "src\InsertCoin.Game"
$suite = Get-Content -Raw -Encoding UTF8 (Join-Path (Join-Path $PSScriptRoot "smoke") "scenarios.json") | ConvertFrom-Json

if ($List) {
  foreach ($s in $suite.scenarios) { "{0,-10} {1}" -f $s.name, $s.about }
  exit 0
}

# --- where things are -------------------------------------------------------------------------------------------
function GitOut([string[]]$GitArgs) {
  $r = & git -C $repo @GitArgs 2>$null
  if ($LASTEXITCODE -ne 0) { return "" }
  return ("" + $r).Trim()
}
$common = GitOut @("rev-parse", "--path-format=absolute", "--git-common-dir")
$main = if ($common -ne "") { [System.IO.Path]::GetDirectoryName($common) } else { $repo }
$sha = GitOut @("rev-parse", "HEAD")
$branch = GitOut @("rev-parse", "--abbrev-ref", "HEAD")
if ($branch -eq "" -or $branch -eq "HEAD") { $branch = "detached-" + $sha.Substring(0, [Math]::Min(7, $sha.Length)) }
if ($Out -eq "") { $Out = Join-Path $main (".qa\smoke\" + ($branch -replace "[^A-Za-z0-9._-]", "-")) }
New-Item -ItemType Directory $Out -Force | Out-Null

$names = @($Only | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
$scenarios = @($suite.scenarios | Where-Object { $names.Count -eq 0 -or $names -contains $_.name })
foreach ($n in $names) { if (-not ($suite.scenarios | Where-Object { $_.name -eq $n })) { throw "No scenario named '$n' (tools\dev\smoke.ps1 -List)" } }

$results = New-Object System.Collections.ArrayList
$started = Get-Date

function Finish([int]$code) {
  $passed = @($results | Where-Object { $_.result -eq "PASS" }).Count
  $failed = @($results | Where-Object { $_.result -eq "FAIL" }).Count
  $blocked = @($results | Where-Object { $_.result -eq "BLOCKED" }).Count
  $report = [ordered]@{
    sha = $sha; branch = $branch; checkout = $repo; when = (Get-Date).ToString("o")
    seconds = [int]((Get-Date) - $started).TotalSeconds; passed = $passed; failed = $failed; blocked = $blocked
    out = $Out; scenarios = @($results)
  }
  $utf8 = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText((Join-Path $Out "report.json"), ($report | ConvertTo-Json -Depth 6), $utf8)

  $md = New-Object System.Collections.ArrayList
  $short = if ($sha.Length -ge 7) { $sha.Substring(0, 7) } else { $sha }
  [void]$md.Add("# Smoke run: $branch at $short")
  [void]$md.Add("")
  [void]$md.Add("$passed passed, $failed failed, $blocked blocked, in $($report.seconds) s ($((Get-Date).ToString('yyyy-MM-dd HH:mm'))). Shots and logs: ``$Out``")
  [void]$md.Add("")
  [void]$md.Add("| Scenario | Result | Seconds | Why |")
  [void]$md.Add("|---|---|---|---|")
  foreach ($r in $results) { [void]$md.Add("| $($r.name) | $($r.result) | $($r.seconds) | $((@($r.problems) | Select-Object -First 1) -replace '\|', '/') |") }
  foreach ($r in $results) {
    [void]$md.Add("")
    [void]$md.Add("## $($r.name): $($r.result)")
    [void]$md.Add("")
    [void]$md.Add("$($r.about)")
    if ($r.script) { [void]$md.Add(""); [void]$md.Add("Script: ``$($r.script)``") }
    if (@($r.problems).Count -gt 0) { [void]$md.Add(""); foreach ($p in $r.problems) { [void]$md.Add("- $p") } }
    if (@($r.shots).Count -gt 0) { [void]$md.Add(""); [void]$md.Add("Screenshots:"); foreach ($s in $r.shots) { [void]$md.Add("- ``$s``") } }
    if (@($r.logLines).Count -gt 0) {
      [void]$md.Add(""); [void]$md.Add("Log lines:"); [void]$md.Add("``````")
      foreach ($l in $r.logLines) { [void]$md.Add($l) }
      [void]$md.Add("``````")
    }
  }
  [System.IO.File]::WriteAllText((Join-Path $Out "report.md"), ($md -join "`r`n") + "`r`n", $utf8)

  ""
  "Smoke: $passed passed, $failed failed, $blocked blocked ($branch at $short)"
  foreach ($r in $results) { "  {0,-8} {1,-10} {2}" -f $r.result, $r.name, ((@($r.problems) | Select-Object -First 1)) }
  "Report: $(Join-Path $Out 'report.md')"
  exit $code
}

function Add-Result($s, [string]$result, $problems, $shots, $logLines, [double]$seconds) {
  [void]$results.Add([ordered]@{
    name = $s.name; about = $s.about; script = $s.script; result = $result; seconds = [int]$seconds
    problems = @($problems); shots = @($shots); logLines = @($logLines | Select-Object -First 30)
  })
}

function Block-All([string]$why) {
  foreach ($s in $scenarios) { Add-Result $s "BLOCKED" @($why) @() @() 0 }
  Finish 3
}

# --- GODOT, models and Godot's import, borrowed from the main checkout in a worktree -----------------------------
function Read-Godot([string]$checkout) {
  $local = Join-Path $checkout "private\local.cmd"
  if (-not (Test-Path $local)) { return "" }
  $m = Select-String -Path $local -Pattern 'GODOT=([^"\r\n]+)' | Select-Object -First 1
  if ($m) { return $m.Matches[0].Groups[1].Value.Trim() }
  return ""
}
$localHere = Join-Path $repo "private\local.cmd"
$localMain = Join-Path $main "private\local.cmd"
if (-not (Test-Path $localHere) -and ($repo -ne $main) -and (Test-Path $localMain)) {
  New-Item -ItemType Directory (Split-Path $localHere) -Force | Out-Null
  Copy-Item $localMain $localHere                                              # shoot.ps1 and clip.ps1 read it there
}
if (-not $env:GODOT) {
  $g = Read-Godot $repo
  if ($g -eq "") { $g = Read-Godot $main }
  if ($g -eq "") { throw "Set GODOT to your Godot 4.7.2 .NET editor's exe, or put set `"GODOT=...`" in private\local.cmd" }
  $env:GODOT = $g
}
$godotConsole = $env:GODOT -replace '\.exe$', '_console.exe'

$models = Join-Path $game "models"
$mainModels = Join-Path $main "src\InsertCoin.Game\models"
if (-not (Test-Path $models) -and ($repo -ne $main) -and (Test-Path $mainModels)) {
  New-Item -ItemType Junction -Path $models -Target $mainModels | Out-Null      # a link, not a 380 MB copy
  "Linked the main checkout's models into this worktree."
}

if (-not $NoBuild) {
  "Building the game..."
  $env:DOTNET_CLI_UI_LANGUAGE = "en"
  $build = & dotnet build (Join-Path $game "InsertCoin.Game.csproj") -nologo -v:q 2>&1 | ForEach-Object { "$_" }
  if ($LASTEXITCODE -ne 0) {
    if ($Prepare) { $build | Select-Object -Last 20; "The game did not build."; exit 1 }
    foreach ($s in $scenarios) { Add-Result $s "FAIL" @("the game did not build") @() ($build | Select-Object -Last 20) 0 }
    Finish 1
  }
}

# --- one run at a time, and never alongside a copy of the app -----------------------------------------------------
function Get-Copies {
  @(Get-CimInstance Win32_Process | Where-Object {
      $_.Name -like "BesideTheCabinet*.exe" -or $_.Name -like "InsertCoin*.exe" -or
      ($_.Name -like "Godot*" -and $_.CommandLine -match "InsertCoin\.Game" -and
       $_.CommandLine -notmatch "(^|\s)(-e|--editor)(\s|$)" -and $_.CommandLine -notmatch "project\.godot" -and
       $_.CommandLine -notmatch "\s--import(\s|$)")   # neither the editor nor an import is a copy
    })
}
function Describe-Copies($copies) {
  ($copies | ForEach-Object {
      $line = "" + $_.CommandLine
      if ($line.Length -gt 160) { $line = $line.Substring(0, 160) + "..." }
      "PID $($_.ProcessId) $($_.Name): $line"
    }) -join "; "
}

$lock = New-Object System.Threading.Mutex($false, "Local\InsertCoinSmoke")
$held = $false
try { $held = $lock.WaitOne([TimeSpan]::FromMinutes($Wait)) } catch [System.Threading.AbandonedMutexException] { $held = $true }
if (-not $held) { Block-All "another smoke run held the lock for $Wait minutes" }

try {
  # Holding the lock, no test run of ours is going: a copy started --offscreen is left over from a killed one.
  foreach ($orphan in @(Get-Copies | Where-Object { $_.CommandLine -match "--offscreen" })) {
    Stop-Process -Id $orphan.ProcessId -Force -ErrorAction SilentlyContinue
    "Stopped a copy left over from a killed test run (PID $($orphan.ProcessId))."
  }
  $copies = Get-Copies
  $needImport = -not (Test-Path (Join-Path $game ".godot\imported"))
  if ($copies.Count -gt 0) {
    if (-not $Prepare) { Block-All ("a copy of the app is running (only one runs at a time): " + (Describe-Copies $copies)) }
    if ($needImport) { "A copy of the app is running, so the project wasn't imported yet: $(Describe-Copies $copies)"; exit 3 }
  }
  if ($needImport) {
    "Importing the project's resources (a fresh checkout's first run)..."
    & $godotConsole --headless --path $game --import 2>&1 | Out-Null
  }
  if ($Prepare) { "Ready for test runs: $repo"; exit 0 }

  Add-Type -AssemblyName System.Drawing
  function Test-Flat([string]$path) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    try {
      $first = $null
      $stepX = [Math]::Max(1, [int]($bmp.Width / 24)); $stepY = [Math]::Max(1, [int]($bmp.Height / 24))
      for ($x = 0; $x -lt $bmp.Width; $x += $stepX) {
        for ($y = 0; $y -lt $bmp.Height; $y += $stepY) {
          $c = $bmp.GetPixel($x, $y).ToArgb()
          if ($null -eq $first) { $first = $c } elseif ($c -ne $first) { return $false }
        }
      }
      return $true
    } finally { $bmp.Dispose() }
  }

  $defaultForbid = @('\bERROR\b', 'Exception\b', 'SCRIPT ERROR')
  $suiteIgnore = @($suite.ignore | Where-Object { $_ })
  $py = Get-Command python -ErrorAction SilentlyContinue

  foreach ($s in $scenarios) {
    $t0 = Get-Date
    $copies = Get-Copies
    if ($copies.Count -gt 0) {
      Add-Result $s "BLOCKED" @("a copy of the app started meanwhile: " + (Describe-Copies $copies)) @() @() 0
      continue
    }
    Get-ChildItem $Out -File -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -like "$($s.name)-*.png" -or $_.Name -like "run-$($s.name).*" -or $_.Name -eq "mind-$($s.name).jsonl" } |
      Remove-Item -Force

    $settings = "" + $s.settings
    $mock = $null
    $mindLog = Join-Path $Out "mind-$($s.name).jsonl"
    if ($s.mind) {
      if (-not $py) { Add-Result $s "FAIL" @("python is not on PATH, and this scenario needs the mock mind") @() @() 0; continue }
      $reply = if ($s.mindReply) { "" + $s.mindReply } else { "Hi! It's good to see you. How did today go?" }
      $quit = [int](($s.shots | Measure-Object -Maximum).Maximum) + 60          # gone by itself if this run is killed
      $mockArgs = @("`"$(Join-Path $PSScriptRoot 'mock_mind.py')`"", "$Port", "--log", "`"$mindLog`"", "--reply", "`"$reply`"",
                    "--quit-after", "$quit")
      $mock = Start-Process $py.Source -ArgumentList $mockArgs -PassThru -WindowStyle Hidden
      Start-Sleep -Milliseconds 800
      $settings += ', "ChatProtocol": "OpenAi", "ChatBaseUrl": "http://127.0.0.1:' + $Port + '/v1", "ChatModel": "mock"'
    }

    $shots = @($s.shots | ForEach-Object { [double]$_ })
    $shootArgs = @{ Hidden = $true; Tag = "" + $s.name; Out = $Out; Shots = [double[]]$shots }
    if ($s.script) { $shootArgs.Script = "" + $s.script }
    if ($settings -ne "") { $shootArgs.Settings = $settings }
    if ($s.window) { $shootArgs.Window = "" + $s.window }
    if ($s.desk) { $shootArgs.Desk = $true }

    "Running $($s.name)..."
    $said = @()
    try { $said = @(& (Join-Path $PSScriptRoot "shoot.ps1") @shootArgs *>&1 | ForEach-Object { "$_" }) }
    catch { $said += "shoot.ps1 failed: $($_.Exception.Message)" }
    finally { if ($mock) { Stop-Process -Id $mock.Id -Force -ErrorAction SilentlyContinue } }

    $problems = New-Object System.Collections.ArrayList
    foreach ($line in $said) {
      if ($line -match "no app process|no window yet|shoot.ps1 failed|no window titled") { [void]$problems.Add($line.Trim()) }
    }

    $pngs = @(Get-ChildItem $Out -File | Where-Object { $_.Name -match ("^" + [regex]::Escape($s.name) + "-\d+\.png$") } | Sort-Object Name)
    if ($pngs.Count -lt $shots.Count) { [void]$problems.Add("only $($pngs.Count) of $($shots.Count) screenshots were taken") }
    foreach ($p in $pngs) { if (Test-Flat $p.FullName) { [void]$problems.Add("$($p.Name) is one flat colour: nothing drawn") } }
    $allShots = @(Get-ChildItem $Out -File | Where-Object { $_.Name -like "$($s.name)-*.png" } | Sort-Object Name | ForEach-Object { $_.FullName })

    $text = @()
    foreach ($f in @("run-$($s.name).log", "run-$($s.name).err")) {
      $path = Join-Path $Out $f
      if (Test-Path $path) { $text += @(Get-Content -Encoding UTF8 $path | ForEach-Object { "$_" }) }
    }
    $ignore = @($suiteIgnore) + @($s.ignore | Where-Object { $_ })
    $forbid = @($defaultForbid) + @($s.forbid | Where-Object { $_ })
    $bad = New-Object System.Collections.ArrayList
    foreach ($line in $text) {
      $skip = $false
      foreach ($pat in $ignore) { if ($line -cmatch $pat) { $skip = $true; break } }
      if ($skip) { continue }
      foreach ($pat in $forbid) { if ($line -cmatch $pat) { [void]$bad.Add($line); break } }
    }
    if ($bad.Count -gt 0) { [void]$problems.Add("$($bad.Count) log line(s) that shouldn't be there, first: $($bad[0].Trim())") }
    foreach ($pat in @($s.expect | Where-Object { $_ })) {
      if (-not ($text | Where-Object { $_ -cmatch $pat })) { [void]$problems.Add("no log line matching /$pat/") }
    }
    if ($s.mind -and $s.mindRequests) {
      $asked = if (Test-Path $mindLog) { @(Get-Content -Encoding UTF8 $mindLog).Count } else { 0 }
      if ($asked -lt [int]$s.mindRequests) { [void]$problems.Add("the mock mind was asked $asked time(s), expected at least $($s.mindRequests)") }
    }

    $result = if ($problems.Count -eq 0) { "PASS" } else { "FAIL" }
    Add-Result $s $result $problems $allShots $bad ((Get-Date) - $t0).TotalSeconds
    "  $result"
  }
} finally {
  if ($held) { $lock.ReleaseMutex() }
  $lock.Dispose()
}

$code = 0
if (@($results | Where-Object { $_.result -eq "FAIL" }).Count -gt 0) { $code = 1 }
elseif (@($results | Where-Object { $_.result -eq "BLOCKED" }).Count -gt 0) { $code = 3 }
Finish $code
