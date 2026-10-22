"""A stand-in model for spike #105 (tools/bench/agenda.cs): her agenda set by tags or by the agenda tool, so the flows'
mechanics can be tried on both protocols without a real provider or key.

Speaks OpenAI's chat completions (/v1/chat/completions) and Anthropic's messages (/v1/messages), streamed, with usage.
- With the agenda tool offered, by what the player said: a line, then a call ("vitamins", "gym"/"健身", "friday",
  "牙医", "moved", "off"); "bank" calls with a time the cabinet can't read; "birthday" calls with no words before it.
- After a tool result: "It's on your agenda." for one that went through, "Hm, that didn't take. What time exactly?" else.
- Without it (tags): the same line with a [plan] or [remind] in front.
Every request is logged, with its size, to agenda-mock.jsonl in the folder given (default: here).

    python agenda_mock.py 8795 [log folder]
"""
import json
import re
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

LOG = Path(sys.argv[2] if len(sys.argv) > 2 else Path(__file__).parent) / "agenda-mock.jsonl"


def text_of(content):
    if isinstance(content, str):
        return content
    return " ".join(part.get("text", "") or "" for part in content or [] if isinstance(part, dict))


def tool_result(body, anthropic):
    last = (body.get("messages") or [{}])[-1]
    if anthropic and isinstance(last.get("content"), list):
        results = [p for p in last["content"] if p.get("type") == "tool_result"]
        if results:
            return text_of(results[-1].get("content")) if not isinstance(results[-1].get("content"), str) else results[-1]["content"]
    elif last.get("role") == "tool":
        return text_of(last.get("content"))
    return None


def said(body):
    for message in reversed(body.get("messages") or []):
        if message.get("role") == "user":
            words = text_of(message.get("content"))
            if words.strip():
                return words.lower()
    return ""


