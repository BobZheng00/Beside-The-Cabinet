# Every check a commit must pass before its pull request is marked ready, in one go, with the result recorded against
# the commit: the solution built with no warnings, the unit tests, the game built with no warnings, and the smoke
# suite (tools\dev\smoke.ps1; skipped when the branch changes only Markdown). The result goes to
# <main checkout>\.qa\results\<sha>.checks.json. QA's verdict on the same commit goes beside it (-Verdict), and the
# gate hook (.claude\hooks\gate.ps1) wants both before `gh pr ready`.
#
#   tools\dev\check.ps1                      # everything, on this checkout's HEAD
#   tools\dev\check.ps1 -NoSmoke             # without the smoke suite (a quick pass while working; not enough for the gate)
#   tools\dev\check.ps1 -Only chat,settings  # the smoke suite cut to those scenarios (not enough for the gate either)
#   tools\dev\check.ps1 -Verdict PASS -Because "what QA looked at, and why it passes"     # QA's verdict on HEAD
#   tools\dev\check.ps1 -Status              # what is recorded for HEAD
#
# Commit first: a result belongs to HEAD, and one taken with uncommitted changes is recorded as not clean, which the
# gate refuses. The smoke suite takes a few minutes and waits its turn: run this in the background.
# Exit code: 0 passed, 1 failed, 3 the smoke suite was blocked (another run, or a copy of the app running).
param([switch]$NoSmoke, [string]$Only = "", [string]$Verdict = "", [string]$Because = "", [switch]$Status)

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
  $out = @(& $work 2>&1 | ForEach-Object { "$_" })
  $code = $LASTEXITCODE
  $result = if ($code -eq 0) { "PASS" } elseif ($code -eq 3 -and $name -eq "smoke") { "BLOCKED" } else { "FAIL" }
  $seconds = [int]((Get-Date) - $t0).TotalSeconds
  [void]$steps.Add([ordered]@{ name = $name; result = $result; seconds = $seconds; tail = @($out | Select-Object -Last 40) })
  Write-Host "   $result in $seconds s"
  if ($result -ne "PASS") { $out | Select-Object -Last 25 | ForEach-Object { Write-Host "   | $_" } }
  return $result
}

Push-Location $repo
try {
  $ok = (Run-Step "build-solution" { dotnet build InsertCoin.slnx @buildFlags }) -eq "PASS"
  if ($ok) { $ok = (Run-Step "unit-tests" { dotnet test InsertCoin.slnx --no-build -nologo -v:q }) -eq "PASS" }
  if ($ok) { $ok = (Run-Step "build-game" { dotnet build src\InsertCoin.Game\InsertCoin.Game.csproj @buildFlags }) -eq "PASS" }

  $partial = ""
  $changed = @((GitOut @("diff", "--name-only", "origin/main...HEAD")) -split "`n" | Where-Object { $_ -ne "" })
  $docsOnly = $changed.Count -gt 0 -and -not ($changed | Where-Object { $_ -notmatch '\.md$' })
  if ($ok) {
    if ($NoSmoke) { $partial = "smoke skipped (-NoSmoke)" }
    elseif ($docsOnly) {
      [void]$steps.Add([ordered]@{ name = "smoke"; result = "SKIPPED"; seconds = 0; tail = @("only Markdown changed since origin/main") })
      "== smoke`n   SKIPPED: only Markdown changed since origin/main"
    } else {
      if ($Only -ne "") { $partial = "smoke cut to: $Only" }
      $smoke = Run-Step "smoke" { & (Join-Path $PSScriptRoot "smoke.ps1") -NoBuild -Only $Only }
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
