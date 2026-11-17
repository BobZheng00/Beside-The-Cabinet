#!/usr/bin/env bash
# The ears on the graphics card beside her voice (#346, round 2): GPT-SoVITS (port 9893, never its own 9880) loaded
# with the voice's weights, then SenseVoice and Qwen3-ASR 0.6B timed on the processor (the app's own build) and on the
# card in one run, so they compare like for like, then (LOOP=1, the default) the whole loop with Qwen3-ASR on the card.
# Only when quiet.sh says the PC is quiet (SKIP_QUIET=1 when the caller has just checked); stops what it started.
#   SPEECH_TRIAL=<trial folder> CUDA_BIN=<CUDA 12 bin> CUDNN_BIN=<cuDNN 9 bin for CUDA 12> \
#     bash tools/bench/speech/run_gpu.sh <ears.dll, processor> <ears.dll, CUDA runtime> <GPT-SoVITS folder> <voice folder> <out folder>
# The CUDA runtime: an ears.cs build whose runtimes/win-x64/native holds the lib folder of k2-fsa's CUDA build of
# sherpa-onnx 1.13.8, and a cudart64_12.dll of CUDA 12.8 or later (its ONNX Runtime calls functions 12.5 lacks).
# CUDA_BIN and CUDNN_BIN give the rest of CUDA 12 (cuBLAS) and cuDNN 9; there are no defaults. A CUDA run that falls
# back to the processor stops the timings (run_timing.sh).
set -u
: "${SPEECH_TRIAL:?set SPEECH_TRIAL to the trial folder}"
: "${CUDA_BIN:?set CUDA_BIN to CUDA 12's bin folder}"
: "${CUDNN_BIN:?set CUDNN_BIN to cuDNN 9's bin folder for CUDA 12}"
[ $# -ge 5 ] || { sed -n 7p "$0" >&2; exit 2; }
abs() { cygpath -m "$(realpath -m "$1")"; }
EARS_CPU=$(abs "$1")
EARS=$(abs "$2")
GSV=$(abs "$3")
VOICE=$(abs "$4")
OUT=$(abs "$5")
HERE=$(abs "$(dirname "$0")")
REPO=$(abs "$HERE/../../..")
export PATH="$CUDA_BIN:$CUDNN_BIN:$PATH"
[ "${SKIP_QUIET:-0}" = 1 ] || bash "$HERE/quiet.sh" || exit 3
cd "$REPO"
mkdir -p "$OUT"
python "$REPO/tools/dev/mock_mind.py" 8797 --log "$OUT/mind.jsonl" --quit-after 7200 \
  --reply "うん、わかった。九時に声をかけるね。それまで少し休んでて。" > "$OUT/mind.log" 2>&1 &
MIND=$!
echo "card before the voice: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
(cd "$GSV" && PYTHONIOENCODING=utf-8 PYTHONUNBUFFERED=1 exec runtime/python.exe api_v2.py -a 127.0.0.1 -p 9893) > "$OUT/gsv.log" 2>&1 &
VOICE_SERVER=$!
trap 'kill $MIND $VOICE_SERVER 2>/dev/null; taskkill //F //T //PID $(powershell -NoProfile -Command "(Get-CimInstance Win32_Process | ? { \$_.CommandLine -match \"api_v2.py -a 127.0.0.1 -p 9893\" }).ProcessId") >/dev/null 2>&1' EXIT
for i in $(seq 1 180); do curl -s -o /dev/null http://127.0.0.1:9893/docs && break; sleep 1; done
# the voice's own weights loaded, as the player has it, before anything is timed
GPT=$(python -c "import json,sys; print(json.load(open(sys.argv[1], encoding='utf-8'))['gpt'])" "$VOICE/voice.json")
SOVITS=$(python -c "import json,sys; print(json.load(open(sys.argv[1], encoding='utf-8'))['sovits'])" "$VOICE/voice.json")
curl -s -o /dev/null "http://127.0.0.1:9893/set_gpt_weights?weights_path=$VOICE/$GPT"
curl -s -o /dev/null "http://127.0.0.1:9893/set_sovits_weights?weights_path=$VOICE/$SOVITS"
echo "card with the voice loaded: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
bash "$HERE/run_timing.sh" "$EARS_CPU" "$OUT/timing" cpu sense-voice qwen3-0.6b
bash "$HERE/run_timing.sh" "$EARS" "$OUT/timing" cuda sense-voice qwen3-0.6b || exit 1
echo "timings done"
if [ "${LOOP:-1}" = 1 ]; then
  dotnet "$EARS" --mode loop --model qwen3-0.6b --provider cuda --rooms clean,far --repeat 3 --voice "$VOICE" \
    --gsv http://127.0.0.1:9893/ --mind http://127.0.0.1:8797/v1/ --out "$OUT/loop-qwen3-0.6b-cuda.jsonl" > "$OUT/loop.log" 2>&1
  echo "loop done; card with the voice loaded: $(nvidia-smi --query-gpu=memory.used --format=csv,noheader)"
fi
