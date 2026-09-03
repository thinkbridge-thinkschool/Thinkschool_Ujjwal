#!/usr/bin/env python3
"""
Standalone load harness for GET /api/quotes - not part of the app.

Fires N genuinely concurrent requests at a single page/size (so they all
target the exact same cache key), and reports:
  - how many of those requests actually reached the database
    (via the app's own /api/diagnostics/db-hits counter, not inferred)
  - p50 / p99 latency
  - measured queries/sec and requests/sec over the burst's wall-clock time

Usage:
  python3 load_test.py --base-url http://localhost:5296 --n 50 --page 1 --size 10
"""
import argparse
import asyncio
import time

import aiohttp


async def reset_counters(session: aiohttp.ClientSession, base_url: str) -> None:
    async with session.post(f"{base_url}/api/diagnostics/db-hits/reset") as resp:
        resp.raise_for_status()


async def read_counters(session: aiohttp.ClientSession, base_url: str) -> dict:
    async with session.get(f"{base_url}/api/diagnostics/db-hits") as resp:
        resp.raise_for_status()
        return await resp.json()


async def one_request(session: aiohttp.ClientSession, url: str) -> tuple[float, int]:
    start = time.perf_counter()
    async with session.get(url) as resp:
        await resp.read()
        elapsed_ms = (time.perf_counter() - start) * 1000.0
        return elapsed_ms, resp.status


def percentile(values: list[float], p: float) -> float:
    if not values:
        return float("nan")
    s = sorted(values)
    idx = min(len(s) - 1, int(round((p / 100.0) * (len(s) - 1))))
    return s[idx]


async def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://localhost:5296")
    parser.add_argument("--n", type=int, default=50)
    parser.add_argument("--page", type=int, default=1)
    parser.add_argument("--size", type=int, default=10)
    args = parser.parse_args()

    url = f"{args.base_url}/api/quotes?page={args.page}&size={args.size}"

    async with aiohttp.ClientSession() as session:
        await reset_counters(session, args.base_url)

        wall_start = time.perf_counter()
        # asyncio.gather with tasks created up front fires them all before
        # any awaits, which is what "genuinely concurrent" requires here -
        # not a sequential loop that happens to use async/await syntax.
        tasks = [asyncio.create_task(one_request(session, url)) for _ in range(args.n)]
        results = await asyncio.gather(*tasks)
        wall_elapsed = time.perf_counter() - wall_start

        counters = await read_counters(session, args.base_url)

    latencies = [r[0] for r in results]
    statuses = [r[1] for r in results]
    ok_count = sum(1 for s in statuses if s == 200)

    db_hits = counters["quotesQueries"]
    p50 = percentile(latencies, 50)
    p99 = percentile(latencies, 99)
    qps = db_hits / wall_elapsed if wall_elapsed > 0 else float("nan")
    hit_rate = (args.n - db_hits) / args.n if args.n > 0 else float("nan")

    print(f"requests:          {args.n} (200 OK: {ok_count})")
    print(f"wall time:         {wall_elapsed * 1000:.1f} ms")
    print(f"db hits (quotesQueries): {db_hits}")
    print(f"db queries/sec:    {qps:.2f}")
    print(f"cache hit rate:    {hit_rate * 100:.1f}%  ((requests - db_hits) / requests)")
    print(f"p50 latency:       {p50:.2f} ms")
    print(f"p99 latency:       {p99:.2f} ms")


if __name__ == "__main__":
    asyncio.run(main())
