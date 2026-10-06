"""Read-only local viewer for Neow's Company generation journals (stdlib only)."""
import argparse
import json
import os
import threading
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit


def generation_directory(path):
    """Accept a generation directory, mod directory, or game directory."""
    path = Path(path).expanduser().resolve()
    for candidate in (path / "mods" / "NeowsCompany" / "data" / "generation",
                      path / "data" / "generation", path):
        if candidate.is_dir() and (candidate.name == "generation" or any(candidate.glob("*.jsonl"))):
            return candidate
    return path


def read_session(path):
    events, warnings = [], []
    with path.open("r", encoding="utf-8-sig", errors="replace") as stream:
        for line_number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            try:
                event = json.loads(line)
                if not isinstance(event, dict) or not isinstance(event.get("kind"), str):
                    raise ValueError("Expected a journal event")
                if not isinstance(event.get("payload"), dict):
                    raise ValueError("Expected an object payload")
                cards = event["payload"].get("cards", [])
                if event["kind"] == "generation_ready" and (
                        not isinstance(cards, list) or any(not isinstance(card, dict) for card in cards)):
                    raise ValueError("Expected card objects")
            except (ValueError, RecursionError):
                warnings.append({"line": line_number, "reason": "无效 JSON 或日志结构（末行可能尚未写完）"})
                continue
            events.append(event)

    requests = {}
    session = {}
    for event in events:
        payload = event["payload"]
        if event["kind"] == "generation_session":
            session = payload
        revision = payload.get("revision")
        if isinstance(revision, bool) or not isinstance(revision, int):
            continue
        request = requests.setdefault(revision, {"revision": revision, "status": "pending", "events": []})
        request["events"].append(event)
        if event["kind"] == "generation_request":
            request["request"] = payload
            request["timestamp_utc"] = event.get("timestamp_utc")
        elif event["kind"] == "generation_response":
            request["response"] = payload
        elif event["kind"] in ("generation_ready", "generation_failed", "generation_discarded"):
            request["status"] = {"generation_ready": "ready", "generation_failed": "failed",
                                 "generation_discarded": "discarded"}[event["kind"]]
            request["result"] = payload
        request.setdefault("timestamp_utc", event.get("timestamp_utc"))

    ordered = sorted(requests.values(), key=lambda item: item["revision"])
    cards = [card for request in ordered if request["status"] == "ready"
             for card in request["result"].get("cards", [])]
    summary = {
        "file": path.name,
        "key": session.get("key", path.stem),
        "run_key": session.get("run_key"),
        "floor": session.get("floor"),
        "timing": session.get("timing"),
        "timestamp_utc": events[0].get("timestamp_utc") if events else None,
        "requests": len(ordered),
        "ready": sum(item["status"] == "ready" for item in ordered),
        "failed": sum(item["status"] == "failed" for item in ordered),
        "discarded": sum(item["status"] == "discarded" for item in ordered),
        "pending": sum(item["status"] == "pending" for item in ordered),
        "cards": [str(card.get("name", "")) for card in cards],
        "models": sorted({str(item.get("request", {}).get("model", "")) for item in ordered} - {""}),
        "reasons": sorted({str(item.get("result", {}).get("reason", "")) for item in ordered} - {""}),
        "warning_count": len(warnings),
    }
    return {"summary": summary, "requests": ordered, "events": events,
            "warnings": warnings[:20], "warning_count": len(warnings)}


class JournalStore:
    def __init__(self, directory):
        self.directory = directory.resolve()
        self._cache = {}
        self._lock = threading.Lock()

    def session(self, name):
        path = self.directory / name
        # No arbitrary file reads, traversal, or symlinks outside the selected folder.
        if path.name != name or path.suffix.lower() != ".jsonl" or path.resolve().parent != self.directory:
            raise FileNotFoundError(name)
        with self._lock:
            stat = path.stat()
            stamp = (stat.st_mtime_ns, stat.st_size)
            cached = self._cache.get(name)
            if cached is None or cached[0] != stamp:
                cached = (stamp, read_session(path))
                self._cache[name] = cached
            return cached[1]

    def index(self):
        sessions, errors = [], []
        paths = sorted(self.directory.glob("*.jsonl"), key=lambda path: path.name, reverse=True)
        for path in paths:
            try:
                sessions.append(self.session(path.name)["summary"])
            except (OSError, ValueError, RecursionError):
                errors.append({"file": path.name, "reason": "无法读取文件；可能已删除或被占用"})
        sessions.sort(key=lambda item: str(item.get("timestamp_utc") or ""), reverse=True)
        with self._lock:
            names = {path.name for path in paths}
            self._cache = {name: item for name, item in self._cache.items() if name in names}
        return {"directory": str(self.directory), "sessions": sessions, "errors": errors}


def make_handler(store):
    page = Path(__file__).with_name("generation_viewer.html").read_bytes()

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            host = self.headers.get("Host", "")
            allowed_hosts = {f"127.0.0.1:{self.server.server_port}", f"localhost:{self.server.server_port}"}
            if host not in allowed_hosts or self.headers.get("Origin", f"http://{host}") != f"http://{host}":
                self.send_error(403)
                return
            url = urlsplit(self.path)
            try:
                if url.path == "/":
                    self.respond(page, "text/html; charset=utf-8")
                elif url.path == "/api/sessions":
                    self.respond_json(store.index())
                elif url.path == "/api/session":
                    names = parse_qs(url.query).get("file", [])
                    if len(names) != 1:
                        self.send_error(400)
                        return
                    self.respond_json(store.session(names[0]))
                else:
                    self.send_error(404)
            except FileNotFoundError:
                self.send_error(404)
            except (OSError, ValueError, RecursionError):
                self.respond_json({"error": "无法读取日志，请刷新后重试。"}, 500)

        def respond_json(self, value, status=200):
            self.respond(json.dumps(value, ensure_ascii=False).encode("utf-8"),
                         "application/json; charset=utf-8", status)

        def respond(self, body, content_type, status=200):
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'unsafe-inline'; "
                             "style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'")
            self.end_headers()
            try:
                self.wfile.write(body)
            except (BrokenPipeError, ConnectionResetError):
                pass

        def log_message(self, format, *args):
            pass

    return Handler


def main():
    parser = argparse.ArgumentParser(description="在本地浏览器查看 data/generation/*.jsonl（无需第三方包）。")
    default_path = os.environ.get("STS2_GAME_DIR") or Path(__file__).resolve().parent.parent / "data" / "generation"
    parser.add_argument("directory", nargs="?", default=default_path,
                        help="generation 日志目录、NeowsCompany Mod 目录或游戏目录；默认使用 STS2_GAME_DIR 或仓库 data/generation")
    parser.add_argument("--port", type=int, default=8765, help="本地端口，默认 8765；0 为自动选择")
    parser.add_argument("--no-browser", action="store_true", help="不自动打开浏览器")
    options = parser.parse_args()
    directory = generation_directory(options.directory)
    if not directory.is_dir():
        parser.error(f"日志目录不存在：{directory}。请传入游戏目录或 data/generation 的实际路径。")
    if not 0 <= options.port <= 65535:
        parser.error("端口必须在 0 到 65535 之间。")
    try:
        server = ThreadingHTTPServer(("127.0.0.1", options.port), make_handler(JournalStore(directory)))
    except OSError:
        parser.error("无法启动本地服务。端口可能已被占用，请使用 --port 0 或指定其他端口。")
    url = f"http://127.0.0.1:{server.server_port}"
    print(f"Generation viewer: {url}\nLogs: {directory}\nPress Ctrl+C to stop.", flush=True)
    if not options.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
