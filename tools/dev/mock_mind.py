"""A stand-in mind for test runs: an OpenAI-compatible chat server on this PC that answers every request with a
fixed line, streamed or not, and logs each request as a line of JSON. No key, no network, nothing billed.

    python tools/dev/mock_mind.py 8797 --log <folder>/mind.jsonl --reply "Hi! It's good to see you."

Point a test profile at it with these settings (shoot.ps1 -Settings, or a smoke scenario with "mind": true):

    "ChatProtocol": "OpenAi", "ChatBaseUrl": "http://127.0.0.1:8797/v1", "ChatModel": "mock"

A server on this PC needs no key, so the profile needs none either. A request that asks for JSON (response_format)
gets "{}"; one that offers tools still gets the plain line. --think waits that many seconds before answering, as a
slow model would. It stops when killed, or after --quit-after seconds.
"""

import argparse
import json
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ARGS = None
LOCK = threading.Lock()
COUNT = 0


def log(entry):
    if not ARGS.log:
        return
    with LOCK:
        with open(ARGS.log, "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")


def last_user_text(messages):
    for m in reversed(messages or []):
        if m.get("role") != "user":
            continue
        content = m.get("content")
        if isinstance(content, str):
            return content
        if isinstance(content, list):
            return " ".join(p.get("text", "") for p in content if isinstance(p, dict))
    return ""


def pieces(text):
    words = text.split(" ")
    return [w + (" " if i < len(words) - 1 else "") for i, w in enumerate(words)]


class Handler(BaseHTTPRequestHandler):
    server_version = "MockMind/1"

    def log_message(self, fmt, *args):  # quiet: the jsonl is the record
        pass

    def send_json(self, status, body):
        data = json.dumps(body).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def read_body(self):
        if "chunked" in (self.headers.get("Transfer-Encoding") or "").lower():
            body = b""
            while True:
                size = int(self.rfile.readline().split(b";")[0].strip() or b"0", 16)
                if size == 0:
                    while self.rfile.readline() not in (b"\r\n", b"\n", b""):
                        pass  # trailers
                    return body
                body += self.rfile.read(size)
                self.rfile.readline()  # the CRLF after each chunk
        length = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(length) if length else b""

    def do_GET(self):
        if self.path.rstrip("/").endswith("/models"):
            self.send_json(200, {"object": "list", "data": [{"id": "mock", "object": "model", "owned_by": "mock"}]})
        else:
            self.send_json(404, {"error": {"message": "not found", "type": "invalid_request_error"}})

    def do_POST(self):
        global COUNT
        raw = self.read_body()
        try:
            body = json.loads(raw or b"{}")
        except ValueError:
            body = {}
        if not self.path.rstrip("/").endswith("/chat/completions"):
            log({"at": time.time(), "path": self.path, "answered": 404})
            self.send_json(404, {"error": {"message": "only chat completions here", "type": "invalid_request_error"}})
            return

        with LOCK:
            COUNT += 1
            n = COUNT
        fmt = (body.get("response_format") or {}).get("type")
        reply = "{}" if fmt in ("json_object", "json_schema") else ARGS.reply
        stream = bool(body.get("stream"))
        log({
            "at": time.time(), "n": n, "path": self.path, "model": body.get("model"), "stream": stream,
            "messages": len(body.get("messages") or []), "tools": len(body.get("tools") or []),
            "response_format": fmt, "user": last_user_text(body.get("messages"))[:200], "reply": reply,
        })
        if ARGS.think > 0:
            time.sleep(ARGS.think)

        created = int(time.time())
        rid = f"chatcmpl-mock-{n}"
        model = body.get("model") or "mock"
        usage = {"prompt_tokens": 100, "completion_tokens": len(reply.split()), "total_tokens": 100 + len(reply.split())}

        if not stream:
            self.send_json(200, {
                "id": rid, "object": "chat.completion", "created": created, "model": model,
                "choices": [{"index": 0, "message": {"role": "assistant", "content": reply}, "finish_reason": "stop"}],
                "usage": usage,
            })
            return

        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "close")
        self.end_headers()

        def event(choices, extra=None):
            chunk = {"id": rid, "object": "chat.completion.chunk", "created": created, "model": model, "choices": choices}
            if extra:
                chunk.update(extra)
            self.wfile.write(b"data: " + json.dumps(chunk).encode("utf-8") + b"\n\n")
            self.wfile.flush()

        try:
            event([{"index": 0, "delta": {"role": "assistant", "content": ""}, "finish_reason": None}])
            for piece in pieces(reply):
                event([{"index": 0, "delta": {"content": piece}, "finish_reason": None}])
                time.sleep(0.02)
            event([{"index": 0, "delta": {}, "finish_reason": "stop"}])
            if (body.get("stream_options") or {}).get("include_usage"):
                event([], {"usage": usage})
            self.wfile.write(b"data: [DONE]\n\n")
            self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            pass  # the app hung up (cut off, or closing): nothing to do


def main():
    global ARGS
    p = argparse.ArgumentParser(description="An OpenAI-compatible stand-in mind for test runs.")
    p.add_argument("port", type=int)
    p.add_argument("--reply", default="Hi! It's good to see you. How did today go?")
    p.add_argument("--think", type=float, default=0.0, help="seconds to wait before answering")
    p.add_argument("--log", default="", help="a .jsonl file each request is written to")
    p.add_argument("--quit-after", type=float, default=0.0, help="stop after this many seconds (0: never)")
    ARGS = p.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", ARGS.port), Handler)
    server.daemon_threads = True
    if ARGS.quit_after > 0:
        threading.Timer(ARGS.quit_after, server.shutdown).start()
    print(f"mock mind on http://127.0.0.1:{ARGS.port}/v1", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
