# Export the blocked list for the Issabel PBX (S-46)

First read `docs/prompts/00-house-rules.md` and follow it.

## Why it matters

Today a blocked caller is only **declined** by each agent's app in turn (A-17).
The PBX still lets them into the queue, and may pass them to the next agent.
The server can't reach the PBX (no inbound port; see the 19 Sep entry "No
inbound port to the PBX"). So **a file the Issabel administrator loads by hand
is the only way to block at the PBX.**

Read the S-46, S-45 and A-17 rows in the SRS, and the "Blocked numbers: the
export stops being optional" part of the 19 Sep entry.

## Find out first, don't guess

**What format does Issabel's Blacklist accept for loading many numbers at
once?** Find out from Issabel's own documentation or its source, and tell me
where you found it. We have administrative access to the Issabel box, so if the
only reliable answer is to look at its screen, give me the exact steps and I'll
check. If there is no bulk import at all, say so. The fallback might be a list
to paste, or a command for the Asterisk console, but ask me before choosing.

**What number format will the blacklist actually match?** The PBX announces
callers as `+970598214351` (see the 22 Sep contacts-seed entry), and a stored
number may be `0598214351`. Export the form the PBX delivers, and say whether
the same number should be exported in more than one form to be safe.

## What to build

- A supervisor-only download in the supervisor web app, next to where blocked
  contacts are listed, that produces the file in that format.
- Include every number on every Blocked contact, not just the first.
- A short section in `docs/DEPLOY-server-runbook.md` for whoever administers
  Issabel: how to load the file, and how often. Also update "For whoever
  administers the Issabel PBX" in the open items.

## Tests

Server tests for the file's contents: numbers in the right form, no duplicates,
unblocked contacts left out, and 403 for agents.
