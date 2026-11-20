"""server/corner_proxy.py against a stand-in for the Messages API (no key, no network): the fast tier."""

import json
import threading
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest

import corner_proxy as P


class _FakeApi(BaseHTTPRequestHandler):
    seen: list = []
    answer: dict = {}

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["content-length"])))
        type(self).seen.append((self.headers, body))
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
    fake = ThreadingHTTPServer(("127.0.0.1", 0), _FakeApi)
    threading.Thread(target=fake.serve_forever, daemon=True).start()
    monkeypatch.setattr(P, "API", f"http://127.0.0.1:{fake.server_port}/v1/messages")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "test-key")
    monkeypatch.setattr(P, "MIN_GAP_S", 0.2)
    monkeypatch.setattr(P.Handler, "token", "")
    P._last[0] = 0.0
    logs = []
    monkeypatch.setattr(P.Handler, "log_message", lambda self, message, *args: logs.append(message % args))
    server = ThreadingHTTPServer(("127.0.0.1", 0), P.Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    url = f"http://127.0.0.1:{server.server_port}/corner"

    def post(obj, raw=None, headers=None, target=None):
        request_headers = {"content-type": "application/json"}
        request_headers.update(headers or {})
        data = raw if raw is not None else json.dumps(obj).encode()
        req = urllib.request.Request(target or url, data=data, method="POST", headers=request_headers)
        try:
            with urllib.request.urlopen(req, timeout=5) as r:
                return r.status, json.loads(r.read())
        except urllib.error.HTTPError as e:
            return e.code, json.loads(e.read())

    yield post, logs, url
    server.shutdown()
    fake.shutdown()


def _wait_gap():
    import time

    time.sleep(P.MIN_GAP_S + 0.05)


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
