"""Run with: python -m unittest discover -s tools -p test_view_generation.py"""
import json
import tempfile
import threading
import unittest
from http.client import HTTPConnection
from http.server import ThreadingHTTPServer
from pathlib import Path

from view_generation import JournalStore, generation_directory, make_handler, read_session


def event(kind, payload):
    return {"schema_version": 1, "timestamp_utc": "2026-10-05T01:00:00+00:00", "kind": kind, "payload": payload}


class ViewerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name) / "data" / "generation"
        self.directory.mkdir(parents=True)
        self.path = self.directory / "combat.jsonl"

    def write(self, events):
        self.path.write_text("\n".join(json.dumps(item, ensure_ascii=False) for item in events)+"\n", encoding="utf-8")

    def test_interleaved_requests_correlate_by_revision_and_keep_reward_events(self):
        self.write([
            event("generation_session", {"key": "combat", "floor": 7, "run_key": "run"}),
            event("generation_request", {"revision": 1, "model": "fixture", "prompt": {"system": "协议", "user": "{}"}}),
            event("generation_request", {"revision": 2, "model": "fixture"}),
            event("generation_response", {"revision": 2, "reasoning_content": "失败诊断", "total_tokens": 500}),
            event("generation_response", {"revision": 1, "content": "  原始输出\n", "reasoning_content": "卡牌设计", "prompt_cache_hit_tokens": 12}),
            event("generation_ready", {"revision": 1, "elapsed_ms": 321, "cards": [{"name": "涅奥的低语"}], "card_texts": [["抽1张牌。", "抽2张牌。"]]}),
            event("generation_failed", {"revision": 2, "reason": "completion_token_limit", "stage": "provider"}),
            event("generation_request", {"revision": 3}),
            event("generation_discarded", {"revision": 3, "reason": "reward_frozen_or_session_ended"}),
            event("generation_request", {"revision": 4}),
            event("reward_frozen", {"cards": []}),
        ])
        result = read_session(self.path)
        summary = result["summary"]
        self.assertEqual([summary[key] for key in ("requests", "ready", "failed", "discarded", "pending")], [4, 1, 1, 1, 1])
        self.assertEqual(summary["cards"], ["涅奥的低语"])
        self.assertEqual(summary["floor"], 7)
        self.assertEqual(result["requests"][0]["response"]["reasoning_content"], "卡牌设计")
        self.assertEqual(result["requests"][0]["response"]["content"], "  原始输出\n")
        self.assertEqual(result["requests"][0]["result"]["card_texts"], [["抽1张牌。", "抽2张牌。"]])
        self.assertEqual(result["requests"][1]["response"]["total_tokens"], 500)
        self.assertEqual(result["events"][-1]["kind"], "reward_frozen")

    def test_missing_request_and_diagnostics_still_show_terminal_result(self):
        self.write([event("generation_ready", {"revision": 2, "cards": []})])
        result = read_session(self.path)
        self.assertEqual(result["summary"]["ready"], 1)
        self.assertNotIn("response", result["requests"][0])
        self.assertEqual(result["requests"][0]["timestamp_utc"], "2026-10-05T01:00:00+00:00")

    def test_bom_bad_lines_and_partial_tail_do_not_hide_valid_records(self):
        self.write([event("generation_request", {"revision": 1})])
        text = self.path.read_text(encoding="utf-8")
        self.path.write_text("\ufeff"+text+'\n[]\n{"kind":"bad","payload":null}\n{"unfinished":', encoding="utf-8")
        result = read_session(self.path)
        self.assertEqual(result["summary"]["pending"], 1)
        self.assertEqual(result["warning_count"], 3)
        self.assertEqual([item["line"] for item in result["warnings"]], [3, 4, 5])

    def test_live_append_invalidates_cache_and_deleted_files_disappear(self):
        self.write([event("generation_request", {"revision": 1})])
        store = JournalStore(self.directory)
        self.assertEqual(store.index()["sessions"][0]["pending"], 1)
        with self.path.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(event("generation_failed", {"revision": 1, "reason": "provider_timeout"}))+"\n")
        self.assertEqual(store.index()["sessions"][0]["failed"], 1)
        self.path.unlink()
        self.assertEqual(store.index()["sessions"], [])
        self.assertEqual(store._cache, {})

    def test_invalid_card_payload_is_reported_instead_of_crashing(self):
        self.write([event("generation_ready", {"revision": 1, "cards": None}),
                    event("generation_ready", {"revision": 2, "cards": ["not a card"]}),
                    event("generation_failed", {"revision": 3, "reason": "invalid_response_json_or_schema"})])
        result = read_session(self.path)
        self.assertEqual(result["warning_count"], 2)
        self.assertEqual(result["summary"]["failed"], 1)

    def test_game_mod_and_direct_paths_resolve(self):
        game = Path(self.temp.name) / "game"
        mod = game / "mods" / "NeowsCompany"
        logs = mod / "data" / "generation"
        logs.mkdir(parents=True)
        for path in (game, mod, logs):
            self.assertEqual(generation_directory(path), logs.resolve())
        self.assertEqual(generation_directory(Path(self.temp.name)), self.directory.resolve())

    def test_only_selected_journals_are_readable(self):
        self.write([])
        store = JournalStore(self.directory)
        for name in ("../config.json", "..\\other.jsonl", str(self.path), "config.json", "missing.jsonl"):
            with self.subTest(name=name), self.assertRaises(FileNotFoundError):
                store.session(name)

    def test_http_api_page_and_origin_restrictions(self):
        self.write([event("generation_request", {"revision": 1})])
        server = ThreadingHTTPServer(("127.0.0.1", 0), make_handler(JournalStore(self.directory)))
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            connection = HTTPConnection("127.0.0.1", server.server_port, timeout=5)
            self.addCleanup(connection.close)
            for endpoint in ("/", "/api/sessions", "/api/session?file=combat.jsonl"):
                connection.request("GET", endpoint)
                response = connection.getresponse()
                self.assertEqual(response.status, 200)
                body = response.read()
                if endpoint == "/":
                    self.assertIn("涅奥".encode("utf-8"), body)
                else:
                    self.assertIsInstance(json.loads(body), dict)
            for endpoint, headers, expected in (
                ("/api/session?file=..%2Foutside.jsonl", {}, 404),
                ("/api/session", {}, 400),
                ("/config.json", {}, 404),
                ("/api/sessions", {"Origin": "https://example.com"}, 403),
                ("/api/sessions", {"Host": "example.com"}, 403),
            ):
                connection.request("GET", endpoint, headers=headers)
                response = connection.getresponse()
                self.assertEqual(response.status, expected)
                response.read()
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=5)


if __name__ == "__main__":
    unittest.main()
