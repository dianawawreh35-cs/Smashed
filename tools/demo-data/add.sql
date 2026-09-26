-- Demo data for looking at the reports: a stretch of realistic calls and
-- messages on the development database, every row of it removable with
-- remove.sql. See README.md.
--
--   docker cp tools\demo-data\add.sql callcenter-db-dev:/tmp/add.sql
--   docker exec callcenter-db-dev psql -U callcenter -d callcenter -v dev=yes -f /tmp/add.sql
--
-- Copied in, not piped: Windows PowerShell re-encodes what it pipes to a
-- program, and every Arabic name arrived as "Ø³Ø§Ø±Ø©" (26 Sep).
--
-- Optional: -v days=90 (how far back) -v per_day=200 (communications a day).
--
-- How it stays removable: everything it writes hangs off five demo agents,
-- logins demo-agent-1 to demo-agent-5. Every call and message has one of them
-- as its agent; every new customer it saves was saved by one of them. Nothing
-- else is written, and remove.sql deletes exactly that.

\set ON_ERROR_STOP on

\if :{?dev}
\else
  \echo 'Refusing: this writes demo data. Add -v dev=yes to confirm this is the development database, never production.'
  \quit
\endif
\if :{?days}
\else
  \set days 90
\endif
\if :{?per_day}
\else
  \set per_day 200
\endif

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM users WHERE login LIKE 'demo-agent-%') THEN
    RAISE EXCEPTION 'demo data is already here: run remove.sql first';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM channels WHERE is_system)
     OR NOT EXISTS (SELECT 1 FROM branches)
     OR NOT EXISTS (SELECT 1 FROM classification_types WHERE name = 'Order')
     OR NOT EXISTS (SELECT 1 FROM contacts WHERE created_by IS NULL) THEN
    RAISE EXCEPTION 'the database has not been seeded: run the server''s seed command first';
  END IF;
END $$;

BEGIN;

-- ---- five demo agents, whom nobody can sign in as -----------------------------

INSERT INTO users (login, password_hash, display_name, role, extension, is_active)
SELECT 'demo-agent-' || a.n, crypt(gen_random_uuid()::text, gen_salt('bf', 10)), a.name || ' (تجريبي)', 'Agent',
       (2900 + a.n)::text, true
FROM (VALUES (1, 'سارة'), (2, 'أحمد'), (3, 'ليلى'), (4, 'محمد'), (5, 'رنا')) AS a(n, name);

CREATE TEMP TABLE d_agents AS SELECT id, row_number() OVER (ORDER BY login) AS n FROM users WHERE login LIKE 'demo-agent-%';
CREATE TEMP TABLE d_branches AS SELECT id, row_number() OVER (ORDER BY name) AS n FROM branches;
CREATE TEMP TABLE d_apps AS SELECT id, row_number() OVER (ORDER BY sort_order, name) AS n FROM channels WHERE NOT is_system AND is_active;
CREATE TEMP TABLE d_forms AS SELECT direction, version FROM form_definitions WHERE is_current;
CREATE TEMP TABLE d_types AS SELECT id, name FROM classification_types;

-- Regular customers: 1,500 of the old system's, so some call again and again.
CREATE TEMP TABLE d_regulars AS
SELECT c.id, p.raw, p.normalised, row_number() OVER (ORDER BY c.id) AS n
FROM contacts c JOIN contact_phones p ON p.contact_id = c.id AND p.is_primary
WHERE c.deleted_at IS NULL AND c.created_by IS NULL AND NOT c.is_blocked
LIMIT 1500;

-- ---- the traffic -------------------------------------------------------------

-- The hours the restaurant is busy, in its own time: a lunch hump, an evening peak.
CREATE TEMP TABLE d_hours (h int, w int);
INSERT INTO d_hours VALUES
  (10, 3), (11, 6), (12, 9), (13, 10), (14, 7), (15, 5), (16, 5), (17, 7),
  (18, 10), (19, 12), (20, 12), (21, 9), (22, 5), (23, 2);

-- One row per communication. Friday and Saturday are a third busier, and
-- every hour of every day varies a little, so the charts are not flat.
CREATE TEMP TABLE d_rows AS
SELECT
  gen_random_uuid() AS id,
  ((date_trunc('day', now() AT TIME ZONE 'Asia/Hebron') - d * interval '1 day'
      + h.h * interval '1 hour' + random() * interval '3599 seconds') AT TIME ZONE 'Asia/Hebron') AS started_at,
  random() AS r_kind, random() AS r_dir, random() AS r_status, random() AS r_contact, random() AS r_pick,
  random() AS r_type, random() AS r_value, random() AS r_agent, random() AS r_branch, random() AS r_note
FROM generate_series(0, :days - 1) AS d
CROSS JOIN d_hours h
CROSS JOIN LATERAL generate_series(1, greatest(0, round(
    h.w * :per_day / 102.0
    * CASE WHEN extract(isodow FROM (now() AT TIME ZONE 'Asia/Hebron')::date - d) IN (5, 6) THEN 1.3 ELSE 1.0 END
    * (0.75 + random() * 0.5))::int)) AS i;

