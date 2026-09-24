# The agent's call details, and playing their own recording (A-50, A-51)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

Double-clicking a call in the agent's log opens the classification form and
nothing else. A-51 asks for more, and it is a Must:

> Open any own call: view details, **play the recording (play/pause/seek)**, and
> — subject to A-42 — classify or edit the classification of an answered call,
> or write or edit the note on a missed, rejected or unanswered one (A-41).

A-50 also asks for a **recording indicator** in the list itself, so an agent can
see at a glance which calls have audio. That is missing too.

Read the A-50, A-51, A-52 and A-42 rows in the SRS, section 9's acceptance
criteria ("the recording is playable from the agent's call log and from the
supervisor web app"), and these `docs/DECISIONS.md` entries:

- "Call recording, part 1" and "part 2" (24 Sep).
- "Classification, part 4: the way back to a skipped call" (21 Sep) — how
  opening a logged call already works.
- "Only answered calls are classified" (24 Sep).

## What exists

The classification half is built: double-click already opens the form, prefilled
for a call that has one, and the note editor for calls that take a note.

The recording half is not. Capture and upload work (A-30 to A-32). The endpoints
that serve a recording are being built separately under
`11-recording-retention-and-playback-A33-S04.md` — **check whether they exist
before you start**, and if they do not, ask me rather than building your own.

The access rule is already settled: a supervisor may play any recording, an
agent may play **their own only** (A-52). The server enforces it; do not
re-implement that check in the app.

## What to build

**The recording indicator (A-50).** A mark in the call log on rows that have
audio. `CommunicationDto` does not carry this today, so it needs a field — a
boolean is enough for the list. A recording deleted by retention (A-33) should
read as **expired**, not as never recorded, so consider whether one boolean is
honest enough or whether the list needs to tell those apart.

**The details view (A-51).** Opening a call shows its details and, when there is
audio, a player with play, pause and seek. Everything the form already does must
keep working.

Decide and tell me: does this **replace** the current double-click behaviour, or
sit around it? The form is what an agent opens a call for most of the time, so
burying it behind a details screen would be a step backwards.

## The trap on this screen

The call log's `DataGrid` has had two faults already — see the 22 Sep entries
"the grid was sorting itself" and "the last column arrived cut off". Both are
fixed **on this grid only**. If you add a column, keep `CanUserSortColumns` off
and give the new column a `MinWidth`, or you will reproduce them.

## Audio in WPF

There is no player control in this project yet. `MediaElement` is in the box and
can play the mu-law WAV the app produces. Two warnings:

- **An unstyled WPF control does not inherit the dark theme.** That has caught
  this project four times. Whatever you add, style it.
- The audio comes from the server over HTTP with a bearer token. `MediaElement`
  will not send that header, so you will most likely need to fetch the bytes and
  play from a stream or a temporary file. Work this out early — it is the part
  most likely to be awkward.

## What to leave alone

- **`CallService.cs`.** Nothing here touches a live call.
- **The supervisor web app.** Its player belongs to `01-call-search-S02-S03.md`.
- **Click-to-call buttons** on this same grid — that is
  `10-click-to-call-and-redial-A20-A22.md`. If both tasks are live at once, say
  so and we will run them one after the other; you will both be editing
  `CallLogView.xaml`.

## When it is done

Everything in the house rules, and:

- Checklist steps for playing a recording, for a call with none, and for one
  whose recording has expired.
- **Ask for a screenshot in both languages**, with the player visible. This is
  the screen an agent uses most.