def plan(body, anthropic):
    """What to say, and the calls to make."""
    result = tool_result(body, anthropic)
    if result is not None:
        return ("Hm, that didn't take. What time exactly?" if "Not " in result else "It's on your agenda."), []
    line = said(body)
    whole = json.dumps(body, ensure_ascii=False)
    item = (re.findall(r"- #(\d+) ", whole) or ["1"])[0]
    offered = [t.get("name") or (t.get("function") or {}).get("name") for t in body.get("tools") or []]
    agenda = "agenda" in offered
    def add(when, what, minutes=None, repeat=None):
        arguments = {"action": "add", "when": when, "what": what}
        if minutes is not None:
            arguments["minutes_before"] = minutes
        if repeat:
            arguments["repeat"] = repeat
        return ("agenda", arguments)
    table = [
        ("vitamins", "Nine tomorrow, vitamins. Noted.", "[remind: 2026-10-22 09:00 | Take your vitamins]", [add("2026-10-22T09:00", "Take your vitamins", 0)]),
        ("gym", "Mondays at seven, gym, an hour's warning.", "[plan: 2026-10-26 07:00 | Gym | 1 hour before, weekly]", [add("2026-10-26T07:00", "Gym", 60, "weekly")]),
        ("健身", "每周一早上七点，健身房。", "[remind: 2026-10-26 07:00 | 去健身房 | weekly]", [add("2026-10-26T07:00", "去健身房", 0, "weekly")]),
        ("friday", "Friday, half three, dentist.", "[plan: 2026-10-23 15:30 | Dentist | 10 min before]", [add("2026-10-23T15:30", "Dentist", 10)]),
        ("牙医", "下周三下午三点，看牙医。", "[plan: 2026-10-28 15:00 | 看牙医 | 1 hour before]", [add("2026-10-28T15:00", "看牙医", 60)]),
        ("bank", "The bank, Monday morning.", "[remind: Monday 9am | Call the bank]", [add("Monday 9am", "Call the bank", 0)]),
        ("birthday", "", "[plan: 2026-10-24 | Mia's birthday]", [add("2026-10-24", "Mia's birthday")]),
        ("moved", "Monday, same time.", f"[change: #{item} | 2026-10-26 15:30]", [("agenda", {"action": "change", "item": int(item), "when": "2026-10-26T15:30"})]),
        ("is off", "Off it comes.", f"[cancel: #{item}]", [("agenda", {"action": "cancel", "item": int(item)})]),
    ]
    for word, words, tag, calls in table:
        if word in line:
            if agenda:
                return words, calls
            return f"{tag} {words or 'On the agenda.'}", []
    return "Mm, I'm here.", []


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        raw = self.rfile.read(int(self.headers["Content-Length"]))
        body = json.loads(raw)
        anthropic = self.path.endswith("/messages")
        text, calls = plan(body, anthropic)
        with LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps({"path": self.path, "bytes": len(raw), "tools": len(body.get("tools") or []),
                                "said": said(body)[-80:], "reply": text, "calls": calls}, ensure_ascii=False) + "\n")
        if not body.get("stream"):
            self._json({"error": {"message": "only streamed replies here"}}, 400)
            return
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.end_headers()
        time.sleep(0.3)
        prompt = len(raw) // 4
        (self._anthropic if anthropic else self._openai)(body, text, calls, prompt)

    def _send(self, data, event=None):
        self.wfile.write(((f"event: {event}\n" if event else "") + f"data: {json.dumps(data, ensure_ascii=False)}\n\n").encode())
        self.wfile.flush()

    def _openai(self, body, text, calls, prompt):
        def chunk(delta, finish=None):
            return {"id": "x", "object": "chat.completion.chunk", "model": "mock", "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
        for i in range(0, len(text), 5):
            self._send(chunk({"content": text[i:i + 5]}))
            time.sleep(0.03)
        for n, (name, arguments) in enumerate(calls):
            self._send(chunk({"tool_calls": [{"index": n, "id": f"call_{n}", "type": "function", "function": {"name": name, "arguments": ""}}]}))
            raw = json.dumps(arguments, ensure_ascii=False)
            for i in range(0, len(raw), 12):
                self._send(chunk({"tool_calls": [{"index": n, "function": {"arguments": raw[i:i + 12]}}]}))
                time.sleep(0.02)
        self._send(chunk({}, "tool_calls" if calls else "stop"))
        self._send({"id": "x", "object": "chat.completion.chunk", "model": "mock", "choices": [],
                    "usage": {"prompt_tokens": prompt, "completion_tokens": 30, "total_tokens": prompt + 30}})
        self.wfile.write(b"data: [DONE]\n\n")

    def _anthropic(self, body, text, calls, prompt):
        self._send({"type": "message_start", "message": {"id": "msg_1", "type": "message", "role": "assistant", "model": body.get("model"), "content": [],
                    "stop_reason": None, "stop_sequence": None, "usage": {"input_tokens": prompt, "output_tokens": 1}}}, "message_start")
        index = 0
        if text:
            self._send({"type": "content_block_start", "index": 0, "content_block": {"type": "text", "text": ""}}, "content_block_start")
            for i in range(0, len(text), 5):
                self._send({"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": text[i:i + 5]}}, "content_block_delta")
                time.sleep(0.03)
            self._send({"type": "content_block_stop", "index": 0}, "content_block_stop")
            index = 1
        for n, (name, arguments) in enumerate(calls):
            self._send({"type": "content_block_start", "index": index, "content_block": {"type": "tool_use", "id": f"toolu_{n}", "name": name, "input": {}}},
                       "content_block_start")
            raw = json.dumps(arguments, ensure_ascii=False)
            for i in range(0, len(raw), 12):
                self._send({"type": "content_block_delta", "index": index, "delta": {"type": "input_json_delta", "partial_json": raw[i:i + 12]}},
                           "content_block_delta")
                time.sleep(0.02)
            self._send({"type": "content_block_stop", "index": index}, "content_block_stop")
            index += 1
        self._send({"type": "message_delta", "delta": {"stop_reason": "tool_use" if calls else "end_turn", "stop_sequence": None},
                    "usage": {"output_tokens": 30}}, "message_delta")
        self._send({"type": "message_stop"}, "message_stop")

    def _json(self, value, status=200):
        data = json.dumps(value).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *args):
        pass


ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1])), Handler).serve_forever()
