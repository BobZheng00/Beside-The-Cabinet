# Whether a picture is one flat colour: nothing drawn in it. A grid of samples, 24 by 24. Dot-sourced by shoot.ps1 (a
# shot waits while its window isn't drawn yet) and smoke.ps1 (a shot that stayed flat fails its scenario).
function Test-Flat($bmp) {
  $first = $null
  $stepX = [Math]::Max(1, [int]($bmp.Width / 24)); $stepY = [Math]::Max(1, [int]($bmp.Height / 24))
  for ($x = 0; $x -lt $bmp.Width; $x += $stepX) {
    for ($y = 0; $y -lt $bmp.Height; $y += $stepY) {
      $c = $bmp.GetPixel($x, $y).ToArgb()
      if ($null -eq $first) { $first = $c } elseif ($c -ne $first) { return $false }
    }
  }
  return $true
}
