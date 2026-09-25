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
                       ('Ringing','Answered','Missed','Rejected','Blocked','Abandoned','Overflowed','NoAnswer','Failed','Logged')),
                     -- Logged   = App entry or manually logged item
                     -- NoAnswer = an OUTBOUND call the customer did not pick up.
                     --            Never Missed: Missed means a customer rang and
                     --            nobody here answered, which is the service
                     --            failure the reports count (A-20, A-21).
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
  notes              varchar(4000),                               -- why a Missed/Rejected/NoAnswer call went that way (A-41); answered calls are classified instead
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

**The file itself** (written by the Agent App's `CallRecorder`, stored by the
server untouched): a WAV of 8 kHz G.711 mu-law, two channels (customer left,
agent right). The header is 58 bytes (`RIFF`, an 18-byte `fmt `, `fact`,
`data`), so the audio starts at byte 58. **When the agent held the call, a
`hold` chunk follows the audio** (added 24 Sep 2026, A-51): one pair of
little-endian `uint32` per hold, the frame it began at and how many frames it
lasted, where a frame is 1/8000 s. A call never held has no such chunk, so
recordings before that date read as never held. Every WAV reader skips a chunk it
does not know, so the file plays anywhere. The reader is `RecordingWav` in the
Agent App. The supervisor's player reads the same chunk in the browser
(`src/CallCenter.Web/src/lib/recordingWav.ts`, since 24 Sep 2026), and also
decodes the mu-law, which Chrome and Edge do not play. Change one reader, change
both.

Notes
- One table for phone and app makes every report one query (`kind`/`channel_id` splits them).
- A message (A-70, since 2026-09-25) is `kind = 'App'`, `direction = 'None'`, `status = 'Logged'`, `source = 'Manual'`, filed under the app's `channel_id`, with `started_at` the time the customer wrote (defaults to when it was recorded; an agent may set it back within the day). No `sip_call_id`, `extension`, `answered_at`, `ended_at` or recording. Its classification, edit window and history are exactly a call's. Written by `ApplicationsService`; searched with `kind=App` on the call search; the call log, the caller card's recent list and the Calls page filter to `kind = 'Call'`.
- `channels` is managed by the supervisor since 2026-09-25 (S-41): add, rename, reorder, hide, never delete. Phone (`is_system`) can be neither renamed nor hidden, because `CommunicationsService.LogCallAsync` files every call under it by name.
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
  version     int NOT NULL UNIQUE,            -- one sequence across both directions
  definition  jsonb NOT NULL,                -- see JSON shape below
  direction   varchar(10) NOT NULL DEFAULT 'In',  -- 'In' or 'Out': each has its own form (A-21, S-40)
  is_current  boolean NOT NULL DEFAULT false,
  created_by  uuid REFERENCES users(id),
  created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_form_current ON form_definitions(direction) WHERE is_current;  -- one current form per direction

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
Built-in kinds `type`, `branch`, `number`(order_value), `textarea`(notes), `checkbox`(follow_up) map to real columns; any other field goes to `custom_values`. Editing a form creates a new version and sets `is_current` for its direction; old classifications keep their `form_version` so history renders correctly. Inbound and outbound calls have separate forms (added 2026-09-24): `GET /api/classifications/form?direction=Out` returns the outbound one, and the Agent App draws whichever matches the call's direction. The types are shared. Messages (A-70, `kind = 'App'`) have a third form under `direction = 'None'` (added 2026-09-25, migration `FormForApplications`): `GET /api/classifications/form?direction=None`. It starts as a copy of the inbound form and the supervisor edits it on its own tab. A classification is allowed on an Answered call or on an App row (which is always `Logged`); the check is `ClassificationService.CanBeClassified`.

---

## 4a. Delivery areas

Where the restaurant delivers, which branch covers it, and the price (A-65,
S-58). Read by agents mid-call; maintained by the supervisor, usually by pasting
a branch's list out of the spreadsheet it already lives in.

```sql
CREATE TABLE delivery_areas (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name             varchar(200) NOT NULL,        -- as typed, as the agent reads it
  name_normalised  varchar(200) NOT NULL,        -- NameNormalizer: hamza folded, spacing levelled
  branch_id        uuid NOT NULL REFERENCES branches(id),
  price            numeric(10,2) NOT NULL,
  is_active        boolean NOT NULL DEFAULT true,
  created_by       uuid REFERENCES users(id),
  created_at       timestamptz NOT NULL DEFAULT now(),
  updated_by       uuid REFERENCES users(id),
  updated_at       timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_delivery_areas_price CHECK (price >= 0)
);
CREATE UNIQUE INDEX ux_delivery_area_name ON delivery_areas(name_normalised);
CREATE INDEX ix_delivery_area_branch ON delivery_areas(branch_id);
```

- **The name is unique on its own, not per branch.** The agent's question is
  "who delivers to Kafr Aqab?", and an area listed under two branches has no
  answer. The restaurant's own lists work this way — 228 areas, four branches,
  none shared.
- **`price = 0` is a real price**, not a missing one: three areas beside the
  Rafat branch deliver free. Nothing may treat 0 as unset. Negative is refused
  by the CHECK.
- **`name_normalised`** gets the same folding as contact names, because these
  are Arabic place names copied from handwritten lists by several people. Without
  it an agent searches, finds nothing, and quotes the wrong price.
- **`is_active`** hides an area without losing its price. Deleting is also
  allowed — nothing references a delivery area — but an area the restaurant stops
  serving usually comes back.

