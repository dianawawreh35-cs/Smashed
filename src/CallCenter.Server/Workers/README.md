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

`AbandonedCallImportWorker` (S-55) is built: every 20 seconds it asks
`AbandonedCallImport` whether a check is due (`pbx.calls.interval_minutes`, one
minute by default), and if so downloads the PBX's Calls Detail report and saves
the abandoned calls (`Features/Pbx`). It does nothing until the PBX's address
and login are entered on the settings screen.

Not planned: an AMI listener (ruled out, S-50) and a CDR file importer (replaced
by the Calls Detail report on 2026-09-26).

See `docs/SRS-Smashed-Burger-Call-Center.md` for the governing requirements.
