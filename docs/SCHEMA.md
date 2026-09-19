# Database Schema — Restaurant Call Center System

PostgreSQL 16. UUID primary keys, all timestamps `timestamptz` (UTC), soft delete only where stated.
Enumerations are `text` columns with CHECK constraints (simple for EF Core, readable in SQL, changeable without migrations of a type).
This document is the source of truth for CC prompt 1; EF Core migrations must produce exactly this.

```sql
CREATE EXTENSION IF NOT EXISTS pgcrypto;   -- gen_random_uuid()
```

---

## 1. Organisation

```sql
CREATE TABLE branches (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name        text NOT NULL UNIQUE,
  sort_order  int  NOT NULL DEFAULT 0,
  is_active   boolean NOT NULL DEFAULT true
);

CREATE TABLE channels (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name        text NOT NULL UNIQUE,          -- Phone, WhatsApp, Facebook, Instagram, Wheels, ...
  is_system   boolean NOT NULL DEFAULT false,-- Phone = true, cannot be deleted
  sort_order  int  NOT NULL DEFAULT 0,
  is_active   boolean NOT NULL DEFAULT true
);

CREATE TABLE users (
  id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  login                 text NOT NULL UNIQUE,
  password_hash         text NOT NULL,                       -- BCrypt/Argon2
  display_name          text NOT NULL,
  role                  text NOT NULL CHECK (role IN ('Agent','Supervisor')),
  -- The two extensions of SRS 2.3. Both make and receive calls; they differ by
  -- who is on the other end, not by direction.
  customer_extension    text,                                -- customers, e.g. '101'
  customer_sip_secret   text,                                -- encrypted at rest (app-level key)
  internal_extension    text,                                -- agents and branches, e.g. '201'
  internal_sip_secret   text,                                -- encrypted at rest
  is_active             boolean NOT NULL DEFAULT true,
  created_at            timestamptz NOT NULL DEFAULT now(),
  last_login_at         timestamptz
);

CREATE TABLE settings (
  key         text PRIMARY KEY,           -- e.g. 'recording.retention_days', 'agent.idle_logout_minutes',
  value       text NOT NULL,              --      'pbx.host', 'pbx.ami.user', 'callback.extension',
  --                                         'sla.answer_seconds', 'reports.internal_numbers' (S-48)
  updated_by  uuid REFERENCES users(id),
  updated_at  timestamptz NOT NULL DEFAULT now()
);
```

Notes
- Two extensions per agent live on the user row; the Agent App receives them after login. Secrets are never returned to the supervisor UI in clear.
- `settings` is a key/value bag so the supervisor can change behaviour without a migration.

---

## 2. Customers

```sql
CREATE TABLE contacts (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name             text,                       -- nullable: a bare number can be flagged before it has a name
  address          text,
  notes            text,
  delivery_notes   text,                       -- gate code, landmark
  is_vip           boolean NOT NULL DEFAULT false,
  is_blocked       boolean NOT NULL DEFAULT false,
  flag_reason      text,
  flag_changed_by  uuid REFERENCES users(id),
  flag_changed_at  timestamptz,
  created_by       uuid REFERENCES users(id),
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_by       uuid REFERENCES users(id),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  merged_into_id   uuid REFERENCES contacts(id), -- set when this contact was merged into another (soft delete)
  deleted_at       timestamptz
);

CREATE TABLE contact_phones (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  contact_id   uuid NOT NULL REFERENCES contacts(id) ON DELETE CASCADE,
  raw          text NOT NULL,                 -- as entered / as received
  normalised   text NOT NULL,                 -- digits only, E.164 without '+', e.g. '970599123456'
  last9        text GENERATED ALWAYS AS (right(normalised, 9)) STORED,
  is_primary   boolean NOT NULL DEFAULT false,
  created_at   timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_contact_phones_normalised ON contact_phones(normalised);
CREATE INDEX ix_contact_phones_last9 ON contact_phones(last9);
CREATE INDEX ix_contacts_name ON contacts USING gin (to_tsvector('simple', coalesce(name,'') || ' ' || coalesce(address,'')));
```

Notes
- Matching a caller: normalise → exact match on `normalised`; if none, match on `last9` (handles 05x vs 9705x vs +9705x). Unique index on `normalised` gives duplicate detection for free.
- Merge = move phones and communications to the target, set `merged_into_id` and `deleted_at` on the source. Never hard-delete.

