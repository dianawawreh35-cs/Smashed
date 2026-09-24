# Supervisor call search and call details (S-02, S-03)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

Give the supervisor web app a way to **find any call and open it**. There is
none today. Calls are recorded (A-14), classified (A-40 to A-43) and noted, but
only the agent's own log (A-50) and a contact's history (A-62) show them.

Read the S-02, S-03 and S-04 rows in the SRS, and these `docs/DECISIONS.md`
entries: "The call log filters on the server" (20 Sep), "Only answered calls are
classified" (24 Sep) and "Call recording, part 1" (24 Sep).

## What to build

**Search (S-02):** a supervisor-only page listing calls from every agent,
filtered on the server and paged. Filters from S-02 that have data today:
phone number, customer name, agent, branch, type, date range, notes text
(classification notes *and* the call's own `communications.notes`), order
value range and status. Direction too, since there are inbound and outbound
calls.

**Details (S-03):** opening a row shows agent, customer (linked to the contact),
branch, date and time, duration, direction, status, type, the classification's
answers, notes, order value, and the **audit trail** of classification changes
(A-43 already stores it).

**Editing (S-04, second half):** a supervisor may edit any classification at any
time. `CallEditWindow` already allows supervisors always. Check whether the
supervisor web app can reuse the form it already has, or needs one. If it is
more than a small addition, tell me and we'll split it into its own task.

## What to leave room for, but not build

- **Recordings.** Upload (A-31) and serving (S-04) don't exist yet. Leave a
  clear place in the details view for the player, and in the filters for "has
  recording". Don't show a player, or a filter that does nothing.
- **App communications (A-70 to A-72)** aren't built. S-02 says "calls, app
  communications and contacts", so shape the endpoint and page so that app
  communications can join the same list later. Don't invent them now.
- **Channel** is an S-02 filter, but it only means something for app
  communications. Leave it out until they exist.

## Things to check, not assume

- Where order value and branch actually live (on the classification's answers,
  or as columns) decides how they can be filtered. Look before designing the
  query.
- Performance matters (N-02): up to about 500 communications a day, so plan for
  a year of data. Check the indexes the filters need, and add a migration if
  they are missing.
- Agents must get 403 from every endpoint here (A-52). Test that.

## Tests

- Server tests for each filter, paging, the 403 for agents, and the details
  response. These are database-backed tests, since the query is the thing being
  tested.
- A web page test beside the existing `*.test.tsx` files.
- Add the manual checks to `docs/TESTING-checklist.md`.
