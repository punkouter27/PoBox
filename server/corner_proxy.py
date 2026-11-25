"""The cornerman's proxy: holds the Claude API key so the game never ships one.

    set ANTHROPIC_API_KEY=sk-ant-...
    python server/corner_proxy.py                        on this PC (the editor)
    python server/corner_proxy.py --host 0.0.0.0 --token SECRET --certfile fullchain.pem --keyfile privkey.pem
                                                                  for a phone on the same Wi-Fi (CORNER: https://PC:8787/corner#token=SECRET)

The key is read from the ANTHROPIC_API_KEY environment variable of the process, here, and from nowhere else: not
from the request, not from a file in the repo. Two endpoints, both POST JSON, both answering {"text": ..., "error": ...}:

/corner (Broadcast/Commentary.cs, Online/CornerAI.cs) gets one fight's numbers
    {"kind": "corner" | "interview", "boxer": "MATT", "opponent": "ZOMBIE", "round": 2, ...numbers...}
and gives one short line: the corner's advice between rounds, or the winner's word after the bell.
Claude Opus 5.5 at low effort; refusals fall back server-side (fallbacks "default").

/commentary (Broadcast/Commentary.cs, colour commentary) gets a compact summary of what just happened
    {"kind": "round" | "knockdown", "round": 2, "rounds": 3, "cards": "...", "red": {...}, "blue": {...}}
and gives one or two short colour lines, one a line of "text". Claude Haiku 5.5 at low effort: it is said aloud
between rounds or a moment after a knockdown, so speed counts more than depth. The system prompt is the same bytes
on every call and is marked for prompt caching; the varying facts go after it, in the user turn.

Every number in a line is the game's; the prompts tell the model to use only those. Any failure (no key, the API
down, a refusal, too slow) is a 502 with an empty text, and the game says its own rule-based lines instead.

Standard library only (this machine's Pythons have no pip), so the Messages API is called over HTTPS.
A request bigger than 4 KB, a wrong --token, or a second request to the same endpoint inside its gap (2 s for
/corner, 3 s for /commentary) is turned away, so a proxy left open on a network cannot be used to spend the key.
Network binds require TLS and a token. The token is accepted only in the x-pobox-token header; URL query strings are
rejected, and access logs never include request paths.
"""

from __future__ import annotations

import argparse
import hmac
import ipaddress
import json
import os
import ssl
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

API = os.environ.get("ANTHROPIC_BASE_URL", "https://api.anthropic.com").rstrip("/") + "/v1/messages"
MODEL = "claude-opus-5-5"
COLOUR_MODEL = "claude-haiku-5-5"
MAX_BODY = 4096
MIN_GAP_S = 2.0
COLOUR_GAP_S = 3.0
COLOUR_LINES = 2
COLOUR_LINE_CHARS = 160

SYSTEM = {
    "corner": (
        "You are the cornerman of a boxer in a physics-simulated boxing broadcast, talking to your boxer on the stool "
        "between rounds. You get the round's numbers as JSON. Say ONE thing to your boxer, at most 22 words, in the "
        "voice of a gruff boxing trainer: the single most useful change for the next round, backed by one of the "
        "numbers. Use only numbers that are in the JSON; never invent one. No preamble, no quotes, no stage directions."
    ),
    "interview": (
        "You are the winning boxer of a physics-simulated boxing broadcast, giving the ringside interviewer one line "
        "right after the decision. You get the fight's numbers as JSON. At most 24 words, confident and a little funny, "
        "naming one real number from the JSON about how you won. Use only numbers that are in the JSON; never invent "
        "one. No preamble, no quotes, no stage directions."
    ),
}

