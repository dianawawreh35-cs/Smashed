# Workers

Long-running `BackgroundService` implementations.

`RecordingRetentionWorker` (A-33) is built: once a day it deletes the audio of
recordings older than `recording.retention_days` and sets `deleted_at`. **The
row stays** - the call and its classification are kept indefinitely (N-08), and
an old call must read as *recorded, expired* rather than as never recorded. An
earlier note here said it pruned the rows; that was never the rule.

`PosLookupWorker` (A-67) is built: every five minutes it asks the restaurant
POS about recent callers with no contact, or with no name or address, creates
or fills in their contacts, and attaches their calls (`Features/Pos`). It does
nothing until `PosLookup:Token` is set.

Still planned:

- **AmiListener** - persistent Asterisk Manager Interface connection, translates
  channel events into communications and pushes screen pops over `AgentHub`.
- **CdrImporter** - periodic reconciliation against the PBX call detail records,
  filling gaps the live AMI feed missed.

See `docs/SRS-Smashed-Burger-Call-Center.md` for the governing requirements.
