"""
Fires N requests at the API at the same instant and reports how they fared.

Written for the concurrency item in docs/DECISIONS.md: "authenticated requests
collapse when they arrive together". One request at a time answered in 10 ms;
39 at once, signed in, answered 7 of 39 in 25 s. This is that measurement, kept
so it can be run again rather than rebuilt from a paragraph.

    py tools/load-probe/probe.py --base http://127.0.0.1:5055 --path /api/menu \
        --login test-supervisor-xxxx --password "correct horse battery" --count 39

Without --login the requests go unsigned, which is the comparison that matters:
the same path signed and unsigned tells you whether the problem is in signing
in. Standard library only, so it runs on any machine with Python 3.
"""

import argparse
import http.client
import json
import statistics
import threading
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from urllib.parse import urlsplit


def sign_in(base: str, login: str, password: str, laptop: str | None) -> str:
    parts = urlsplit(base)
    conn = http.client.HTTPConnection(parts.hostname, parts.port, timeout=30)
    body = {"login": login, "password": password}
    if laptop:
        body["laptopId"] = laptop
    conn.request("POST", "/api/auth/login", json.dumps(body), {"Content-Type": "application/json"})
    response = conn.getresponse()
    payload = response.read()
    if response.status != 200:
        raise SystemExit(f"sign-in failed: {response.status} {payload[:200]!r}")
    return json.loads(payload)["accessToken"]


def one_request(base: str, path: str, token: str | None, start: threading.Barrier, timeout: float):
    parts = urlsplit(base)
    # A connection per request, as a burst of browser or app requests would be.
    conn = http.client.HTTPConnection(parts.hostname, parts.port, timeout=timeout)
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    start.wait()
    began = time.perf_counter()
    try:
        conn.request("GET", path, headers=headers)
        response = conn.getresponse()
        response.read()
        return response.status, time.perf_counter() - began
    except Exception as ex:  # a timeout is a result here, not a crash
        return type(ex).__name__, time.perf_counter() - began
    finally:
        conn.close()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base", default="http://127.0.0.1:5000")
    parser.add_argument("--path", default="/api/menu")
    parser.add_argument("--count", type=int, default=39)
    parser.add_argument("--rounds", type=int, default=1)
    parser.add_argument("--login")
    parser.add_argument("--password")
    parser.add_argument("--laptop", help="send a laptop id, as the Agent App does")
    parser.add_argument("--timeout", type=float, default=30.0)
    args = parser.parse_args()

    token = sign_in(args.base, args.login, args.password, args.laptop) if args.login else None
    label = "signed in" if token else "unsigned"

    for round_no in range(1, args.rounds + 1):
        start = threading.Barrier(args.count)
        began = time.perf_counter()
        with ThreadPoolExecutor(max_workers=args.count) as pool:
            results = list(pool.map(
                lambda _: one_request(args.base, args.path, token, start, args.timeout),
                range(args.count)))
        wall = time.perf_counter() - began

        statuses = Counter(str(status) for status, _ in results)
        ok = sum(1 for status, _ in results if status == 200)
        times = sorted(seconds * 1000 for _, seconds in results)
        print(
            f"round {round_no}: {args.path} {label} x{args.count}: "
            f"{ok}/{args.count} ok in {wall:.2f}s | "
            f"ms min {times[0]:.0f} median {statistics.median(times):.0f} max {times[-1]:.0f} | "
            f"{dict(statuses)}")


if __name__ == "__main__":
    main()
