# Runs the smoke suite: every scenario in tools\dev\smoke\scenarios.json, one after another, through shoot.ps1 -Hidden
# (a throwaway muted profile, Steam ignored, the real profile never touched; on a Windows desktop of its own, so nothing
# reaches the player's screen, taskbar or focus, and a scenario that showed them anything fails), and says PASS, FAIL or
# BLOCKED for each, with the screenshots and log lines that show why.
# One smoke run at a time on this PC: a second run waits for the first. Each scenario's copy of the app is a test copy
# (--offscreen): it runs beside the player's own copies (their app, the demo) on a profile of its own, without the
# app's one-copy lock, and leaves their tray, notices, talk key, mouse and microphone alone. This script never stops
# anything of the user's.
# Works the same in a worktree: it borrows the main checkout's GODOT setting when the worktree has none, and the app
# itself finds the main checkout's models (a worktree holds no link to them).
#
#   tools\dev\smoke.ps1                      # every scenario
#   tools\dev\smoke.ps1 -Only boot,chat      # some of them (from bash: -Only "boot,chat")
#   tools\dev\smoke.ps1 -List                # the scenarios and what each one checks
#   tools\dev\smoke.ps1 -Prepare             # only make this checkout ready for test runs (GODOT, import, build)
#   tools\dev\smoke.ps1 -Changed             # only those the branch's changes since origin/main need (check.ps1 runs this)
#   tools\dev\smoke.ps1 -Changed -Explain    # which those are, and why each other one is skipped, without running any
#
# -Only      scenario names, comma-separated
# -Changed   pick the scenarios by what changed (scenarios.json's "about" has the rule): the others are recorded as
#            SKIPPED with the reason; none picked, nothing is run (no build, no lock). When git can't say what
#            changed, or nothing did, every scenario runs
# -Files     with -Changed: these changed paths (from the checkout's root) instead of git's <Base>...HEAD
# -Base      with -Changed: what the branch is compared with (default origin/main)
# -NoBuild   skip building the game first (check.ps1 has just built it)
# -Out       where shots, logs and the report go (default: <main checkout>\.qa\smoke\<branch>)
# -Wait      minutes to wait for another smoke run before giving up as BLOCKED (default 20)
# -SelfCheck only check, on made-up copies of the app, how "beside" judges whose the player's lock is; then stop
# -Port      the mock mind's port (default: a free one for each scenario, so another agent's mock on 8797 can't answer)
# -Prepare   in a worktree, before shoot.ps1 or clip.ps1 runs of your own: copies the main checkout's private\local.cmd
#            (where Godot is), removes a link to its models left by an older -Prepare, imports the project and builds
#            the game; then stops
# Test runs take turns: shoot.ps1 and clip.ps1 take the same lock (Local\InsertCoinSmoke), so an agent's own run waits
# for a smoke run and the other way round. Copies of the app left over from a killed test run (started --offscreen)
# are stopped; nothing else is.
# A worktree has no models folder of its own: run from source, the app reads the main checkout's (ModelFiles). An older
# -Prepare linked them in (a junction), and git worktree remove --force followed that link and emptied the main
# checkout's models (#122): -Prepare, and every smoke run (whatever it then runs), now remove such a link (cmd /c rmdir, which takes only the
# link), so any way of removing a worktree (git worktree remove, --force or not, Remove-Item -Recurse) is safe.
# Output: <Out>\report.md and report.json, <scenario>-<n>.png per shot (-window, -desk as shoot.ps1 makes them),
#         run-<scenario>.log and .err, mind-<scenario>.jsonl for scenarios with the mock mind.
# A scenario with "beside" also starts stand-ins for the player's own app first (hidden copies that aren't test runs,
# on throwaway profiles): the first holds a one-copy lock of their own (--lock, never the player's), a second must
# quit at once, and the scenario runs beside them; the player's own lock must stay free meanwhile.
# Exit code: 0 every scenario passed, 1 something failed, 3 blocked (another smoke run held the lock too long).
param([string[]]$Only = @(), [switch]$NoBuild, [string]$Out = "", [double]$Wait = 20, [int]$Port = 0, [switch]$List,
      [switch]$Prepare, [switch]$Changed, [string[]]$Files = $null, [switch]$Explain, [string]$Base = "origin/main",
      [switch]$SelfCheck)

