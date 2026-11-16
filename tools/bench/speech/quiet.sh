#!/usr/bin/env bash
# Whether the PC is quiet enough for timings (#346): no Godot, no GPT-SoVITS or other Python work on the graphics card,
# no game, and the card under 10% busy. Prints what is in the way, or "quiet"; exits 0 only when quiet.
busy=$(powershell -NoProfile -Command "Get-CimInstance Win32_Process | ? { \$_.Name -notmatch 'powershell|wallpaper|bash|cmd|conhost|dotnet' } | ? { \$_.Name -match 'Godot|BesideTheCabinet|InsertCoin' -or \$_.CommandLine -match 'api_v2|GPT-SoVITS|tts-server|steamapps|VALORANT|League of Legends' } | % { \$_.Name }" | tr -d '\r' | sort -u | tr '\n' ' ')
util=$(nvidia-smi --query-gpu=utilization.gpu --format=csv,noheader,nounits | tr -d ' \r')
if [ -n "$busy" ] || [ "${util:-100}" -ge 10 ]; then
  echo "busy: ${busy}card ${util}%"
  exit 1
fi
echo "quiet: card ${util}%"
