"""INSERT COIN's voice trainer: trains a voice's own GPT-SoVITS model on its clips, the way GPT-SoVITS's web UI does
(webui.py open1abc, open1Ba, open1Bb): prepare the text, the HuBERT features and 32 kHz audio, the voiceprints and
the semantic tokens, then train SoVITS and then GPT from the v2ProPlus base. Run with GPT-SoVITS's own Python, in
its folder:

    runtime\\python.exe -s train_voice.py --list clips.list --work logs\\insert-coin-hana --out C:\\voices\\hana.voice
                        --name hana --batch 4 --sovits-epochs 8 --gpt-epochs 15

Besides GPT-SoVITS's own output it prints lines for the cabinet: "@stage what it is doing", "@progress 0.42" (of the
whole), and at the end "@done <gpt file> <sovits file>" (copied into --out) or "@failed why". The working folder is
deleted when it is done.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

import yaml

VERSION = "v2ProPlus"
S2G = "GPT_SoVITS/pretrained_models/v2Pro/s2Gv2ProPlus.pth"
S1 = "GPT_SoVITS/pretrained_models/s1v3.ckpt"
# share of the whole each step takes (Hana, 306 s of speech: 12, 15, 10, 7, 207 and 74 s)
STEPS = [("Reading the words", 0.04), ("Listening to the clips", 0.05), ("Taking voiceprints", 0.03),
         ("Turning speech into tokens", 0.02), ("Training her tone (SoVITS)", 0.64), ("Training her speech (GPT)", 0.22)]


def say(kind, text=""):
    print(f"@{kind} {text}".rstrip(), flush=True)


def paths():
    """What GPT-SoVITS's web UI writes on every start (webui.py): its folders on the path of its own Python, which
    the training scripts import from."""
    here = os.getcwd().replace("\\", "/")
    site = Path(sys.executable).parent / "Lib" / "site-packages"
    (site / "users.pth").write_text(f"{here}\n{here}/GPT_SoVITS/BigVGAN\n{here}/tools\n{here}/tools/asr\n{here}/GPT_SoVITS\n{here}/tools/uvr5",
                                    encoding="utf-8")


def run(index, script, args=(), epochs=None, **env):
    """Runs one step, passing its output on; with epochs, reports progress through them as it goes."""
    name, share = STEPS[index]
    before = sum(s for _, s in STEPS[:index])
    say("stage", name)
    say("progress", f"{before:.3f}")
    environment = dict(os.environ, version=VERSION, is_half="True", i_part="0", all_parts="1", _CUDA_VISIBLE_DEVICES="0",
                       PYTHONIOENCODING="utf-8", **{k: str(v) for k, v in env.items()})
    process = subprocess.Popen([sys.executable, "-s", script, *args], env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    pending = ""
    for raw in iter(lambda: process.stdout.read1(4096), b""):
        pending += raw.decode("utf-8", "replace")
        *lines, pending = re.split(r"[\r\n]", pending)
        for line in lines:
            if not line.strip():
                continue
            print(line, flush=True)
            if epochs:
                done = None
                if m := re.search(r"====> Epoch: (\d+)", line):                  # SoVITS: an epoch finished
                    done = int(m.group(1)) / epochs
                elif m := re.search(r"Epoch (\d+):\s+(\d+)%", line):              # GPT: Lightning's progress bar
                    done = (int(m.group(1)) + int(m.group(2)) / 100) / epochs
                if done is not None:
                    say("progress", f"{before + share * min(done, 1):.3f}")
    if process.wait() != 0:
        raise RuntimeError(f"{name} failed (GPT-SoVITS's own messages are above)")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--list", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--batch", type=int, default=4)
    parser.add_argument("--sovits-epochs", type=int, default=8)
    parser.add_argument("--gpt-epochs", type=int, default=15)
    args = parser.parse_args()
    work = Path(args.work).resolve()
    try:
        paths()
        shutil.rmtree(work, ignore_errors=True)
        work.mkdir(parents=True)
        common = dict(inp_text=Path(args.list).resolve(), inp_wav_dir="", exp_name=args.name, opt_dir=work)

        run(0, "GPT_SoVITS/prepare_datasets/1-get-text.py", **common,
            bert_pretrained_dir="GPT_SoVITS/pretrained_models/chinese-roberta-wwm-ext-large")
        os.replace(work / "2-name2text-0.txt", work / "2-name2text.txt")
        features = dict(common, cnhubert_base_dir="GPT_SoVITS/pretrained_models/chinese-hubert-base",
                        sv_path="GPT_SoVITS/pretrained_models/sv/pretrained_eres2netv2w24s4ep4.ckpt")
        run(1, "GPT_SoVITS/prepare_datasets/2-get-hubert-wav32k.py", **features)
        run(2, "GPT_SoVITS/prepare_datasets/2-get-sv.py", **features)
        run(3, "GPT_SoVITS/prepare_datasets/3-get-semantic.py", **common, pretrained_s2G=S2G,
            s2config_path=f"GPT_SoVITS/configs/s2{VERSION}.json")
        semantic = (work / "6-name2semantic-0.tsv").read_text(encoding="utf-8").strip("\n")
        (work / "6-name2semantic.tsv").write_text("item_name\tsemantic_audio\n" + semantic + "\n", encoding="utf-8")
        (work / "6-name2semantic-0.tsv").unlink()

        weights = work / "weights"
        weights.mkdir()
        s2 = json.loads(Path(f"GPT_SoVITS/configs/s2{VERSION}.json").read_text(encoding="utf-8"))
        s2["train"].update(batch_size=args.batch, epochs=args.sovits_epochs, text_low_lr_rate=0.4, pretrained_s2G=S2G,
                           pretrained_s2D=S2G.replace("s2G", "s2D"), if_save_latest=True, if_save_every_weights=True,
                           save_every_epoch=args.sovits_epochs, gpu_numbers="0", grad_ckpt=False, lora_rank="32")
        s2["model"]["version"] = VERSION
        s2["data"]["exp_dir"] = s2["s2_ckpt_dir"] = str(work)
        s2.update(save_weight_dir=str(weights), name=args.name, version=VERSION)
        (work / "s2.json").write_text(json.dumps(s2), encoding="utf-8")
        (work / f"logs_s2_{VERSION}").mkdir()
        run(4, "GPT_SoVITS/s2_train.py", ["--config", str(work / "s2.json")], epochs=args.sovits_epochs)

        s1 = yaml.safe_load(Path("GPT_SoVITS/configs/s1longer-v2.yaml").read_text(encoding="utf-8"))
        s1["train"].update(batch_size=args.batch, epochs=args.gpt_epochs, save_every_n_epoch=args.gpt_epochs,
                           if_save_every_weights=True, if_save_latest=True, if_dpo=False, half_weights_save_dir=str(weights),
                           exp_name=args.name)
        s1.update(pretrained_s1=S1, train_semantic_path=str(work / "6-name2semantic.tsv"),
                  train_phoneme_path=str(work / "2-name2text.txt"), output_dir=str(work / f"logs_s1_{VERSION}"))
        (work / "s1.yaml").write_text(yaml.dump(s1, default_flow_style=False), encoding="utf-8")
        (work / "logs_s1").mkdir()
        run(5, "GPT_SoVITS/s1_train.py", ["--config_file", str(work / "s1.yaml")], epochs=args.gpt_epochs, hz="25hz")

        gpt = max(weights.glob("*.ckpt"), key=os.path.getmtime)
        sovits = max(weights.glob("*.pth"), key=os.path.getmtime)
        out = Path(args.out)
        for f in (gpt, sovits):
            shutil.copy2(f, out / f.name)
        say("progress", "1")
        say("done", f"{gpt.name} {sovits.name}")
    except Exception as e:
        say("failed", str(e))
        sys.exit(1)
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    main()
