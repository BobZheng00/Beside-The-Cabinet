#!/usr/bin/env bash
# The ears on the graphics card beside her voice (#346, round 2): GPT-SoVITS loaded with the voice and warmed with one
# line, then the timings of Qwen3-ASR 0.6B and SenseVoice on the card, then the whole loop with Qwen3-ASR 0.6B on the
# card. Only when quiet.sh says the PC is quiet and no other voice server runs; stops what it started.
#   bash tools/bench/speech/run_gpu.sh <GPT-SoVITS folder> <voice folder> <ears.dll on the CUDA runtime> [out folder]
# The CUDA runtime: an ears.cs build whose runtimes/win-x64/native holds k2-fsa's CUDA build of sherpa-onnx (its lib
# folder), with CUDA 12 and cuDNN 9 on PATH (CUDA_BIN, CUDNN_BIN).
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "$HERE/../../.." && pwd)
GSV=${1:?GPT-SoVITS folder}
VOICE=${2:?voice folder}
EARS=${3:?ears.dll built on the CUDA runtime}
OUT=${4:-E:/Tools/speech-trial/runs-gpu}
CUDA_BIN=${CUDA_BIN:-/c/Program Files/NVIDIA GPU Computing Toolkit/CUDA/v12.5/bin}
CUDNN_BIN=${CUDNN_BIN:-/c/Program Files/NVIDIA/CUDNN/v9.5/bin/12.6}
export PATH="$CUDA_BIN:$CUDNN_BIN:$PATH"
[ "${SKIP_QUIET:-0}" = 1 ] || bash "$HERE/quiet.sh" || exit 3   # the caller may have just checked
mkdir -p "$OUT"
python "$REPO/tools/dev/mock_mind.py" 8797 --log "$OUT/mind.jsonl" --quit-after 7200 \
  --reply "うん、わかった。九時に声をかけるね。それまで少し休んでて。" > "$OUT/mind.log" 2>&1 &
MIND=$!
echo "card before the voice: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
(cd "$GSV" && PYTHONIOENCODING=utf-8 PYTHONUNBUFFERED=1 exec runtime/python.exe api_v2.py -a 127.0.0.1 -p 9893) > "$OUT/gsv.log" 2>&1 &
VOICE_SERVER=$!
trap 'kill $MIND $VOICE_SERVER 2>/dev/null; taskkill //F //T //PID $(powershell -NoProfile -Command "(Get-CimInstance Win32_Process | ? { \$_.CommandLine -match \"api_v2.py -a 127.0.0.1 -p 9893\" }).ProcessId") >/dev/null 2>&1' EXIT
for i in $(seq 1 180); do curl -s -o /dev/null http://127.0.0.1:9893/docs && break; sleep 1; done
# the voice's own weights loaded and one line said, as the player has it, before anything is timed
GPT=$(python -c "import json,sys; print(json.load(open(sys.argv[1], encoding='utf-8'))['gpt'])" "$VOICE/voice.json")
SOVITS=$(python -c "import json,sys; print(json.load(open(sys.argv[1], encoding='utf-8'))['sovits'])" "$VOICE/voice.json")
VOICE_WIN=$(cygpath -m "$VOICE")
curl -s -o /dev/null "http://127.0.0.1:9893/set_gpt_weights?weights_path=$VOICE_WIN/$GPT"
curl -s -o /dev/null "http://127.0.0.1:9893/set_sovits_weights?weights_path=$VOICE_WIN/$SOVITS"
CLIP=$(cygpath -m "$(ls "$VOICE"/clips/*.wav | head -1)")
curl -s -o /dev/null -X POST http://127.0.0.1:9893/tts -H "Content-Type: application/json" \
  -d "{\"text\":\"こんにちは。\",\"text_lang\":\"ja\",\"ref_audio_path\":\"$CLIP\",\"prompt_lang\":\"ja\",\"prompt_text\":\"\"}"
echo "card with the voice loaded: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
bash "$HERE/run_timing.sh" "$EARS" "$OUT/timing" cuda qwen3-0.6b sense-voice
echo "timings done"
dotnet "$EARS" --mode loop --model qwen3-0.6b --provider cuda --rooms clean,far --repeat 3 --voice "$VOICE" \
  --gsv http://127.0.0.1:9893/ --mind http://127.0.0.1:8797/v1/ --out "$OUT/loop-qwen3-0.6b-cuda.jsonl" > "$OUT/loop.log" 2>&1
echo "loop done; card with the voice loaded: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
