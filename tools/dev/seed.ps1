# Assert-Seed: stops a test run before it launches when the settings.json it seeds isn't strict JSON. The app would set
# such a file aside and start on its defaults: not what the run asked for. Dot-sourced by shoot.ps1 and clip.ps1.
# Strict on purpose: ConvertFrom-Json takes `, AlwaysOnTop: true` (the quotes a nested `powershell -File` strips),
# which the app can't read; Windows' own JSON parser refuses it, as the app does.
function Assert-Seed([string]$Json, [string]$Settings, [string]$Script) {
  $null = [Windows.Data.Json.JsonObject, Windows.Data.Json, ContentType = WindowsRuntime]
  try { $null = [Windows.Data.Json.JsonObject]::Parse($Json) }
  catch {
    throw ("${Script}: -Settings doesn't make settings the app can read, so nothing was run (it would start on its " +
           "defaults instead). The text: $Settings`n" +
           "Called through ``powershell -File``, a value's quotes are lost: call $Script directly in PowerShell.")
  }
}
