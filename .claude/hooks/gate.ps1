# The gate: a PreToolUse hook on shell commands that touch pull requests or pushes (.claude\settings.json). It refuses
# three things outright and lets one through only once it has been earned:
#   - `gh pr merge`: the user merges every pull request, on GitHub (Rebase and merge);
#   - `git push` to main or master, or a push from a checkout on main that doesn't name another branch;
#   - `gh pr create` without --draft: a pull request starts as a draft and becomes ready through this gate;
#   - `gh pr ready` (not --undo): only for a pull request whose head commit passed tools\dev\check.ps1 on a clean
#     checkout and has QA's PASS (check.ps1 -Verdict), both recorded in <main checkout>\.qa\results.
# It is a guard rail, not a lock: the permission rules in .claude\settings.json deny `gh pr merge` as well.
# Exit 2 refuses the command and tells Claude why on stderr; exit 0 lets it through to the usual permissions.

function Refuse([string]$why) {
  [Console]::Error.WriteLine("Refused by .claude/hooks/gate.ps1: $why")
  exit 2
}

$reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), (New-Object System.Text.UTF8Encoding($false)))
$raw = $reader.ReadToEnd()
if (-not $raw) { exit 0 }
try { $hook = $raw | ConvertFrom-Json } catch { exit 0 }
$cmd = "" + $hook.tool_input.command
if ($cmd.Trim() -eq "") { exit 0 }
$cwd = "" + $hook.cwd
if ($cwd -eq "" -or -not (Test-Path $cwd)) { $cwd = (Get-Location).Path }

function GitAt([string[]]$GitArgs) {
  $r = & git -C $cwd @GitArgs 2>$null
  if ($LASTEXITCODE -ne 0) { return "" }
  return ("" + ($r -join "`n")).Trim()
}

