#!/usr/bin/env bash
# How long each model takes to hear turns of about 2, 6 and 15 seconds (#346): 9 clean clips of each length across
# the languages, 5 times each. Run only when the PC is quiet: no game, no other test runs, the graphics card idle.
#   SPEECH_TRIAL=<trial folder> bash tools/bench/speech/run_timing.sh <ears.dll> <runs folder> [cpu|cuda [models...]]
# A CUDA run that fell back to the processor (ears.cs's provider_used) stops the script.
set -u
: "${SPEECH_TRIAL:?set SPEECH_TRIAL to the trial folder}"
[ $# -ge 2 ] || { sed -n 4p "$0" >&2; exit 2; }
EARS=$(cygpath -m "$(realpath -m "${1:?ears.dll}")")
RUNS=$(cygpath -m "$(realpath -m "${2:?runs folder}")")
PROVIDER=${3:-cpu}
shift $(( $# < 3 ? $# : 3 ))
MODELS=${*:-sense-voice qwen3-0.6b nemotron-560 nemotron-160 fun-asr-nano qwen3-1.7b}
cd "$(dirname "$0")/../../.."
mkdir -p "$RUNS"
for m in $MODELS; do
  dotnet "$EARS" --model "$m" --provider "$PROVIDER" --accuracy 0 --rooms clean --timing 9 \
    --out "$RUNS/$m-$PROVIDER.jsonl" > "$RUNS/$m-$PROVIDER.log" 2>&1
  used=$(head -1 "$RUNS/$m-$PROVIDER.jsonl" | sed -n 's/.*"provider_used":"\([^"]*\)".*/\1/p')
  echo "done $m $PROVIDER (ran on: ${used:-unknown})"
  if [ "$PROVIDER" = cuda ] && [ "$used" != cuda ]; then
    echo "$m did not run on the card: stopping" >&2
    exit 1
  fi
done