-- Today stops at now.
DELETE FROM d_rows WHERE started_at > now();

-- Run before the restaurant opens and today would be empty, and so would the
-- dashboard's figures. A couple of dozen since midnight, then, whatever the hour.
INSERT INTO d_rows
SELECT gen_random_uuid(),
       t.start + random() * (now() - t.start),
       random(), random(), random(), random(), random(), random(), random(), random(), random(), random()
FROM (SELECT (date_trunc('day', now() AT TIME ZONE 'Asia/Hebron') AT TIME ZONE 'Asia/Hebron') AS start) t
CROSS JOIN generate_series(1, 25)
WHERE NOT EXISTS (SELECT 1 FROM d_rows WHERE started_at >= t.start);

INSERT INTO communications (id, kind, channel_id, direction, status, agent_id, contact_id, branch_id,
  remote_number_raw, remote_normalised, started_at, answered_at, ended_at, duration_sec, source,
  sip_call_id, extension, notes)
SELECT
  r.id, k.kind,
  CASE WHEN k.kind = 'App'
       THEN (SELECT id FROM d_apps WHERE n = 1 + floor(r.r_pick * (SELECT count(*) FROM d_apps)))
       ELSE (SELECT id FROM channels WHERE is_system) END,
  k.direction, s.status, a.id,
  CASE WHEN r.r_contact < 0.8 THEN reg.id END,
  b.id,
  CASE WHEN r.r_contact < 0.8 THEN reg.raw ELSE '059977' || lpad(floor(r.r_pick * 300)::text, 4, '0') END,
  CASE WHEN r.r_contact < 0.8 THEN reg.normalised ELSE '97059977' || lpad(floor(r.r_pick * 300)::text, 4, '0') END,
  r.started_at,
  CASE WHEN s.status = 'Answered' THEN r.started_at + interval '6 seconds' END,
  CASE WHEN k.kind = 'Call' THEN r.started_at + interval '6 seconds' + (CASE WHEN s.status = 'Answered' THEN (40 + floor(r.r_value * 260)) ELSE 20 END) * interval '1 second' END,
  CASE WHEN s.status = 'Answered' THEN 40 + floor(r.r_value * 260)::int END,
  CASE WHEN k.kind = 'App' THEN 'Manual' ELSE 'AgentApp' END,
  CASE WHEN k.kind = 'Call' THEN r.id::text || '@demo' END,
  CASE WHEN k.kind = 'Call' THEN (2900 + a.n)::text END,
  -- A-41: some missed and unanswered calls carry a note.
  CASE WHEN s.status IN ('Missed', 'Rejected', 'NoAnswer') AND r.r_note < 0.3
       THEN (ARRAY['اتصلنا به لاحقاً', 'كان الخط مشغولاً', 'لم يرد على الاتصال'])[1 + floor(r.r_note * 10)::int % 3] END
FROM d_rows r
CROSS JOIN LATERAL (SELECT
    CASE WHEN r.r_kind < 0.10 THEN 'App' ELSE 'Call' END AS kind,
    CASE WHEN r.r_kind < 0.10 THEN 'None' WHEN r.r_dir < 0.85 THEN 'In' ELSE 'Out' END AS direction) k
CROSS JOIN LATERAL (SELECT CASE
    WHEN k.kind = 'App' THEN 'Logged'
    WHEN k.direction = 'In' THEN CASE WHEN r.r_status < 0.82 THEN 'Answered' WHEN r.r_status < 0.93 THEN 'Missed'
                                      WHEN r.r_status < 0.98 THEN 'Rejected' ELSE 'Blocked' END
    ELSE CASE WHEN r.r_status < 0.70 THEN 'Answered' WHEN r.r_status < 0.95 THEN 'NoAnswer' ELSE 'Failed' END
  END AS status) s
JOIN d_agents a ON a.n = 1 + floor(r.r_agent * 5)
JOIN d_branches b ON b.n = 1 + floor(r.r_branch * (SELECT count(*) FROM d_branches))
-- Skewed towards the first customers, so some are regulars.
LEFT JOIN d_regulars reg ON reg.n = 1 + floor(power(r.r_pick, 2.2) * (SELECT count(*) FROM d_regulars));

-- ---- new customers, saved during the period by the demo agents (R-16) ----------

CREATE TEMP TABLE d_new AS
SELECT gen_random_uuid() AS id, g AS n, 'زبون تجريبي ' || g AS name,
       '0599' || lpad((880000 + g)::text, 6, '0') AS raw,
       '970599' || lpad((880000 + g)::text, 6, '0') AS normalised,
       now() - random() * (:days * interval '1 day') AS created_at,
       (SELECT id FROM d_agents WHERE n = 1 + (g % 5)) AS agent_id,
       (SELECT id FROM d_branches WHERE n = 1 + (g % (SELECT count(*) FROM d_branches))) AS branch_id
FROM generate_series(1, 120) AS g;
DELETE FROM d_new WHERE EXISTS (SELECT 1 FROM contact_phones p WHERE p.normalised = d_new.normalised);