# The command as words, quotes respected ("D:\CS Projects" is one word, and so is a --body with ; or | in it), then
# cut into simple commands at ; && || | and newlines. Quote marks are dropped from the words.
$tokens = [regex]::Matches($cmd, "(?:[^\s`"';&|]|`"(?:[^`"\\]|\\.)*`"|'[^']*')+|&&|\|\||[;|]|\r?\n") | ForEach-Object { $_.Value }
$segments = New-Object System.Collections.ArrayList
$current = New-Object System.Collections.ArrayList
foreach ($t in $tokens) {
  if ($t -match "^(&&|\|\||;|\||\r?\n)$") {
    if ($current.Count -gt 0) { [void]$segments.Add($current.ToArray()); $current = New-Object System.Collections.ArrayList }
  } else { [void]$current.Add(($t -replace "[`"']", "")) }
}
if ($current.Count -gt 0) { [void]$segments.Add($current.ToArray()) }

# The words after a command's name, flags and their values left out ("-R owner/repo" counts as a flag with a value).
function Plain-Words($words, [int]$from, [string[]]$withValue) {
  $out = @(); $i = $from
  while ($i -lt $words.Count) {
    $w = $words[$i]
    if ($w.StartsWith("-")) { if ($withValue -contains $w) { $i++ }; $i++; continue }
    $out += $w; $i++
  }
  return , $out
}

foreach ($words in $segments) {
  if ($words.Count -lt 2) { continue }

  # git [-C path] push ...
  $at = 0
  if ($words[0] -eq "git") {
    $at = 1
    while ($at -lt $words.Count -and $words[$at] -eq "-C") { $at += 2 }
    if ($at -lt $words.Count -and $words[$at] -eq "push") {
      $plain = Plain-Words $words ($at + 1) @("-o", "--push-option", "--repo", "--receive-pack", "--exec")
      $refspecs = @($plain | Select-Object -Skip 1)
      foreach ($r in $refspecs) {
        if ($r -match "(^|:|\+|/)(main|master)$") { Refuse "main takes changes only through pull requests. Push your branch and open a draft pull request." }
      }
      $here = GitAt @("rev-parse", "--abbrev-ref", "HEAD")
      if (($here -eq "main" -or $here -eq "master") -and (($refspecs.Count -eq 0) -or ($refspecs | Where-Object { $_ -match "^(\+)?HEAD(:|$)" }))) {
        Refuse "this checkout is on $here, so that push would push $here. Work on a branch (git switch -c issue-<n>-<slug>) and push that."
      }
    }
    continue
  }

  if ($words[0] -ne "gh" -or $words[1] -ne "pr" -or $words.Count -lt 3) { continue }
  $sub = $words[2]

  if ($sub -eq "merge") {
    Refuse "the user merges every pull request (Rebase and merge on GitHub). Mark it ready with gh pr ready and list it in the digest instead."
  }

  if ($sub -eq "create" -and -not ($words -contains "--draft" -or $words -contains "-d")) {
    Refuse "open pull requests as drafts (gh pr create --draft ...). The PM marks one ready once checks, QA and review have passed."
  }

  if ($sub -eq "ready" -and -not ($words -contains "--undo")) {
    $plain = Plain-Words $words 3 @("-R", "--repo")
    $viewArgs = @("pr", "view") + @($plain | Select-Object -First 1)
    $repoFlag = [Array]::IndexOf($words, "-R"); if ($repoFlag -lt 0) { $repoFlag = [Array]::IndexOf($words, "--repo") }
    if ($repoFlag -ge 0 -and $repoFlag + 1 -lt $words.Count) { $viewArgs += @("--repo", $words[$repoFlag + 1]) }
    $viewArgs += @("--json", "headRefOid,number")
    Push-Location $cwd
    try { $view = ("" + ((& gh @viewArgs 2>$null) -join "`n")).Trim(); $viewCode = $LASTEXITCODE } finally { Pop-Location }
    $pr = $null
    if ($viewCode -eq 0) { try { $pr = $view | ConvertFrom-Json } catch { $pr = $null } }
    if (-not $pr -or ("" + $pr.headRefOid) -notmatch "^[0-9a-f]{40}$") {
      Refuse "couldn't find the pull request's head commit to check it (gh pr view said: $view). Try again with its number: gh pr ready <number>."
    }
    $sha = "" + $pr.headRefOid
    $short = $sha.Substring(0, 7)

    $common = GitAt @("rev-parse", "--path-format=absolute", "--git-common-dir")
    if ($common -eq "") { Refuse "couldn't find the repository from $cwd to look up the checks." }
    $results = Join-Path (Join-Path ([System.IO.Path]::GetDirectoryName($common)) ".qa") "results"
    $checksFile = Join-Path $results "$sha.checks.json"
    $verdictFile = Join-Path $results "$sha.verdict.md"

    $missing = @()
    if (-not (Test-Path $checksFile)) {
      $missing += "no checks recorded for ${short}: run tools\dev\check.ps1 (in the background) in a checkout at that commit"
    } else {
      $c = Get-Content -Raw -Encoding UTF8 $checksFile | ConvertFrom-Json
      if (-not $c.clean) { $missing += "the checks for $short ran with uncommitted changes: commit, push and run tools\dev\check.ps1 again" }
      elseif (-not $c.pass) {
        $why = if ($c.partial) { "they were partial ($($c.partial))" } elseif ($c.blocked) { "the smoke suite was blocked" } else { "they failed" }
        $missing += "the checks for $short did not pass: $why (tools\dev\check.ps1 -Status in that checkout)"
      }
    }
    if (-not (Test-Path $verdictFile)) {
      $missing += "no QA verdict for ${short}: the qa agent records one with tools\dev\check.ps1 -Verdict PASS|FAIL -Because ..."
    } elseif (("" + (Get-Content -Encoding UTF8 $verdictFile | Select-Object -First 1)).Trim() -ne "PASS") {
      $missing += "QA's verdict on $short is not PASS ($verdictFile)"
    }
    if ($missing.Count -gt 0) {
      Refuse ("pull request #$($pr.number) (head $short) isn't ready yet: " + ($missing -join "; ") + ".")
    }
  }
}

exit 0