$repo = Split-Path (Split-Path $PSScriptRoot)
$game = Join-Path $repo "src\InsertCoin.Game"
$suite = Get-Content -Raw -Encoding UTF8 (Join-Path (Join-Path $PSScriptRoot "smoke") "scenarios.json") | ConvertFrom-Json

if ($List) {
  foreach ($s in $suite.scenarios) { "{0,-10} {1}" -f $s.name, $s.about }
  exit 0
}

# "beside": whose the player's lock (InsertCoin.Cabinet) is once the stand-ins are up, from whether it was held before
# they started, whether it is now, and the copies of the app running now: a problem, or a note for the report. Held
# now but not before, it is a stand-in's only if no copy of the player's (neither --lock=beside nor --offscreen) runs:
# their app may have started meanwhile.
function Lock-Verdict([bool]$heldBefore, [bool]$heldNow, $copies) {
  if ($heldBefore) { return @{ note = "a copy of the player's own already held InsertCoin.Cabinet, so whether the stand-ins leave it free wasn't checked" } }
  if (-not $heldNow) { return @{ note = "the player's own lock (InsertCoin.Cabinet) stayed free beside the stand-ins" } }
  $theirs = @($copies | Where-Object { "$($_.CommandLine)" -notmatch "--lock=beside|--offscreen" })
  if ($theirs.Count) { return @{ note = "the player's own app started meanwhile (PID $($theirs[0].ProcessId)) and holds InsertCoin.Cabinet, so whether the stand-ins leave it free wasn't checked" } }
  return @{ problem = "a stand-in took the player's own lock (InsertCoin.Cabinet): their app couldn't have started" }
}

if ($SelfCheck) {                                           # Lock-Verdict on made-up copies; pick-check.ps1 runs this
  $standIn = [pscustomobject]@{ ProcessId = 1; CommandLine = "godot --path x -- --data=p --no-steam --lock=beside" }
  $test = [pscustomobject]@{ ProcessId = 2; CommandLine = "godot --path x -- --data=q --no-steam --offscreen --shots=s" }
  $player = [pscustomobject]@{ ProcessId = 3; CommandLine = "godot --path x -- --data=r --no-steam" }
  $cases = @(
    @{ before = $true; now = $true; copies = @($standIn); want = "note:already held" },
    @{ before = $false; now = $false; copies = @($standIn, $test); want = "note:stayed free" },
    @{ before = $false; now = $true; copies = @($standIn, $test, $player); want = "note:started meanwhile (PID 3)" },
    @{ before = $false; now = $true; copies = @($standIn, $test); want = "problem:took the player's own lock" }
  )
  $wrong = 0
  foreach ($c in $cases) {
    $v = Lock-Verdict $c.before $c.now $c.copies
    $kind, $words = $c.want -split ":", 2
    if ($v.ContainsKey($kind) -and $v[$kind].Contains($words) -and $v.Count -eq 1) { "ok    lock verdict: $($c.want)" }
    else { $wrong++; "WRONG lock verdict: wanted $($c.want), got $($v | ConvertTo-Json -Compress)" }
  }
  exit $(if ($wrong) { 1 } else { 0 })
}