INSERT INTO contacts (id, name, name_normalised, created_by, created_at, updated_at)
SELECT id, name, name, agent_id, created_at, created_at FROM d_new;
INSERT INTO contact_phones (contact_id, raw, normalised, is_primary, created_at)
SELECT id, raw, normalised, true, created_at FROM d_new;

-- Their calls: the one they were saved on, and one or two more since.
CREATE TEMP TABLE d_new_calls AS
SELECT gen_random_uuid() AS id, nw.id AS contact_id, nw.raw, nw.normalised, nw.agent_id, nw.branch_id,
       CASE WHEN k = 1 THEN nw.created_at ELSE nw.created_at + random() * (now() - nw.created_at) END AS started_at,
       random() AS r_type, random() AS r_value
FROM d_new nw CROSS JOIN LATERAL generate_series(1, 1 + floor(random() * 3)::int) AS k;

INSERT INTO communications (id, kind, channel_id, direction, status, agent_id, contact_id, branch_id,
  remote_number_raw, remote_normalised, started_at, answered_at, ended_at, duration_sec, source, sip_call_id, extension)
SELECT c.id, 'Call', (SELECT id FROM channels WHERE is_system), 'In', 'Answered', c.agent_id, c.contact_id, c.branch_id,
       c.raw, c.normalised, c.started_at, c.started_at + interval '5 seconds',
       c.started_at + interval '5 seconds' + (60 + floor(c.r_value * 200)) * interval '1 second',
       60 + floor(c.r_value * 200)::int, 'AgentApp', c.id::text || '@demo',
       (SELECT (2900 + n)::text FROM d_agents WHERE id = c.agent_id)
FROM d_new_calls c;

-- ---- classifications ---------------------------------------------------------------

-- Every message, all answered outgoing calls and nine answered incoming calls
-- in ten, so the Unclassified figures have something in them.
INSERT INTO classifications (communication_id, type_id, order_value, notes, follow_up, resolved, resolved_at, resolved_by,
  form_version, custom_values, classified_by, classified_at)
SELECT
  c.id, t.id,
  CASE WHEN t.name IN ('Order', 'Cancellation') THEN round((25 + r.r_value * 175)::numeric, 2) END,
  CASE t.name
    WHEN 'Complaint' THEN (ARRAY['تأخر الطلب', 'وصل الطلب بارداً', 'نقص في الطلب', 'السائق لم يجد العنوان'])[1 + floor(r.r_value * 4)::int]
    WHEN 'Cancellation' THEN (ARRAY['غيّر رأيه', 'تأخر التوصيل', 'طلب مكرر'])[1 + floor(r.r_value * 3)::int]
  END,
  t.name = 'Complaint' AND r.r_value < 0.6,
  CASE WHEN t.name = 'Complaint' THEN r.r_value < 0.55 END,
  CASE WHEN t.name = 'Complaint' AND r.r_value < 0.55 THEN least(now(), c.started_at + (1 + r.r_value * 60) * interval '1 hour') END,
  CASE WHEN t.name = 'Complaint' AND r.r_value < 0.55 THEN (SELECT id FROM users WHERE role = 'Supervisor' ORDER BY created_at LIMIT 1) END,
  f.version, '{}'::jsonb, c.agent_id, c.started_at
FROM communications c
JOIN (SELECT id, r_type, r_value, r_status FROM d_rows
      UNION ALL SELECT id, r_type, r_value, 0 FROM d_new_calls) r ON r.id = c.id
JOIN d_forms f ON f.direction = c.direction
JOIN d_types t ON t.name = CASE
    WHEN c.direction = 'Out' THEN CASE WHEN r.r_type < 0.40 THEN 'Inquiry' WHEN r.r_type < 0.60 THEN 'Order' WHEN r.r_type < 0.70 THEN 'Complaint' ELSE 'Other' END
    WHEN r.r_type < 0.52 THEN 'Order' WHEN r.r_type < 0.70 THEN 'Inquiry' WHEN r.r_type < 0.79 THEN 'Complaint'
    WHEN r.r_type < 0.87 THEN 'Cancellation' WHEN r.r_type < 0.93 THEN 'WrongNumber' ELSE 'Other' END
WHERE c.kind = 'App'
   OR (c.status = 'Answered' AND (c.direction = 'Out' OR r.r_status < 0.74));

-- ---- three demo agents signed in, for the dashboard -----------------------------------

INSERT INTO agent_sessions (user_id, laptop_id, app_version, logged_in_at)
SELECT id, 'DEMO-LAPTOP-' || n, 'demo', now() - interval '2 hours' FROM d_agents WHERE n <= 3;

COMMIT;

SELECT kind, count(*) AS added FROM communications WHERE agent_id IN (SELECT id FROM d_agents) GROUP BY kind
UNION ALL SELECT 'new customers', count(*) FROM d_new
UNION ALL SELECT 'classified', count(*) FROM classifications WHERE classified_by IN (SELECT id FROM d_agents);
