import json
import threading
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


_REQUESTS = []
_LOCK = threading.Lock()
_MAX_REQUESTS = 200


def _append_request(entry):
    with _LOCK:
        _REQUESTS.append(entry)
        del _REQUESTS[:-_MAX_REQUESTS]


class AlertWebhookHandler(BaseHTTPRequestHandler):
    server_version = "alert-webhook-mock/1.0"

    def do_GET(self):
        if self.path == "/health":
            self._write_json(200, {"status": "ok"})
            return

        if self.path == "/requests":
            with _LOCK:
                payload = list(_REQUESTS)
            self._write_json(200, payload)
            return

        self._write_json(404, {"error": "not_found"})

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        raw_body = self.rfile.read(length) if length > 0 else b""
        body_text = raw_body.decode("utf-8", errors="replace")

        try:
            parsed_body = json.loads(body_text) if body_text else None
        except json.JSONDecodeError:
            parsed_body = None

        entry = {
            "receivedAtUtc": datetime.now(timezone.utc).isoformat(),
            "method": self.command,
            "path": self.path,
            "headers": {key: value for key, value in self.headers.items()},
            "body": parsed_body if parsed_body is not None else body_text,
        }

        _append_request(entry)
        print(json.dumps(entry, ensure_ascii=True), flush=True)
        self._write_json(200, {"status": "accepted"})

    def log_message(self, format, *args):
        return

    def _write_json(self, status_code, payload):
        body = json.dumps(payload, ensure_ascii=True).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    server = ThreadingHTTPServer(("0.0.0.0", 8080), AlertWebhookHandler)
    print("alert-webhook-mock listening on :8080", flush=True)
    server.serve_forever()