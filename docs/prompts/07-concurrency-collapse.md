# Signed-in requests collapse when they arrive together

First read `docs/prompts/00-house-rules.md` and follow it.

## The problem

Read the 21 Sep `docs/DECISIONS.md` section "Open, and genuinely unresolved: the
server collapses under concurrency here". In short:

- 39 requests at once to `/health`, or to `/api/menu` without a token, all
  finish in under 0.1 s.
- 39 at once to `/api/menu` **with** a token: 7 finish in 25 s.
- One at a time, each takes 10 ms.
- During the collapse, `pg_stat_activity` shows only one or two connections,
  and there are eleven-second gaps where nothing is logged.
- Already ruled out: Postgres itself, Docker port forwarding, IPv4/IPv6, log
  volume, OneDrive, and blocking on async calls in our code.

**A call centre with several agents is exactly a burst of signed-in requests**,
so this must be settled before handover.

## How to work

**Diagnose before changing anything.**

1. **Reproduce it** with the same measurement the entry describes. Write that
   measurement as a small script under `tools/` so anyone can rerun it and it
   isn't lost again.
2. **Test the leading hypothesis first:** `EnableRetryOnFailure()` on the Npgsql
   provider. Turn it off, measure again, and turn it back on. If that's the
   cause, find out *why* connections fail under load. Retrying hides the
   failure, it doesn't explain it. Log the exceptions the execution strategy is
   swallowing.
3. If it isn't that, the gap is between "request arrives" and "query runs", and
   only signed-in requests hit it. So look at what signing in adds to each
   request: token validation, anything that loads the user or checks a password
   or key per request, the connection pool settings, and thread pool starvation
   (`dotnet-counters` shows it).
4. **Check production's shape too, if you can:** the server on Linux with
   `network_mode: host`, as in `deploy/`. If it only happens on this machine,
   that is a result worth having. Say how sure you are.

## When done

- Fix the cause, not the symptom. If the right fix is a trade-off, stop and
  explain it before applying it.
- Rerun the measurement, and put the before and after numbers in a new
  `docs/DECISIONS.md` entry.
- Update the "Concurrency" row in "Open items (live)": delete it if it's
  resolved, or say exactly what's left.
- If it can be tested automatically, for example N concurrent signed-in
  requests against the test host finishing in a bounded time, add a test.
