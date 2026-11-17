#!/usr/bin/env bash
# Catching their name (#346): each way of catching it on the test clips that hold a name (caught when said first, and
# not when it comes later), then on the other speech make_other_speech.py made (false starts). The streaming model
# runs on the first 20 minutes only: it takes about half the processor all the time. The keyword spotter (kws-*) runs
# on one thread, as it would listening all day.
#   bash tools/bench/speech/run_names.sh <ears.dll> <runs folder> <other speech folder> [which...]
set -u
EARS=${1:-E:/Tools/speech-trial/bin-ears3/ears.dll}
RUNS=${2:-E:/Tools/speech-trial/runs-names}
OTHER=${3:-E:/Tools/speech-trial/other}
shift 3 2>/dev/null
WHICH=${@:-sense-voice-near sense-voice-far qwen3-0.6b-near qwen3-0.6b+hints-near nemotron-560-near20 nemotron-160-near20}
HERE=$(cygpath -m "$(cd "$(dirname "$0")" && pwd)")
NAMES="Shino,诗乃,シノ,Hana,ハナ,花,Fumiriya,フミリヤ"
mkdir -p "$RUNS" "$OTHER/near20"
for i in $(seq -w 0 19); do cp -n "$OTHER/near/stream-$i.wav" "$OTHER/near20/" 2>/dev/null; done
run() { dotnet "$EARS" --mode names --model "$1" --only-names 1 --out "$RUNS/$2.jsonl" "${@:3}" > "$RUNS/$2.log" 2>&1; echo "done $2: $(grep -c false_start "$RUNS/$2.jsonl") false starts; $(tail -1 "$RUNS/$2.jsonl")"; }
for w in $WHICH; do
  case $w in
    sense-voice-near) run sense-voice $w --streams "$OTHER/near" ;;
    sense-voice-far) run sense-voice $w --rooms clean --streams "$OTHER/far" ;;
    qwen3-0.6b-near) run qwen3-0.6b $w --streams "$OTHER/near" ;;
    qwen3-0.6b+hints-near) run qwen3-0.6b $w --hints "$NAMES" --streams "$OTHER/near" ;;
    nemotron-560-near20) run nemotron-560 $w --streams "$OTHER/near20" ;;
    nemotron-160-near20) run nemotron-160 $w --rooms clean --streams "$OTHER/near20" ;;
    kws-near) run kws $w --threads 1 --keywords "$HERE/keywords.txt" --streams "$OTHER/near" ;;
    kws-far) run kws $w --threads 1 --keywords "$HERE/keywords.txt" --rooms far --streams "$OTHER/far" ;;
    kws-0.15-near) run kws $w --threads 1 --keywords "$HERE/keywords.txt" --kws-threshold 0.15 --streams "$OTHER/near" ;;
    kws-more-near) run kws $w --threads 1 --keywords "$HERE/keywords-more.txt" --streams "$OTHER/near" ;;
    kws-more-far) run kws $w --threads 1 --keywords "$HERE/keywords-more.txt" --rooms far --streams "$OTHER/far" ;;
  esac
done
echo ALL DONE
