# Supervisor contact import from Excel/CSV (A-64)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

A-64 (a *Should*) lets a supervisor load contacts from an Excel or CSV file.
It isn't the one-off seed of the old system's 15,358 customers. That is done,
and ships inside the server. This is the supervisor's own ongoing import.

Read the A-64 and A-63 rows in the SRS, and the 22 Sep `docs/DECISIONS.md`
entry "The old system's 15,000 customers ship inside the server". It explains
how the seed handles duplicates, normalising and the city column, and **why the
server deliberately doesn't read Excel**.

## The design question to settle first

That entry rejected a spreadsheet parser as a permanent server dependency
*for a one-off seed*. A-64 isn't one-off, so the question is open again. Options
include parsing in the browser and sending the rows as JSON, accepting CSV only
on the server, or adding a parser after all. Recommend one, with the trade-off,
and **ask me before building**.

## Rules the import must keep

- **Show a preview before anything is saved.** Show how many rows will be added,
  which are skipped and why, and let the supervisor cancel.
- **Numbers go through `PhoneNormalizer` on the server**, the same as the seed.
  Two implementations of the rules would drift apart.
- **The same duplicate rules as the seed**, reusing its code where possible: a
  number already on a contact isn't added twice. Say what happens to a row whose
  number belongs to an existing contact. Is it skipped, or does it fill in a
  missing address? Ask me.
- **Names are never matched automatically** (A-63). A matching name isn't a
  reason to merge.
- The supervisor chooses which column holds which field, or the page states the
  expected column headings. Recommend one.
- Arabic text must survive the round trip, including a CSV saved from Excel
  in a non-UTF-8 encoding.

## Tests

Server tests for the import rules, database-backed. A web page test for the
preview. Add the steps to `docs/TESTING-checklist.md`, with a small sample file
committed under `tests/` to test with.