---

## 3. Communications (calls and app entries in one table)

```sql
CREATE TABLE communications (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  kind               text NOT NULL CHECK (kind IN ('Call','App')),
  channel_id         uuid NOT NULL REFERENCES channels(id),       -- Phone for calls
  direction          text NOT NULL CHECK (direction IN ('In','Out','None')), -- None for App entries
  status             text NOT NULL CHECK (status IN
                       ('Ringing','Answered','Missed','Rejected','Blocked','Abandoned','Overflowed','Failed','Logged')),
                     -- Logged = App entry or manually logged item
  agent_id           uuid REFERENCES users(id),                   -- NULL for Abandoned/Overflowed
  contact_id         uuid REFERENCES contacts(id),
  branch_id          uuid REFERENCES branches(id),
  remote_number_raw  text,
  remote_normalised  text,
  remote_name        text,                                        -- caller-ID name if any
  started_at         timestamptz NOT NULL,                        -- ring start / app entry time
  answered_at        timestamptz,
  ended_at           timestamptz,
  duration_sec       int,                                         -- talk time (answered→ended)
  wait_sec           int,                                         -- queue wait, from the CDR import (S-55)
  queue_name         text,
  extension          text,                                        -- which extension handled it
  sip_call_id        text,                                        -- from the Agent App INVITE
  pbx_unique_id      text,                                        -- Asterisk uniqueid; the CDR import's dedupe key (needs loguniqueid=yes)
  source             text NOT NULL CHECK (source IN ('AgentApp','AMI','CDR','Manual')),
  laptop_id          text,                                        -- machine name that logged it
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_comm_started        ON communications(started_at DESC);
CREATE INDEX ix_comm_agent_started  ON communications(agent_id, started_at DESC);
CREATE INDEX ix_comm_contact        ON communications(contact_id, started_at DESC);
CREATE INDEX ix_comm_remote         ON communications(remote_normalised);
CREATE INDEX ix_comm_status         ON communications(status);
CREATE UNIQUE INDEX ux_comm_pbx_unique ON communications(pbx_unique_id) WHERE pbx_unique_id IS NOT NULL;
CREATE UNIQUE INDEX ux_comm_sip_call   ON communications(sip_call_id, extension) WHERE sip_call_id IS NOT NULL;
```

> **Two values in these CHECK constraints are no longer written** (SRS 4.5, 19 Sep 2026): `source = 'AMI'`, because AMI is ruled out, and `status = 'Overflowed'`, because the call-back extension that produced it was removed. Both are **left in place deliberately** — narrowing a CHECK is a migration, it would gain nothing, and a future method could use either again. `ux_comm_pbx_unique` is what makes re-reading the same CDR rows harmless.

```sql

CREATE TABLE recordings (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  communication_id  uuid NOT NULL UNIQUE REFERENCES communications(id) ON DELETE CASCADE,
  path              text NOT NULL,          -- relative to recordings root: yyyy/MM/dd/<comm-id>.wav|.opus
  size_bytes        bigint,
  duration_sec      int,
  format            text NOT NULL DEFAULT 'wav',
  uploaded_at       timestamptz NOT NULL DEFAULT now(),
  deleted_at        timestamptz             -- set by retention job; row kept, file removed
);
```

Notes
- One table for phone and app makes every report one query (`kind`/`channel_id` splits them).
- Reconciliation: an AMI/CDR record and an Agent App record for the same call are joined on `pbx_unique_id` when the app can see it, otherwise on `remote_normalised` + time window; the reconciler merges wait_sec/queue_name into the agent's row.

---

## 4. Classification (dynamic form)

