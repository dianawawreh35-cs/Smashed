# Database Schema — Restaurant Call Center System

PostgreSQL 16. UUID primary keys, all timestamps `timestamptz` (UTC), soft delete only where stated.
Enumerations are `text` columns with CHECK constraints (simple for EF Core, readable in SQL, changeable without migrations of a type).
It was written first, as the design the first prompt built from. Since then the EF Core migrations are the record, and this document is kept in step with them, a migration at a time.
*Reconciled with the EF Core model snapshot on 27 Sep 2026 (review item, docs section).*

Conventions the blocks below leave out, to stay readable:
- Every foreign key is `ON DELETE RESTRICT` unless the block says otherwise (`CASCADE` or `SET NULL`).
- EF Core also indexes every foreign-key column that no listed index already covers, named `ix_<table>_<column>` (for example `ix_contacts_created_by`). Those indexes are not listed.
- A column marked `UNIQUE` is a unique index in the database, named `ix_<table>_<column>`, not a named constraint. `form_definitions.version` has a unique constraint as well (`ak_form_definitions_version`), because `classifications.form_version` references it.
- CHECK constraints are named `ck_<table>_<column>`, for example `ck_communications_status`.

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
  -- One extension per agent (SRS 2.3, migration SingleExtensionPerAgent, 17 Sep 2026).
  extension             text,                                -- the agent's PBX extension, e.g. '101'
  sip_secret            text,                                -- its SIP password, encrypted at rest (app-level key)
  is_active             boolean NOT NULL DEFAULT true,
  created_at            timestamptz NOT NULL DEFAULT now(),
  last_login_at         timestamptz
);

