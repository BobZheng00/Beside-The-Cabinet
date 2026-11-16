#!/usr/bin/env bash
# Catching their name (#346): each way of catching it on the test clips that hold a name (caught when said first, and
# not when it comes later), then on the other speech make_other_speech.py made (false starts). The streaming model
# runs on the first 20 minutes only: it takes about half the processor all the time.
#   bash tools/bench/speech/run_names.sh <ears.dll> <runs folder> <other speech folder>
set -u
EARS=${1:-E:/Tools/speech-trial/bin-ears3/ears.dll}
RUNS=${2:-E:/Tools/speech-trial/runs-names}
OTHER=${3:-E:/Tools/speech-trial/other}
mkdir -p "$RUNS" "$OTHER/near20"
for i in $(seq -w 0 19); do cp -n "$OTHER/near/stream-$i.wav" "$OTHER/near20/" 2>/dev/null; done
run() { dotnet "$EARS" --mode names --model "$1" --only-names 1 --out "$RUNS/$2.jsonl" "${@:3}" > "$RUNS/$2.log" 2>&1; echo "done $2: $(grep -c false_start "$RUNS/$2.jsonl") false starts; $(tail -1 "$RUNS/$2.jsonl")"; }
run sense-voice sense-voice-near --streams "$OTHER/near"
run sense-voice sense-voice-far --rooms clean --streams "$OTHER/far"
run qwen3-0.6b qwen3-0.6b-near --streams "$OTHER/near"
run nemotron-560 nemotron-560-near20 --streams "$OTHER/near20"
run nemotron-160 nemotron-160-near20 --rooms clean --streams "$OTHER/near20"
echo ALL DONE