---

## 4b. Menu

What the restaurant sells, what is in it, what it costs and a picture of it
(A-66, S-59). Read by agents mid-call; maintained by the supervisor.

```sql
CREATE TABLE menu_categories (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name             varchar(200) NOT NULL,
  name_normalised  varchar(200) NOT NULL,
  sort_order       int NOT NULL DEFAULT 0,       -- the order the printed menu reads
  is_active        boolean NOT NULL DEFAULT true
);
CREATE UNIQUE INDEX ux_menu_category_name ON menu_categories(name_normalised);

CREATE TABLE menu_items (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  category_id        uuid NOT NULL REFERENCES menu_categories(id),
  name               varchar(200) NOT NULL,
  name_normalised    varchar(200) NOT NULL,
  description        text,                        -- contents, as the menu prints them
  price              numeric(10,2),               -- item alone, or the sandwich
  meal_price         numeric(10,2),               -- with fries and a drink
  is_surcharge       boolean NOT NULL DEFAULT false,
  image_file_name    varchar(200),                -- the photograph, under the menu-images folder
  sort_order         int NOT NULL DEFAULT 0,
  is_active          boolean NOT NULL DEFAULT true,
  created_by         uuid REFERENCES users(id),
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_by         uuid REFERENCES users(id),
  updated_at         timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_menu_items_price      CHECK (price IS NULL OR price >= 0),
  CONSTRAINT ck_menu_items_meal_price CHECK (meal_price IS NULL OR meal_price >= 0)
);
CREATE UNIQUE INDEX ux_menu_item_name_in_category ON menu_items(category_id, name_normalised);
CREATE INDEX ix_menu_item_name  ON menu_items(name_normalised);
CREATE INDEX ix_menu_item_order ON menu_items(category_id, sort_order);
```

- **Arabic only.** The source spreadsheet carries English names and contents as
  well; they are deliberately not stored. The menu is Arabic, agents speak
  Arabic to customers, and a second set of names is a second thing to keep
  correct.
- **`price NULL` and `price = 0` mean different things.** Null is "the menu
  prints no price" — there is one such item, a combination offer. Zero is the
  price of a free extra. An agent must be able to tell "free" from "ask the
  branch".
- **`is_surcharge`** marks the add-ons, which the menu writes as "+2": an amount
  added to another item, not a price of its own. Without it, "إضافة الجبنة — 2"
  reads as a portion of cheese costing 2.
- **The name is unique per category, not globally** — unlike delivery areas.
  Two categories may each legitimately hold a "كولا".
- **The picture is a file, and this column is only its name.** The first
  version stored the bytes here as `bytea`; that was wrong because **`rsync` is
  incremental and `pg_dump` is not**. Menu photographs never change, so on disk
  the nightly backup (runbook step 9) copies them once, while in the database
  they were re-dumped and re-copied every night for ever. The backup already
  rsyncs a data folder — the recordings — so files were never the extra thing to
  remember they were assumed to be.
  The name is the item's id plus an extension, never anything typed, so there is
  no path to traverse. Storing it rather than deriving it lets the list say
  whether an item has a picture without asking the disk once per row.
  A row whose file is missing simply has no picture and answers 404 for it; the
  pictures ship as embedded resources, so re-running `seed` puts them back.

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

- branches: رافات, بطن الهوى, ايكون, نابلس (renameable by the supervisor; the delivery areas hold a branch id, not a name)
- delivery_areas: 228 rows from the branches' own price lists (A-65)
- menu_categories / menu_items: 12 categories and 44 items with 39 pictures, from the printed menu (A-66)
- contacts / contact_phones: 15,289 customers and 15,529 numbers, carried over from the old ordering system (A-61). The export is `docs/Contacts.xlsx`; `tools/contacts-import/convert.py` turns it into the gzipped CSV embedded in the server assembly. Numbers go through `PhoneNormalizer` at seed time, so caller matching (A-13) finds them. 69 of the 15,358 exported rows are dropped as duplicates - every number on them already belongs to a contact inserted earlier.
- channels: Phone (system), WhatsApp, Facebook, Instagram, Wheels, Yummy, FoodOnTime, PalEat (the last three added 2026-09-25; the seed adds any name that is missing)
- classification_types: Order, Cancellation, Complaint, Inquiry, WrongNumber, Other (Order/Complaint system)
- form_definitions v1: the JSON above without the `reason` field (direction `In`)
- form_definitions, outbound: `type`, `notes`, `follow_up` (direction `Out`), numbered after whatever exists. The migration `FormPerDirection` adds it to a database that predates it.
- form_definitions, applications: a copy of v1 (direction `None`), for messages (A-70). The migration `FormForApplications` adds it to a database that predates it.
- settings: `recording.retention_days=90`, `agent.idle_logout_minutes=240`, `agent.edit_window=SameDay`, `sla.answer_seconds=20`, `pbx.host=`, `reports.internal_numbers=`, `cdr.interval_seconds=300`, `cdr.last_offset=0`, `agent.call_log_days=7`
  - `cdr.last_offset` is the byte position in `Master.csv` the importer has read to (SRS S-55). It is state, not configuration, and is kept here so a restart resumes rather than re-reads. A file shorter than this value means the log rotated: reset to 0 and log it.
  - `callback.extension` and `pbx.ami.enabled` were **removed on 2026-09-21**. Both belonged to approaches ruled out in SRS 4.5, and a setting the supervisor can edit that changes nothing is worse than a missing one.
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
