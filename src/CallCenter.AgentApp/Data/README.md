# Data — the offline buffer

A SQLite database at `%LOCALAPPDATA%\CallCenter\agent-buffer.db`.

Its job is A-04: when the server is unreachable, what the agent did is queued
here and replayed once the connection returns, so no interaction is lost during
a network, VPN or server outage.

## What is in it

One table, `pending_uploads`, holding every kind of thing waiting to be sent.
A row says what kind it is and carries the request body as JSON.

| Kind | Requirement |
| --- | --- |
| `Call` | A-14 — a finished call |
| `Classification` | A-40 — what the call was about |
| `Notes` | A-41 — why an outgoing call went unanswered |
| `Recording` | A-31 — the path of the call's audio on this laptop |

Every row carries the id of the agent who was signed in when it was made
(`UserId`, F-03), and is replayed only while they are signed in again. Rows
from before that column existed are given to the next agent to sign in, once,
and the log says how many.

## Why one generic table rather than a table per kind

**Ordering.** A classification belongs to a call. Replayed out of order, a
classification arrives for a call the server has never heard of. One table with
one auto-incrementing id keeps everything in the order it happened, whatever it
is.

**It rarely has to change shape.** Adding a kind is not a migration. That is
what makes it safe to create this database with `EnsureCreated()` instead of
shipping migration tooling to every agent's laptop. The one change so far
(27 Sep, `UserId` and `SetAsideAt`) added two nullable columns in place, in
`AgentBufferDbContext.UpgradeAsync`; any later one should do the same.

## How a call gets here

1. The call ends and `CallService` raises it
2. `CallLogQueue` writes it to this database — **before** anything is sent
3. `CallLogReporter` tries to send everything waiting, oldest first, recordings last
4. A row is deleted only once the server has acknowledged it

If the buffer cannot be written, the call is sent directly, once, and the agent
is told if that fails too (M-A03).

## When a send fails (F-08)

- **The server cannot be reached, or the sign-in has ended (401):** the pass
  stops, and nothing counts against the row.
- **The server refuses it for good** (`bad_request`, `extension_not_yours`,
  `invalid_request` and the rest in `CallLogReporter.IsPermanent`): the row is
  **set aside** at once.
- **A server error, or an upload that ran out of time:** the row is skipped
  for this pass, with whatever belongs to its call, and the pass carries on. It
  counts against the row only if something after it went through in the same
  pass. After five such failures it is set aside.
- **A classification, note or recording whose call is not on the server yet**
  waits: that is the normal case during a call. After a day with no call in the
  queue it is set aside.

Set aside means `SetAsideAt` is filled in and the row is no longer tried. It
stays in the file, and the call log shows the agent how many, with a Try again
button that puts them back in their old places. Whatever belongs to a call that
was set aside is set aside with it.

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