# The colour commentator. Static text (no dates, ids or numbers that change), so it is a stable prefix for prompt
# caching; long enough to be cached (Haiku 5.5 caches a prefix of 512 tokens or more).
COLOUR_SYSTEM = """You are the colour commentator of PoBox, a boxing broadcast in portrait for phones. The two boxers \
are physics-driven characters: every punch, stagger and knockdown is simulated, nothing is animated or scripted. A \
play-by-play voice has already called each punch as it landed. Your job is the other half of the booth: the short, \
human, colourful observation that tells the viewer what the numbers mean.

You will receive one JSON object describing what just happened. Its "kind" is one of:
- "round": a round has just ended. "round" is the round that ended and "rounds" the scheduled number of rounds. \
"red" and "blue" describe each boxer: "name", punches "landedRound" and "thrownRound" in that round, "landedBout" \
and "thrownBout" so far, "accuracy" (percent landed in the bout), "hardestPunch" (the biggest impulse landed, in \
newton-seconds), "fastestKmh" (the fastest glove that landed), "knockdowns" (scored on the opponent), "downs" \
(times this boxer went down), "headTaken" (impulse taken to the head this round), "daze" (0 = clear-headed, \
1 = legs gone), "stamina" (0 = empty, 1 = fresh). "cards" are the three judges' scores so far.
- "knockdown": a boxer has just gone down. "attacker" and "victim" are names, "punch" is the punch that did it, \
"impulse" its impulse in newton-seconds, "round" the round and "clock" the time left in it; "red" and "blue" are \
as above.

Write one or two lines, each on its own line, each at most 18 words. They are shown on a ticker and read aloud by a \
text-to-speech voice between rounds, so:
- Plain sentences only: no lists, no headings, no emoji, no quotation marks, no hashtags, no stage directions.
- Spell the boxers' names exactly as given, in capitals as given.
- Use numbers only if they are in the JSON, and at most one number per line. Round them the way a commentator \
would say them aloud ("eleven of thirty", "forty newton-seconds", "about thirty kilometres an hour").
- Never invent an injury, a cut, a history, a trainer, a crowd reaction or anything else that is not in the JSON. \
The boxers are simulations; do not speculate about their feelings beyond what the numbers show.
- Prefer the most telling contrast: accuracy against volume, power against pace, a fading stamina, a boxer who is \
behind on the cards but landing harder, a daze that has not cleared.
- Vary your openings. Do not start with "Well", "Folks", "What a round" or the boxer's name every time.
- After a knockdown, react to the punch and what it means for the fight; do not count, the referee does that.
- After the final round, do not announce a winner; the judges do that.
- Keep it family-friendly and good-natured; a little dry wit is welcome, cruelty is not.

Answer with the lines only."""

_lock = threading.Lock()
# Per endpoint: when it last called the API.
_last = {"/corner": 0.0, "/commentary": 0.0}


def gap_for(path: str) -> float:
    return COLOUR_GAP_S if path == "/commentary" else MIN_GAP_S


def call(body: dict, timeout_s: float, beta: str = "") -> dict:
    """One Messages API call over HTTPS; the parsed reply. The key comes from this process's environment only."""
    key = os.environ.get("ANTHROPIC_API_KEY", "")
    if not key:
        raise RuntimeError("ANTHROPIC_API_KEY is not set")
    headers = {"content-type": "application/json", "x-api-key": key, "anthropic-version": "2023-06-01"}
    if beta:
        headers["anthropic-beta"] = beta
    req = urllib.request.Request(API, data=json.dumps(body).encode("utf-8"), method="POST", headers=headers)
    with urllib.request.urlopen(req, timeout=timeout_s) as r:
        msg = json.loads(r.read().decode("utf-8"))
    if msg.get("stop_reason") == "refusal":
        raise RuntimeError(f"refused: {(msg.get('stop_details') or {}).get('category')}")
    return msg


def text_of(msg: dict) -> str:
    text = " ".join(b.get("text", "") for b in msg.get("content", []) if b.get("type") == "text").strip()
    if not text:
        raise RuntimeError(f"no text (stop_reason {msg.get('stop_reason')})")
    return text


def ask(kind: str, facts: dict, timeout_s: float) -> str:
    body = {
        "model": MODEL,
        "max_tokens": 2000,  # thinking is always on for this model and counts towards it
        "output_config": {"effort": "low"},
        "fallbacks": "default",
        "system": SYSTEM[kind],
        "messages": [{"role": "user", "content": json.dumps(facts, sort_keys=True)}],
    }
    return text_of(call(body, timeout_s, "server-side-fallback-2026-07-01")).strip('"')


def colour_lines(text: str) -> list:
    """At most COLOUR_LINES non-empty lines, bullets and quotes stripped, each cut to COLOUR_LINE_CHARS."""
    out = []
    for line in text.splitlines():
        line = line.strip().lstrip("-*• ").strip().strip('"').strip()
        if line:
            out.append(line[:COLOUR_LINE_CHARS])
        if len(out) == COLOUR_LINES:
            break
    return out


def ask_colour(facts: dict, timeout_s: float) -> tuple:
    """One or two colour lines for a round's or a knockdown's summary, and the call's usage (for the log)."""
    body = {
        "model": COLOUR_MODEL,
        # Thinking is on by default on Haiku 5.5 and counts towards max_tokens; low effort keeps it short.
        "max_tokens": 1000,
        "output_config": {"effort": "low"},
        "system": [{"type": "text", "text": COLOUR_SYSTEM, "cache_control": {"type": "ephemeral"}}],
        "messages": [{"role": "user", "content": json.dumps(facts, sort_keys=True)}],
    }
    msg = call(body, timeout_s)
    lines = colour_lines(text_of(msg))
    if not lines:
        raise RuntimeError("no line in the answer")
    return "\n".join(lines), msg.get("usage") or {}


