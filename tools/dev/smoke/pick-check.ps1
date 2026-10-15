# Checks how smoke.ps1 -Changed picks scenarios, on a few lists of changed files. check.ps1 runs it; by hand:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\dev\smoke\pick-check.ps1
# Exit code: 0 every case picked what it should, 1 otherwise.
$smoke = Join-Path (Split-Path $PSScriptRoot) "smoke.ps1"
$every = "boot,firstrun,settings,chat,memory,crt,reminder,focus,memos"
$cases = @(
  @{ files = "docs/README.md,tests/InsertCoin.Mind.Tests/RecallTests.cs"; want = "" },
  @{ files = "src/InsertCoin.Game/README.md"; want = "" },
  @{ files = "src/InsertCoin.Game/App/MemoryBook.cs"; want = "boot,memory" },
  @{ files = "src/InsertCoin.Game/App/VoiceSettings.cs,docs/README.md"; want = "boot,settings" },
  @{ files = "src/InsertCoin.Mind/MindStore.cs"; want = "boot,chat,memory,reminder" },
  @{ files = "src/InsertCoin.Adapters/LeetCodeSource.cs"; want = "boot" },
  @{ files = "src/InsertCoin.Game/App/Crt/MemosApp.cs"; want = "boot,crt,memos" },
  @{ files = "src/InsertCoin.Game/Main.cs"; want = $every },
  @{ files = "src/InsertCoin.Core/Ledger.cs"; want = $every },
  @{ files = "tools/dev/shoot.ps1"; want = $every },
  @{ files = "tools/dev/smoke/scenarios.json"; want = $every }
)
$failed = 0
foreach ($case in $cases) {
  $picks = @(& $smoke -Changed -Explain -Files $case.files | ForEach-Object { $f = "$_" -split '\s+', 3; [pscustomobject]@{ run = $f[0] -eq "run"; name = $f[1]; why = $f[2] } })
  $got = ($picks | Where-Object { $_.run } | ForEach-Object { $_.name }) -join ","
  $everyOneSaysWhy = -not ($picks | Where-Object { "$($_.why)".Trim() -eq "" })
  $named = $case.files -split ","                                        # one that runs names the file it runs for
  if ($picks | Where-Object { $w = $_.why; $_.run -and -not ($named | Where-Object { $w.EndsWith($_) }) }) { $everyOneSaysWhy = $false }
  if ($got -eq $case.want -and $everyOneSaysWhy -and $picks.Count -eq 9) { "ok    $($case.files) -> $(if ($got) { $got } else { 'none' })" }
  else { $failed++; "WRONG $($case.files) -> '$got', wanted '$($case.want)'" }
}
if ($failed) { "$failed case(s) picked the wrong scenarios"; exit 1 }
"Every case picked what it should."
exit 0