```sql
CREATE TABLE classification_types (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name        text NOT NULL UNIQUE,          -- Order, Cancellation, Complaint, Inquiry, WrongNumber, Other
  label_ar    text NOT NULL,
  label_en    text NOT NULL,
  colour      text,                          -- hex for charts/badges
  is_system   boolean NOT NULL DEFAULT false,
  sort_order  int NOT NULL DEFAULT 0,
  is_active   boolean NOT NULL DEFAULT true
);

CREATE TABLE form_definitions (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  version     int NOT NULL UNIQUE,
  definition  jsonb NOT NULL,                -- see JSON shape below
  is_current  boolean NOT NULL DEFAULT false,
  created_by  uuid REFERENCES users(id),
  created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_form_current ON form_definitions(is_current) WHERE is_current;

CREATE TABLE classifications (
  communication_id  uuid PRIMARY KEY REFERENCES communications(id) ON DELETE CASCADE,
  type_id           uuid NOT NULL REFERENCES classification_types(id),
  order_value       numeric(10,2),
  notes             text,
  follow_up         boolean NOT NULL DEFAULT false,
  resolved          boolean,                 -- meaningful for Complaint
  resolved_at       timestamptz,
  resolved_by       uuid REFERENCES users(id),
  form_version      int NOT NULL REFERENCES form_definitions(version),
  custom_values     jsonb NOT NULL DEFAULT '{}'::jsonb,   -- { fieldKey: value }
  classified_by     uuid NOT NULL REFERENCES users(id),
  classified_at     timestamptz NOT NULL DEFAULT now(),
  updated_by        uuid REFERENCES users(id),
  updated_at        timestamptz
);
CREATE INDEX ix_class_type ON classifications(type_id);
CREATE INDEX ix_class_custom ON classifications USING gin (custom_values);

CREATE TABLE classification_history (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  communication_id  uuid NOT NULL REFERENCES communications(id) ON DELETE CASCADE,
  changed_by        uuid NOT NULL REFERENCES users(id),
  changed_at        timestamptz NOT NULL DEFAULT now(),
  before            jsonb,                   -- NULL on first classification
  after             jsonb NOT NULL
);
CREATE INDEX ix_class_hist_comm ON classification_history(communication_id);
```

Form definition JSON shape (stored in `form_definitions.definition`):
```json
{
  "fields": [
    { "key": "type",        "kind": "type",     "required": true },
    { "key": "branch",      "kind": "branch",   "required": true },
    { "key": "order_value", "kind": "number",   "label": {"ar": "قيمة الطلب", "en": "Order value"}, "required": false, "showWhenType": ["Order","Cancellation"] },
    { "key": "notes",       "kind": "textarea", "label": {"ar": "ملاحظات", "en": "Notes"}, "required": false },
    { "key": "follow_up",   "kind": "checkbox", "label": {"ar": "متابعة", "en": "Follow-up required"} },
    { "key": "reason",      "kind": "select",   "label": {"ar": "سبب الشكوى", "en": "Complaint reason"},
      "options": [{"value":"late","label":{"ar":"تأخير","en":"Late"}}, {"value":"cold","label":{"ar":"بارد","en":"Cold"}}],
      "showWhenType": ["Complaint"] }
  ]
}
```
Built-in kinds `type`, `branch`, `number`(order_value), `textarea`(notes), `checkbox`(follow_up) map to real columns; any other field goes to `custom_values`. Editing the form creates a new version and sets `is_current`; old classifications keep their `form_version` so history renders correctly.

---

## 5. Follow-up tasks

```sql
CREATE TABLE follow_up_tasks (
  id                        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  contact_id                uuid REFERENCES contacts(id),
  communication_id          uuid REFERENCES communications(id),
  title                     text NOT NULL,
  assigned_to               uuid REFERENCES users(id),
  due_at                    timestamptz,
  status                    text NOT NULL CHECK (status IN ('Open','Done','Cancelled')) DEFAULT 'Open',
  created_from              text NOT NULL CHECK (created_from IN ('Complaint','Abandoned','Missed','Manual')),
  created_by                uuid REFERENCES users(id),
  created_at                timestamptz NOT NULL DEFAULT now(),
  closed_at                 timestamptz,
  closed_by                 uuid REFERENCES users(id),
  closed_by_communication_id uuid REFERENCES communications(id) -- the outbound call that closed it
);
CREATE INDEX ix_tasks_open ON follow_up_tasks(status, due_at) WHERE status = 'Open';
CREATE INDEX ix_tasks_contact ON follow_up_tasks(contact_id);
```

---

## 6. Plumbing / audit

