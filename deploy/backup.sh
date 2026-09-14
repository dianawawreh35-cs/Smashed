#!/bin/bash
#
# Nightly backup for the Restaurant Call Center System.
# Implements step 9 of docs/DEPLOY-server-runbook.md and satisfies N-07.
#
#   - gzipped pg_dump of the callcenter database  -> backups/db-<date>.sql.gz
#   - recordings mirrored to the second disk      -> /mnt/backup/recordings/
#   - a copy of the dump on the second disk       -> /mnt/backup/
#   - both locations pruned after 30 days
#
# Install (see the runbook):
#   chmod +x backup.sh
#   crontab -e
#   30 3 * * * /opt/callcenter/backup.sh >> /opt/callcenter/backups/backup.log 2>&1
#
# Run it by hand once and check backups/backup.log.
# Restore procedure is in the runbook, step 9 — test it before handover.

set -e

D=$(date +%F)
cd /opt/callcenter

docker compose exec -T db pg_dump -U callcenter callcenter | gzip > backups/db-$D.sql.gz
rsync -a --delete data/recordings/ /mnt/backup/recordings/
cp backups/db-$D.sql.gz /mnt/backup/
find backups -name 'db-*.sql.gz' -mtime +30 -delete
find /mnt/backup -name 'db-*.sql.gz' -mtime +30 -delete
echo "$D backup OK"
