#!/usr/bin/env bash
# Every model on every test clip, on the processor (#346): accuracy, then the names with hints. One at a time, so the
# PC keeps its memory. Timings are taken apart, when the PC is quiet (see the note).
#   bash tools/bench/speech/run_cpu.sh <ears.dll> <runs folder>
set -u
EARS=${1:-E:/Tools/speech-trial/bin-ears2/ears.dll}
RUNS=${2:-E:/Tools/speech-trial/runs}
HINTS="Shino,诗乃,シノ,Hana,ハナ,Fumiriya,フミリヤ,Alex,林晓,健太"
mkdir -p "$RUNS"
run() { dotnet "$EARS" --model "$1" --out "$RUNS/$2.jsonl" "${@:3}" > "$RUNS/$2.log" 2>&1; echo "done $2"; }
run sense-voice sense-voice
run qwen3-0.6b qwen3-0.6b
run nemotron-560 nemotron-560
run fun-asr-nano fun-asr-nano
run qwen3-1.7b qwen3-1.7b
run qwen3-0.6b qwen3-0.6b+hints --hints "$HINTS" --only-names 1
run fun-asr-nano fun-asr-nano+hints --hints "$HINTS" --only-names 1
run qwen3-1.7b qwen3-1.7b+hints --hints "$HINTS" --only-names 1
run nemotron-160 nemotron-160
echo ALL DONE
