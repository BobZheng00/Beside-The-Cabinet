#!/usr/bin/env bash
# How long each model takes to hear turns of about 2, 6 and 15 seconds (#346): 9 clean clips of each length across
# the languages, 5 times each. Run only when the PC is quiet: no game, no other test runs, the graphics card idle.
#   bash tools/bench/speech/run_timing.sh <ears.dll> <runs folder> [cpu|cuda] [models...]
set -u
EARS=${1:-E:/Tools/speech-trial/bin-ears3/ears.dll}
RUNS=${2:-E:/Tools/speech-trial/runs-timing}
PROVIDER=${3:-cpu}
shift 3 2>/dev/null
MODELS=${@:-sense-voice qwen3-0.6b nemotron-560 nemotron-160 fun-asr-nano qwen3-1.7b}
mkdir -p "$RUNS"
for m in $MODELS; do
  dotnet "$EARS" --model "$m" --provider "$PROVIDER" --accuracy 0 --rooms clean --timing 9 \
    --out "$RUNS/$m-$PROVIDER.jsonl" > "$RUNS/$m-$PROVIDER.log" 2>&1
  echo "done $m $PROVIDER"
done
