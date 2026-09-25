# Managing branches: create, rename, disable (S-41, the branch half)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

S-41 asks the supervisor to manage the **channels** list and the **branches**.
Channels were built on 25 Sep (Settings, `ChannelsCard`, `ChannelsService`).
**Branches are still read-only:** the four seeded at install (رافات, بطن الهوى,
ايكون, نابلس) can't be added to, renamed or retired from the app. Read the S-41
row in the SRS. It says renaming must be safe, because everything refers to a
branch by id, never by name.

## Build it the way channels were built

Read `ChannelsService`, `ChannelsController`, `ChannelsCard.tsx` and their tests
first, and follow them: the same card on the Settings page, the same verbs, the
same refusals, the same tests. Two lists managed two ways would be worse than
either.

- **Add** a branch; **rename** one; **reorder** them; **disable** one
  (hide it from new use). **Never delete:** calls, classifications, delivery
  areas and reports hold branch ids, and a deleted branch would leave them
  pointing at nothing.
- Names are required, trimmed, and unique. Check how the unique index on
  `branches.name` compares, and whether two spellings of one Arabic name
  should count as the same (the contacts' `NameNormalizer` exists for exactly
  that).
- Supervisor-only endpoints. Today's `GET /api/branches` lives in
  `Features/Delivery/BranchesController.cs`. Decide whether branches get their
  own feature folder now that they are managed, and say why.

## Where branches are used, and what "disabled" must mean in each

Find every use before changing anything (`grep` for `BranchId`, `Branches`,
`branch` across server, Agent App and web). At least:

- **The classification form's branch field** (A-40), in the Agent App, the
  supervisor's editor (S-04) and Applications (A-70). A disabled branch is **not
  offered** for a new classification. The server should refuse it too
  (`ClassificationService` already refuses an unknown branch). But an **old**
  call that has it must still show it, and a supervisor correcting that call
  must not be forced to change the branch unless they want to. The web editor
  does exactly this for a hidden type: follow it.
- **Delivery areas** (A-65, S-58): each area belongs to one branch. What happens
  to the areas of a branch being disabled is a decision (below).
- **The call search, the reports and the dashboard**: a disabled branch stays
  filterable and its past figures stay. Disabling is about the future, not
  history.
- **The Agent App**: it receives branches from the server (the form, the
  Delivery tab). Check it needs no change beyond getting the active ones.

## Decisions to ask me before building

1. **Disabling a branch that still has delivery areas:** refuse until they are
   moved to another branch (saying how many); allow it and move them in the same
   step to a branch the supervisor picks; or allow it and leave them, with the
   Delivery tab no longer showing them? Recommend refusing, with the count.
2. **Can the last active branch be disabled?** Recommend no: the classification
   form would have no branch to offer.
3. **Branch names that differ only in spelling** (هـ / ة, with or without hamza):
   the same name or not?

## When it is done

Everything in the house rules, and specifically:

- **Database-backed tests** (`[DatabaseFact]`): add, rename (and that a call
  classified under the old name shows the new one), reorder, disable; a disabled
  branch refused for a new classification but kept on an old one and on a
  supervisor's edit that leaves it alone; the delivery-areas rule you agreed; the
  last-active-branch rule; agents refused every write.
- Web tests for the Settings card, as `ChannelsCard.test.tsx` has.
- Update the S-41 row in the SRS, which still says "Branches are still read-only".
- Checklist steps; **ask for a screenshot** of the Settings card in both
  languages.
