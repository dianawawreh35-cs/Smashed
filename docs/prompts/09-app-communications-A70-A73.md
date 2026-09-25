# App communications: the conversations that don't arrive by phone (A-70 to A-73)

> **Replaced by `14-applications-A70-A73.md` (25 Sep).** Dia wants application communications
> on their own screens in both apps and with their own reports. Use prompt 14, not
> this one.

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

**This is the largest unbuilt piece of the whole system, and nothing of it
exists.** Three Musts and a Should, untouched, sitting behind the telephony work
that has taken the last week.

Customers reach Smashed Burger on WhatsApp, Facebook, Instagram and Wheels as
well as by telephone. Today an agent who takes an order on WhatsApp has nowhere
to put it. It never reaches the customer's history, and it never reaches a single
supervisor report. Every number the client sees is therefore a count of *phone
calls*, presented as if it were a count of *business*.

Read the A-70, A-71, A-72 and A-73 rows in the SRS. Then read these
`docs/DECISIONS.md` entries, because this feature is deliberately built on top
of what they describe rather than beside it:

- "Classification, part 1: the server" (21 Sep) — the dynamic form.
- "Classification, part 2: the form the agent fills in" (21 Sep).
- "Only answered calls are classified" (24 Sep).
- Whatever the newest "Open items (live)" section says about A-70.

Also read `docs/SCHEMA.md` on `communications` and `channels`.

## What already exists, and is the whole point

**The database was designed for this from the start.** `communications` has a
`kind` column with values `Call` and **`App`**, a `channel_id`, and a `direction`
that allows **`None`** precisely because an app entry has no direction. The
`channels` table exists and is seeded. `CommunicationKinds.App`,
`Directions.None` and `CommunicationStatuses.Logged` are all defined in
`CallCenter.Shared` and none of them has ever been written by anything.

SCHEMA.md says it outright: *"One table for phone and app makes every report one
query."*

So this is **not** a new table, a new list or a new report. It is a second way
of creating a row in the table the whole system already reads. If you find
yourself designing a parallel set of screens and queries for app messages, stop
and re-read this paragraph.

## What to build

**A-70, recording one.** An agent records a communication that arrived by app.
Fields per the SRS row: channel (WhatsApp, Facebook, Instagram, Wheels, other),
the customer, and then **the same classification form a call uses** — type,
branch, order value, notes, follow-up. Reuse `ClassificationFormViewModel`; do
not write a second form. The contact is found the way the pop-up finds one, by
number through `PhoneNormalizer`, and an unknown number can create a contact
exactly as A-11 does.

**A-71, the agent's own list.** Today's entries, editable. Older ones read-only
for agents. `CallEditWindow` already implements exactly this rule for calls
(A-42) — supervisors always, agents until the window closes. Reuse it rather
than inventing a second definition of "today".

**A-72, it shows up everywhere calls do.** The contact's history panel and every
supervisor report, with the channel name. If the row is written correctly this
should cost almost nothing, and that is the test of whether it was written
correctly.

**A-73, quick entry (Should).** Start typing a number to pick the customer.
Channel and branch remembered for the session. Do this last, and drop it if the
rest is taking longer than expected.

## Decisions to make before building

Ask me about these. They change what the client gets.

1. **Where does this live in the Agent App?** A new section in the rail beside
   Call log and Contacts is the obvious answer. Confirm the Arabic label with me.
2. **What does an app communication use for the `sip_call_id` the classification
   is keyed on?** Calls use the PBX's reference. App entries have none. Look at
   how `ClassifyByCall` works and decide whether app entries classify by
   communication id instead — and say which you chose and why.
3. **Can an agent delete one?** The SRS does not say. Calls cannot be deleted.
   My instinct is edit within the window, never delete, but ask.

## What to leave alone

- **Don't touch `CallService.cs`.** Nothing here goes near the phone. If you
  think it does, you have misread the task.
- **Don't add a new status value.** `Logged` already exists for exactly this.
- **Don't build supervisor screens.** They come with the call search
  (`01-call-search-S02-S03.md`), which is explicitly shaped to take app
  communications later. Make the server side return them; leave the browser to
  that task.

## When it is done

Everything in the house rules, and specifically:

- **Database-backed tests.** An app communication is a row with unusual values
  in three columns; the check constraints are the thing most likely to reject
  it. Use `[DatabaseFact]`.
- A test that an app communication **appears in a contact's history beside
  calls** (A-72). That is the requirement most likely to be quietly missed,
  because it passes by doing nothing.
- The Agent App screens are UI: add checklist steps and **ask for a screenshot
  in both languages** before calling it done.