```sql
CREATE TABLE pbx_events_raw (
  id           bigserial PRIMARY KEY,
  received_at  timestamptz NOT NULL DEFAULT now(),
  source       text NOT NULL CHECK (source IN ('AMI','CDR','SIP')),
  event_name   text,
  payload      jsonb NOT NULL
);
CREATE INDEX ix_pbx_events_received ON pbx_events_raw(received_at);
-- retention job deletes rows older than 30 days

CREATE TABLE agent_sessions (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id       uuid NOT NULL REFERENCES users(id),
  laptop_id     text NOT NULL,              -- machine name
  app_version   text,
  logged_in_at  timestamptz NOT NULL DEFAULT now(),
  logged_out_at timestamptz,
  logout_reason text                        -- Manual, Idle, AppClosed, Forced, PasswordReset
);
CREATE INDEX ix_sessions_user ON agent_sessions(user_id, logged_in_at DESC);

CREATE TABLE audit_log (
  id           bigserial PRIMARY KEY,
  at           timestamptz NOT NULL DEFAULT now(),
  user_id      uuid REFERENCES users(id),
  entity       text NOT NULL,               -- 'contact','user','settings','form','flag'
  entity_id    text,
  action       text NOT NULL,               -- 'create','update','merge','flag','unflag','delete'
  before       jsonb,
  after        jsonb
);
CREATE INDEX ix_audit_entity ON audit_log(entity, entity_id);

CREATE TABLE outbox_sync (          -- server-side record of Agent App offline uploads, for idempotency
  client_op_id  uuid PRIMARY KEY,   -- generated on the laptop
  user_id       uuid NOT NULL REFERENCES users(id),
  applied_at    timestamptz NOT NULL DEFAULT now()
);
```

---

## 7. Seed data

- branches: Branch 1–4 (renamed by the supervisor)
- channels: Phone (system), WhatsApp, Facebook, Instagram, Wheels
- classification_types: Order, Cancellation, Complaint, Inquiry, WrongNumber, Other (Order/Complaint system)
- form_definitions v1: the JSON above without the `reason` field
- settings: `recording.retention_days=90`, `agent.idle_logout_minutes=30`, `agent.edit_window=SameDay`, `sla.answer_seconds=20`, `pbx.host=`, `reports.internal_numbers=`, `cdr.interval_seconds=300`, `cdr.last_offset=0`
  - `cdr.last_offset` is the byte position in `Master.csv` the importer has read to (SRS S-55). It is state, not configuration, and is kept here so a restart resumes rather than re-reads. A file shorter than this value means the log rotated: reset to 0 and log it.
  - **Still seeded but now unused:** `callback.extension` (S-56 removed) and `pbx.ami.enabled` (S-50 ruled out). They remain in the settings catalogue in code; removing them is a follow-up to be done with the CDR importer, not before.
- users: one Supervisor created by the seed command

---

## 8. Rules enforced in the API (not the DB)

- Agents read/write only `communications` where `agent_id = me`; classification edit allowed only if `started_at::date = today` (server local time); Supervisors unrestricted.
- Agents read all `contacts`, may create/edit name/address/notes, may not change `is_vip`/`is_blocked`.
- Blocked check: Agent App caches `SELECT normalised FROM contact_phones JOIN contacts ... WHERE is_blocked`; refreshed on login and on push.
- Recording retention job: for `recordings` where `uploaded_at < now() - retention`, delete file, set `deleted_at`.
- `pbx_events_raw` older than 30 days deleted nightly.

---

## 9. Reports → tables (sanity check)

| Report | Tables |
|---|---|
| R‑01 counts, R‑03 per type, R‑04 per day/agent | communications ⨝ classifications |
| R‑02 full list | communications ⨝ classifications ⨝ contacts ⨝ users ⨝ branches ⨝ channels ⨝ recordings |
| R‑05 problems | classifications(type=Complaint) ⨝ follow_up_tasks |
| R‑10 peak hours | communications(started_at) |
| R‑11 missed + callback time | communications(status in Missed/Abandoned/Overflowed) self-join on remote_normalised, next direction=Out |
| R‑12/13 by channel, order value | communications ⨝ channels ⨝ classifications.order_value |
| R‑14 cancellation rate | classifications by type |
| R‑15 agent productivity | communications by agent_id |
| R‑16 new vs returning, inactive | contacts + min(started_at) per contact |
| R‑17 complaint handling | classifications.resolved_at, follow_up_tasks |
| R‑18 data quality | communications without classification / contact; contact_phones duplicates |
| R‑20/21 abandoned, SLA | communications(status, wait_sec) |

Every report resolves to these tables — nothing missing.
