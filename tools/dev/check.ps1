# Every check a commit must pass before its pull request is marked ready, in one go, with the result recorded against
# the commit: the solution built with no warnings, the unit tests, the game built with no warnings, and the smoke
# suite (tools\dev\smoke.ps1 -Changed: the scenarios what the branch changed since origin/main needs, by the rule in
# tools\dev\smoke\scenarios.json, checked first by smoke\pick-check.ps1; none when only tests or docs changed; the
# record says which ran and why each other one didn't). The result goes to
# <main checkout>\.qa\results\<sha>.checks.json, in the pm plugin's format. QA's verdict on the same commit goes beside
# it (-Verdict), and the plugin's gate wants both before `gh pr ready`.
#
#   tools\dev\check.ps1                      # everything, on this checkout's HEAD
#   tools\dev\check.ps1 -All                 # every smoke scenario, whatever changed
#   tools\dev\check.ps1 -NoSmoke             # without the smoke suite (a quick pass while working; not enough for the gate)
#   tools\dev\check.ps1 -Only chat,settings  # the smoke suite cut to those scenarios (not enough for the gate either)
#   tools\dev\check.ps1 -Verdict PASS -Because "what QA looked at, and why it passes"     # QA's verdict on HEAD
#   tools\dev\check.ps1 -Status              # what is recorded for HEAD
#   tools\dev\check.ps1 -SelfCheck           # only check that a smoke step that stops early is never a PASS (pick-check.ps1 runs it)
#
# Commit first: a result belongs to HEAD, and one taken with uncommitted changes is recorded as not clean, which the
# gate refuses. The smoke suite takes a few minutes and waits its turn: run this in the background.
# Exit code: 0 passed, 1 failed, 3 the smoke suite was blocked (another smoke run held the lock too long). The
# player's own copies of the app (their app, the demo) never block it: its test copies run beside them.
# The smoke step passes, or is blocked, only on a report of this commit's run written by that step and saying the same:
# one that stops on an error, or ends without that report, fails (#353).
param([switch]$NoSmoke, [switch]$All, [string]$Only = "", [string]$Verdict = "", [string]$Because = "", [switch]$Status,
      [switch]$SelfCheck)

$repo = Split-Path (Split-Path $PSScriptRoot)
function GitOut([string[]]$GitArgs) {
  $r = & git -C $repo @GitArgs 2>$null
  if ($LASTEXITCODE -ne 0) { return "" }
  return ("" + ($r -join "`n")).Trim()
}
$common = GitOut @("rev-parse", "--path-format=absolute", "--git-common-dir")
$main = if ($common -ne "") { [System.IO.Path]::GetDirectoryName($common) } else { $repo }
$sha = GitOut @("rev-parse", "HEAD")
$branch = GitOut @("rev-parse", "--abbrev-ref", "HEAD")
if ($branch -eq "" -or $branch -eq "HEAD") { $branch = "detached-" + $sha.Substring(0, [Math]::Min(7, $sha.Length)) }   # as smoke.ps1 names it
$clean = (GitOut @("status", "--porcelain")) -eq ""
$results = Join-Path (Join-Path $main ".qa") "results"
New-Item -ItemType Directory $results -Force | Out-Null
$checksFile = Join-Path $results "$sha.checks.json"
$verdictFile = Join-Path $results "$sha.verdict.md"
$short = if ($sha.Length -ge 7) { $sha.Substring(0, 7) } else { $sha }
$utf8 = New-Object System.Text.UTF8Encoding($false)

if ($Status) {
  "HEAD $short on $branch ($(if ($clean) { 'clean' } else { 'uncommitted changes' }))"
  if (Test-Path $checksFile) {
    $c = Get-Content -Raw -Encoding UTF8 $checksFile | ConvertFrom-Json
    "Checks:  $(if ($c.pass) { 'PASS' } elseif ($c.blocked) { 'BLOCKED' } else { 'FAIL' })$(if (-not $c.clean) { ' (not clean)' })$(if ($c.partial) { ' (partial: ' + $c.partial + ')' }) at $($c.when)"
    foreach ($s in $c.steps) { "  {0,-15} {1,-8} {2,4} s" -f $s.name, $s.result, $s.seconds }
  } else { "Checks:  none recorded" }
  if (Test-Path $verdictFile) { "Verdict: $((Get-Content -Encoding UTF8 $verdictFile | Select-Object -First 1))" } else { "Verdict: none recorded" }
  exit 0
}

