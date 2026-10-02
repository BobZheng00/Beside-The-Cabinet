# Lays crops of several screenshots side by side (a contact sheet), e.g. the frames of a gesture.
#
#   tools\dev\sheet.ps1 -In a-0.png,a-1.png,a-2.png -Out wave.png -X 0 -Y 40 -W 300 -H 500 -Zoom 0.6 -Cols 8
#
# -In are file names inside -Dir; X, Y, W, H is the crop taken from each; Cols wraps into rows.
param([string[]]$In, [string]$Out = "sheet.png", [int]$X = 0, [int]$Y = 0, [int]$W = 300, [int]$H = 600, [double]$Zoom = 1, [int]$Cols = 0,
      [string]$Dir = (Join-Path $env:TEMP "insert-coin-shots"))
Add-Type -AssemblyName System.Drawing
if ($Cols -le 0) { $Cols = $In.Count }
$rows = [math]::Ceiling($In.Count / $Cols)
$tw = [int]($W * $Zoom); $th = [int]($H * $Zoom)
$sheet = New-Object System.Drawing.Bitmap ($tw * $Cols), ($th * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
for ($i = 0; $i -lt $In.Count; $i++) {
  $path = Join-Path $Dir $In[$i]
  if (-not (Test-Path $path)) { continue }
  $shot = [System.Drawing.Image]::FromFile($path)
  $g.DrawImage($shot, (New-Object System.Drawing.Rectangle ($tw * ($i % $Cols)), ($th * [math]::Floor($i / $Cols)), $tw, $th),
               (New-Object System.Drawing.Rectangle $X, $Y, $W, $H), [System.Drawing.GraphicsUnit]::Pixel)
  $shot.Dispose()
}
$file = Join-Path $Dir $Out
$sheet.Save($file); $g.Dispose(); $sheet.Dispose()
$file
