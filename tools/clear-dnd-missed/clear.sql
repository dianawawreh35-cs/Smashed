-- Clears the Missed rows that Do not disturb logged before 1 Oct 2026 (A-18).
-- See DECISIONS.md, "Do not disturb no longer logs a Missed call".
--
-- Looks only, by default: prints what it would clear and rolls back.
--
--   docker cp tools/clear-dnd-missed/clear.sql callcenter-db:/tmp/clear.sql
--   docker exec callcenter-db psql -U callcenter -d callcenter -f /tmp/clear.sql
--
-- To clear them, add -v apply=yes before -f. (callcenter-db-dev on a laptop.)
--
-- How a DND row is told apart. The Agent App saved a refusal with its start
-- and end at the same instant, and no other row is saved that way except a
-- second call during a call, which falls inside one of the same extension's
-- other calls (Do not disturb is only asked when the line is free). A real
-- missed ring lasts seconds. Rows with a classification, a follow-up or a
-- note are left alone, as nothing should be lost with them.
--
-- Nothing is lost: each row is copied into cleared_dnd_missed first, and can
-- be put back from there. Safe to run twice.

\set ON_ERROR_STOP on
\pset pager off

BEGIN;

CREATE TABLE IF NOT EXISTS cleared_dnd_missed (LIKE communications);

CREATE TEMP TABLE dnd AS
SELECT c.id
FROM communications c
WHERE c.kind = 'Call' AND c.direction = 'In' AND c.status = 'Missed'
  AND c.source = 'AgentApp'
  AND c.started_at = c.ended_at AND c.answered_at IS NULL
  AND c.notes IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM communications o
      WHERE o.id <> c.id AND o.kind = 'Call' AND o.extension = c.extension
        AND o.started_at <= c.started_at
        AND COALESCE(o.ended_at, o.started_at) > c.started_at)
  AND NOT EXISTS (SELECT 1 FROM classifications x WHERE x.communication_id = c.id)
  AND NOT EXISTS (SELECT 1 FROM follow_up_tasks x
                  WHERE x.communication_id = c.id OR x.closed_by_communication_id = c.id);

\echo 'Do not disturb rows, per agent:'
SELECT COALESCE(u.display_name, '(no agent)') AS agent, c.extension,
       COUNT(*) AS rows, MIN(c.started_at)::date AS first, MAX(c.started_at)::date AS last
FROM communications c LEFT JOIN users u ON u.id = c.agent_id
WHERE c.id IN (SELECT id FROM dnd)
GROUP BY 1, 2 ORDER BY 3 DESC;

INSERT INTO cleared_dnd_missed SELECT * FROM communications WHERE id IN (SELECT id FROM dnd);
DELETE FROM communications WHERE id IN (SELECT id FROM dnd);

SELECT COUNT(*) AS cleared FROM dnd;

\if :{?apply}
  COMMIT;
  \echo 'Cleared. A copy of every row is in cleared_dnd_missed.'
\else
  ROLLBACK;
  \echo 'Nothing changed: this was a look. Add -v apply=yes to clear them.'
\endif
