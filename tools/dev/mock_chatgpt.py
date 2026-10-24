"""A stand-in ChatGPT for test runs: OpenAI's sign-in (Sign in with ChatGPT, PKCE, a redirect back to the app's own
listener on 127.0.0.1), the account's models and the Responses API as a player's ChatGPT plan answers them. No real
account, no network, nothing billed. Each request is logged as a line of JSON.

    python tools/dev/mock_chatgpt.py 8798 --log <folder>/mind.jsonl --reply "Hi! It's good to see you."

Point a test run at it with the dev action chatgptat:http://127.0.0.1:8798 (the smoke suite does it for a scenario
with "mind": "chatgpt", writing {mind} in its script for the address), then sign in on Settings → Mind (ChatGPT plan,
Continue with ChatGPT): a test run opens no browser, but visits the sign-in itself, which this answers with the
redirect a browser would follow. The account is player@example.com, offering two models.

Every request on the plan is checked against the plan's rules (OpenAI's preview limitations): streamed, not stored, no
system items, none of the refused fields, no hosted tools. One that breaks them is turned down as the plan would (400
subscription_sharing_unsupported_capability) and logged with "broke". Otherwise it gets the reply, streamed, ending
with response.completed. Once the player's words say "limit", the plan's limit is reached: that request and every one
after it get 429 subscription_sharing_usage_limit_exceeded (with "midstream" in the words too, the first comes as
response.failed after the stream began). Words with "signedout" get 401 subscription_sharing_invalid_user, and every
refresh after it invalid_grant, until the next sign-in (the player disconnected the app in ChatGPT); "ineligible", 403
subscription_sharing_user_not_eligible; "unavailable", a passing 503 subscription_sharing_usage_unavailable. Words with
"reset" lift the limit again (the plan works once more), and that request is answered on the plan. Words with "notebook"
get a call of her print_memo tool, and the request that brings its result reaches the limit (a turn stopped part-way);
the backup calls print_memo too ("Notebook again") for such words, so a turn asked again after the yes would show.

It also stands in for the player's backup behind the plan: an OpenAI-compatible server at /backup/v1 (chat
completions, streamed or not, answering --backup-reply, no key needed), each request logged with "backup": true. It
stops when killed, or after --quit-after seconds.
"""

import argparse
import base64
import hashlib
import json
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlencode, urlparse

ARGS = None
LOCK = threading.Lock()
PENDING = {}            # code -> what the authorize request said
STATE = {"issued": 0, "limit": False}
CLIENT = "oaiapp_mock"
SCOPES = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct"
REFUSED = ["background", "conversation", "max_output_tokens", "max_tool_calls", "metadata", "moderation", "multi_agent", "prompt",
           "prompt_cache_retention", "safety_identifier", "temperature", "top_logprobs", "top_p", "truncation", "user", "previous_response_id"]
HOSTED = ["image_generation", "file_search", "code_interpreter", "computer_use_preview", "mcp", "tool_search"]
MODELS = [{"slug": "gpt-mock", "display_name": "GPT Mock", "visibility": "list"},
          {"slug": "gpt-hidden", "display_name": "Hidden", "visibility": "hide"},
          {"slug": "gpt-mock-mini", "display_name": "GPT Mock mini", "visibility": "list"}]


def log(entry):
    if not ARGS.log:
        return
    with LOCK:
        with open(ARGS.log, "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")


