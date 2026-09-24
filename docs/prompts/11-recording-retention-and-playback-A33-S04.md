# Recording retention and playback (A-33, S-04, S-43)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

Recordings are captured on the laptop and uploaded to the server, attached to
the call they belong to. Two halves are missing, and they pull in opposite
directions:

- **Nothing ever deletes one** (A-33). Ninety days is configured and ignored.
- **Nothing can play one** (S-04). The audio is arriving somewhere nobody can
  reach.

Read the A-30 to A-33, S-04 and S-43 rows in the SRS, `docs/SCHEMA.md` on
`recordings`, and these `docs/DECISIONS.md` entries:

- "Call recording, part 1: capturing the audio" (24 Sep).
- "Call recording, part 2: the file reaches the call" (24 Sep).

## What already exists

`RecordingStore` (server) writes, opens and deletes the files and already knows
the layout `yyyy/MM/dd/<communication-id>.wav`. The `recordings` table has
`deleted_at` for exactly this job, and the entity's comment already states the
rule: **the file goes, the row stays**. The setting `recording.retention_days`
is seeded at 90 and appears on the supervisor's settings screen, because that
screen lists whatever the server exposes.

So both halves are smaller than they sound. Most of the thinking is already
written down; your job is mainly to honour it.

## What to build

**A-33, the retention job.** A background job on the server that deletes files
older than `recording.retention_days` and sets `deleted_at`. Nightly is fine.

Three things it must get right:

1. **The row survives.** The call and its classification are kept indefinitely
   (N-08); only the audio expires. A report over last year must still be
   correct, and a deleted recording must read as *expired*, not as *never
   recorded*.
2. **It reads the setting every run**, not at startup. A supervisor changing 90
   to 30 should not need the server restarted.
3. **It must never take the server down.** One unreadable file, or a folder
   that has been moved, is a logged warning and a job that carries on.

Also consider, and tell me what you decide: **empty date folders**. After a year
the folder tree holds 365 directories, most of them empty. Removing an empty one
is easy; racing the upload that is about to write into it is not.

**S-04, playing and downloading.** A supervisor-only endpoint that streams a
recording, and one that offers it as a download. Notes:

- **Supervisors only.** Every other communications endpoint is open to any
  signed-in account; this one is not, and the existing comment in
  `CommunicationsController` says so explicitly. An agent must not be able to
  fetch another agent's audio, or their own.
- **Stream it, never buffer it.** `RecordingStore.Open` already returns a
  stream.
- **A recording whose file has gone answers 404**, and the message must let the
  screen say "expired" rather than "missing".
- Support **range requests** if it is cheap, so a player can seek. If it is not
  cheap, say so and leave it.

**S-43's other half.** The requirement asks for retention *and a storage usage
view*. An endpoint reporting how much disk recordings occupy, and how many are
kept. Small, and it is what tells the client whether 90 days is affordable — the
estimate is about 50 GB for four agents, and nobody has measured the real
number.

## What to leave alone

**The browser side.** The player belongs on the supervisor's call details
screen, which is `01-call-search-S02-S03.md` and may not exist yet. That task
was told to leave a space for it. Build the endpoints, tell me they are ready,
and let the call search put a button on them. If the call search is already
done, say so and I will decide whether you add the player or it does.

## When it is done

Everything in the house rules, and:

- **Database-backed tests** for both halves. Retention especially: a test that
  an expired recording keeps its row and loses its file, and that its call and
  classification are untouched.
- A test that **an agent is refused** the playback endpoint. That is the one
  that matters, and it is the one easiest to leave out.
- Tell me the real storage number once you can measure it.
