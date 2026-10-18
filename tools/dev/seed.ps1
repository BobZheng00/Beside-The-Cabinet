# Assert-Seed: stops a test run before it launches when the settings.json it seeds isn't strict JSON. The app would set
# such a file aside and start on its defaults: not what the run asked for. Dot-sourced by shoot.ps1 and clip.ps1.
# Strict on purpose: ConvertFrom-Json takes `, AlwaysOnTop: true` (the quotes a nested `powershell -File` strips),
# which the app can't read; Windows' own JSON parser refuses it, as the app does. That parser is a WinRT type, which
# only Windows PowerShell 5.1 (powershell.exe) loads, not pwsh 7: run the test scripts with powershell.exe.
function Assert-Seed([string]$Json, [string]$Settings, [string]$Script) {
  try { $null = [Windows.Data.Json.JsonObject, Windows.Data.Json, ContentType = WindowsRuntime] }
  catch { throw "${Script}: run it in Windows PowerShell 5.1 (powershell.exe): it checks the settings it seeds with Windows' JSON parser, which pwsh 7 can't load." }
  try { $null = [Windows.Data.Json.JsonObject]::Parse($Json) }
  catch {
    throw ("${Script}: -Settings doesn't make settings the app can read, so nothing was run (it would start on its " +
           "defaults instead). The text: $Settings`n" +
           "Called through ``powershell -File``, a value's quotes are lost: call $Script directly in PowerShell.")
  }
}