if ($Verdict -ne "") {
  $Verdict = $Verdict.ToUpperInvariant()
  if ($Verdict -ne "PASS" -and $Verdict -ne "FAIL") { throw "-Verdict is PASS or FAIL" }
  if (-not $clean) { throw "Uncommitted changes: a verdict belongs to a commit. Commit (or look at a clean checkout) first." }
  if ($Because.Trim() -eq "") { throw "Say why with -Because: what was looked at, and what was found." }
  $text = @($Verdict, "", "Commit: $sha", "Branch: $branch", "When: $((Get-Date).ToString('o'))", "", $Because.Trim()) -join "`r`n"
  [System.IO.File]::WriteAllText($verdictFile, $text + "`r`n", $utf8)
  "QA verdict $Verdict recorded for $short ($verdictFile)"
  exit 0
}

# --- the checks ---------------------------------------------------------------------------------------------------
$env:DOTNET_CLI_UI_LANGUAGE = "en"          # this PC's dotnet speaks Chinese otherwise; the record should read the same anywhere
# --no-incremental: an up-to-date build compiles nothing and so logs no warnings, and -warnaserror would pass it.
# NuGetAudit off: a new advisory for a package shouldn't fail a commit that didn't touch it (CI reports them).
$buildFlags = @("-nologo", "-v:q", "--no-incremental", "-warnaserror", "-p:NuGetAudit=false")
$steps = New-Object System.Collections.ArrayList
$blocked = $false

function Run-Step([string]$name, [scriptblock]$work) {       # says how it went on the console, returns PASS, FAIL or BLOCKED
  Write-Host "== $name"
  $t0 = Get-Date
  $out = New-Object System.Collections.ArrayList
  # a script stopped by an error leaves $LASTEXITCODE as an earlier command set it (0 after a build): it fails (#353)
  try { & $work 2>&1 | ForEach-Object { [void]$out.Add("$_") }; $code = $LASTEXITCODE }
  catch { [void]$out.Add("stopped by an error: $($_.Exception.Message)"); $code = 1 }
  $result = if ($code -eq 0) { "PASS" } elseif ($code -eq 3 -and $name -eq "smoke") { "BLOCKED" } else { "FAIL" }
  $seconds = [int]((Get-Date) - $t0).TotalSeconds
  [void]$steps.Add([ordered]@{ name = $name; result = $result; seconds = $seconds; tail = @($out | Select-Object -Last 40) })
  Write-Host "   $result in $seconds s"
  if ($result -ne "PASS") { $out | Select-Object -Last 25 | ForEach-Object { Write-Host "   | $_" } }
  return $result
}

# The smoke step's result stands only on its report: of this commit, written since the step began, with a result for
# every scenario asked for (-Only's, or the suite's: -Changed reports the others SKIPPED), and saying the same.
# Otherwise the step is a FAIL, with why: it once stopped before any scenario, exited 0 and was recorded PASS (#353).
$suiteCount = @((Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot "smoke\scenarios.json") | ConvertFrom-Json).scenarios).Count
function Confirm-Smoke([string]$result, [string]$report, [datetime]$since) {
  if ($result -eq "FAIL") { return "FAIL" }
  $asked = if ($Only -ne "") { @($Only -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique).Count } else { $suiteCount }
  $ran = $null
  try { if ((Test-Path $report) -and (Get-Item $report).LastWriteTime -ge $since) { $ran = Get-Content -Raw -Encoding UTF8 $report | ConvertFrom-Json } } catch { }
  $why = if (-not $ran) { "the smoke step ended without a report of its run ($report)" }
         elseif ($ran.sha -ne $sha) { "the smoke report is of $($ran.sha), not of $sha" }
         elseif (@($ran.scenarios).Count -ne $asked) { "the smoke report has $(@($ran.scenarios).Count) of the $asked scenarios asked for" }
         elseif ($result -eq "PASS" -and ($ran.failed -or $ran.blocked)) { "the smoke step exited 0, but its report has $($ran.failed) failed and $($ran.blocked) blocked" }
         elseif ($result -eq "BLOCKED" -and -not $ran.blocked) { "the smoke step said blocked, but its report has no scenario blocked" }
         else { "" }
  if ($why -eq "") { return $result }
  $step = $steps[$steps.Count - 1]
  $step.result = "FAIL"; $step.tail = @($step.tail) + $why
  Write-Host "   FAIL: $why"
  return "FAIL"
}

