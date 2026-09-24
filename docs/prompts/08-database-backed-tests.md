# Database-backed tests for login, call logging and flags

First read `docs/prompts/00-house-rules.md` and follow it.

## Why now

"Known gaps in what is built" in `docs/DECISIONS.md` lists three areas whose
real behaviour has no test, because the suite used to run without PostgreSQL.
Since 23 Sep, CI runs the server tests on Linux against a real `postgres:16`
(see the 23 Sep entry "CI gets a database" and `.github/workflows/`). The
reason is gone. The tests aren't written.

Remember that **every login used to answer 500 while 176 tests were green,
because none of them posted a login.** That is the kind of thing these tests are
for.

## What to cover

Use the existing `CallCenterApiFactory`, and follow how it decides a database is
present. Test through the HTTP endpoints, as the client does, not by calling
services directly.

**Login**
- A real login for an agent and for a supervisor: the response has what each
  app needs. For an agent, that includes the extension and the SIP details.
- Wrong password, disabled account, and the password lockout, including the way
  out (`reset-password`; see the 19 Sep entry).

**Call logging** (`POST /api/communications/calls`)
- A reported call is matched to the right contact by number, whatever the
  number's format.
- Reporting the same call twice updates the row instead of inserting a second
  one. The 19 Sep entry says the design depends on this.
- A call from an unknown number is stored with no contact.
- Blocked, Missed, Rejected and NoAnswer are all stored as reported.

**Flags** (VIP and Blocked)
- Flagging a number that belongs to a contact flags that contact.
- Flagging a bare number that nobody has creates a nameless contact.
- The audit record is written with who and when.

## Rules

- Each test sets up its own data and doesn't depend on the order tests run in.
  Look at how the existing database-backed tests isolate themselves, and do the
  same.
- **The suite must still pass without a database.** Locally, without
  `ConnectionStrings__Default`, these tests are skipped, not failed. Check what
  the existing ones do.
- Tell me how to run them locally against the dev database, in plain steps, so
  I'm not relying on CI alone.

## When done

Run them locally against PostgreSQL and report the numbers. Then remove the three
"no database-backed test" items from "Known gaps in what is built", along with
the general note at the top of that list if nothing is left under it.
