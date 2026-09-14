# Workers

Long-running `BackgroundService` implementations. Empty for now; planned:

- **AmiListener** - persistent Asterisk Manager Interface connection, translates
  channel events into communications and pushes screen pops over `AgentHub`.
- **CdrImporter** - periodic reconciliation against the PBX call detail records,
  filling gaps the live AMI feed missed.
- **RetentionJob** - deletes recordings older than `RECORDING_RETENTION_DAYS`
  and prunes the corresponding rows.

See `docs/SRS-Smashed-Burger-Call-Center.md` for the governing requirements.
