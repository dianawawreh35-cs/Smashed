# Merging two contacts (A-63)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

A-63 says names are never matched automatically, and **merging two existing
contacts is a separate, deliberate action**. Creating and editing contacts
exists; merging doesn't. Build it.

Read the A-63, A-62, S-45 and N-06 rows in the SRS, and these
`docs/DECISIONS.md` entries: "The old system's 15,000 customers ship inside the
server" (22 Sep, the part about duplicates) and "The flags move onto the
contact" (18 Sep).

## Questions to bring me before building

1. **Who can merge?** I expect supervisors only, in the supervisor web app,
   because a wrong merge mixes two customers' order histories. Confirm, or tell
   me why agents should be able to.
2. **Can a merge be undone?** If not, the confirmation step has to say so
   plainly. If yes, say what it costs to build.

## What a merge has to get right

- **Everything moves to the contact that is kept:** phone numbers, calls
  (`communications`), and whatever else points at a contact. Search the schema
  (`docs/SCHEMA.md`, `Data/Configurations`) for every foreign key to contacts.
  Don't rely on memory.
- **Conflicting fields** (name, address, notes): the supervisor chooses per field,
  side by side. Nothing is silently overwritten.
- **Flags:** decide what happens when one contact is VIP or Blocked and the other
  isn't, and write the rule down. Blocked should probably survive a merge,
  since losing it would let a blocked caller through.
- **Audit (N-06):** record who merged what into what, and when. The flags use the
  log as their audit trail; see how, and do the same unless there's a reason not
  to.
- **Normalised numbers stay unique** (`ux_contact_phones_normalised`).
- The Agent App's block-list cache must still be right after a merge.

## The old customer list

The 22 Sep entry says 110 numbers were repeated in the export, and 69 rows were
skipped entirely. Most of them are the same person entered once in Arabic and
once in English. Those skipped rows are **not in the database**, so a merge
screen can't reach them. Tell me whether they are worth a one-off tool that
reads the seed CSV and offers them as merge candidates. Don't build it without
asking.

## Tests

Database-backed server tests: every related row moves, flags combine as decided,
the audit is written, and agents get 403 if merging is supervisor-only. Add the
steps to `docs/TESTING-checklist.md`.
