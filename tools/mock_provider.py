"""Offline integration fixture, not the production card generator. No third-party packages."""
import argparse
import json
import re
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--port", type=int, default=8000)
parser.add_argument("--delay", type=float, default=0)
options = parser.parse_args()
fixture = json.loads((Path(__file__).parent.parent / "examples" / "cards.json").read_text(encoding="utf-8"))["cards"][0]


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/v1/chat/completions":
            self.send_error(404)
            return
        size = int(self.headers.get("Content-Length", "0"))
        if size > 1024 * 1024:
            self.send_error(413)
            return
        request = json.loads(self.rfile.read(size))
        content = request["messages"][-1]["content"]
        match = re.search(r"REQUESTED_COUNT=(\d+)", content)
        count = min(3, int(match.group(1))) if match else 1
        cards = [dict(fixture, name=fixture["name"] + (str(i + 1) if count > 1 else "")) for i in range(count)]
        time.sleep(options.delay)
        output = json.dumps({"choices": [{"message": {"role": "assistant", "content": json.dumps({"cards": cards}, ensure_ascii=False)},
            "finish_reason": "stop"}]}, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(output)))
        self.end_headers()
        try:
            self.wfile.write(output)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, format, *args):
        print("mock-provider:", args[0])  # Never log headers, authorization or prompts.


print(f"Neow's Company test fixture at http://127.0.0.1:{options.port}/v1; delay={options.delay}s", flush=True)
ThreadingHTTPServer(("127.0.0.1", options.port), Handler).serve_forever()
