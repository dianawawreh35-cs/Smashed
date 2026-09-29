#!/bin/bash
#
# Nightly backup for the Restaurant Call Center System.
# Implements step 9 of docs/DEPLOY-server-runbook.md and satisfies N-07.
#
#   - gzipped pg_dump of the callcenter database  -> backups/db-<date>-<time>.sql.gz
#   - recordings mirrored to the second disk      -> /mnt/backup/recordings/
#   - menu photographs mirrored to the second disk -> /mnt/backup/menu-images/
#   - needs rsync (runbook step 1)
#   - a copy of the dump on the second disk       -> /mnt/backup/
#   - both locations pruned after 30 days
#   - no disk mounted at /mnt/backup: the dump is kept here, a WARNING line
#     says nothing reached the second disk, and the run still succeeds
#
# Install (see the runbook):
#   chmod +x backup.sh
#   crontab -e
#   30 3 * * * /opt/callcenter/backup.sh >> /opt/callcenter/backups/backup.log 2>&1
#
# Run it by hand once and check backups/backup.log.
# Restore procedure is in the runbook, step 9 — test it before handover.
#
# The last line it prints on success is "backup OK: <path of the dump>";
# update.sh reads it to tell you which dump to restore if an update goes wrong.

# F-07 (27 Sep review). -e alone let `pg_dump | gzip` succeed whenever gzip
# did: with the database down it wrote a 20-byte file, printed "backup OK" and
# update.sh carried on. pipefail makes the dump's own failure count.
set -Eeuo pipefail

STAMP=$(date +%F-%H%M%S)
cd /opt/callcenter

# The time is in the name so the dump update.sh makes at 14:00 never replaces
# the one from 03:30, here or on the second disk.
DUMP="backups/db-$STAMP.sql.gz"
PARTIAL="$DUMP.partial"

trap 'rm -f "$PARTIAL"; echo "$(date "+%F %T") backup FAILED at line $LINENO - the previous dumps are untouched" >&2' ERR

# Written under another name and renamed only once it has been checked, so a
# dump cut short is never mistaken for a good one.
docker compose exec -T db pg_dump -U callcenter callcenter | gzip > "$PARTIAL"
gzip -t "$PARTIAL"

# pg_dump's last lines say it finished. A dump missing them was cut off, even
# if the gzip around it is whole. grep -c, not -q, so it reads to the end and
# the pipe is not broken under pipefail. The last 20 lines, not fewer: since
# PostgreSQL 16.10 an "unrestrict" line follows the "complete" line, which on
# the server on 27 Sep 2026 was already the 5th from the end.
zcat "$PARTIAL" | tail -n 20 | grep -c "PostgreSQL database dump complete" > /dev/null

mv "$PARTIAL" "$DUMP"
find backups -name 'db-*.sql.gz' -mtime +30 -delete

# N-07. Only when the second disk is really mounted there: an empty folder at
# /mnt/backup would take the copy onto the server's own disk and fill it. With
# no disk, the dump stays here and the run still succeeds, so update.sh can
# back up before an update (29 Sep 2026: /mnt/backup had never existed, and
# every update needed --no-backup and a dump made by hand).
if mountpoint -q /mnt/backup; then
    rsync -a --delete data/recordings/ /mnt/backup/recordings/
    rsync -a --delete data/menu-images/ /mnt/backup/menu-images/
    cp "$DUMP" /mnt/backup/
    find /mnt/backup -name 'db-*.sql.gz' -mtime +30 -delete
else
    echo "$(date "+%F %T") WARNING: no disk mounted at /mnt/backup - the database is saved on this disk only, and the recordings and menu photographs are not copied anywhere"
fi

echo "$(date "+%F %T") backup OK: /opt/callcenter/$DUMP"
