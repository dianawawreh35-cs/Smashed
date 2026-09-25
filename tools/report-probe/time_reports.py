"""
Times every report endpoint over a year of data (N-02: "report generation
under 5 s for one year of data at the expected volume").

Run against a server pointed at a scratch database filled by fill.sql - never
at the development database. See README.md.

    py tools/report-probe/time_reports.py --base http://127.0.0.1:5055 \
        --login probe-supervisor --password "ProbePass!2026"

Each report is fetched --runs times (the first warms the server's caches)
and the slowest and median of the rest are printed, with how many rows came
back. Standard library only.
"""

import argparse
import http.client
import json
import statistics
import time
from datetime import datetime, timedelta, timezone
from urllib.parse import quote, urlsplit

REPORTS = [
    "/api/reports/calls/summary?groupBy=day",
    "/api/reports/calls/summary?groupBy=month",
    "/api/reports/calls/by-type",
    "/api/reports/calls/breakdown?groupBy=day",
    "/api/reports/calls/breakdown?groupBy=agent",
    "/api/reports/calls/recurring-customers",
    "/api/reports/calls/complaints",
    "/api/reports/calls/complaints/by?groupBy=branch",
    "/api/reports/calls/repeat-complainers",
    "/api/reports/calls/orders?groupBy=channel",
    "/api/reports/calls/peak-hours",
    "/api/reports/calls/missed?groupBy=day",
    "/api/reports/calls/missed/list",
    "/api/reports/calls/orders-by-channel",
    "/api/reports/calls/orders-trend?groupBy=month",
    "/api/reports/calls/cancellations?groupBy=branch",
    "/api/reports/calls/cancellations/list",
    "/api/reports/calls/agents",
    "/api/reports/calls/customers?groupBy=month",
    "/api/reports/calls/top-customers?by=value",
    "/api/reports/calls/inactive-customers?days=30",
    "/api/reports/calls/data-quality",
    "/api/reports/calls/unknown-numbers",
    "/api/reports/calls/duplicate-names",
    "/api/reports/dashboard/today",
    "/api/reports/dashboard/period",
    "/api/reports/applications/by-channel",
    "/api/communications/search?pageSize=50",
    "/api/communications/search/export?lang=en",
]


def sign_in(conn: http.client.HTTPConnection, login: str, password: str) -> str:
    conn.request("POST", "/api/auth/login", json.dumps({"login": login, "password": password}),
                 {"Content-Type": "application/json"})
    response = conn.getresponse()
    payload = response.read()
    if response.status != 200:
        raise SystemExit(f"sign-in failed: {response.status} {payload[:200]!r}")
    return json.loads(payload)["accessToken"]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default="http://127.0.0.1:5055")
    parser.add_argument("--login", required=True)
    parser.add_argument("--password", required=True)
    parser.add_argument("--runs", type=int, default=4)
    args = parser.parse_args()

    parts = urlsplit(args.base)
    conn = http.client.HTTPConnection(parts.hostname, parts.port, timeout=120)
    token = sign_in(conn, args.login, args.password)

    now = datetime.now(timezone.utc)
    period = f"from={quote((now - timedelta(days=366)).isoformat())}&to={quote((now + timedelta(days=1)).isoformat())}"

    print(f"{'report':58} {'rows':>8} {'median s':>9} {'max s':>7}")
    for path in REPORTS:
        url = f"{path}{'&' if '?' in path else '?'}{period}"
        times, size, status = [], 0, 0
        for _ in range(args.runs):
            start = time.perf_counter()
            conn.request("GET", url, headers={"Authorization": f"Bearer {token}"})
            response = conn.getresponse()
            body = response.read()
            times.append(time.perf_counter() - start)
            status = response.status
        if status != 200:
            print(f"{path:58} HTTP {status} {body[:120]!r}")
            continue
        if path.endswith("export?lang=en"):
            size = body.count(b"\r\n") - 1
        else:
            data = json.loads(body)
            size = len(data) if isinstance(data, list) else data.get("total", len(data)) if isinstance(data, dict) else 0
        warm = times[1:] or times
        print(f"{path:58} {size:>8} {statistics.median(warm):>9.2f} {max(warm):>7.2f}")


if __name__ == "__main__":
    main()
