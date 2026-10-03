"""INSERT COIN's voice server for Qwen3-TTS, run by the cabinet with the Python of the player's Qwen3-TTS (one with
faster-qwen3-tts installed). It listens on this PC only and makes one sentence at a time:

    GET  /health   {"model": the model loaded, or null}
    POST /load     {"model": "Qwen/Qwen3-TTS-12Hz-0.6B-Base"}: loads it (unless it is loaded) and warms it up
    POST /tts      {"text", "language" (ja, zh, en…), "ref_audio" (a path), "ref_text", "chunk_size"}: a 16-bit mono
                   WAV that streams back while it is being made; closing the connection stops it

Errors come back as {"message": …, "Exception": …}, like GPT-SoVITS's. Models are looked for in HF_HOME first and
downloaded only when missing.

    python qwen_server.py --port 9881
"""
import argparse
import gc
import json
import struct
import sys
import threading
import time
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import numpy as np

LANGUAGES = {"zh": "Chinese", "en": "English", "ja": "Japanese", "ko": "Korean", "de": "German", "fr": "French",
             "ru": "Russian", "pt": "Portuguese", "es": "Spanish", "it": "Italian"}

gpu = threading.Lock()          # one sentence at a time: the graphics card makes them one after another anyway
model, model_name = None, None


def load(name):
    global model, model_name
    if name == model_name:
        return
    from faster_qwen3_tts import FasterQwen3TTS
    import torch

    model, model_name = None, None
    gc.collect()                # the old model's graphics memory back first: both sizes at once may not fit
    torch.cuda.empty_cache()
    start = time.perf_counter()
    try:
        loaded = FasterQwen3TTS.from_pretrained(name, local_files_only=True)
    except Exception:
        print(f"{name} is not here yet: downloading it", flush=True)
        loaded = FasterQwen3TTS.from_pretrained(name)
    loaded.warmup()
    model, model_name = loaded, name
    print(f"loaded {name} in {time.perf_counter() - start:.1f} s", flush=True)


def wav_header(rate):
    """A WAV header whose lengths are unknown: the samples run to the end of the stream."""
    fmt = struct.pack("<HHIIHH", 1, 1, rate, rate * 2, 2, 16)
    return b"RIFF" + struct.pack("<I", 0xFFFFFFFF) + b"WAVE" + b"fmt " + struct.pack("<I", len(fmt)) + fmt \
        + b"data" + struct.pack("<I", 0xFFFFFFFF)


def pcm(audio):
    return (np.clip(np.asarray(audio, dtype=np.float32).reshape(-1), -1, 1) * 32767).astype("<i2").tobytes()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def answer(self, status, body):
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def fail(self, message, error):
        print(f"{message}: {error}", flush=True)
        traceback.print_exc()
        self.answer(400 if isinstance(error, ValueError) else 500, {"message": message, "Exception": str(error)})

    def body(self):
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            data = b""
            while (size := int(self.rfile.readline().split(b";")[0], 16)) > 0:
                data += self.rfile.read(size)
                self.rfile.readline()
            self.rfile.readline()
        else:
            data = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        return json.loads(data or b"{}")

    def do_GET(self):
        if self.path == "/health":
            self.answer(200, {"model": model_name})
        else:
            self.answer(404, {"message": "no such page"})

    def do_POST(self):
        try:
            request = self.body()
        except Exception as e:
            return self.fail("could not read the request", e)
        if self.path == "/load":
            try:
                with gpu:
                    load(request["model"])
            except Exception as e:
                return self.fail(f"could not load {request.get('model')}", e)
            return self.answer(200, {"model": model_name})
        if self.path == "/tts":
            return self.speak(request)
        self.answer(404, {"message": "no such page"})

    def speak(self, request):
        with gpu:
            if model is None:
                return self.answer(400, {"message": "no model loaded"})
            text = request.get("text", "").strip()
            if not text:
                return self.answer(400, {"message": "nothing to say"})
            start = time.perf_counter()
            pieces = model.generate_voice_clone_streaming(
                text=text,
                language=LANGUAGES.get(request.get("language", ""), "Auto"),
                ref_audio=request["ref_audio"],
                ref_text=request.get("ref_text", ""),
                chunk_size=int(request.get("chunk_size", 4)),
                max_new_tokens=min(2048, int(12 * (5 + 0.4 * len(text)))),     # a sentence never runs on for minutes
            )
            try:
                audio, rate, _ = next(pieces)
            except StopIteration:
                return self.answer(500, {"message": "it made no sound"})
            except Exception as e:
                return self.fail("could not say it", e)
            first = time.perf_counter() - start
            seconds = 0.0
            try:
                self.send_response(200)
                self.send_header("Content-Type", "audio/wav")
                self.end_headers()
                self.wfile.write(wav_header(rate))
                while True:
                    self.wfile.write(pcm(audio))
                    self.wfile.flush()
                    seconds += np.asarray(audio).size / rate
                    audio, rate, _ = next(pieces)
            except StopIteration:
                print(f"said {len(text)} characters: first audio after {first:.2f} s, {seconds:.1f} s of speech "
                      f"made in {time.perf_counter() - start:.1f} s", flush=True)
            except OSError:
                print(f"cut off after {seconds:.1f} s of speech", flush=True)        # the cabinet stopped listening
            except Exception as e:
                print(f"stopped after {seconds:.1f} s of speech: {e}", flush=True)  # too late for an error answer: the sound just ends
                traceback.print_exc()
            finally:
                pieces.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=9881)
    args = parser.parse_args()
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    server.daemon_threads = True
    print(f"Qwen3-TTS server on http://{args.host}:{args.port} (Python {sys.version.split()[0]})", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