if ($SelfCheck) {                     # made-up smoke steps, through Run-Step and Confirm-Smoke as a real one goes
  $dir = Join-Path $repo ".qa\check-selfcheck"
  $report = Join-Path $dir "report.json"
  function Report([string]$of, [int]$blockedCount, [int]$count = $suiteCount) {
    New-Item -ItemType Directory $dir -Force | Out-Null
    $r = [ordered]@{ sha = $of; passed = $count - $blockedCount; failed = 0; blocked = $blockedCount
                     scenarios = @(1..$count | Where-Object { $count -gt 0 } | ForEach-Object { @{ name = "s$_"; result = $(if ($_ -le $blockedCount) { "BLOCKED" } else { "PASS" }) } }) }
    [System.IO.File]::WriteAllText($report, ($r | ConvertTo-Json), $utf8)
  }
  $cutOff = Join-Path $dir "a-file"                     # smoke.ps1 -Out under a file: it can't write its report
  $cases = @(
    @{ what = "a step stopped by an error, after a report and a command that exited 0"; want = "FAIL"
       work = { Report $sha 0; cmd /c exit 0; throw "Add-Type : file not found" } },
    @{ what = "smoke.ps1 unable to write its report"; want = "FAIL"; out = $cutOff; report = (Join-Path $cutOff "smoke\report.json")
       work = { & (Join-Path $PSScriptRoot "smoke.ps1") -Changed -Files "docs/README.md" -Out (Join-Path $cutOff "smoke") } },
    @{ what = "exit 0 and no report"; want = "FAIL"; work = { cmd /c exit 0 } },
    @{ what = "exit 0 and a report of another commit"; want = "FAIL"; work = { Report ("0" * 40) 0; cmd /c exit 0 } },
    @{ what = "exit 0 and a report of this commit from before the step"; want = "FAIL"; old = $true; work = { cmd /c exit 0 } },
    @{ what = "exit 0 and a report of this commit with a scenario blocked"; want = "FAIL"; work = { Report $sha 1; cmd /c exit 0 } },
    @{ what = "exit 0 and a report of this commit with no scenario in it"; want = "FAIL"; work = { Report $sha 0 0; cmd /c exit 0 } },
    @{ what = "exit 0 and a report of this commit missing a scenario"; want = "FAIL"; work = { Report $sha 0 ($suiteCount - 1); cmd /c exit 0 } },
    @{ what = "exit 3 and no report"; want = "FAIL"; work = { cmd /c exit 3 } },
    @{ what = "exit 3 and a report of this commit with a scenario blocked"; want = "BLOCKED"; work = { Report $sha 1; cmd /c exit 3 } },
    @{ what = "exit 0 and a report of this commit, all passed"; want = "PASS"; work = { Report $sha 0; cmd /c exit 0 } }
  )
  $wrong = 0
  foreach ($c in $cases) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    if ($c.old) { Report $sha 0; (Get-Item $report).LastWriteTime = (Get-Date).AddHours(-1) }
    if ($c.out) { New-Item -ItemType Directory $dir -Force | Out-Null; Set-Content $c.out "" }
    $since = Get-Date
    $got = Confirm-Smoke (Run-Step "smoke" $c.work 6>$null) $(if ($c.report) { $c.report } else { $report }) $since 6>$null
    if ($got -eq $c.want) { "ok    $($c.what): $got" } else { $wrong++; "WRONG $($c.what): $got, wanted $($c.want)" }
  }
  if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
  exit $(if ($wrong) { 1 } else { 0 })
}