CREATE TABLE settings (
  key         text PRIMARY KEY,           -- e.g. 'recording.retention_days', 'agent.idle_logout_minutes',
  value       text NOT NULL,              --      'pbx.host', 'sla.answer_seconds', 'reports.internal_numbers' (S-48);
  --                                         the full list is in section 7
  updated_by  uuid REFERENCES users(id),
  updated_at  timestamptz NOT NULL DEFAULT now()
);
```

Notes
- One extension per agent lives on the user row; the Agent App receives it and its secret after login. The secret is never returned to the supervisor UI in clear.
- There used to be two per agent, `customer_extension` and `internal_extension`, each with its secret. The migration `SingleExtensionPerAgent` (17 Sep 2026) dropped the internal pair and renamed the customer pair to `extension` and `sip_secret`. Internal calls are now told apart by the other party's number, against `reports.internal_numbers` (S-48).
- `settings` is a key/value bag so the supervisor can change behaviour without a migration.

---

## 2. Customers

```sql
CREATE TABLE contacts (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name             text,                       -- nullable: a bare number can be flagged before it has a name
  name_normalised  text,                       -- NameNormalizer's folded form of name, for matching (A-63); NULL when no name
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
  source           varchar(10) NOT NULL DEFAULT 'Agent'
                   CONSTRAINT ck_contacts_source CHECK (source IN ('Seed','Agent','Pos')),
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
CREATE INDEX ix_contacts_name_normalised ON contacts(name_normalised);
```

Notes
- Matching a caller: normalise → exact match on `normalised`; if none, match on `last9` (handles 05x vs 9705x vs +9705x). Unique index on `normalised` gives duplicate detection for free.
- Merge = move phones and communications to the target, set `merged_into_id` and `deleted_at` on the source. Never hard-delete.
- `name_normalised` (migration `ContactNameNormalised`, 17 Sep 2026) is the name compared for the duplicate-name warning (A-63), because PostgreSQL does not treat `'أحمد'` and `'احمد'` as equal. It folds أ إ آ ٱ to ا, ى to ي, ة to ه, ؤ to و and ئ to ي, deletes the tashkeel and tatweel, lower-cases and levels the spacing. `CallCenter.Shared.Text.NameNormalizer` sets it on every save and is authoritative; the migration's SQL backfill only filled the rows that existed then. A nameless contact keeps it NULL, so it matches no other nameless contact.
- `ix_contacts_name` is an expression index, so it exists only as raw SQL in its own migration (`AddContactsFullTextIndex`), not in the EF model.
- `source` (migration `AddContactSource`, 27 Sep 2026): where the contact came from. `Seed` is the old system's customer book loaded by `seed`, `Agent` anything a person saved in either app, `Pos` a contact the POS lookup made (A-67). R-16 counts every contact as new in the period it was made except `Seed`. It used to be read from an empty `created_by`, which the POS lookup's contacts have too (F-10). The migration worked the existing rows out: a creator → `Agent`; no creator and made within ten minutes of the first creator-less contact (the seed runs once, in about ten seconds) → `Seed`; any other creator-less contact → `Pos`.

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
  wait_sec           int,                                         -- queue wait, from the PBX import (S-55)
  queue_name         text,
  extension          text,                                        -- which extension handled it
  sip_call_id        text,                                        -- from the Agent App INVITE
  pbx_unique_id      text,                                        -- the PBX import's key: 'issabel:' + hang-up time|number|queue (S-55)
  abandoned_call_id  uuid REFERENCES communications(id) ON DELETE SET NULL, -- on an untaken Agent App ring: the abandoned call it was part of (S-55)
  source             text NOT NULL CHECK (source IN ('AgentApp','AMI','CDR','Manual')),
  laptop_id          text,                                        -- laptop that logged it: machine name + install tag since 27 Sep (A-05)
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
CREATE INDEX ix_comm_abandoned_call    ON communications(abandoned_call_id) WHERE abandoned_call_id IS NOT NULL;
```

> **Two values in these CHECK constraints are no longer written** (SRS 4.5, 19 Sep 2026): `source = 'AMI'`, because AMI is ruled out, and `status = 'Overflowed'`, because the call-back extension that produced it was removed. Both are **left in place deliberately** — narrowing a CHECK is a migration, it would gain nothing, and a future method could use either again. `ux_comm_pbx_unique` is what makes downloading the same day from the PBX every minute harmless.

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
- Abandoned calls (S-55, since 2026-09-26) are `source = 'CDR'`, `status = 'Abandoned'`, `direction = 'In'`, no agent, from the PBX's Calls Detail report; `started_at` is when the caller joined the queue, `ended_at` the hang-up, `wait_sec` the time between. The Agent App's untaken rings of that call (Missed, Rejected, Blocked, same number, inside the wait) point at it through `abandoned_call_id`, and the reports leave them out of every figure but R-15's, so the customer counts once. A call every ring of which was Blocked is saved as `Blocked`. The import's own state (address, login, last check) is in `settings` under `pbx.calls.*`, outside the settings catalogue.

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
  direction   varchar(10) NOT NULL DEFAULT 'In',  -- 'In', 'Out' or 'None': In and Out each have their own form (A-21, S-40),
  --                                                None is the messages' form (A-70). No CHECK: the application writes only these.
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
  communication_id  uuid NOT NULL REFERENCES communications(id) ON DELETE RESTRICT, -- CASCADE until 27 Sep 2026 (M-D05)
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
Built-in kinds `type`, `branch`, `number`(order_value), `textarea`(notes), `checkbox`(follow_up) map to real columns; any other field goes to `custom_values`. Editing a form creates a new version and sets `is_current` for its direction; old classifications keep their `form_version` so history renders correctly. Inbound and outbound calls have separate forms (added 2026-09-24): `GET /api/classifications/form?direction=Out` returns the outbound one, and the Agent App draws whichever matches the call's direction. The type list is shared, and each form may narrow it: its `type` field can carry `"types": ["Order", "Complaint"]`, type **names** (like `showWhenType`), which the Agent App, the web editor and `ClassificationService.SaveAsync` all hold to (`type_not_offered`). No list means every type; an empty list or an unknown name is refused on publish (added 2026-09-25). The types are shared. Messages (A-70, `kind = 'App'`) have a third form under `direction = 'None'` (added 2026-09-25, migration `FormForApplications`): `GET /api/classifications/form?direction=None`. It starts as a copy of the inbound form and the supervisor edits it on its own tab. A classification is allowed on an Answered call or on an App row (which is always `Logged`); the check is `ClassificationService.CanBeClassified`.

Notes
- `classification_history.communication_id` is `ON DELETE RESTRICT` (migration `ProtectClassificationHistory`, 27 Sep 2026): the audit trail is not deleted with its call (M-D05). Nothing in the application deletes a call; a script that does must delete the history first, on purpose. `classifications` and `recordings` still cascade.

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
  sort_order       int NOT NULL,                 -- the order the printed menu reads; no default, the application sets it
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
  is_surcharge       boolean NOT NULL,            -- no default, the application sets it
  image_file_name    varchar(200),                -- the photograph, under the menu-images folder
  sort_order         int NOT NULL,                -- no default, the application sets it
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

## 4c. Mistakes

The mistakes made by a branch or an agent, as the supervisor records them on
the Mistakes page (S-65). Added by migration `AddMistakes` (1 Oct 2026).

```sql
CREATE TABLE mistakes (
  id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  occurred_on          date NOT NULL,                 -- the restaurant's day; never after today (API)
  branch_id            uuid NOT NULL REFERENCES branches(id),
  responsible          text NOT NULL,                 -- 'Branch' or 'Agent'
  agent_id             uuid REFERENCES users(id),     -- set exactly when responsible = 'Agent'
  value                numeric(10,2),                 -- shekels; NULL is "no value", not zero
  contact_id           uuid REFERENCES contacts(id),  -- the saved customer the number belonged to on save
  customer_number_raw  varchar(32),                   -- as typed, kept when nobody has it on file
  customer_normalised  varchar(32),                   -- PhoneNormalizer, for the search
  notes                text NOT NULL,                 -- what went wrong; never blank (API)
  created_by           uuid REFERENCES users(id),
  created_at           timestamptz NOT NULL DEFAULT now(),
  updated_by           uuid REFERENCES users(id),
  updated_at           timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_mistakes_responsible CHECK (responsible IN ('Branch','Agent')),
  CONSTRAINT ck_mistakes_agent CHECK ((responsible = 'Agent') = (agent_id IS NOT NULL)),
  CONSTRAINT ck_mistakes_value CHECK (value IS NULL OR value >= 0)
);
CREATE INDEX ix_mistakes_occurred ON mistakes(occurred_on);
```

- **Every mistake has a branch**, whoever is responsible. `ck_mistakes_agent`
  holds the rest: a branch's mistake names no agent, an agent's always names one.
- **`occurred_on` is a `date`**, the one exception to "all timestamps
  `timestamptz`": nobody knows the minute a mistake happened, and a day has no
  time zone to shift it across midnight.
- **The customer is kept by number.** `contact_id` is found on save by the
  caller lookup's rule (A-13) and kept while the number is unchanged. A number
  nobody has on file stays in `customer_number_raw` with no contact.
- **Hard-deleted** when the supervisor removes one entered by error; the
  `audit_log` row (`entity = 'mistake'`) keeps what it said.

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
CREATE TABLE pbx_events_raw (       -- nothing writes to this table at present (review of 27 Sep 2026)
  id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  received_at  timestamptz NOT NULL DEFAULT now(),
  source       text NOT NULL CHECK (source IN ('AMI','CDR','SIP')),
  event_name   text,
  payload      jsonb NOT NULL
);
CREATE INDEX ix_pbx_events_received ON pbx_events_raw(received_at);
-- planned: a retention job deleting rows older than 30 days; none exists yet

CREATE TABLE pbx_blacklist (        -- S-46: numbers the server has put on the PBX's own blacklist (*30), or is trying to add or remove
  number          varchar(32) PRIMARY KEY CHECK (number ~ '^[0-9]+$'),  -- as keyed into the PBX: local form, 0599123456
  on_pbx          boolean NOT NULL DEFAULT false,  -- the last *30 succeeded and no *31 has since
  added_at        timestamptz,
  last_attempt_at timestamptz,
  last_error      text,                             -- why the last call failed; NULL when it succeeded
  failed_attempts int NOT NULL DEFAULT 0            -- in a row; reset by a success
);
-- The Blocked flag on contacts is what the supervisor decided; this is what the PBX has been told.
-- A number put on the PBX's blacklist by hand has no row here and is never removed by the server.

CREATE TABLE agent_sessions (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id       uuid NOT NULL REFERENCES users(id),
  laptop_id     text NOT NULL,              -- machine name + install tag, DESKTOP-X-7F3A2C (since 27 Sep; the name alone before)
  app_version   text,
  logged_in_at  timestamptz NOT NULL DEFAULT now(),
  logged_out_at timestamptz,
  logout_reason text,                       -- Manual, Idle, AppClosed, Forced, PasswordReset, SignedInElsewhere (N-05)
  last_seen_at  timestamptz                 -- last request with this session's token, to the minute; online = within 5 min
);
CREATE INDEX ix_sessions_user ON agent_sessions(user_id, logged_in_at DESC);

CREATE TABLE agent_breaks (         -- A-86: each break, Break in to Break out; migration AddAgentBreaks (1 Oct 2026)
  id          uuid PRIMARY KEY,                           -- made by the Agent App, so a resend is the same row
  user_id     uuid NOT NULL REFERENCES users(id),
  session_id  uuid REFERENCES agent_sessions(id) ON DELETE SET NULL,  -- the sign-in it was taken under
  started_at  timestamptz NOT NULL,
  ended_at    timestamptz,                                -- NULL while going, or until the server works the end out
  ended_by    text,                                       -- BreakOut, SignedOut (sent by the app); SessionEnded, NotHeard (the server's)
  created_at  timestamptz NOT NULL DEFAULT now(),
  updated_at  timestamptz NOT NULL DEFAULT now(),
  CONSTRAINT ck_agent_breaks_ended_by CHECK (ended_by IN ('BreakOut','SignedOut','SessionEnded','NotHeard')),
  CONSTRAINT ck_agent_breaks_end CHECK ((ended_at IS NULL) = (ended_by IS NULL) AND (ended_at IS NULL OR ended_at >= started_at))
);
CREATE INDEX ix_agent_breaks_user    ON agent_breaks(user_id, started_at DESC);
CREATE INDEX ix_agent_breaks_started ON agent_breaks(started_at);
CREATE INDEX ix_agent_breaks_session_id ON agent_breaks(session_id);
-- A break with no end takes it from its sign-in (BreakClock): when the sign-in ended (SessionEnded),
-- or when the app was last heard from if that was more than five minutes earlier (NotHeard).
-- The server writes that end in when the sign-in closes, or when the agent starts another break.
-- A break over midnight counts on each day for its own part of it.

CREATE TABLE audit_log (
  id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  at           timestamptz NOT NULL DEFAULT now(),
  user_id      uuid REFERENCES users(id),
  entity       text NOT NULL,               -- 'contact','user','settings','form','flag','queue','listen'
  entity_id    text,
  action       text NOT NULL,               -- 'create','update','merge','flag','unflag','delete'
  before       jsonb,
  after        jsonb
);
CREATE INDEX ix_audit_entity ON audit_log(entity, entity_id);

CREATE TABLE outbox_sync (          -- server-side record of Agent App offline uploads, for idempotency;
                                    -- nothing writes to it at present (review of 27 Sep 2026)
  client_op_id  uuid PRIMARY KEY,   -- generated on the laptop
  user_id       uuid NOT NULL REFERENCES users(id),
  applied_at    timestamptz NOT NULL DEFAULT now()
);
```

---

## 7. Seed data

- branches: رافات, بطن الهوى, ايكون, نابلس (renameable by the supervisor; the delivery areas hold a branch id, not a name)
- delivery_areas: 228 rows from the branches' own price lists (A-65)
- menu_categories / menu_items: 14 categories and 48 items with 43 pictures, from the printed menu (A-66). The two crispy burger categories were added 2026-09-27; the seed adds any category or item that is missing and changes none that exist
- contacts / contact_phones: 15,289 customers and 15,529 numbers, carried over from the old ordering system (A-61). The export is `docs/Contacts.xlsx`; `tools/contacts-import/convert.py` turns it into the gzipped CSV embedded in the server assembly. Numbers go through `PhoneNormalizer` at seed time, so caller matching (A-13) finds them. 69 of the 15,358 exported rows are dropped as duplicates - every number on them already belongs to a contact inserted earlier.
- channels: Phone (system), WhatsApp, Facebook, Instagram, Wheels, Yummy, FoodOnTime, PalEat (the last three added 2026-09-25; the seed adds any name that is missing)
- classification_types: Order, Cancellation, Complaint, Inquiry, WrongNumber, Other (Order/Complaint system)
- form_definitions v1: the JSON above without the `reason` field (direction `In`)
- form_definitions, outbound: `type`, `notes`, `follow_up` (direction `Out`), numbered after whatever exists. The migration `FormPerDirection` adds it to a database that predates it.
- form_definitions, applications: a copy of v1 (direction `None`), for messages (A-70). The migration `FormForApplications` adds it to a database that predates it.
- settings: `recording.retention_days=90`, `agent.call_log_days=7`, `pos.lookup.interval_minutes=5`, `breaks.daily_limit_minutes=60` (A-86, added 1 Oct 2026; also written by the migration `SeedBreakAllowance` where missing, since an update does not run the seed), `agent.idle_logout_minutes=240`, `agent.edit_window=SameDay`, `sla.answer_seconds=20`, `pbx.host=`, `reports.internal_numbers=`, `queue.auto_open_time=07:00` (`SeedData.Settings`). These ten are also the whole settings catalogue (`SettingsCatalog`, S-47): the only keys the supervisor may change, and any other key is refused.
  - The server also keeps its own state in `settings`, outside the catalogue and not seeded; each row is written the first time the feature saves it:
    - `pbx.calls.*` (the abandoned-call import, S-55): `url`, `username`, `password` (encrypted with the SIP-secret key), `interval_minutes`, `last_checked_at`, `last_succeeded_at`, `last_error`, `last_added`, `synced_through`.
    - `pbx.features.*` (the server's own PBX extension, which places the `*30`/`*31` and `*280` feature-code calls): `extension`, `secret` (encrypted with the SIP-secret key).
    - `pbx.queue.*` (the queue switch, S-60): `is_open`, `changed_at`, `changed_by` (a user id, or `auto`), `auto_done_on`, `auto_tried_at`, `auto_problem`, `auto_problem_on`.
    - `pbx.blacklist.*` (the PBX blacklist sync, S-46): `last_succeeded_at`.
  - There is no `cdr.*` setting: `cdr.interval_seconds` and `cdr.last_offset` (a `Master.csv` importer's state) were listed here but are neither seeded nor read by anything. The abandoned calls come from the PBX's Calls Detail report instead (`pbx.calls.*` above).
  - `pbx.ip` was renamed to `pbx.host`; the migration `SingleExtensionPerAgent` deletes the left-over `pbx.ip` row.
  - `callback.extension` and `pbx.ami.enabled` were **removed on 2026-09-21**. Both belonged to approaches ruled out in SRS 4.5, and a setting the supervisor can edit that changes nothing is worse than a missing one.
- users: one Supervisor created by the seed command

---

## 8. Rules enforced in the API (not the DB)

- Agents read/write only `communications` where `agent_id = me`; classification edit allowed only if `started_at::date = today` (server local time); Supervisors unrestricted.
- Agents read all `contacts`, may create/edit name/address/notes, may not change `is_vip`/`is_blocked`.
- Blocked check: Agent App caches `SELECT normalised FROM contact_phones JOIN contacts ... WHERE is_blocked`; refreshed on login and on push.
- Recording retention job: for `recordings` where `uploaded_at < now() - retention`, delete file, set `deleted_at`.
- `pbx_events_raw` older than 30 days deleted nightly (planned; nothing writes to the table and no job exists yet).

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
| R‑16 new vs returning, inactive | contacts(source, created_at) + min(started_at) per contact |
| R‑17 complaint handling | classifications.resolved_at, follow_up_tasks |
| R‑18 data quality | communications without classification / contact; contact_phones duplicates |
| R‑20/21 abandoned, SLA | communications(status, wait_sec) |
| R‑22 breaks, S‑66 break monitor | agent_breaks ⨝ agent_sessions ⨝ users |

Every report resolves to these tables — nothing missing.

---

## 10. The Agent App's offline buffer (SQLite, on each laptop)

Not part of the server's database: `%LOCALAPPDATA%\CallCenter\agent-buffer.db` on
every agent laptop, holding what has not reached the server yet (A-04). Created
with `EnsureCreated`, and changed only by adding nullable columns in place
(`AgentBufferDbContext.UpgradeAsync`), so a laptop with rows waiting keeps them
through an update. See `src/CallCenter.AgentApp/Data/README.md`.

```sql
CREATE TABLE pending_uploads (
  "Id"          INTEGER PRIMARY KEY AUTOINCREMENT,  -- the replay order
  "Kind"        TEXT NOT NULL,     -- Call | Classification | Notes | Recording | Break (A-86, 1 Oct 2026)
  "Payload"     TEXT NOT NULL,     -- the request body as JSON; a recording's is its file path
  "Reference"   TEXT,              -- SipCallId|Extension: the call it belongs to; break|{id} for a break
  "CreatedAt"   TEXT NOT NULL,
  "Attempts"    INTEGER NOT NULL,  -- failures that count towards setting it aside
  "LastError"   TEXT,              -- the server's code, or why it was set aside
  "UserId"      TEXT,              -- added 27 Sep (F-03): who queued it; replayed only as them
  "SetAsideAt"  TEXT               -- added 27 Sep (F-08): given up on; null while still tried
);
CREATE INDEX ix_pending_order     ON pending_uploads ("Id");
CREATE INDEX ix_pending_reference ON pending_uploads ("Reference");
```
