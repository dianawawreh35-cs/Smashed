-- Removes everything add.sql wrote, and nothing else. See README.md.
--
--   docker cp tools\demo-data\remove.sql callcenter-db-dev:/tmp/remove.sql
--   docker exec callcenter-db-dev psql -U callcenter -d callcenter -f /tmp/remove.sql
--
-- What add.sql writes hangs off the five demo agents (logins demo-agent-1 to
-- demo-agent-5): their calls and messages (with the classifications, notes and
-- change history on them, which go with the row), the customers they saved,
-- their sign-ins, and the agents themselves. Real calls, real agents and the
-- old system's customers are never touched. Safe to run twice.

\set ON_ERROR_STOP on

BEGIN;

CREATE TEMP TABLE r_users AS SELECT id FROM users WHERE login LIKE 'demo-agent-%';
CREATE TEMP TABLE r_contacts AS SELECT id FROM contacts WHERE created_by IN (SELECT id FROM r_users);
CREATE TEMP TABLE r_comms AS SELECT id FROM communications WHERE agent_id IN (SELECT id FROM r_users);

-- A real call that happened to come from a demo customer's number keeps its
-- row and loses only the link to the demo customer.
UPDATE communications SET contact_id = NULL
WHERE contact_id IN (SELECT id FROM r_contacts) AND id NOT IN (SELECT id FROM r_comms);

-- Classifications, their history and recordings cascade with the row.
DELETE FROM follow_up_tasks
WHERE communication_id IN (SELECT id FROM r_comms) OR closed_by_communication_id IN (SELECT id FROM r_comms)
   OR contact_id IN (SELECT id FROM r_contacts);
DELETE FROM communications WHERE id IN (SELECT id FROM r_comms);

-- A supervisor who edited a demo customer keeps nothing pointing at them;
-- the customer goes, with its numbers (they cascade).
DELETE FROM contacts WHERE id IN (SELECT id FROM r_contacts);

DELETE FROM agent_sessions WHERE user_id IN (SELECT id FROM r_users);
DELETE FROM users WHERE id IN (SELECT id FROM r_users);

SELECT (SELECT count(*) FROM r_comms) AS communications_removed,
       (SELECT count(*) FROM r_contacts) AS customers_removed,
       (SELECT count(*) FROM r_users) AS agents_removed;

COMMIT;