# Which scenarios a change needs, each with why it runs or is skipped: all of them when shared code or the harness
# changed (the suite's "all"), each whose "covers" match a changed file, and boot whenever anything the app runs changed
# (the suite's "app"). READMEs never count (a character's persona.md does: the app reads it). Patterns are wildcards
# (-like) on paths with forward slashes. With nothing listed (git couldn't say, or on main itself), every one runs.
function Pick([string[]]$changed) {
  $changed = @($changed | ForEach-Object { $_.Trim().Replace('\', '/') } | Where-Object { $_ -ne "" })
  if ($changed.Count -eq 0) {
    foreach ($s in $suite.scenarios) { [pscustomobject]@{ name = $s.name; run = $true; why = "no list of what changed: every scenario runs" } }
    return
  }
  $changed = @($changed | Where-Object { $_ -notmatch '(^|/)README[^/]*\.md$' })
  function Matching($patterns) { @($changed | Where-Object { $f = $_; @($patterns | Where-Object { $_ -and $f -like $_ }).Count -gt 0 }) }
  $shared = @(Matching $suite.all)
  $app = @(Matching $suite.app)
  foreach ($s in $suite.scenarios) {
    $hit = @(Matching $s.covers)
    $why = if ($shared.Count) { "shared code or the harness changed: $($shared[0])" } elseif ($hit.Count) { "it covers $($hit[0])" }
           elseif ($s.name -eq "boot" -and $app.Count) { "the app changed: $($app[0])" } else { "" }
    $skip = if ($app.Count) { "nothing it covers changed" } else { "nothing the app runs changed" }
    [pscustomobject]@{ name = $s.name; run = $why -ne ""; why = $(if ($why -ne "") { $why } else { $skip }) }
  }
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
# before any early exit (-Explain, nothing to run, blocked): an older -Prepare's link to the main checkout's models
$models = Get-Item -Force (Join-Path $game "models") -ErrorAction SilentlyContinue
if ($models -and ($repo -ne $main) -and ($models.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
  cmd /c rmdir "$($models.FullName)"                     # the link only, never what it points to
  "Removed this worktree's link to the main checkout's models: the app finds them by itself."
}

$names = @($Only | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
$scenarios = @($suite.scenarios | Where-Object { $names.Count -eq 0 -or $names -contains $_.name })
foreach ($n in $names) { if (-not ($suite.scenarios | Where-Object { $_.name -eq $n })) { throw "No scenario named '$n' (tools\dev\smoke.ps1 -List)" } }

$results = New-Object System.Collections.ArrayList
$started = Get-Date
$picks = @()
if ($Changed) {
  $pickedBy = "by the files given (-Files)"
  if ($null -eq $Files) {
    $Files = @(& git -C $repo diff --name-only "$Base...HEAD" 2>$null)
    if ($LASTEXITCODE -ne 0) { $Files = @() }                        # git couldn't say: every scenario runs
    $pickedBy = "by what changed since $Base"
  }
  $picks = @(Pick @($Files | ForEach-Object { $_ -split "," }))
  if ($Explain) {
    foreach ($p in $picks) { "{0,-4} {1,-10} {2}" -f $(if ($p.run) { "run" } else { "skip" }), $p.name, $p.why }
    exit 0
  }
  $scenarios = @($scenarios | Where-Object { $n = $_.name; ($picks | Where-Object { $_.name -eq $n }).run })
}

function Finish([int]$code) {
  $passed = @($results | Where-Object { $_.result -eq "PASS" }).Count
  $failed = @($results | Where-Object { $_.result -eq "FAIL" }).Count
  $blocked = @($results | Where-Object { $_.result -eq "BLOCKED" }).Count
  $skipped = @($results | Where-Object { $_.result -eq "SKIPPED" }).Count
  $report = [ordered]@{
    sha = $sha; branch = $branch; checkout = $repo; when = (Get-Date).ToString("o")
    seconds = [int]((Get-Date) - $started).TotalSeconds; passed = $passed; failed = $failed; blocked = $blocked
    skipped = $skipped; picked = $(if ($Changed) { $pickedBy } else { "all asked for" })
    out = $Out; scenarios = @($results)
  }
  $utf8 = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText((Join-Path $Out "report.json"), ($report | ConvertTo-Json -Depth 6), $utf8)

  $md = New-Object System.Collections.ArrayList
  $short = if ($sha.Length -ge 7) { $sha.Substring(0, 7) } else { $sha }
  [void]$md.Add("# Smoke run: $branch at $short")
  [void]$md.Add("")
  [void]$md.Add("$passed passed, $failed failed, $blocked blocked, $skipped skipped, in $($report.seconds) s ($((Get-Date).ToString('yyyy-MM-dd HH:mm'))). Scenarios: $($report.picked). Shots and logs: ``$Out``")
  [void]$md.Add("")
  [void]$md.Add("| Scenario | Result | Seconds | Why | Picked |")
  [void]$md.Add("|---|---|---|---|---|")
  foreach ($r in $results) { [void]$md.Add("| $($r.name) | $($r.result) | $($r.seconds) | $((@($r.problems) | Select-Object -First 1) -replace '\|', '/') | $($r.picked -replace '\|', '/') |") }
  foreach ($r in $results) {
    [void]$md.Add("")
    [void]$md.Add("## $($r.name): $($r.result)")
    [void]$md.Add("")
    [void]$md.Add("$($r.about)")
    if ($r.script) { [void]$md.Add(""); [void]$md.Add("Script: ``$($r.script)``") }
    if (@($r.problems).Count -gt 0) { [void]$md.Add(""); foreach ($p in $r.problems) { [void]$md.Add("- $p") } }
    if (@($r.notes).Count -gt 0) { [void]$md.Add(""); [void]$md.Add("Notes:"); foreach ($n in $r.notes) { [void]$md.Add("- $n") } }
    if (@($r.shots).Count -gt 0) { [void]$md.Add(""); [void]$md.Add("Screenshots:"); foreach ($s in $r.shots) { [void]$md.Add("- ``$s``") } }
    if (@($r.logLines).Count -gt 0) {
      [void]$md.Add(""); [void]$md.Add("Log lines:"); [void]$md.Add("``````")
      foreach ($l in $r.logLines) { [void]$md.Add($l) }
      [void]$md.Add("``````")
    }
  }
  [System.IO.File]::WriteAllText((Join-Path $Out "report.md"), ($md -join "`r`n") + "`r`n", $utf8)

  ""
  "Smoke: $passed passed, $failed failed, $blocked blocked, $skipped skipped ($branch at $short; scenarios $($report.picked))"
  foreach ($r in $results) { "  {0,-8} {1,-10} {2}" -f $r.result, $r.name, $(if (@($r.problems).Count) { @($r.problems)[0] } else { $r.picked }) }
  "Report: $(Join-Path $Out 'report.md')"
  exit $code
}

function Add-Result($s, [string]$result, $problems, $shots, $logLines, [double]$seconds, $notes = @()) {
  [void]$results.Add([ordered]@{
    name = $s.name; about = $s.about; script = $s.script; result = $result; seconds = [int]$seconds
    picked = $(if ($Changed) { "" + ($picks | Where-Object { $_.name -eq $s.name }).why } else { "" })
    problems = @($problems); notes = @($notes); shots = @($shots); logLines = @($logLines | Select-Object -First 30)
  })
}

function Block-All([string]$why) {
  foreach ($s in $scenarios) { Add-Result $s "BLOCKED" @($why) @() @() 0 }
  Finish 3
}

foreach ($s in $suite.scenarios) {
  if ($Changed -and -not ($scenarios | Where-Object { $_.name -eq $s.name })) { Add-Result $s "SKIPPED" @() @() @() 0 }
}
if ($Changed -and $scenarios.Count -eq 0) { Finish 0 }               # nothing to run: no build, no lock, no app

# --- GODOT and Godot's import, borrowed from the main checkout in a worktree -------------------------------------
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

# --- one run at a time; the player's own copies of the app are left alone ---------------------------------------
function Get-Copies {
  @(Get-CimInstance Win32_Process | Where-Object {
      $_.Name -like "BesideTheCabinet*.exe" -or $_.Name -like "InsertCoin*.exe" -or
      ($_.Name -like "Godot*" -and $_.CommandLine -match "InsertCoin\.Game" -and
       $_.CommandLine -notmatch "(^|\s)(-e|--editor)(\s|$)" -and $_.CommandLine -notmatch "project\.godot" -and
       $_.CommandLine -notmatch "\s--import(\s|$)")   # neither the editor nor an import is a copy
    })
}

# A stand-in for the player's own app ("beside" scenarios): a hidden copy that isn't a test run (no --offscreen), so it
# keeps to one copy at a time as theirs does, on a throwaway profile, watched as test runs are. Its lock is one of its
# own (--lock, honoured only on a profile that isn't the player's), so the player's own app can always start meanwhile.
$standInLock = "InsertCoin.Cabinet.beside"
function Start-StandIn([string]$name) {
  if (-not ("Backstage" -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot "hidden.cs") }
  $profile = Join-Path $Out "profile-$name"
  if (Test-Path $profile) { Remove-Item $profile -Recurse -Force }
  New-Item -ItemType Directory $profile | Out-Null
  '{ "SetUp": true, "Initials": "BOB", "Muted": true, "AlwaysOnTop": false, "WindowX": -4000, "WindowY": 100, "TalkKey": "Off" }' |
    Out-File -Encoding ascii (Join-Path $profile "settings.json")
  $p = [Backstage]::Start("`"$godotConsole`" --path `"$game`" --log-file `"$profile\godot.log`" -- --data=`"$profile`" --no-steam --lock=beside --test-run",
                          (Join-Path $Out "run-$name.log"), (Join-Path $Out "run-$name.err"))
  [pscustomobject]@{ Name = $name; Process = $p; Watch = (New-Object Unseen $p.Id) }
}
function Lock-Held([string]$name) {
  $m = $null
  if ([System.Threading.Mutex]::TryOpenExisting($name, [ref]$m)) { $m.Dispose(); return $true }
  return $false
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
  # The player's own copies (their app, the demo) are left running: test copies run beside them.
  & (Join-Path $PSScriptRoot "import.ps1") -Godot $godotConsole          # the art again, if anything changed since the last import
  if ($Prepare) { "Ready for test runs: $repo"; exit 0 }

  Add-Type -AssemblyName System.Drawing
  . (Join-Path $PSScriptRoot "flat.ps1")
  function Test-FlatFile([string]$path) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    try { Test-Flat $bmp } finally { $bmp.Dispose() }
  }

  $defaultForbid = @('\bERROR\b', 'Exception\b', 'SCRIPT ERROR')
  $suiteIgnore = @($suite.ignore | Where-Object { $_ })
  $py = Get-Command python -ErrorAction SilentlyContinue

  foreach ($s in $scenarios) {
    $t0 = Get-Date
    Get-ChildItem $Out -File -ErrorAction SilentlyContinue |
      Where-Object { $_.Name -like "$($s.name)-*.png" -or $_.Name -like "run-$($s.name).*" -or $_.Name -eq "mind-$($s.name).jsonl" } |
      Remove-Item -Force

    # "beside": the player's own app, played by a stand-in holding its one-copy lock (a lock of its own, never the
    # player's), and a second such copy, which must quit at once; then the scenario's test copy runs beside them
    $standIns = @(); $besideProblems = @(); $besideNotes = @()
    if ($s.beside) {
      $playersHeld = Lock-Held "InsertCoin.Cabinet"                          # held already only by a copy of the player's own
      $player = Start-StandIn "$($s.name)-player"
      $standIns += $player
      $until = (Get-Date).AddSeconds(60)
      while (-not (Lock-Held $standInLock) -and -not $player.Process.HasExited -and (Get-Date) -lt $until) { Start-Sleep -Milliseconds 250 }
      if (-not (Lock-Held $standInLock)) { $besideProblems += "the stand-in for the player's app never took its lock" }
      $second = Start-StandIn "$($s.name)-second"
      $standIns += $second
      if (-not $second.Process.WaitForExit(30000)) { $besideProblems += "a second copy for the player didn't quit: the one-copy lock didn't hold" }
      elseif (-not (Select-String -Path (Join-Path $Out "run-$($s.name)-second.log") -Pattern "^Main: another copy is running; this one quits" -Quiet)) {
        $besideProblems += "the second copy for the player quit without saying another copy is running"
      }
      if (-not (Lock-Held $standInLock)) { $besideProblems += "nothing held the stand-ins' lock when the test copy started" }
      if (-not (Select-String -Path (Join-Path $Out "run-$($s.name)-player.log") -Pattern "^Main: a copy for tests, holding a lock of its own \($([regex]::Escape($standInLock))\)" -Quiet)) {
        $besideProblems += "the stand-in didn't say it holds a lock of its own"
      }
      $verdict = Lock-Verdict $playersHeld (Lock-Held "InsertCoin.Cabinet") $(if (-not $playersHeld) { Get-Copies } else { @() })
      if ($verdict.problem) { $besideProblems += $verdict.problem } else { $besideNotes += $verdict.note; "  $($verdict.note)" }
    }

    $settings = "" + $s.settings
    $mock = $null
    $mindLog = Join-Path $Out "mind-$($s.name).jsonl"
    if ($s.mind) {
      if (-not $py) { Add-Result $s "FAIL" @("python is not on PATH, and this scenario needs the mock mind") @() @() 0; continue }
      $reply = if ($s.mindReply) { "" + $s.mindReply } else { "Hi! It's good to see you. How did today go?" }
      $quit = [int](($s.shots | Measure-Object -Maximum).Maximum) + 60          # gone by itself if this run is killed
      $mindPort = $Port
      if ($mindPort -eq 0) {                                                    # a port nothing else listens on
        $probe = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $probe.Start(); $mindPort = $probe.LocalEndpoint.Port; $probe.Stop()
      }
      # "mind": "chatgpt" is a stand-in ChatGPT plan instead, signed in to by the scenario (its script's {mind} is the address)
      $chatGpt = "$($s.mind)" -eq "chatgpt"
      $mockScript = if ($chatGpt) { 'mock_chatgpt.py' } else { 'mock_mind.py' }
      $mockArgs = @("`"$(Join-Path $PSScriptRoot $mockScript)`"", "$mindPort", "--log", "`"$mindLog`"", "--reply", "`"$reply`"",
                    "--quit-after", "$quit")
      $mock = Start-Process $py.Source -ArgumentList $mockArgs -PassThru -WindowStyle Hidden
      Start-Sleep -Milliseconds 800
      if ($mock.HasExited) { Add-Result $s "FAIL" @("the mock mind didn't start on port $mindPort (taken?)") @() @() 0; continue }
      if (-not $chatGpt) { $settings += ', "ChatProtocol": "OpenAi", "ChatBaseUrl": "http://127.0.0.1:' + $mindPort + '/v1", "ChatModel": "mock"' }
    }

    $shots = @($s.shots | ForEach-Object { [double]$_ })
    $shootArgs = @{ Hidden = $true; Tag = "" + $s.name; Out = $Out; Shots = [double[]]$shots }
    if ($s.script) { $shootArgs.Script = ("" + $s.script).Replace("{mind}", "http://127.0.0.1:$mindPort") }
    if ($settings -ne "") { $shootArgs.Settings = $settings }
    if ($s.window) { $shootArgs.Window = "" + $s.window }
    if ($s.desk) { $shootArgs.Desk = $true }

    "Running $($s.name)..."
    $said = @()
    try { $said = @(& (Join-Path $PSScriptRoot "shoot.ps1") @shootArgs *>&1 | ForEach-Object { "$_" }) }
    catch { $said += "shoot.ps1 failed: $($_.Exception.Message)" }
    finally {
      if ($mock) { Stop-Process -Id $mock.Id -Force -ErrorAction SilentlyContinue }
      if ($standIns.Count) {
        [Backstage]::EndAll()                                                   # the stand-ins end with the run
        foreach ($x in $standIns) { $besideProblems += @($x.Watch.Stop()) }    # anything of theirs the player saw
      }
    }

    $problems = New-Object System.Collections.ArrayList
    foreach ($line in @($besideProblems)) { [void]$problems.Add($line) }
    foreach ($line in $said) {
      if ($line -match "no app process|no window yet|shoot.ps1 failed|no window titled|seen by the player|would run after the last shot") { [void]$problems.Add($line.Trim()) }
      elseif ($line -match "(\S+\.png) .*; (waited .*)$") { $besideNotes += "$(Split-Path $Matches[1] -Leaf): $($Matches[2])" }   # a shot that waited for its window
      elseif ($line -match "^the script's clock started|^the app never said when") { $besideNotes += $line.Trim() }   # how its shots were timed, when not as usual
    }
    # a test copy writes Godot's log into its own profile, never over the player's (in Godot's user folder)
    if (-not (Test-Path (Join-Path $Out "profile-$($s.name)\godot.log"))) { [void]$problems.Add("Godot's log isn't in the run's own profile") }

    $pngs = @(Get-ChildItem $Out -File | Where-Object { $_.Name -match ("^" + [regex]::Escape($s.name) + "-\d+\.png$") } | Sort-Object Name)
    if ($pngs.Count -lt $shots.Count) { [void]$problems.Add("only $($pngs.Count) of $($shots.Count) screenshots were taken") }
    foreach ($p in $pngs) { if (Test-FlatFile $p.FullName) { [void]$problems.Add("$($p.Name) is one flat colour: nothing drawn") } }
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
    Add-Result $s $result $problems $allShots $bad ((Get-Date) - $t0).TotalSeconds $besideNotes
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