ROUTES = {"/corner": tuple(SYSTEM), "/commentary": ("round", "knockdown")}


class Handler(BaseHTTPRequestHandler):
    token = ""
    timeout_s = 8.0
    colour_timeout_s = 6.0

    def log_request(self, code="-", size="-") -> None:
        self.log_message("%s %s", self.client_address[0], code)

    def reply(self, code: int, text: str = "", error: str = "") -> None:
        data = json.dumps({"text": text, "error": error}).encode("utf-8")
        self.send_response(code)
        self.send_header("content-type", "application/json")
        self.send_header("content-length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self) -> None:  # noqa: N802 (http.server's name)
        url = urllib.parse.urlsplit(self.path)
        if url.path not in ROUTES:
            return self.reply(404, error="not found")
        try:
            n = int(self.headers.get("content-length") or 0)
        except ValueError:
            return self.reply(400, error="invalid content length")
        if n <= 0 or n > MAX_BODY:
            if n > 0:
                self.rfile.read(min(n, MAX_BODY * 4))
            return self.reply(413, error="body too big or empty")
        if url.query or url.fragment:
            self.rfile.read(n)
            return self.reply(400, error="credentials and query parameters are not accepted in the URL")
        given = self.headers.get("x-pobox-token", "")
        if self.token and not hmac.compare_digest(given, self.token):
            self.rfile.read(n)
            return self.reply(403, error="bad token")
        kinds = ROUTES[url.path]
        try:
            facts = json.loads(self.rfile.read(n).decode("utf-8"))
            kind = facts.get("kind", kinds[0])
            if not isinstance(facts, dict) or kind not in kinds:
                raise ValueError("kind must be " + " or ".join(kinds))
        except (ValueError, AttributeError) as e:
            return self.reply(400, error=str(e))
        with _lock:
            if time.time() - _last[url.path] < gap_for(url.path):
                return self.reply(429, error="too soon")
            _last[url.path] = time.time()
        try:
            if url.path == "/commentary":
                text, usage = ask_colour(facts, self.colour_timeout_s)
                self.log_message(
                    "colour %s R%s (cache read %s, written %s): %s",
                    kind,
                    facts.get("round", "?"),
                    usage.get("cache_read_input_tokens", 0),
                    usage.get("cache_creation_input_tokens", 0),
                    text.replace("\n", " / "),
                )
            else:
                text = ask(kind, facts, self.timeout_s)
                self.log_message("%s %s: %s", kind, facts.get("boxer", "?"), text)
        except (OSError, RuntimeError, ValueError, urllib.error.URLError) as e:
            self.log_message("upstream: %s", e)
            return self.reply(502, error=str(e)[:200])
        self.reply(200, text=text)


def is_loopback_host(host: str) -> bool:
    if host.lower() == "localhost":
        return True
    try:
        return ipaddress.ip_address(host.split("%", 1)[0]).is_loopback
    except ValueError:
        return False


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8787)
    ap.add_argument("--token", default="", help="shared token sent by the game in the x-pobox-token header")
    ap.add_argument("--certfile", help="TLS certificate chain for network access")
    ap.add_argument("--keyfile", help="TLS private key for network access")
    ap.add_argument("--timeout", type=float, default=8.0, help="seconds a /corner call may take")
    ap.add_argument("--colour-timeout", type=float, default=6.0, help="seconds a /commentary call may take")
    args = ap.parse_args()
    loopback = is_loopback_host(args.host)
    if bool(args.certfile) != bool(args.keyfile):
        ap.error("--certfile and --keyfile must be supplied together")
    if not loopback and not args.token:
        ap.error("a --token is required when listening on a network interface")
    if not loopback and not args.certfile:
        ap.error("TLS is required when listening on a network interface")

    Handler.token, Handler.timeout_s, Handler.colour_timeout_s = args.token, args.timeout, args.colour_timeout
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    if args.certfile:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(args.certfile, args.keyfile)
        server.socket = context.wrap_socket(server.socket, server_side=True)
    scheme = "https" if args.certfile else "http"
    print(
        f"corner proxy on {scheme}://{args.host}:{args.port}/corner ({MODEL}) and /commentary ({COLOUR_MODEL}); "
        f"key {'set' if os.environ.get('ANTHROPIC_API_KEY') else 'NOT SET'}"
    )
    server.serve_forever()


if __name__ == "__main__":
    main()
