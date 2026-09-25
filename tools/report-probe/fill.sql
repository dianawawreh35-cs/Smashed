-- A year of synthetic calls and messages, for timing the reports (N-02).
--
-- NEVER run this against `callcenter`. It is written for a scratch database
-- that has been migrated and seeded (see README.md), and it refuses anything
-- else below.
--
-- The shape is the SRS's expected volume (about 500 communications a day) and
-- what the restaurant looks like: busy lunch and evenings, quiet mornings, five
-- agents, four branches, most calls answered, most answered calls classified,
-- about half of them orders, and customers drawn from the seeded contact book
-- (with some numbers nobody saved).

DO $$
BEGIN
  IF current_database() = 'callcenter' THEN
    RAISE EXCEPTION 'refusing to fill the development database';
  END IF;
END $$;

-- Five agents.
INSERT INTO users (login, password_hash, display_name, role, extension, is_active)
SELECT 'probe-agent-' || g, 'x', 'Probe agent ' || g, 'Agent', (2100 + g)::text, true
FROM generate_series(1, 5) g
ON CONFLICT (login) DO NOTHING;

CREATE TEMP TABLE probe_agents AS SELECT id, row_number() OVER (ORDER BY login) AS n FROM users WHERE login LIKE 'probe-agent-%';
CREATE TEMP TABLE probe_branches AS SELECT id, row_number() OVER (ORDER BY name) AS n FROM branches;
CREATE TEMP TABLE probe_apps AS SELECT id, row_number() OVER (ORDER BY name) AS n FROM channels WHERE NOT is_system;
CREATE TEMP TABLE probe_contacts AS
  SELECT c.id, p.raw, p.normalised, row_number() OVER (ORDER BY c.id) AS n
  FROM contacts c JOIN contact_phones p ON p.contact_id = c.id AND p.is_primary;

-- The hour weights: 0 before 10:00, a lunch hump and an evening peak.
CREATE TEMP TABLE probe_hours (h int, w int);
INSERT INTO probe_hours VALUES
  (10, 3), (11, 6), (12, 9), (13, 10), (14, 7), (15, 5), (16, 5), (17, 7),
  (18, 10), (19, 12), (20, 12), (21, 9), (22, 5), (23, 2);

-- One row per communication: 365 days x ~500, spread over the hours by weight.
CREATE TEMP TABLE probe_rows AS
SELECT
  gen_random_uuid() AS id,
  (date_trunc('day', now()) - (d || ' days')::interval + (h.h || ' hours')::interval
     + (random() * 3599 || ' seconds')::interval) AS started_at,
  random() AS r_kind, random() AS r_dir, random() AS r_status, random() AS r_contact,
  random() AS r_type, random() AS r_value, random() AS r_agent, random() AS r_branch
FROM generate_series(0, 364) d
CROSS JOIN probe_hours h
CROSS JOIN LATERAL generate_series(1, (h.w * 500 / 102)) i;

INSERT INTO communications (id, kind, channel_id, direction, status, agent_id, contact_id, branch_id,
  remote_number_raw, remote_normalised, started_at, answered_at, ended_at, duration_sec, source, sip_call_id, extension)
SELECT
  r.id,
  k.kind,
  CASE WHEN k.kind = 'App' THEN (SELECT id FROM probe_apps WHERE n = 1 + floor(r.r_type * (SELECT count(*) FROM probe_apps))) ELSE (SELECT id FROM channels WHERE is_system) END,
  k.direction,
  s.status,
  a.id,
  CASE WHEN r.r_contact < 0.75 THEN ct.id END,
  b.id,
  CASE WHEN r.r_contact < 0.75 THEN ct.raw ELSE '059' || lpad((floor(r.r_contact * 9999999))::text, 7, '0') END,
  CASE WHEN r.r_contact < 0.75 THEN ct.normalised ELSE '97059' || lpad((floor(r.r_contact * 9999999))::text, 7, '0') END,
  r.started_at,
  CASE WHEN s.status = 'Answered' THEN r.started_at + interval '8 seconds' END,
  r.started_at + interval '3 minutes',
  CASE WHEN s.status = 'Answered' THEN 30 + floor(r.r_value * 300)::int END,
  CASE WHEN k.kind = 'App' THEN 'Manual' ELSE 'AgentApp' END,
  CASE WHEN k.kind = 'Call' THEN r.id::text || '@probe' END,
  CASE WHEN k.kind = 'Call' THEN '2101' END
FROM probe_rows r
CROSS JOIN LATERAL (SELECT
    CASE WHEN r.r_kind < 0.08 THEN 'App' ELSE 'Call' END AS kind,
    CASE WHEN r.r_kind < 0.08 THEN 'None' WHEN r.r_dir < 0.85 THEN 'In' ELSE 'Out' END AS direction) k
CROSS JOIN LATERAL (SELECT CASE
    WHEN k.kind = 'App' THEN 'Logged'
    WHEN k.direction = 'In' THEN CASE WHEN r.r_status < 0.80 THEN 'Answered' WHEN r.r_status < 0.92 THEN 'Missed'
                                      WHEN r.r_status < 0.97 THEN 'Rejected' WHEN r.r_status < 0.99 THEN 'Blocked' ELSE 'Missed' END
    ELSE CASE WHEN r.r_status < 0.70 THEN 'Answered' WHEN r.r_status < 0.95 THEN 'NoAnswer' ELSE 'Failed' END
  END AS status) s
JOIN probe_agents a ON a.n = 1 + floor(r.r_agent * 5)
JOIN probe_branches b ON b.n = 1 + floor(r.r_branch * 4)
LEFT JOIN probe_contacts ct ON ct.n = 1 + floor(r.r_contact / 0.75 * (SELECT count(*) FROM probe_contacts));

-- Classify 90% of answered calls and every message: about half orders.
INSERT INTO classifications (communication_id, type_id, order_value, notes, follow_up, resolved, resolved_at,
  form_version, custom_values, classified_by, classified_at)
SELECT
  c.id,
  t.id,
  CASE WHEN t.name = 'Order' THEN round((20 + r.r_value * 180)::numeric, 2) END,
  CASE WHEN t.name IN ('Complaint', 'Cancellation') THEN 'probe note' END,
  t.name = 'Complaint' AND r.r_value < 0.5,
  CASE WHEN t.name = 'Complaint' THEN r.r_value < 0.6 END,
  CASE WHEN t.name = 'Complaint' AND r.r_value < 0.6 THEN c.started_at + (r.r_value * 48 || ' hours')::interval END,
  (SELECT min(version) FROM form_definitions),
  '{}'::jsonb,
  c.agent_id,
  c.started_at
FROM communications c
JOIN probe_rows r ON r.id = c.id
JOIN classification_types t ON t.name = CASE
    WHEN r.r_type < 0.50 THEN 'Order' WHEN r.r_type < 0.70 THEN 'Inquiry' WHEN r.r_type < 0.78 THEN 'Complaint'
    WHEN r.r_type < 0.85 THEN 'Cancellation' WHEN r.r_type < 0.92 THEN 'WrongNumber' ELSE 'Other' END
WHERE (c.status = 'Answered' AND r.r_status < 0.72) OR c.kind = 'App';

ANALYZE communications;
ANALYZE classifications;

SELECT kind, count(*) FROM communications GROUP BY kind;
SELECT count(*) AS classified FROM classifications;
