# Checks how smoke.ps1 -Changed picks scenarios, on a few lists of changed files, and how the beside scenario judges
# whose the player's lock is (smoke.ps1 -SelfCheck), and that shoot.ps1 and clip.ps1 refuse settings the app couldn't
# read. check.ps1 runs it; by hand:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\smoke\pick-check.ps1
# Exit code: 0 every case picked what it should, 1 otherwise.
$smoke = Join-Path (Split-Path $PSScriptRoot) "smoke.ps1"
$suite = Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot "scenarios.json") | ConvertFrom-Json
$every = ($suite.scenarios | ForEach-Object { $_.name }) -join ","
$cases = @(
  @{ files = "docs/README.md,tests/InsertCoin.Mind.Tests/RecallTests.cs"; want = "" },
  @{ files = "src/InsertCoin.Game/README.md,src/InsertCoin.Game/addons/vrm/README.ja.md"; want = "" },
  @{ files = "src/InsertCoin.Game/characters/shino/persona.md"; want = "boot" },
  @{ files = "src/InsertCoin.Game/App/MemoryBook.cs"; want = "boot,memory,agendacancel" },
  @{ files = "src/InsertCoin.Game/App/VoiceSettings.cs,docs/README.md"; want = "boot,settings" },
  @{ files = "src/InsertCoin.Mind/MindStore.cs"; want = "boot,chat,memory,agendacancel,chathistory,chathistory-hidden,reminder" },
  @{ files = "src/InsertCoin.Adapters/LeetCodeSource.cs"; want = "boot" },
  @{ files = "src/InsertCoin.Game/App/Crt/MemosApp.cs"; want = "boot,crt,memos" },
  @{ files = "src/InsertCoin.Game/Main.cs"; want = $every },
  @{ files = "src/InsertCoin.Game/App/AppSettings.cs"; want = $every },
  @{ files = "src/InsertCoin.Core/Ledger.cs"; want = $every },
  @{ files = "tools/dev/shoot.ps1"; want = $every },
  @{ files = "tools/dev/smoke/scenarios.json"; want = $every },
  @{ files = ""; want = $every; why = "no list of what changed" },       # nothing listed (on main itself): everything
  @{ base = "no-such-branch"; want = $every; why = "no list of what changed" }   # git can't say what changed: everything
)
$failed = 0
foreach ($s in $suite.scenarios) {                                      # every scenario says what it covers
  if (@($s.covers | Where-Object { $_ }).Count -eq 0) { $failed++; "WRONG scenario '$($s.name)' covers nothing" }
}
foreach ($case in $cases) {
  $pickArgs = @{ Changed = $true; Explain = $true }
  if ($case.ContainsKey("base")) { $pickArgs.Base = $case.base; $label = "git diff $($case.base)...HEAD" }
  else { $pickArgs.Files = $case.files; $label = $(if ($case.files) { $case.files } else { "(no files)" }) }
  $picks = @(& $smoke @pickArgs | ForEach-Object { $f = "$_" -split '\s+', 3; [pscustomobject]@{ run = $f[0] -eq "run"; name = $f[1]; why = $f[2] } })
  $got = ($picks | Where-Object { $_.run } | ForEach-Object { $_.name }) -join ","
  $everyOneSaysWhy = -not ($picks | Where-Object { "$($_.why)".Trim() -eq "" })
  $named = @("$($case.files)" -split "," | Where-Object { $_ })        # one that runs names the file it runs for
  if ($named.Count -and ($picks | Where-Object { $w = $_.why; $_.run -and -not ($named | Where-Object { $w.EndsWith($_) }) })) { $everyOneSaysWhy = $false }
  if ($case.why -and ($picks | Where-Object { -not "$($_.why)".StartsWith($case.why) })) { $everyOneSaysWhy = $false }
  if ($got -eq $case.want -and $everyOneSaysWhy -and $picks.Count -eq $suite.scenarios.Count) { "ok    $label -> $(if ($got) { $got } else { 'none' })" }
  else { $failed++; "WRONG $label -> '$got', wanted '$($case.want)'" }
}
# Settings a run seeds that the app couldn't read stop shoot.ps1 and clip.ps1 before they launch: given directly, and
# through a nested `powershell -File`, which strips -Settings' quotes (the trap of #77)
$seedOut = Join-Path $env:TEMP "insert-coin-seed-check"
foreach ($tool in @("shoot.ps1", "clip.ps1")) {
  $path = Join-Path (Split-Path $PSScriptRoot) $tool
  $direct = try { & $path -Godot none -Out $seedOut -Tag seed -Settings ', "Muted": false,' *>&1 | Out-Null; "" } catch { $_.Exception.Message }
  $nested = (powershell -NoProfile -ExecutionPolicy Bypass -File $path -Godot none -Out $seedOut -Tag seed -Settings ', "Muted": false' 2>&1 | Out-String) -replace '\s+', ' '
  $nestedExit = $LASTEXITCODE
  $written = Test-Path (Join-Path $seedOut "profile-seed\settings.json")
  if ($direct -match "^$tool`: -Settings doesn't make settings the app can read.*The text: , `"Muted`": false," -and
      $nestedExit -ne 0 -and $nested -match "The text: , Muted: false" -and -not $written) { "ok    $tool refuses settings the app couldn't read" }
  else { $failed++; "WRONG $tool on unreadable settings: direct '$direct', nested (exit $nestedExit) '$nested', settings.json written: $written" }
  if (Test-Path $seedOut) { Remove-Item $seedOut -Recurse -Force }
  # and every copy it starts, hidden or not, is told it's a test run (--test-run), so settings it can't read leave it muted
  $base = @(Select-String -Path $path -Pattern '^\$userArgs = ".*"')
  if ($base.Count -eq 1 -and $base[0].Line -match '\s--test-run[\s"]') { "ok    $tool starts every copy with --test-run" }
  else { $failed++; "WRONG $tool doesn't start every copy with --test-run: $($base.Line -join ' | ')" }
}
& $smoke -SelfCheck                                                   # the beside scenario's verdict on the player's lock
if ($LASTEXITCODE -ne 0) { $failed++ }
if ($failed) { "$failed case(s) picked the wrong scenarios, judged the lock wrongly or let unreadable settings through"; exit 1 }
"Every case picked what it should."
exit 0