Push-Location $repo
try {
  $ok = (Run-Step "build-solution" { dotnet build InsertCoin.slnx @buildFlags }) -eq "PASS"
  if ($ok) { $ok = (Run-Step "unit-tests" { dotnet test InsertCoin.slnx --no-build -nologo -v:q }) -eq "PASS" }
  if ($ok) { $ok = (Run-Step "build-game" { dotnet build src\InsertCoin.Game\InsertCoin.Game.csproj @buildFlags }) -eq "PASS" }

  if ($ok) { $ok = (Run-Step "smoke-picks" { & (Join-Path $PSScriptRoot "smoke\pick-check.ps1") }) -eq "PASS" }

  $partial = ""
  if ($ok) {
    if ($NoSmoke) { $partial = "smoke skipped (-NoSmoke)" }
    else {
      # the scenarios what changed since origin/main needs (smoke.ps1 -Changed), or every one with -All
      if ($Only -ne "") { $partial = "smoke cut to: $Only" }
      $smokeStart = Get-Date
      $smoke = Run-Step "smoke" {
        if ($Only -ne "") { & (Join-Path $PSScriptRoot "smoke.ps1") -NoBuild -Only $Only }
        elseif ($All) { & (Join-Path $PSScriptRoot "smoke.ps1") -NoBuild }
        else { & (Join-Path $PSScriptRoot "smoke.ps1") -NoBuild -Changed }
      }
      $report = Join-Path $main (".qa\smoke\" + ($branch -replace "[^A-Za-z0-9._-]", "-") + "\report.json")
      $smoke = Confirm-Smoke $smoke $report $smokeStart
      $ran =if (Test-Path $report) { Get-Content -Raw -Encoding UTF8 $report | ConvertFrom-Json } else { $null }
      if ($ran -and $ran.sha -eq $sha) {                             # which scenarios ran, and why each other one didn't
        $steps[$steps.Count - 1].scenarios = @($ran.scenarios | ForEach-Object { [ordered]@{ name = $_.name; result = $_.result; picked = $_.picked } })
        # on the console too, pass or fail, so a record made from this output (the pm plugin's) says it as well
        Write-Host "   scenarios $($ran.picked):"
        foreach ($s in $ran.scenarios) { Write-Host ("   {0,-8} {1,-10} {2}" -f $s.result, $s.name, $(if ($s.picked) { $s.picked } else { "asked for" })) }
      }
      if ($smoke -eq "BLOCKED") { $blocked = $true; $ok = $false }
      elseif ($smoke -ne "PASS") { $ok = $false }
    }
  }
} finally { Pop-Location }

$record = [ordered]@{
  sha = $sha; branch = $branch; clean = $clean; checkout = $repo; when = (Get-Date).ToString("o")
  pass = ($ok -and $partial -eq ""); partial = $partial; blocked = $blocked
  smokeReport = (Join-Path $main (".qa\smoke\" + ($branch -replace "[^A-Za-z0-9._-]", "-") + "\report.md"))
  steps = @($steps)
}
[System.IO.File]::WriteAllText($checksFile, ($record | ConvertTo-Json -Depth 6), $utf8)

""
$word = if ($record.pass) { "PASS" } elseif ($blocked) { "BLOCKED" } elseif ($ok) { "PASS (partial, not enough for the gate)" } else { "FAIL" }
"Checks for $short on ${branch}: $word$(if (-not $clean) { ' -- but the checkout had uncommitted changes, so the gate will not take it' })"
if ($partial -ne "") { "  ($partial)" }
"Recorded in $checksFile"
if ($blocked) { exit 3 }
if ($ok) { exit 0 }
exit 1
