# Add-Type in a folder of the checkout's, dot-sourced by smoke.ps1, shoot.ps1 and clip.ps1 for the C# they compile.
# Windows PowerShell compiles with csc into %TEMP% and loads the .dll from there, and a clean-up tool on this PC empties
# %TEMP%: once it took the .dll in between, and the smoke suite stopped before running any scenario (#353). So the
# compile goes to <checkout>\.qa\compile (git ignores .qa) for its duration; the test runs it starts keep %TEMP%.
#
#   . (Join-Path $PSScriptRoot "compile.ps1")
#   Compile-InCheckout { Add-Type -Path (Join-Path $PSScriptRoot "hidden.cs") }
function Compile-InCheckout([scriptblock]$addType) {
  $dir = Join-Path (Split-Path (Split-Path $PSScriptRoot)) ".qa\compile"
  New-Item -ItemType Directory $dir -Force | Out-Null
  $tmp, $temp = $env:TMP, $env:TEMP
  $env:TMP = $dir; $env:TEMP = $dir                    # csc's temporary files follow TMP (Path.GetTempPath)
  try { & $addType } finally { $env:TMP = $tmp; $env:TEMP = $temp }
}
