"""server/corner_proxy.py against a stand-in for the Messages API (no key, no network): the fast tier."""

import json
import os
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "server"))
import corner_proxy as P  # noqa: E402


class _FakeApi(BaseHTTPRequestHandler):
    seen: list = []
    answer: dict = {}
    delay = 0.0

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["content-length"])))
        type(self).seen.append((self.headers, body))
        time.sleep(type(self).delay)
        data = json.dumps(type(self).answer).encode()
        self.send_response(200)
        self.send_header("content-length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *a):
        pass


@pytest.fixture
def proxy(monkeypatch):
    """A fake Messages API and the proxy in front of it, each on its own free port; yields (post, logs)."""
    _FakeApi.seen = []
    _FakeApi.delay = 0.0
    fake = ThreadingHTTPServer(("127.0.0.1", 0), _FakeApi)
    threading.Thread(target=fake.serve_forever, daemon=True).start()
    monkeypatch.setattr(P, "API", f"http://127.0.0.1:{fake.server_port}/v1/messages")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "test-key")
    monkeypatch.setattr(P, "MIN_GAP_S", 0.2)
    monkeypatch.setattr(P, "COLOUR_GAP_S", 0.2)
    monkeypatch.setattr(P.Handler, "token", "")
    monkeypatch.setattr(P.Handler, "colour_timeout_s", 1.0)
    for k in P._last:
        P._last[k] = 0.0
    logs = []
    monkeypatch.setattr(P.Handler, "log_message", lambda self, message, *args: logs.append(message % args))
    server = ThreadingHTTPServer(("127.0.0.1", 0), P.Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    url = f"http://127.0.0.1:{server.server_port}/corner"

    def post(obj, raw=None, headers=None, target=None, path=None):
        request_headers = {"content-type": "application/json"}
        request_headers.update(headers or {})
        data = raw if raw is not None else json.dumps(obj).encode()
        where = target or (url.replace("/corner", path) if path else url)
        req = urllib.request.Request(where, data=data, method="POST", headers=request_headers)
        try:
            with urllib.request.urlopen(req, timeout=5) as r:
                return r.status, json.loads(r.read())
        except urllib.error.HTTPError as e:
            return e.code, json.loads(e.read())

    yield post, logs, url
    server.shutdown()
    fake.shutdown()


def _wait_gap():
    time.sleep(max(P.MIN_GAP_S, P.COLOUR_GAP_S) + 0.05)


def test_corner_line_is_relayed_with_the_game_numbers(proxy):
    post, _, _ = proxy
    _FakeApi.answer = {
        "stop_reason": "end_turn",
        "content": [{"type": "thinking", "thinking": ""}, {"type": "text", "text": "Jab first."}],
    }
    code, out = post({"kind": "corner", "boxer": "MATT", "landed": 4})
    assert (code, out["text"]) == (200, "Jab first.")
    headers, body = _FakeApi.seen[-1]
    assert body["model"] == P.MODEL
    assert body["fallbacks"] == "default" and body["output_config"] == {"effort": "low"}
    assert headers["x-api-key"] == "test-key" and headers["anthropic-version"] == "2023-06-01"
    assert headers["anthropic-beta"] == "server-side-fallback-2026-07-01"
    assert json.loads(body["messages"][0]["content"])["landed"] == 4


def test_rate_limit_refusal_size_and_kind(proxy):
    post, _, _ = proxy
    _FakeApi.answer = {"stop_reason": "end_turn", "content": [{"type": "text", "text": "ok"}]}
    assert post({"kind": "corner"})[0] == 200
    assert post({"kind": "corner"})[0] == 429  # under MIN_GAP_S since the last
    _wait_gap()
    _FakeApi.answer = {"stop_reason": "refusal", "stop_details": {"category": "x"}, "content": []}
    assert post({"kind": "interview"})[0] == 502  # a refusal is the game's fallback, not a line
    assert post(None, raw=b"x" * 5000)[0] == 413
    assert post({"kind": "speech"})[0] == 400


def test_token_is_required_only_in_the_header_and_never_logged(proxy, monkeypatch):
    post, logs, url = proxy
    monkeypatch.setattr(P.Handler, "token", "s3cret")
    _FakeApi.answer = {"stop_reason": "end_turn", "content": [{"type": "text", "text": "ok"}]}
    assert post({"kind": "corner"})[0] == 403
    _wait_gap()
    assert post({"kind": "corner"}, headers={"x-pobox-token": "s3cret"}) == (200, {"text": "ok", "error": ""})
    assert post({"kind": "corner"}, target=url + "?token=s3cret")[0] == 400
    assert not any("s3cret" in entry or "?token=" in entry for entry in logs)


def test_loopback_hosts():
    assert P.is_loopback_host("127.0.0.1") and P.is_loopback_host("localhost") and P.is_loopback_host("::1")
    assert not P.is_loopback_host("0.0.0.0")


ROUND = {
    "kind": "round",
    "round": 2,
    "rounds": 3,
    "cards": "20-18 · 19-19 · 20-18",
    "red": {"name": "MATT", "landedRound": 7, "thrownRound": 21, "daze": 0.1, "stamina": 0.6},
    "blue": {"name": "ZOMBIE", "landedRound": 3, "thrownRound": 30, "daze": 0.5, "stamina": 0.4},
}


def test_colour_lines_use_haiku_with_the_system_prompt_cached(proxy):
    post, logs, _ = proxy
    _FakeApi.answer = {
        "stop_reason": "end_turn",
        "content": [
            {"type": "text", "text": '- MATT is picking his shots.\n\n"ZOMBIE throws thirty, lands three."\nA third.'}
        ],
        "usage": {"cache_read_input_tokens": 640, "cache_creation_input_tokens": 0},
    }
    code, out = post(ROUND, path="/commentary")
    assert code == 200
    assert out["text"].split("\n") == ["MATT is picking his shots.", "ZOMBIE throws thirty, lands three."]
    headers, body = _FakeApi.seen[-1]
    assert body["model"] == P.COLOUR_MODEL == "claude-haiku-5-5"
    assert body["output_config"] == {"effort": "low"} and "fallbacks" not in body
    assert body["system"] == [{"type": "text", "text": P.COLOUR_SYSTEM, "cache_control": {"type": "ephemeral"}}]
    assert json.loads(body["messages"][0]["content"])["red"]["landedRound"] == 7
    assert headers["x-api-key"] == "test-key" and "anthropic-beta" not in headers
    assert any("cache read 640" in entry for entry in logs)


def test_colour_system_prompt_is_static_and_long_enough_to_cache():
    # A prefix that changes per call never caches; one under the model's minimum (512 tokens) silently does not either.
    assert len(P.COLOUR_SYSTEM.split()) > 420
    for volatile in ("{", "}", "%", "202"):
        assert volatile not in P.COLOUR_SYSTEM


def test_colour_failures_are_502_with_no_text(proxy, monkeypatch):
    post, _, _ = proxy
    _FakeApi.answer = {"stop_reason": "refusal", "stop_details": {"category": "x"}, "content": []}
    assert post(ROUND, path="/commentary") == (502, {"text": "", "error": "refused: x"})
    _wait_gap()
    _FakeApi.answer = {"stop_reason": "end_turn", "content": [{"type": "text", "text": "  \n \n"}]}
    assert post(ROUND, path="/commentary")[0] == 502
    _wait_gap()
    _FakeApi.answer = {"stop_reason": "end_turn", "content": [{"type": "text", "text": "late"}]}
    _FakeApi.delay = 1.5  # longer than the colour timeout
    code, out = post(ROUND, path="/commentary")
    assert code == 502 and out["text"] == ""
    _FakeApi.delay = 0.0
    _wait_gap()
    monkeypatch.delenv("ANTHROPIC_API_KEY")
    assert post(ROUND, path="/commentary") == (502, {"text": "", "error": "ANTHROPIC_API_KEY is not set"})


def test_each_endpoint_keeps_its_own_gap_and_kinds(proxy, monkeypatch):
    post, _, _ = proxy
    monkeypatch.setattr(P, "COLOUR_GAP_S", 1.0)  # wide enough that a loaded machine still lands inside it
    _FakeApi.answer = {"stop_reason": "end_turn", "content": [{"type": "text", "text": "ok"}]}
    assert post({"kind": "corner"})[0] == 200
    assert post(ROUND, path="/commentary")[0] == 200  # the corner's call does not hold up the commentator's
    assert post(ROUND, path="/commentary")[0] == 429
    _wait_gap()
    assert post({"kind": "corner"}, path="/commentary")[0] == 400
    assert post({"kind": "round"})[0] == 400
    assert post(ROUND, path="/other")[0] == 404