def b64url(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def words_of(body):
    """The player's newest words in a request: the last user message's text, as sent (with the app's notes around it)."""
    for item in reversed(body.get("input") or []):
        if isinstance(item, dict) and item.get("role") == "user":
            content = item.get("content")
            if isinstance(content, str):
                return content
            return " ".join(p.get("text", "") for p in content or [] if isinstance(p, dict))
    return ""


def newest(words):
    """Only the newest thing they said: the last paragraph before the closing <character> note. A turn also carries
    what went unanswered before it, which mustn't trip a trigger a second time."""
    words = words.rsplit("<character>", 1)[0]
    paragraphs = [p.strip() for p in words.split("\n\n") if p.strip() and not p.strip().startswith("<")]
    return paragraphs[-1] if paragraphs else ""


def broken_rules(body):
    broke = [f for f in REFUSED if f in body]
    if body.get("stream") is not True:
        broke.append("not streamed")
    if body.get("store") is not False:
        broke.append("stored")
    if any(isinstance(i, dict) and i.get("role") == "system" for i in body.get("input") or []):
        broke.append("a system item")
    broke += [f"hosted tool {t.get('type')}" for t in body.get("tools") or [] if t.get("type") in HOSTED or t.get("type", "").startswith("web_search")]
    return broke


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def answer(self, status, payload, headers=None):
        data = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(data)

    def bearer(self):
        auth = self.headers.get("Authorization", "")
        return auth[7:] if auth.startswith("Bearer ") else None

    def do_GET(self):
        url = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(url.query).items()}
        if url.path == "/api/accounts/authorize":
            problems = []
            if q.get("client_id") not in ("dynamic_agent_client", CLIENT):
                problems.append("client_id")
            if q.get("client_id") == "dynamic_agent_client" and not q.get("agent_name_hint"):
                problems.append("no agent_name_hint")
            if not q.get("ext_agent_host_id", "").startswith("urn:uuid:"):
                problems.append("ext_agent_host_id")
            if q.get("scope") != SCOPES or q.get("resource") != "https://api.openai.com/v1" or q.get("code_challenge_method") != "S256":
                problems.append("scope, resource or PKCE")
            if not q.get("redirect_uri", "").startswith("http://127.0.0.1:"):
                problems.append("redirect_uri")
            log({"path": url.path, "client_id": q.get("client_id"), "named": q.get("agent_name_hint"), "problems": problems})
            if problems:
                self.answer(400, {"error": "invalid_request", "error_description": ", ".join(problems)})
                return
            code = "code-" + b64url(hashlib.sha256(q["state"].encode()).digest())[:12]
            PENDING[code] = q
            back = urlencode({"code": code, "state": q["state"], "client_id": CLIENT, "scope": SCOPES})
            self.send_response(302)
            self.send_header("Location", f"{q['redirect_uri']}?{back}")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if url.path == "/v1/models":
            log({"path": url.path, "bearer": bool(self.bearer())})
            if not self.bearer():
                self.answer(401, {"error": {"code": "invalid_api_key", "message": "no token"}})
                return
            self.answer(200, {"models": MODELS})
            return
        self.answer(404, {"error": "not found"})

    def do_POST(self):
        url = urlparse(self.path)
        raw = self.rfile.read(int(self.headers.get("Content-Length", "0") or 0)).decode("utf-8")
        if url.path == "/api/accounts/oauth/token":
            form = {k: v[0] for k, v in parse_qs(raw).items()}
            grant = form.get("grant_type")
            if grant == "authorization_code":
                asked = PENDING.pop(form.get("code", ""), None)
                verified = asked and b64url(hashlib.sha256(form.get("code_verifier", "").encode()).digest()) == asked["code_challenge"]
                log({"path": url.path, "grant": grant, "client_id": form.get("client_id"), "pkce": bool(verified)})
                if not verified or form.get("client_id") != CLIENT or form.get("redirect_uri") != asked["redirect_uri"]:
                    self.answer(400, {"error": "invalid_grant"})
                    return
                nonce = asked["nonce"]
                STATE["signed_out"] = False                     # signed in again
            elif grant == "refresh_token":
                log({"path": url.path, "grant": grant, "client_id": form.get("client_id"), "refused": bool(STATE.get("signed_out"))})
                if STATE.get("signed_out"):
                    self.answer(400, {"error": "invalid_grant"})
                    return
                nonce = None
            else:
                self.answer(400, {"error": "unsupported_grant_type"})
                return
            STATE["issued"] += 1
            n = STATE["issued"]
            claims = {"iss": "mock", "aud": CLIENT, "sub": "user-mock", "email": "player@example.com", "exp": int(time.time()) + 3600}
            if nonce:
                claims["nonce"] = nonce
            id_token = f"{b64url(b'{}')}.{b64url(json.dumps(claims).encode())}.mock"
            self.answer(200, {"access_token": f"mock-access-{n}", "refresh_token": f"mock-refresh-{n}", "id_token": id_token, "token_type": "Bearer",
                              "expires_in": 3600, "scope": SCOPES})
            return
        if url.path == "/api/accounts/oauth/revoke":
            log({"path": url.path})
            self.send_response(200)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        if url.path == "/backup/v1/chat/completions":
            body = json.loads(raw or "{}")
            said = next((m.get("content") for m in reversed(body.get("messages") or []) if m.get("role") == "user"), "")
            said = said if isinstance(said, str) else " ".join(p.get("text", "") for p in said or [] if isinstance(p, dict))
            log({"path": url.path, "backup": True, "model": body.get("model"), "said": said[-200:], "stream": bool(body.get("stream"))})
            answered = any(m.get("role") == "tool" for m in body.get("messages") or [])
            if "notebook" in newest(said).lower() and body.get("tools") and not answered and body.get("stream"):
                self.backup_call(body.get("model") or "backup-mock", "print_memo", {"title": "Notebook again", "text": "- a memo from the backup"})
                return
            self.backup(body.get("model") or "backup-mock", ARGS.backup_reply, bool(body.get("stream")))
            return
        if url.path != "/v1/responses":
            self.answer(404, {"error": "not found"})
            return
        body = json.loads(raw or "{}")
        words = words_of(body)
        broke = broken_rules(body)
        if not self.bearer():
            self.answer(401, {"error": {"code": "subscription_sharing_invalid_user", "message": "no token"}})
            return
        said = newest(words).lower()
        if "ineligible" in said:
            log({"path": url.path, "said": words[-60:], "answered": 403})
            self.answer(403, {"error": {"code": "subscription_sharing_user_not_eligible", "message": "This account can't share its plan."}})
            return
        if "signedout" in said:                                 # disconnected in ChatGPT: this and every refresh after it turned down
            STATE["signed_out"] = True
            log({"path": url.path, "said": words[-60:], "answered": 401})
            self.answer(401, {"error": {"code": "subscription_sharing_invalid_user", "message": "Sign in again."}})
            return
        if "unavailable" in said:
            log({"path": url.path, "said": words[-60:], "answered": 503})
            self.answer(503, {"error": {"code": "subscription_sharing_usage_unavailable", "message": "Try again later."}})
            return
        if "limit" in said:
            STATE["limit"] = True
        if "reset" in said:
            STATE["limit"] = False                              # the plan's limit lifted: it answers again
        entry = {"path": url.path, "model": body.get("model"), "said": words[-200:], "instructions": bool(body.get("instructions")),
                 "roles": [i.get("role") or i.get("type") for i in body.get("input") or [] if isinstance(i, dict)],
                 "tools": [t.get("name") or t.get("type") for t in body.get("tools") or []]}
        if broke:
            log({**entry, "broke": broke})
            self.answer(400, {"error": {"code": "subscription_sharing_unsupported_capability", "param": broke[0], "message": "not on the plan"}})
            return
        if "notebook" in said and not STATE["limit"]:
            if any(isinstance(i, dict) and i.get("type") == "function_call_output" for i in body.get("input") or []):
                STATE["limit"] = True                           # the memo is written: the request with its result reaches the limit
            else:
                log({**entry, "tool_call": "print_memo"})
                self.call(body.get("model") or "gpt-mock", "print_memo", {"title": "Notebook", "text": "- a memo from the plan"})
                return
        if STATE["limit"] and "midstream" not in said:
            log({**entry, "limit": True})
            self.answer(429, {"error": {"code": "subscription_sharing_usage_limit_exceeded", "message": "The plan's usage limit for this app is reached."}})
            return
        log({**entry, "limit": STATE["limit"]})
        self.stream(body.get("model") or "gpt-mock", ARGS.reply, failed=STATE["limit"])

    def backup(self, model, text, streamed):
        """The backup's answer, as an OpenAI-compatible server gives one: chat completion chunks, or one whole completion."""
        usage = {"prompt_tokens": 800, "completion_tokens": 12, "total_tokens": 812}
        if not streamed:
            self.answer(200, {"id": "chatcmpl-backup", "object": "chat.completion", "created": int(time.time()), "model": model,
                              "choices": [{"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": "stop"}], "usage": usage})
            return
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.end_headers()

        def send(delta, finish=None, extra=None):
            chunk = {"id": "chatcmpl-backup", "object": "chat.completion.chunk", "created": int(time.time()), "model": model,
                     "choices": [{"index": 0, "delta": delta, "finish_reason": finish}], **(extra or {})}
            self.wfile.write(f"data: {json.dumps(chunk, ensure_ascii=False)}\n\n".encode("utf-8"))
            self.wfile.flush()

        send({"role": "assistant", "content": ""})
        for i in range(0, len(text), 12):
            send({"content": text[i:i + 12]})
            time.sleep(0.02)
        send({}, "stop", {"usage": usage})
        self.wfile.write(b"data: [DONE]\n\n")
        self.wfile.flush()

    def backup_call(self, model, name, arguments):
        """The backup calling one of her tools, as chat completion chunks stream a tool call."""
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.end_headers()

        def send(delta, finish=None):
            chunk = {"id": "chatcmpl-backup-call", "object": "chat.completion.chunk", "created": int(time.time()), "model": model,
                     "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
            self.wfile.write(f"data: {json.dumps(chunk)}\n\n".encode("utf-8"))
            self.wfile.flush()

        send({"role": "assistant", "content": None, "tool_calls": [{"index": 0, "id": "call_backup", "type": "function",
                                                                    "function": {"name": name, "arguments": ""}}]})
        send({"tool_calls": [{"index": 0, "function": {"arguments": json.dumps(arguments)}}]})
        send({}, "tool_calls")
        self.wfile.write(b"data: [DONE]\n\n")
        self.wfile.flush()

    def call(self, model, name, arguments):
        """A reply on the plan that calls one of her tools, as the Responses API streams a function call."""
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.end_headers()
        args = json.dumps(arguments)
        response = {"id": "resp_call", "object": "response", "created_at": int(time.time()), "status": "in_progress", "model": model, "output": []}
        item = {"id": "fc_mock", "type": "function_call", "status": "in_progress", "call_id": "call_mock", "name": name, "arguments": ""}
        done = {**item, "status": "completed", "arguments": args}

        def send(kind, payload):
            self.wfile.write(f"event: {kind}\ndata: {json.dumps({'type': kind, **payload})}\n\n".encode("utf-8"))
            self.wfile.flush()

        send("response.created", {"sequence_number": 0, "response": response})
        send("response.output_item.added", {"sequence_number": 1, "output_index": 0, "item": item})
        send("response.function_call_arguments.delta", {"sequence_number": 2, "item_id": "fc_mock", "output_index": 0, "delta": args})
        send("response.function_call_arguments.done", {"sequence_number": 3, "item_id": "fc_mock", "output_index": 0, "arguments": args})
        send("response.output_item.done", {"sequence_number": 4, "output_index": 0, "item": done})
        send("response.completed", {"sequence_number": 5, "response": {**response, "status": "completed", "output": [done],
             "usage": {"input_tokens": 900, "input_tokens_details": {"cached_tokens": 0}, "output_tokens": 30,
                       "output_tokens_details": {"reasoning_tokens": 0}, "total_tokens": 930}}})

    def stream(self, model, text, failed):
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.end_headers()
        response = {"id": "resp_mock", "object": "response", "created_at": int(time.time()), "status": "in_progress", "model": model, "output": []}

        def send(kind, payload):
            payload = {"type": kind, **payload}
            self.wfile.write(f"event: {kind}\ndata: {json.dumps(payload, ensure_ascii=False)}\n\n".encode("utf-8"))
            self.wfile.flush()

        send("response.created", {"sequence_number": 0, "response": response})
        if failed:
            send("response.failed", {"sequence_number": 1, "response": {**response, "status": "failed",
                 "error": {"code": "subscription_sharing_usage_limit_exceeded", "message": "The plan's usage limit for this app is reached."}}})
            return
        item = {"id": "msg_mock", "type": "message", "status": "in_progress", "role": "assistant", "content": []}
        send("response.output_item.added", {"sequence_number": 1, "output_index": 0, "item": item})
        n = 2
        for i in range(0, len(text), 12):
            send("response.output_text.delta", {"sequence_number": n, "item_id": "msg_mock", "output_index": 0, "content_index": 0, "delta": text[i:i + 12]})
            n += 1
            time.sleep(0.02)
        done = {**item, "status": "completed", "content": [{"type": "output_text", "text": text, "annotations": []}]}
        send("response.output_item.done", {"sequence_number": n, "output_index": 0, "item": done})
        send("response.completed", {"sequence_number": n + 1, "response": {**response, "status": "completed", "output": [done],
             "usage": {"input_tokens": 900, "input_tokens_details": {"cached_tokens": 600}, "output_tokens": 20,
                       "output_tokens_details": {"reasoning_tokens": 0}, "total_tokens": 920}}})


def main():
    global ARGS
    parser = argparse.ArgumentParser()
    parser.add_argument("port", type=int)
    parser.add_argument("--log")
    parser.add_argument("--reply", default="Hi! It's good to see you.")
    parser.add_argument("--backup-reply", default="Still here, on the backup.")
    parser.add_argument("--quit-after", type=float)
    ARGS = parser.parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", ARGS.port), Handler)
    if ARGS.quit_after:
        threading.Timer(ARGS.quit_after, server.shutdown).start()
    print(f"mock ChatGPT on http://127.0.0.1:{ARGS.port}", file=sys.stderr)
    server.serve_forever()


if __name__ == "__main__":
    main()
