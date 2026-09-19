# Data — the offline buffer

A SQLite database at `%LOCALAPPDATA%\CallCenter\agent-buffer.db`.

Its job is A-04: when the server is unreachable, what the agent did is queued
here and replayed once the connection returns, so no interaction is lost during
a network, VPN or server outage.

## What is in it

One table, `pending_uploads`, holding every kind of thing waiting to be sent.
A row says what kind it is and carries the request body as JSON.

| Kind | Requirement | Written yet |
| --- | --- | --- |
| `Call` | A-14 — a finished call | **Yes** |
| `Classification` | A-40 — what the call was about | No; the form does not exist |

## Why one generic table rather than a table per kind

**Ordering.** A classification belongs to a call. Replayed out of order, a
classification arrives for a call the server has never heard of. One table with
one auto-incrementing id keeps everything in the order it happened, whatever it
is. That is also why a failed send stops the flush rather than skipping ahead.

**It never has to change shape.** Adding notes or app orders later is a new
`Kind`, not a migration. That is what makes it safe to create this database with
`EnsureCreated()` instead of shipping migration tooling to every agent's laptop —
a buffer that needed a migration would have to choose between losing an offline
shift's work and running migrations on a machine nobody administers.

## How a call gets here

1. The call ends and `CallService` raises it
2. `CallLogQueue` writes it to this database — **before** anything is sent
3. `CallLogReporter` tries to send everything waiting, oldest first
4. A row is deleted only once the server has acknowledged it

A send that fails for a reason that might clear — unreachable, a 500, an expired
token — leaves the row and stops, so the order holds. A refusal the server is
certain about (`unknown_value`, `invalid_request`) will never succeed however
often it is retried, and would block every row behind it, so it is discarded with
an error in the log.

Resending is safe: the server keys a call on its SIP Call-ID and extension, so a
call sent twice updates rather than duplicates. Nothing here has to work out what
already arrived.

## History

This started as one JSON object per line in `pending-calls.jsonl`, because a list
of calls needs nothing more and `SQLitePCLRaw` was pinned to a version carrying
CVE-2025-6965. That advisory is fixed by pinning 2.1.12 in
`Directory.Packages.props`, and with classifications about to join the queue —
two kinds of row that must arrive in order — a database earns its place.

The old file is imported and deleted on first start, so a laptop that had calls
waiting does not lose them.
