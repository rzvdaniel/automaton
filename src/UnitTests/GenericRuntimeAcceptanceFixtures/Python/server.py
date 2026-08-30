import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path.startswith("/health"):
            body = b"ready"
        else:
            version = os.environ.get("ACCEPTANCE_VERSION", "unknown")
            body = (
                f"python:{version}:{self.path}:"
                f"{os.environ.get('AUTOMATON_APP_DATA_DIR', '')}"
            ).encode("utf-8")

        self.send_response(200)
        self.send_header("Content-Type", "text/plain")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        return


port = int(os.environ["AUTOMATON_PORT"])
ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
