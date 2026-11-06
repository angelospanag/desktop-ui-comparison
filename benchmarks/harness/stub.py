"""A local stand-in for the Open-Meteo API, so network time doesn't affect the results."""

import http.server
import json

BODY = json.dumps({
    "current": {
        "time": "2026-11-06T08:00",
        "temperature_2m": 10.2,
        "apparent_temperature": 8.1,
        "relative_humidity_2m": 73,
        "wind_speed_10m": 6.7,
        "weather_code": 1,
    },
    "daily": {
        "time": ["2026-11-06", "2026-11-07", "2026-11-08", "2026-11-09", "2026-11-10"],
        "weather_code": [2, 3, 2, 61, 61],
        "temperature_2m_max": [15.9, 15.0, 14.1, 13.8, 18.8],
        "temperature_2m_min": [9.0, 7.9, 8.0, 8.0, 11.1],
    },
}).encode()


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(BODY)))
        self.end_headers()
        self.wfile.write(BODY)

    def log_message(self, *args):
        pass


http.server.ThreadingHTTPServer(("127.0.0.1", 8765), Handler).serve_forever()
