# Messages: the conversations that don't arrive by phone (A-70 to A-73)

First read `docs/prompts/00-house-rules.md` and follow it. **This prompt replaces
`09-app-communications-A70-A73.md`.** Dia changed the shape of the feature on 25
Sep. Where the two disagree, this one wins.

## The job

Customers reach Smashed Burger on WhatsApp, Facebook, Instagram and Wheels as
well as by phone. Today an agent who takes an order on WhatsApp has nowhere to
put it, so it never reaches the customer's history or any report.

Read the A-70, A-71, A-72 and A-73 rows in the SRS, and S-41 (the channel list),
S-05, S-06 and S-07 (export, charts, common filters). Then read these
`docs/DECISIONS.md` entries: the classification entries of 21 Sep (the dynamic
form), "Only answered calls are classified" (24 Sep), "The supervisor can search
every call" (24 Sep), "The supervisor classifies or corrects any call from the
call screen" (25 Sep), and the entry for commit `133e635` (a form per direction).
Read `docs/SCHEMA.md` on `communications`, `channels` and the classification
tables.

## Dia's three requirements, which shape everything

1. **Its own screen in the Agent App, separate from the Call log.** A new rail
   section, *Messages*, with the agent's messages and a way to record one. The
   Call log stays calls only.
2. **Its own page in the supervisor web app, separate from Calls.** *Messages*
   in the sidebar: every agent's messages, searchable, each opening under its
   row as calls do. The Calls page stays calls only.
3. **Its own reports.** A *Message reports* page in the supervisor app, about
   messages, not calls.

Separate **screens** and **reports**, not a separate **table**. That's the one
thing prompt 09 got right and it still stands. `communications` was designed for
this: `kind` takes `Call` and **`App`**, `channel_id` says which app,
`direction` allows **`None`**, and `CommunicationKinds.App`, `Directions.None`
and `CommunicationStatuses.Logged` already exist in `CallCenter.Shared`, never
yet written. A message is a `communications` row with `kind = 'App'`. So a
contact's history (A-72) shows calls and messages together, a combined report
later is one query, and nothing needs a second schema. If you find yourself
designing a `messages` table, stop.

## What to build

### Server

- **Record, edit, list.** Endpoints for an agent to record a message (A-70),
  edit their own within the edit window (A-71, `CallEditWindow`, the same rule
  as calls; supervisors always), and list their own messages. A supervisor
  search endpoint over messages with the call search's filters where they make
  sense: number or name, agent, branch, **channel**, type, dates, notes, order
  value. Server-side filtering and paging, as `CallSearchService` does.
  **Calls-only screens must not start returning messages**: check the call
  search, the Agent App call log and the caller card, and filter them to
  `kind = 'Call'` where they don't already.
- **Classification.** A message is classified with the same form machinery as a
  call: type, branch, order value, notes, follow-up, and the supervisor's own
  questions. Two things block it today, and both are yours to solve. The
  classify endpoint refuses anything not `Answered` (`not_answered`), and the
  forms are per direction (`In`/`Out`) while a message has `None`. Which form
  a message uses is a decision to ask about (below).
- **Channels (S-41, the channel half).** A-70 says the channel list is managed by
  the supervisor. If nothing manages `channels` yet: add, rename, reorder and
  hide a channel. **Phone is a system channel and cannot be hidden or renamed.**
  Renaming must be safe: rows hold the channel's id.
- **Reports.** Endpoints for the message reports below, filtered by S-07's
  common set: period (day, week, month, custom), agent, branch, channel, type,
  and a per day / week / month grouping where it applies.

### Agent App: *Messages*, its own rail section

- **Record a message:** channel, the customer's number (the contact found the
  way the pop-up finds one, through `PhoneNormalizer`; an unknown number can be
  saved as a new customer the way A-11 does), then the classification form.
  **Reuse `ClassificationFormViewModel`**; do not write a second form.
- **The agent's own list** (A-71): today's messages, editable; older ones
  read-only. Opening one shows it under its row or in place, never jumping the
  screen (the 24 Sep smoothness fixes on the web apply in spirit here).
- **Quick entry** (A-73, Should): start typing a number to pick the customer;
  channel and branch remembered for the session. Last, and drop it if the rest
  runs long.

### Supervisor web app

- ***Messages*** page, in the sidebar near Calls: search and filters as above,
  each message opening under its row with its classification, **Edit** and
  **Classify** exactly as calls have them (reuse `ClassificationEditor` and the
  details panel's patterns: no scroll on open, no selected word on double-click,
  the panel drawn from the row at once).
- ***Message reports*** page. At least:
  - messages per channel, and per type within each channel;
  - orders and order value per channel, per branch, per agent, per day (the
    message half of R-12 and R-13);
  - messages per day or hour, a trend chart;
  - per agent: messages recorded, orders, order value, unclassified;
  - cancellations and complaints per channel.

  Each report is a table and, where numeric, a chart (S-06), and can be exported
  to CSV (S-05). The filters are S-07's. Charts: use the `dataviz` skill's
  guidance. Pick one small chart library and say which and why before adding it.
- **Channels** management, if you built the server half (S-41).
- A contact's history already lists calls. It must list **messages too, with
  their channel** (A-72), and open them the same way.

## Decisions to ask me before building

These change what the client gets. Ask; don't guess.

1. **Which classification form does a message use?** Options: the inbound call
   form; a third form, "Messages", designed separately in the Classification
   page; or one form per channel. Recommend one.
2. **Can a message be recorded with the server down?** Calls queue offline. A
   message is typed by the agent, who can simply wait. Recommend queue or not.
3. **Can an agent delete a message?** Calls can't be deleted. My instinct: edit
   within the window, never delete.
4. **The Arabic names** for *Messages* and *Message reports* in both apps.
5. **Does "time" on a message mean when it was recorded, or can the agent set
   when the customer wrote?** Matters for the per-hour reports.

## What to leave alone

- **`CallService.cs` and the SIP layer.** Nothing here touches the phone.
- **No new status.** `Logged` exists for this.
- **The call reports and the dashboard** (R-01 etc., S-20) are not this task. Build
  the report endpoints and page so the call reports can later reuse them, but
  build the message reports only.

## When it is done

Everything in the house rules, and specifically:

- **Database-backed tests** (`[DatabaseFact]`): a message is a row with unusual
  values in three columns, and the check constraints are the likeliest thing to
  reject it. Test recording, the edit window, classification of a message, the
  supervisor search's filters, each report's figures against rows you created,
  and that **calls-only screens return no messages**.
- **A test that a message appears in the contact's history beside calls**
  (A-72). It is the requirement most likely to be quietly missed.
- Web tests for the Messages page, the reports page (figures, filters, export)
  and the channel management.
- The Agent App screens are UI: add checklist steps and **ask for a screenshot in
  both languages** before calling them done.
- Several sessions share this working copy: stage files by name, and say in a
  commit which parts of a shared file (`App.xaml.cs`, the rail in
  `HomeView.xaml.cs`, the language files, `AppLayout.tsx`) are yours.
