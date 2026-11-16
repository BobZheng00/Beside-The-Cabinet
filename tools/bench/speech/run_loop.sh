#!/usr/bin/env bash
# The whole loop and the timings with her voice loaded beside the ears, as the player has it (#346). Starts the mock
# mind and a GPT-SoVITS server of its own, only when quiet.sh says the PC is quiet and no other voice server runs, and
# stops both at the end.
#   bash tools/bench/speech/run_loop.sh <GPT-SoVITS folder> <voice folder> [out folder]
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "$HERE/../../.." && pwd)
GSV=${1:?GPT-SoVITS folder}
VOICE=${2:?voice folder}
OUT=${3:-E:/Tools/speech-trial/runs-loop}
EARS=${EARS:-E:/Tools/speech-trial/bin-ears3/ears.dll}
bash "$HERE/quiet.sh" || exit 3
mkdir -p "$OUT"
python "$REPO/tools/dev/mock_mind.py" 8797 --log "$OUT/mind.jsonl" --quit-after 7200 \
  --reply "うん、わかった。九時に声をかけるね。それまで少し休んでて。" > "$OUT/mind.log" 2>&1 &
MIND=$!
(cd "$GSV" && PYTHONIOENCODING=utf-8 PYTHONUNBUFFERED=1 exec runtime/python.exe api_v2.py -a 127.0.0.1 -p 9893) > "$OUT/gsv.log" 2>&1 &
VOICE_SERVER=$!
trap 'kill $MIND $VOICE_SERVER 2>/dev/null; taskkill //F //T //PID $(powershell -NoProfile -Command "(Get-CimInstance Win32_Process | ? { \$_.CommandLine -match \"api_v2.py -a 127.0.0.1 -p 9893\" }).ProcessId") >/dev/null 2>&1' EXIT
for i in $(seq 1 180); do curl -s -o /dev/null http://127.0.0.1:9893/docs && break; sleep 1; done
echo "voice server up after ${i} s; card: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
for m in sense-voice qwen3-0.6b; do
  dotnet "$EARS" --mode loop --model "$m" --rooms clean,far --repeat 3 --voice "$VOICE" --gsv http://127.0.0.1:9893/ \
    --mind http://127.0.0.1:8797/v1/ --out "$OUT/loop-$m.jsonl" > "$OUT/loop-$m.log" 2>&1
  echo "loop done $m"
done
EARS="$EARS" bash "$HERE/run_timing.sh" "$EARS" "${OUT}/timing-beside-voice" cpu
echo "card with the voice loaded: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
