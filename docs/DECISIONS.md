# Decision log

Decisions that shaped the repository, newest first. Each entry records what was
chosen and why, so a later reader does not have to re-derive it.

---

## 2026-09-14 — Initial repository scaffold

### Stack (given, not chosen here)

| Layer | Choice |
| --- | --- |
| Runtime | .NET 8 SDK, C# `latest`, nullable + implicit usings on, `TreatWarningsAsErrors=false` |
| Server | ASP.NET Core Web API, SignalR, EF Core 8, Npgsql, Serilog |
| Agent App | WPF (.NET 8, Windows), SIPSorcery + SIPSorceryMedia.Windows, EF Core SQLite offline buffer, CommunityToolkit.Mvvm |
| Supervisor Web | React 18, TypeScript, Vite, Tailwind, Recharts, react-router, TanStack Query, i18next (Arabic RTL default, English) |
| Shared | Class library referenced by Server and Agent App |
| Tests | xUnit + FluentAssertions; Vitest for the web |
| Containers | Docker + Docker Compose, PostgreSQL 16 |
| Web toolchain | Node 20 |

### Decisions made while scaffolding

**Central package management.** All NuGet versions live in
`Directory.Packages.props`; project files reference packages without versions.
One place to audit and bump, and the server and agent app cannot silently drift
onto different EF Core versions.

**Agent app targets `net8.0-windows10.0.17763.0`, not `net8.0-windows`.**
`SIPSorceryMedia.Windows` ships only a `net8.0-windows10.0.17763` asset, so the
default `net8.0-windows7.0` moniker fails to restore. Windows 10 1809 is
therefore the minimum supported client OS for the agent desktop.

Two floors stack here, and SIPSorcery is the smaller one: .NET 8 itself does not
support Windows 8.1 or Windows 7 — its oldest supported client is Windows 10
1607 — and SIPSorceryMedia.Windows raises that to 1809. **The agent laptops run
Windows 11**, so neither floor is a practical constraint; `app.manifest`
declares Windows 10/11 compatibility only. Nothing older than Windows 10 1809 is
reachable without abandoning .NET 8 for the agent app, which is not on the table.

**SIPSorcery pinned to 8.0.23, SIPSorceryMedia.Windows to 8.0.14.** These are
the newest releases of each that still target .NET 8 — SIPSorcery 10.x and
SIPSorceryMedia.Windows 10.x are `net10.0` only. **Known issue:** NuGet reports
two high-severity advisories against SIPSorcery 8.x
([GHSA-28gm-jrmw-xx93](https://github.com/advisories/GHSA-28gm-jrmw-xx93),
[GHSA-jwjp-4649-v8jp](https://github.com/advisories/GHSA-jwjp-4649-v8jp)) that
are fixed only in the 10.x line. Restore surfaces them as `NU1903` warnings, not
errors. Resolving this means moving the agent app to .NET 10, which is a stack
change outside this scaffold — it needs an explicit decision before the SIP
work begins.

**FluentAssertions pinned to 6.12.2.** Version 7 and later require a paid
commercial licence; 6.x remains Apache-2.0.

**`/health` needs no database.** The health check is liveness only, so the API
starts and answers `/health` with PostgreSQL down, and the server smoke test
runs in CI without a database service. A readiness check that verifies the
connection is added with the entity work.

**The SPA is served by the API in production, proxied in development.** The
Docker build compiles the web app with Node 20 and copies `dist/` into the
server's `wwwroot`; `MapFallbackToFile("index.html")` makes client-side routing
survive a refresh. In development Vite proxies `/api`, `/hubs`, `/health` and
`/swagger` to `http://localhost:5000`, so the API base URL is relative (`/api`)
in both environments and CORS is only configured for the Development
environment. Note that `wwwroot` must exist when the host starts or static file
serving is silently disabled — hence the tracked `.gitkeep`.

**API runs with `network_mode: host` in production.** SIP signalling and the RTP
media path do not survive Docker's NAT, and the AMI connection needs to reach
the PBX directly. PostgreSQL is bound to `127.0.0.1:5432` so it is never exposed
on the LAN.

**`Asia/Hebron` is set on every container.** Shift boundaries, daily reports and
retention windows are all local-time concepts; leaving containers on UTC would
silently shift report day boundaries.

**Arabic is the default language, right-to-left.** `index.html` ships
`lang="ar" dir="rtl"`, the WPF shell sets `FlowDirection="RightToLeft"`, and the
language switcher flips both the i18next language and `document.documentElement.dir`.
English is the secondary language, not the fallback default.

**CI builds the .NET solution on `windows-latest`.** The WPF agent app cannot be
built on Linux; the web job and the Docker image job run on `ubuntu-latest`.

**Phone numbers are stored as E.164 without the leading `+`.** `PhoneNormalizer`
folds every dialled form — `05x…`, `+970…`, `00970…`, Arabic-Indic digits,
separators — to one canonical string, and `Last9()` gives the fuzzy match key
used to find a contact from caller ID. Israeli (`972`) numbers are recognised
and deliberately passed through unchanged rather than rewritten, and numbers of
five digits or fewer are treated as internal extensions and never given a
country code.

**Enum string values are PascalCase, matching the schema exactly.** The CHECK
constraints in `SCHEMA.md` store `'Call'`, `'In'`, `'AgentApp'`, `'AMI'` and so
on, not lowercase or snake_case. `EnumStrings.ToDbValue()` maps each member
explicitly rather than relying on `ToString()`, because the acronym members
(`Ami`, `Cdr`, `Sip`) must serialise as `AMI`, `CDR`, `SIP`. Parsing is
case-insensitive but rejects numeric input, so a corrupt column value fails
loudly instead of silently becoming the member with that ordinal.
`EnumStringsTests` locks all eight sets against the schema.

> **`CallCenter.Shared.TaskStatus` shadows `System.Threading.Tasks.TaskStatus`**,
> which implicit usings bring into scope. Outside the `CallCenter.Shared`
> namespace it must be qualified or aliased. The name was kept because it is the
> name the schema and the prompts use.

### Reconciled against the SRS (2026-09-14)

All three source documents are now in `docs/`. What they confirmed or corrected:

**There is no `Admin` application role, and that is correct.** SRS §2.2 lists a
third role, *Administrator (technical)* — but its permissions are "server
settings, backups, updates", and it "may be the same person as the supervisor or
the developer". It is an operator of the box, not a user of the software, which
is why `users.role` is constrained to `('Agent','Supervisor')`. Do not add an
Admin role to the schema on the strength of §2.2.

**`PhoneNormalizer` is validated by A-13**, which requires numbers to match
"regardless of format (05…, +9705…, 02…, 972…)" — the exact four forms the
normaliser folds, including passing `972` through unchanged.

**Windows 10/11 is the contracted platform** (N-10), so the
`net8.0-windows10.0.17763.0` floor is inside the agreed scope. Five agents on
four shared laptops (§2.4), so the app must support agent switching (A-05).

**Arabic RTL + English is contractual** (A-80, N-09), for *both* applications —
not a preference. The SPA and the WPF shell already default to Arabic.

**Capacity target is ~500 communications/day, reports under 5 s over one year**
(N-02), 5 agents scaling to 20 (N-03). That is small — roughly 180k rows a year.
The indexes in `SCHEMA.md` are comfortably sufficient; no partitioning needed.

**The PBX is an Issabel (Asterisk) system hosted by an external telephony
provider and reached over a VPN, and AMI is TCP 5038** (S-50). Issabel enables
AMI by default, but the provider must create a user for us and permit the
server's VPN address, so availability stays an explicit client dependency
(§12.3): if the provider will not grant it, S-55 (CDR
import) and S-56 (call-back extension) replace it and the real-time requirement
is waived. The `Workers/` folder must therefore support both paths, and
`settings.pbx.ami.enabled` exists for exactly this.

### Docker image verified (2026-09-14)

`docker build -f src/CallCenter.Server/Dockerfile -t callcenter-api:latest .`
builds clean and the container was smoke-tested: `/health` returns 200
`Healthy`, `/` and `/dashboard` return 200 from the SPA baked into `wwwroot`
(so the Node stage and the copy into the publish output both work), the
container `HEALTHCHECK` reports `healthy`, and `date` inside the container is
EEST — `Asia/Hebron` is applied. Image size 344 MB.

Docker Desktop on Windows needs WSL 2: `wslEngineEnabled` is true by default and
the engine will not start without it (`wsl --install --no-distribution`, then
reboot).

### Discrepancies to resolve

- ~~**Assembly name.**~~ **Resolved: the project stays `CallCenter.Server`, and
  the runbook was corrected to match.** Renaming would touch the csproj,
  namespaces, the solution, the Dockerfile, CI, the published image and the
  deployment scripts; the runbook was one word. `Server` is also the SRS's own
  term for this component (§2.1, "Server (mini PC, LAN)"), of which the API is
  one of three jobs.
- ~~**The seed command does not exist yet.**~~ **Resolved:**
  `dotnet CallCenter.Server.dll seed` creates the §7 data and the first
  supervisor. Every step is idempotent, and it refuses to create a supervisor
  once any user exists — it is an installation command, not a way to mint an
  account on a running system.
- ~~**Migrations are not applied on startup.**~~ **Resolved:**
  `DatabaseInitialiser` applies pending migrations before the host serves, which
  is what step 6 of the runbook has always claimed. `Database:MigrateOnStartup=false`
  turns it off for a site that would rather run them by hand. Fine for one
  server; if a second API instance is ever added, two could migrate at once and
  this should move behind a PostgreSQL advisory lock.
- **`/downloads/AgentApp-Setup.exe`.** Runbook step 10 installs the agent app
  from the server; A-82/N-11 require updates to be served the same way. The API
  needs a downloads endpoint and the build needs an installer — neither is
  scaffolded.
- **Ports 80 and 5001.** Resolved: the 5001 rule is dropped, since nothing ever
  listened there. The API now also binds port 80 so the supervisor reaches the
  dashboard by typing the server address with no port number; the SPA's
  catch-all route already redirects `/` to `/dashboard`. 5000 stays bound for
  the container healthcheck. This is plain HTTP on the LAN, which is acceptable
  while the system is not reachable from outside the restaurant. Exposing it to
  the internet would need a domain name and TLS on 443 behind a reverse proxy,
  and the authentication guards that App.tsx still marks as placeholders.

### Open items raised on 2026-09-14 (historical — see "Open items (live)" at the end of this file)

- ~~The SIPSorcery advisory above needs a decision: accept the risk on 8.x, or
  move the agent app to .NET 10.~~ **Resolved 2026-09-17:** moved to .NET 10.
- `contact_phones.last9` is a stored generated column (`right(normalised, 9)`).
  EF Core 8 must map it as computed/read-only (`ValueGeneratedOnAddOrUpdate`),
  and the `pgcrypto` extension plus the GIN index on
  `to_tsvector(name || address)` need explicit migration handling — none of
  these are scaffolded by convention.
- **This repository contains commercial terms.** SRS §12 carries pricing, the
  payment schedule and signature blocks. Keep the repository private, or strip
  §12 before making it public.
- SRS §10 lists three [TBC] items the client must answer before installation:
  whether the provider grants AMI and whether incoming routing is a queue or a ring
  group; the exact channel and classification-type lists; and whether
  supervisors get live agent status and call monitoring (not included by
  default).

---

## 2026-09-16 — The PBX moves to a hosted Issabel reached over a VPN

### What changed

The telephony platform is no longer a Yeastar S20 sitting on the restaurant LAN.
It is an **Issabel (Asterisk) system operated by an external telephony provider**,
reached over a **VPN**. Each agent laptop runs its own VPN client. The server —
database, recordings, API, supervisor app — stays on the mini PC at the
restaurant, unchanged.

### Why it matters beyond a name change

Three statements in the SRS were false as written and were corrected rather than
patched:

- **N-01** promised no internet dependency. Now split honestly: everything that
  is not telephony (call log, contacts, classification, app orders, reports)
  still works with no internet at all; calls do not, because the PBX is across
  it.
- **2.4** assumed laptops, server and PBX shared a LAN. Only the laptops and the
  server do now.
- **2.4** assumed no internet was needed for daily operation. It is, for calls.

**N-04** still holds — the phone does not depend on our server — but it now
depends on the VPN and the provider, neither of which the developer controls.
The agreed uptime and support hours for those are the client's arrangement with
the provider, and provider outages are excluded from support (§12).

Added **N-04a**: the app must say *which* part is missing — VPN down, not
registered, or server unreachable. With a remote PBX, "the phone doesn't work"
has more possible causes, and an agent needs to know whether to call IT or the
provider.

### Call recording — unchanged, and deliberately so

Issabel can record calls itself (per extension, queue or route; files under
`/var/spool/asterisk/monitor`, named in `asteriskcdrdb.cdr.recordingfile`) and
that was considered. **Rejected for now:** recording stays in the Agent App with
upload to our server (A-30 to A-33). Server-side recording would have removed
real complexity from the app and given cleaner audio, but it puts the audio on
the provider's disk, hands them control of the 90-day retention that S-43 gives
the supervisor, and makes the call-to-recording link a matching problem rather
than a guaranteed key. Worth revisiting if the app-side recording proves
troublesome over the VPN.

### Settings key renamed

`pbx.ip` becomes `pbx.host`: it is now the PBX's address on the VPN, not an IP
on the restaurant LAN. Changed in the seeder, `AuthService`, the schema document
and the tests. `PBX_IP` in `.env.example` becomes `PBX_HOST` with the same
reasoning.

### Deferred — the abandoned-call capture (SRS 4.5)

**Decision deferred on 2026-09-16.** Section 4.5, and with it **R-20** and
**R-21**, is on hold until the telephony provider answers §12.3. Deferred, not
dropped — the requirements stand as written and are the starting point once the
answers arrive. Runbook step 8 is marked skip-for-now.

The thing most likely to be missed: **4.5 needs the server to have its own VPN
connection**, because the server talks to the PBX directly over AMI or CDR. The
VPN on the agent laptops does nothing for it. If the provider will not give the
server VPN access, 4.5 and those two reports cannot be delivered at all — so
that question is worth asking early even though the work is deferred.

Issabel itself is not the obstacle: AMI is part of Asterisk and Issabel enables
it by default on TCP 5038, and the CDR lives in the `cdr` table of
`asteriskcdrdb`. What is unknown is what the *provider* will grant.

---

## 2026-09-17 — The Agent App moves to .NET 10

`SIPSorcery` 8.0.23 carries **GHSA-28gm-jrmw-xx93**: a malformed UDP packet on
the RTP/ICE socket ends an active media session. That is the socket carrying
call audio, so it is squarely in the path this project is about to build on.
Fixed in 10.0.9. A second advisory, **GHSA-jwjp-4649-v8jp**, is in the WebRTC
data channels and does not apply here.

The earlier note in this file said the choice was "accept the risk on 8.x, or
move the agent app to .NET 10". That was right, though the reason is narrower
than it sounds: **`SIPSorcery` 10.0.16 targets `net8.0` perfectly well.** It is
**`SIPSorceryMedia.Windows`** — the Windows microphone and speaker endpoints —
that ships `net10.0-windows` only from 10.0.5, and there is no 9.x: the line
jumps 8.0.14 → 10.0.5.

### What moved, and what did not

Only `CallCenter.AgentApp`, to `net10.0-windows10.0.17763.0`. The server, the
shared library, both test projects and the simulator stay on `net8.0`; a net10
app references a net8.0 library without trouble. `global.json` had to allow the
.NET 10 SDK, and both CI workflows now install 8 and 10 — `release.yml` needs it
even though it only tests net8.0 projects, because `global.json` applies to the
whole repository.

### Why now rather than later

The deciding argument was not the advisory. **There is no SIP code yet**, and
the 8.x and 10.x APIs differ; moving before writing the registration layer costs
one line, and moving after it costs a rewrite. **.NET 8 also leaves support in
November 2026**, so this was weeks away regardless.

The server still targets net8.0 and its Dockerfile still uses 8.0 images. That
move is not urgent today but should be deliberate, and before November.

### Distribution

The app is published **self-contained** (`--self-contained true`), about 185 MB
with the runtime bundled, so the four laptops need no .NET runtime installed.
See `docs/RELEASING.md`. This also avoids adding a runtime prerequisite to an
install that may already run into executable-blocking policies.

### Still open — SQLite

Fixing SIPSorcery left `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 visible, carrying
**CVE-2025-6965** (high, memory corruption). It arrives transitively through
`Microsoft.EntityFrameworkCore.Sqlite` in the Agent App. Fixed versions exist
from 2.1.12.

**Not fixed yet, deliberately:** nothing reaches it. The offline buffer is still
a README — no DbContext, no database file, no code path that opens SQLite. It
should be pinned when the offline buffer is built, and that work must not start
without doing so.

---

## 2026-09-17 — One extension per agent, internal calls told apart by number

The two-extension split of SRS 2.3 is gone. **Each agent now has one extension**
used for every call they handle. Internal calls are identified by the other
party's number against a list the supervisor keeps (new **S-48**), not by which
extension carried the call.

Internal calls are **recorded and kept** — agent call log, contact history,
recording and classification all unchanged — and **left out of the
customer-facing reports**, so internal chatter does not distort order counts,
complaint rates, volumes or the service level. That was a deliberate choice over
discarding them: a call misjudged as internal would otherwise be gone with no way
to audit it.

Numbers are entered as a plain comma-separated list, validated as digits only —
letting a name through would silently match nothing.

### Why it is better

A new branch number is now a settings change rather than a PBX change plus a
visit to four laptops. It also halves what the provider has to supply per agent,
and removes a class of confusion: with two extensions, one could register while
the other was refused, and the status bar had to reconcile them.

### The migration is hand-written, and that mattered

EF scaffolded it backwards: it kept `internal_extension` and dropped
`customer_extension`. Applied as generated it would have destroyed every working
extension number and secret, keeping whatever placeholder sat in the internal
column — in the development database that was the literal string `-`. The
migration now drops the internal columns and renames the customer ones, keeping
the values that matter.

It still loses the internal extensions and their secrets, which is intended but
irreversible. **Take a backup before applying it to a database whose contents
matter.**

The same migration clears the dead `pbx.ip` settings row left behind by the
earlier `pbx.ip` → `pbx.host` rename, which changed column names but not that
row, and seeds `reports.internal_numbers` for existing databases.

### Still to build

S-48 stores the list and the settings screen edits it. **Nothing consumes it
yet** — the reports that would exclude internal calls do not exist. The list is
in place so that when they are built the rule is already recorded and
configurable, rather than being invented then.

---

## 2026-09-17 — Contacts: matching, and why the full-text index is unused

The shared contact list (A-60 to A-63), server side.

### Matching is by number, never by name

Two customers may genuinely share a name, so duplicate detection looks only at
the phone number. `contacts` has no unique index at all; `contact_phones` is
unique on `normalised`. One contact may hold several numbers, and a call from
any of them finds the same person.

Caller lookup (A-10, A-13) tries the exact normalised number first, then falls
back to the last nine digits, so `0599123456` finds a contact saved as
`+970599123456`. **The fallback is guarded to tails of exactly nine digits.**
`PhoneNormalizer.Last9` returns short numbers whole, so without that guard
extension `2001` would fuzzy-match every other extension — every internal number
would collide with every other.

Duplicates are checked before saving rather than left to the unique index, so the
refusal can name the contact that already holds the number and return its id. The
screen can then offer to open it, instead of surfacing an index violation.

### The full-text index is deliberately unused

The schema carries a GIN index over `to_tsvector(name || address)`. Search uses
`ILIKE` instead.

`to_tsvector` was created with the default text configuration, which does not
stem Arabic — so it would miss the names most contacts actually have, which is
the opposite of useful. `ILIKE` behaves the same in both languages and is
predictable. At the expected volume (hundreds of contacts, a few thousand at
most) the difference is not measurable.

If it ever does become slow, the fix is an Arabic text-search configuration and
an index over that, not a rewrite of the query.

### Not built yet

Merging duplicates (A-63), the VIP and Blocked flags (S-45 — deliberately
untouchable by any endpoint here, so an agent editing an address cannot clear a
block), Excel/CSV import (A-64), the contact history panel (A-62, needs
communications), and the screens in both clients.

---

## 2026-09-17 — Arabic name matching, and the duplicate-name warning

A second contact with the same name used to be saved with no signal at all, so
two records for one customer could drift apart for months. A-63 now says a
matching name is a **warning**, never a refusal: the agent is shown the existing
contacts and chooses — add this number to that person, or save a separate
contact.

Names are never matched automatically. "أحمد" and "محمد" are common, and
silently joining two customers would mix their order histories with no way to
unpick them.

### Exact matching, which required fixing Arabic first

Substring matching was written and then rejected: "Ahmad" would warn about a
dozen unrelated people every time and be ignored within a week.

But exact matching on the stored name would have been worse — it would almost
never fire. **PostgreSQL considers `'أحمد' = 'احمد'` false**, confirmed against
the database, and the same for `ILIKE` and `lower()`. The same Arabic name is
written several ways: with or without the hamza, `ة` or `ه`, `ى` or `ي`, with or
without diacritics.

So `CallCenter.Shared.Text.NameNormalizer` folds a name to a comparison form, as
`PhoneNormalizer` does for numbers, and `contacts.name_normalised` stores it.
Matching compares that; **the folded form is never displayed**, because it is not
how anyone spells their name.

The two changes depend on each other: exact matching only works because of the
folding, and the folding is only safe to rely on because matching is exact.

### Search improved as a side effect

Search now matches the normalised name too, so searching `احمد` finds a contact
saved as `أحمد`. That was silently broken and would have surfaced as "search does
not find people" long after anyone remembered why.

### The backfill duplicates the rules, deliberately

Existing rows are not re-saved by the application, so the migration folds them in
SQL — `translate` for the letter forms and the diacritics, `regexp_replace` for
spacing. That repeats the C# logic, which is a real risk, so **the SQL was run
against the database and checked to produce the same output as the C# tests
expect** for all nine cases.

**The C# version is authoritative** from here: it runs on every save. If the
rules ever change, the migration is history and must not be edited — write a new
one.

## 2026-09-18 — VIP and Blocked flags, and why they have their own screen

S-45 built: a supervisor-only endpoint and screen, the list of every flagged
number, and the audit trail. Agents see the flags and cannot change them.

### The flags live on the contact, not in a list of numbers

There is no `blocked_numbers` table. `contacts` already carries `is_vip`,
`is_blocked`, `flag_reason`, `flag_changed_by` and `flag_changed_at`, so
**nothing was migrated** — the columns were laid down with the initial schema
and were waiting for this.

That is the right shape and not just the cheap one. A flag is a property of a
customer, and a customer may have three numbers; a table of numbers would need
its own matching rules, and they would drift from the ones caller lookup uses
(A-13). Flagging a number therefore **finds the contact that already owns it**,
by exact normalised match and then by the last nine digits — the same two steps
as the pop-up. Flagging `0599123456` flags the customer saved as
`+970599123456` rather than creating a second record for the same person.

A number that matches nobody creates a **nameless contact** to carry the flag.
S-45 asks for a bare number to be flaggable, and `contacts.name` has been
nullable since the start for exactly this. The alternative — a separate kind of
row for numbers with no name — would mean every lookup in the system asking two
questions instead of one.

### Separate service, separate controller, and that is the enforcement

`ContactFlagsService` and `ContactFlagsController` sit beside
`ContactsService`/`ContactsController` rather than inside them.

`ContactsController` is open to any signed-in account, because A-61 says every
agent sees every contact and A-63 says agents create and edit them. If the
flags lived there, "an agent cannot change a flag" would be a rule someone has
to remember while adding a field to the contact form. Split, it is a fact about
the routing table: every write carries `SupervisorOnly`, and
`UpsertContactRequest` has no flag fields at all, so an agent fixing an address
cannot clear a block as a side effect.

Reading is deliberately **not** supervisor-only. Agents need the flags: the
pop-up shows a VIP badge (A-16) and the app rejects a blocked caller from a
cached copy of the block list (A-17).

### Three refusals, and why each exists

**VIP and Blocked at once is refused.** They ask the Agent App for opposite
behaviour — show a badge, or reject the call without ringing — and there is no
sensible winner. Refusing is also why both flags travel in one request rather
than one endpoint each: two calls could leave a contact marked both between
them.

**Setting a flag with no reason is refused** (SRS S-45, amended today). A block
nobody can account for is one nobody later dares remove. Removing a flag needs
no reason, because the removal is logged with who and when, which is what a
later reader actually wants.

**Removing is the same request with both flags false**, not a `DELETE` of its
own. One write path means one audit vocabulary: every change, including a
removal, lands in the log the same way.

### The audit trail is the log, not a column

The contact's own columns hold the current state, which answers "who blocked
this number?" and not "who unblocked it in March?" — and the second question is
the one a dispute turns on. So flag changes are read back out of `audit_log`
(`entity = contact`, action `flag` or `unflag`), with the before and after
states in the existing `jsonb` columns, and the supervisor can open the full
history of any flagged contact.

The audit payload is a **named record**, `FlagState`, rather than the anonymous
object the contact audit uses. It has to be deserialised again to render the
history, and a write shape and a read shape that can drift apart is a bug
waiting for a rename.

### The screen was its own, and that was wrong — see the next entry

The flags were first built as a separate "VIP and blocked" screen with its own
nav entry. That was reversed the same day; the reasoning is in the entry below,
and the server described above did not change.

### What the Agent App got

A read-only flag column in the contacts grid — a chip, not a checkbox, with no
editor anywhere in the app (S-45, A-60).

The block-list endpoint (`GET /api/contacts/blocked-numbers`, normalised
numbers only, so customer names stay off every agent's laptop) is built and
**not yet consumed**. Caching it locally and rejecting on it is A-17, which
belongs with the call work — the flags existing is what unblocks that, and was
the whole reason for doing this first.

### Not done here

S-46, exporting the blocked list for Issabel's own blacklist, is a *Should* and
was left alone. It is a format question for the provider, and the list it would
export now exists.

---

## 2026-09-18 — The development commands are written down

`docs/DEVELOPING.md`. Until now the three commands that actually start this
system lived only in conversation, and two of them are not the obvious ones:
`dotnet run` is blocked by executable policy on the development machine, so
everything runs as `dotnet <name>.dll` **from its output folder**, or it starts
with no `appsettings.json`; and the Agent App is `net10.0-windows` while
everything else is `net8.0`, so its path looks wrong to anyone expecting
`net8.0`.

It also records the two things that cost time every session: stop the running
apps before building, or the compiler cannot replace their files; and a green
build proves structure, not that a screen looks right.

The README's quick start was left as it is — it is the textbook version, correct
on a machine without the policy — with a pointer to `DEVELOPING.md` above it.

## 2026-09-18 — The flags move onto the contact, and the list becomes a filter

Reversed within hours of building it, on the developer's question: why is
flagging its own tab rather than something you do to a contact?

### The reason that did not survive

Three reasons were given for the separate screen. The third was **"the contact
form is the one agents use, so the flags must not be on it at all"** — and that
is false for the screen in question. The **supervisor web app refuses agent
accounts at sign-in** (`not_a_supervisor`, `src/CallCenter.Web/src/api/auth.ts`),
so an agent can never open its contact list. The argument holds only for the
Agent App's contact form, which was never getting flag controls anyway.

The second reason — that a reason should be given deliberately rather than in
passing — survives the question but does not favour a separate screen. Clicking
**Flag** and being asked for the reason is *more* deliberate than typing a
number into a form, not less.

The first reason is real and is what the new design has to keep: S-45 asks for
a list of every flagged number, and a per-contact action cannot produce one.

### What it looks like now

**Flagging is on the contact.** Each row in the contact list has a **Flag**
button beside Edit. It opens a dialog: VIP or Blocked, the reason, and — only
when the contact is already flagged — Remove. The flag history is underneath.

**The list is a filter, not a screen.** `GET /api/contacts?flag=vip|blocked`,
the ordinary contact search narrowed. All / VIP / Blocked sit next to the search
box. This is strictly better than the separate list: filtering and searching
**compose**, so "blocked contacts in Ramallah" is one query, and there is **one
contact list in the app** rather than two that can disagree about what a contact
is. `GET /api/contacts/flagged` was deleted as redundant.

`ContactSummaryDto` gained `FlagReason` so the filtered list can be read without
opening every row — the one thing the separate screen did better, kept.

**Bare numbers survive as an offer on an empty search.** S-45 allows a nuisance
caller who is not a customer to be blocked, and with flagging attached to a
contact there would otherwise be no way in. So a search that matches nothing and
looks like a phone number offers *"Nobody has 0599… — flag it anyway"*. That is
better than the form it replaces: the supervisor searched for the number first,
which is what they should do anyway, and the offer only appears once the search
has proved nobody has it.

### What did not change

**None of the server work.** `ContactFlagsService`, the supervisor-only writes,
the three refusals, the audit trail, the block-list endpoint for A-17 — all of
it was right and none of it moved. What changed was two React components and a
query parameter. That is the argument for having split the service from the
controller in the first place: the rule about who may write a flag survived a
complete rearrangement of where the buttons live.

**`UpsertContactRequest` still carries no flags.** The contact *form* cannot
touch them; only the flag dialog can, through its own endpoint. Moving the
button next to Edit did not put the flags in the contact save path, and that is
the guarantee that mattered.

### The lesson worth keeping

The separate screen was defended with three reasons and only two were true. A
reason that sounds like a safety argument — "agents must not see this" — is worth
checking against who can actually reach the screen before it is used to justify
a shape.

## 2026-09-18 — The phone rings: incoming calls, and blocking before the ring

A-10 (in part), A-12 (in part) and A-17 in full. A call now arrives, a pop-up
appears, Answer / Reject / Hang up work, and a blocked caller never gets that
far.

### The block check runs first, and that shaped the design

A-17 says a blocked caller gets "no pop-up, no ringing". That is not a filter
applied to a call in progress — it decides whether a call is ever shown. So the
check is the first statement in the incoming-INVITE handler, before the ring,
before the window, before anything is awaited.

Which in turn means it cannot ask the server. Two reasons, and the second is the
stronger one: A-17 requires it to work with the server unreachable, and even
with the server up, a round trip does not belong in the second before the PBX
gives up on this extension. So **the list is fetched at sign-in and answered
from memory**, with a copy on disk for the next start.

The matching lives in `CallCenter.Shared` as `BlockList`, not in the Agent App.
It is the rule in this app with the sharpest consequences — a false positive
silently drops a customer, a false negative lets through someone a supervisor
deliberately blocked — and in Shared it can be tested without a PBX, a laptop or
a call. Sixteen tests cover it.

**A withheld or unreadable caller id is not blocked.** Rejecting what cannot be
identified would silently drop every withheld-number call; A-17 is about numbers
a supervisor named.

**Short numbers are exempt from the last-nine-digits fallback.** Matching
otherwise mirrors caller lookup (A-13), so a caller presenting `0599123456` is
recognised as the number blocked as `+970599123456`. But indexing an extension
like `2001` by its tail would block every number ending `2001`, and blocking a
branch by accident is expensive.

**A corrupt cache blocks nobody rather than everybody.** That is the safer
failure: a nuisance caller gets through once, instead of customers being
dropped.

### Plain JSON, not the SQLite offline buffer

The cache is one list of strings, replaced wholesale. It needs no database. This
also keeps it clear of `SQLitePCLRaw.lib.e_sqlite3`, which is still pinned to a
version carrying CVE-2025-6965 and still blocks the offline buffer proper.

### One SIP socket, shared — this would otherwise have been a long evening

Registration used to create and own its own `SIPTransport`. Calls cannot use a
second one: the address the PBX learns from a REGISTER is where it sends the
INVITE. Two transports and the phone shows as registered and never rings, with
nothing in any log to say why.

So `SipTransportHost` owns one transport for the life of the process, and both
the registration agent and the call agent bind to it. Signing out unregisters
but leaves the socket — unregistering is what stops calls arriving, and tearing
the socket down would gain nothing and hand the next sign-in a different port.

### The pop-up exists from startup

Built once, hidden, then shown and hidden. Building a WPF window inside a SIP
callback would put window construction on a background thread in the one second
that matters. The window also **refuses to close**: closing is turned into
hiding, because a pop-up the agent closed once would be gone for every later
call and the app would look as though it had stopped taking them.

Bringing it to the front is `Topmost = true; Activate(); Topmost = false`.
Windows refuses `Activate()` from a process without the foreground, which is
exactly the case A-10 describes — an agent whose app is behind a browser. Topmost
is turned straight back off so the pop-up does not pin itself over everything
for the rest of the call.

### A second call is refused as busy

One extension, one call (SRS 2.3). Busy rather than silence lets the PBX offer
the caller to another agent instead of leaving them ringing at an agent who is
already talking.

### Audio is opened per call, not held

The microphone is acquired when a call is answered and released when it ends. On
a laptop shared between shifts, an app sitting on the microphone between calls
is both rude and a question somebody will eventually ask.

### What is deliberately not here

- **Mute and Hold** (the rest of A-12). Answer, Reject, Hang up and the timer
  are what make the phone usable; the other two are additions to a working call.
- **The caller's identity** — name, address, notes, VIP badge (A-11, A-13 in the
  pop-up, A-16). The pop-up shows the number only. Next.
- **Logging calls** (A-14). Nothing is recorded yet: not answered calls, not
  missed ones, and **not the blocked ones**, which A-17 also requires to appear
  in the supervisor's reports. This is the largest gap and it needs the
  communications table, which does not exist.
- **Outbound** (A-20 to A-22).

### Untested, and honestly so

None of this has met a PBX. The build compiles and the block-list rule is
covered, but "a call arrives and the pop-up appears" has been verified by
nobody. It needs the developer to ring extension 2001 from a phone.

## 2026-09-18 — Why the phone never rang: three bugs the proof-of-concept already knew

The first real call was never offered to the app. The log showed sign-in,
block list, socket open, `Extension 2001 registered` — and then nothing at all.

The developer pointed at `Desktop\pOC`, an earlier proof-of-concept that already
works against **this same PBX and this same extension**. All three causes were
solved there, with comments explaining each. They had not been read.

### 1. The PBX had decided the extension was unreachable

Asterisk sends `OPTIONS` every few seconds to qualify a registered extension.
SIPSorcery answers nothing outside an established dialogue, so those went
unanswered, the PBX marked 2001 unreachable, and **stopped offering calls** —
while registration carried on succeeding every two minutes.

That is the worst shape a bug can have: every indicator green, the thing simply
does not happen. `CallService` now answers `OPTIONS` and `NOTIFY` with 200 OK,
advertising `Allow`, at the transport level.

### 2. The Contact header did not name the extension

`SIPRegistrationUserAgent` takes `sendUsernameInContactHeader`, and without it
the Contact is `sip:<ip>:<port>` with no user part — nothing tying the
registration to extension 2001. The PoC sets it. Now so does this.

Either of these two alone is enough to produce silence, which is why both were
fixed before testing again rather than one at a time.

### 3. Busy was never going to work

`SIPUserAgent` silently drops an INVITE arriving while it holds a dialogue, so
`OnIncomingCall` never fires for a second call and the busy reply sat in code
that could not run. The caller would have heard nothing until the PBX timed out.
Busy is now handled in the transport handler, where the INVITE actually lands,
and a re-INVITE for the call in progress is told apart by Call-ID.

### What the app logs now, and why that was the real failure

When the call did not arrive, the app had logged **nothing**. "The PBX never
sent it" and "it arrived and we mishandled it" were indistinguishable, and they
need opposite fixes.

Every SIP message in and out is now logged, one line each, at Information. A
single extension is not chatty enough for that to be a problem, and a call that
does not arrive is worth more than a tidy log. The full message goes out at
Debug.

That gap was the actual mistake here. The three bugs were ordinary; not being
able to tell them apart from a network problem is what made the session
expensive.

### Taken from the proof-of-concept, deliberately

**The queue.** The dialplan sets `X-Queue-Name` on the INVITE. SIPSorcery drops
unrecognised headers into `UnknownHeaders` as raw text, so it is read by hand.
The queue now shows as a badge **above the number** on the pop-up — the agent
needs it before they speak, because Delivery and Complaints are answered
differently — and it rides on `CallState` ready to be stored with the call and
used in classification (A-40) and per-queue reports.

When no queue header arrives, the log says which custom headers *were* present.
Otherwise a dialplan that never set it and a reader that failed to parse it look
identical, and they need opposite fixes.

**Caller ID.** A trunk call often carries the real subscriber number in
`P-Asserted-Identity` while `From` holds whatever the caller claimed; RFC 3325
makes the former the identity the network vouches for, so it wins. The PoC also
knows the several words switches use for "withheld". Adopted whole — with
`LooksLikeNumber` and `SameNumber` rewired onto `PhoneNormalizer`, so this system
keeps one definition of a phone number rather than two.

### The lesson

There was a working reference implementation against this exact PBX, in a folder
on the same desktop, and this code was written from the library documentation
instead. **Before the next piece of telephony — outbound, transfer, recording —
read `pOC` first.** It has met the hardware.

## 2026-09-18 — The first real call, and the ringback a blocked caller should never have heard

Calls now arrive. The three fixes above worked, and the log proves each one:
`SIP IN "OPTIONS"` answered, `to 2001` on the INVITE, and INVITEs reaching the
app for the first time.

The first three test calls were all rejected as blocked — correctly. The
developer's own test phone had been blocked earlier while trying out the flags
screen, so A-17 was working on the person testing it.

### The bug that found: a blocked caller heard ringback

The caller reported being left holding rather than cut off, and they were right.

`SIPUserAgent.AcceptCall()` sends **100 Trying and 180 Ringing** before it hands
back a server user agent. The blocked path called `AcceptCall(request).Reject(...)`,
so the sequence on the wire was ring, ring, decline. A-17 says "no pop-up, no
ringing"; half of that was broken, and the code carried a comment claiming
"nothing rings" directly above the line that rang.

Fixed by sending the final response straight into a new `UASInviteTransaction`,
which skips the provisional responses entirely. The busy path already did this
and now shares the helper.

**603 Decline for a block, 486 Busy Here for a second call**, and the difference
matters. 6xx is a global failure: RFC 3261 has a proxy stop trying other
branches and pass it upstream, which is the closest a phone can get to "hang up
on this caller". 486 says "this one line is busy" and invites the PBX to try
somewhere else — right for a busy agent, wrong for a blocked number.

### The limit worth being honest about

Declining is not hanging up. The PBX owns the caller, and what they hear next is
its decision: a queue may hold them and offer the call to other agents, or move
them to a failover destination once every app has declined. Nothing the Agent
App sends can change that.

Only blocking at the PBX — Issabel's own blacklist — stops the call before it
enters the queue. That is **S-46**, the export of the blocked list for the
provider to load, and it is a *Should* that has not been built. This is already
noted in SRS section 6; it has now been met in practice rather than in theory,
and S-46 is worth more than its priority suggests.

### A gap the same test exposed

The Agent App refreshes the block list **only at sign-in**. A supervisor
unblocking a number at 10am leaves every already-signed-in agent rejecting that
caller for the rest of the shift. For a customer blocked by mistake that is a
long time to be unreachable with nobody able to explain why.

The server already has a SignalR hub the agents connect to. Pushing flag changes
down it is small, and belongs with the call work.

## 2026-09-19 — No inbound port to the PBX. AMI is ruled out, and 4.5 is settled

The question that had blocked section 4.5 since 16 September is answered, and
the answer is no: **the PBX accepts no inbound connections, on any port.** The
server **can** reach it outbound on the SIP port, exactly as the agents' laptops
do.

Section 4.5 was deferred waiting for this. It is now settled rather than
deferred, and most of it survives.

### What died

**S-50 (AMI)** and **S-52 (database grant)**. Both need to connect *in* to the
PBX. Marked **Ruled out**, not deferred — a requirement waiting for an answer
that has arrived is not waiting for anything. Leaving them as "deferred" would
have someone re-ask the provider in six months.

Also dead: any automatic write to Issabel's blacklist. There is no path to it.

### What survives, and why it survives

**S-56, the call-back extension, is now the primary method and a Must.** It was
a *Should* in the alternatives table.

The reason it works is the whole point of the day: **a registration is
outbound.** The server registers a dedicated extension the same way an agent's
laptop registers theirs — same port, same direction, nothing opened, no firewall
rule. The provider points the queue timeout at that extension; the server
answers, plays "all agents are busy, we will call you back", hangs up, and
records the caller with a call-back task. Real time, and it needs no more access
than the Agent App already has.

That is a better position than "AMI is unavailable, fall back to a daily
import". Most of the real-time capability is kept.

**S-55 (CDR) survives in reduced form.** It read "read-only access to the
`asteriskcdrdb` database, **or** an export we can reach". The first half is gone;
the export half stands, fetched outbound by the server.

### The coverage, stated plainly

- **Caller waits to the timeout** → real time, with a call-back task (S-56).
- **Caller hangs up before the timeout** → not until the next CDR import (S-55).

That is the honest shape of R-20 and R-21 now, and both are written that way.
Neither is deferred any more.

### Blocked numbers: the export stops being optional

S-46 exports the blocked list for loading into Issabel's Blacklist screen. It
was a *Should*, described as blocking "optionally... at the PBX level as well".

With no inbound port it is the **only** route to PBX-level blocking, and today's
testing showed why that matters: an Agent App can only *decline* a call, and a
declined call is still the queue's problem — the caller may be held and offered
to other agents. Only the PBX blacklist stops the call before the queue.

Its priority stays *Should* because the client can type numbers into Issabel by
hand, but section 6 now says PBX-level blocking is **recommended** rather than
optional, and names the export as the way to feed it.

### Three of the six provider questions are closed

Questions 1–3 (server VPN, AMI, database access) are answered by this and were
removed rather than ticked. The list is now about what is actually needed: the
call-back extension and its credentials, how the CDR export will be delivered,
queue or ring group, and who loads the blacklist.

### What this does not change

The Agent App. It already only talks outbound, and every fix made today —
answering the PBX's keep-alives, naming the extension in the Contact header —
was about being reachable *through* an existing registration rather than about
opening anything. The architecture was already the one this constraint requires;
nobody had stated the constraint.

## 2026-09-19 — The call-back extension is rejected. CDR import takes its place

Two things changed since this morning's entry, and between them they reverse the
decision it recorded.

**We have administrative access to the Issabel box ourselves.** Not through the
provider. We can read `/var/log/asterisk/cdr-csv/`, check `/etc/asterisk/cdr.conf`,
create a restricted SFTP account and look at the queue settings. A row of things
written as "ask the provider" are now things we do, which is why the question
list in this file and in SRS §10 is shorter than it was.

**The client has rejected S-56 on customer experience.** S-56 had the server
answer a waiting caller, say "all agents are busy, we will call you back", and
hang up. The client will not have a caller hung up on. That is their call to
make about their customers, and it ends the requirement.

### S-56 is removed, not deferred

It was the **primary** method as of this morning. This is a reversal of a
decision a few hours old, not a refinement of it, and it is worth being plain
about that.

The design was sound for the constraint it was built against: no inbound port,
so capture the caller by having the PBX send them to something we *do* control.
It solved that well. What it could not do was be pleasant, and nobody had asked
the client whether a recorded brush-off was acceptable before it was written
into the SRS as a Must.

The ID is retired. It is listed in 4.5 under "Ruled out" with the reason, so a
later reader does not propose it again, and it is not reused.

### CDR is better anyway, and that matters

This is not purely a concession. S-56 could only ever catch callers who waited
all the way to the **queue timeout**. Somebody who gave up after five seconds
was invisible to it — structurally, not through any defect. The CDR file has a
row for **every** call, so the coverage is complete.

So the change trades real-time capture of *some* abandoned calls for delayed
capture of *all* of them. For a call-back list that is the better trade: the
business action is ringing the customer back within the hour, not within the
minute. A caller who abandons at 19:05 appearing at 19:10 changes nothing about
what anybody does with it.

### What is lost, stated so nobody is surprised

**Real-time capture is gone.** There is no longer any path to knowing about an
abandoned call the moment it happens. R-11, R-20 and R-21 now say so in their
own definitions rather than only in 4.5, because a report that quietly lags by
five minutes is worse than one that says it lags by five minutes.

### The mechanism, and the one setting it depends on

Asterisk's `cdr_csv` module appends a row to `Master.csv` as each call ends,
independently of the `asteriskcdrdb` database — which is the point, since the
database would need an inbound port and is ruled out. The server pulls that file
over **SFTP, outbound, key-based**, every few minutes, reading from the byte
offset it last reached so that a file which grows all year is not re-downloaded.
A file shorter than the stored offset means rotation: reset to zero and log it.

**`loguniqueid=yes` in `cdr.conf` is a prerequisite, not a preference.** It puts
Asterisk's `uniqueid` on every row, which maps to `communications.pbx_unique_id`
and its unique index. That index is what makes re-reading the same lines
harmless. Without it there is no stable key, the importer cannot tell a row it
has already seen from a new one, and duplicate protection is gone. Runbook step
8 checks it first for that reason.

### The rule for "abandoned" is not written yet, deliberately

`disposition = 'NO ANSWER'` is **not** the test, and writing it into the SRS as
settled fact would have been the easy mistake here. Where an inbound route or an
IVR answers the call before it reaches the queue, Asterisk records `ANSWERED`
even though no agent ever spoke. The expectation is `lastapp = 'Queue'` with a
zero or near-zero `billsec` — but that is an expectation, not an observation.

**Prerequisite before any parser is written:** three test calls — one answered by
an agent, one hung up while ringing, one left to the timeout — then read the last
rows of `Master.csv` and write down what distinguishes them. The importer is
written against those rows.

The same look answers whether the CDR carries a usable per-call **wait time**.
R-21 is a service-level percentage if it does and a count of answered versus
abandoned if it does not, and the SRS says so conditionally rather than
promising a figure that may not be available.

### Dependency on A-14

The importer writes `communications` rows. **A-14 has to land first** — the table
and the endpoint have to exist before anything can import into them. Nothing
about the CDR work changes that ordering; it lengthens the queue behind A-14
rather than competing with it.

### What did not change

The schema. `pbx_unique_id` with its unique index, `source = 'CDR'`,
`status = 'Abandoned'`, `wait_sec`, `queue_name` and `pbx_events_raw` were all
laid down in the initial schema and accommodate this without a migration. Two
values in the CHECK constraints — `source = 'AMI'` and `status = 'Overflowed'` —
are now never written, and are left in place: narrowing a CHECK is a migration
that would gain nothing.

Two settings, `callback.extension` and `pbx.ami.enabled`, are now dead in the
catalogue. They are noted in `SCHEMA.md` rather than removed, because removing
them is a code change and this was a documentation task.

## 2026-09-19 — Calls are recorded: A-14, and the offline queue that is not SQLite

Every call the Agent App sees now reaches the server: answered, missed, rejected
by the agent, and rejected automatically because the number was blocked. This is
the piece everything else was queued behind.

### No migration. The table was already there

`communications` has been in the database since the initial schema on
14 September — with `sip_call_id`, `pbx_unique_id`, `wait_sec`, `queue_name` and
both unique indexes. The entity and its EF configuration existed too. What was
missing was only the code to use them, so A-14 turned out to be a service, a
controller and the Agent App half, with nothing touching the schema.

Worth noting for the CDR importer, which is in the same position: the shape it
needs is already laid down.

### Reporting a call twice is harmless, and that shaped the design

`ux_comm_sip_call` is unique on (`sip_call_id`, `extension`). The service looks
for that pair and updates rather than inserts when it finds one.

That one index is what lets the Agent App's offline queue resend blindly. The
alternative — an app that asks the server what already arrived and reconciles —
is more code on the side of the system least able to run it, and it would be
wrong on exactly the day it mattered, when the network is flapping and half the
answers are missing. Better to make the repeat harmless than to avoid it.

### The offline queue is a text file, deliberately

A-04 requires the app to keep working with the server unreachable, and a call
log that silently loses a shift would be worse than none: the supervisor's
reports would be wrong with nothing to say they were wrong. So each call is
written to disk **before** the request is attempted and removed only once the
server has taken it.

The plan called for the EF Core SQLite offline buffer. This is not that, and the
reason is on the must-fix list: `SQLitePCLRaw.lib.e_sqlite3` is still pinned to a
version carrying CVE-2025-6965, and **the offline buffer must not start without
pinning it first**. This queue appends a record, reads them back in order and
deletes the ones that succeeded. One JSON object per line does that in fifty
lines and pulls in nothing.

Appending rather than rewriting matters: a power cut during a write can cost the
call being written, never the ones already queued. A torn final line is skipped
rather than taken as a reason to discard the file.

If the buffer later has to hold recordings and classifications too, that is the
point to revisit this — and to pin the library first. The same reasoning produced
the block-list cache, which is also a plain file for the same reason.

### A refusal the server is sure about is dropped, not retried forever

An `unknown_value` or `invalid_request` will fail identically however many times
it is sent, and a queue that retries it blocks every call behind it — one bad
record and the shift's log never arrives. Those are discarded with an error in
the log. Anything else — unreachable, a 500, an expired token — stops the flush
where it is and keeps the order.

### What the app reports, and what it deliberately does not

It sends what it saw: the Call-ID, the extension, the number, the name the PBX
gave, the queue, and the times. It does **not** send a contact id. Matching a
number to a customer is the server's job and its rules live there (A-13);
working it out in the app would mean a second copy of those rules, and two
copies is how a call ends up filed against the wrong customer.

It also does not send the channel. A call is always Phone, and `ChannelNames.Phone`
is now a shared constant so the seed and the service cannot drift apart on the
name.

### Duration is talk time, not ring time

A call that rang for forty seconds and was never answered has no duration. Give
it one and forty seconds of imaginary conversation goes into every average,
which is the sort of error that is only noticed when somebody queries a report
six months later.

### Blocked and busy calls are reported although they never appear on screen

A-17 requires a blocked call in the supervisor's reports, so the rejection now
raises a finished-call record even though the agent never learns it happened. A
second call refused while the agent is talking is reported as **Missed** — from
the customer's side that is what it was, and "we were too busy to take it" is
precisely what these reports exist to surface.

### Still open

The supervisor cannot see any of this yet. The endpoints exist and the data
arrives; there is no screen. The agent's own call log (A-50), the contact
history panel (A-62) and the reports are the next things, and they are now
unblocked for the first time.

## 2026-09-19 — The SQLite advisory is fixed, and the buffer becomes a database

`SQLitePCLRaw.lib.e_sqlite3` is pinned to **2.1.12** and CVE-2025-6965 is gone
from the solution. The call queue built this morning has moved off its text file
and onto the SQLite offline buffer A-04 always described.

### The fix was forward, not backward

Worth writing down because the instinct is natural and wrong: the advisory
affects versions *up to* 2.1.11 and is fixed in 2.1.12. Going to an older
release does not escape it — it lands somewhere still affected, and collects
every other bug fixed since. A security advisory is not a bad production run
with a good batch behind it.

The pin is one line in `Directory.Packages.props` plus an explicit
`PackageReference` in the Agent App, because EF Core 8.0.11 asks for 2.1.6 and
the explicit reference is what makes the newer version win. It comes out when EF
Core itself asks for 2.1.12 or later.

### Why the file went, having been defended this morning

The text file was the right call at the time and is no longer. Two things
changed.

The advisory is fixed, so the reason for avoiding SQLite has gone.

And **classifications are next**, which changes what the queue has to promise.
A classification belongs to a call. Replay them out of order and a
classification arrives for a call the server has never heard of. A file can hold
a list; it cannot promise that two related things stay in order across a crash,
and it cannot delete a call and its classification together or not at all.

That is the argument for a database, and it was not true this morning when the
queue held one kind of row.

### One generic table, deliberately

`pending_uploads` holds every kind of waiting item: a `Kind`, the request body
as JSON, a `Reference` so a classification can find its call while both are
queued, and the attempt count and last error so a row that never succeeds can be
found rather than silently retried.

Not a table per kind, for two reasons. **Ordering**: one auto-incrementing id
across everything is what keeps a call ahead of its classification. And **the
shape never changes**: adding notes or app orders later is a new `Kind`, not a
migration — which is what makes `EnsureCreated()` safe here. A buffer that needed
migrations would have to choose between losing an offline shift's work and
shipping a migration runner to laptops nobody administers.

### The old file is imported, not abandoned

On first start the app reads `pending-calls.jsonl`, writes its rows into the
buffer, commits, and only then deletes the file. It matters on exactly one day —
the first run after this update on a laptop that had calls waiting — and skipping
it would lose a shift's work silently, which is the precise failure the queue
exists to prevent.

### What this closes

The "must fix before handover" item for CVE-2025-6965 is resolved, and the
condition attached to it — *the offline-buffer work must not start without
pinning this first* — was honoured: the pin landed before the buffer did.

---

# How this project is tracked

Three files, each with one job. Kept current as part of doing the work, not
afterwards — a change that is not written down did not happen.

| File | Holds | Updated when |
|---|---|---|
| [`SRS-Smashed-Burger-Call-Center.md`](SRS-Smashed-Burger-Call-Center.md) | **What the system must do.** Every requirement has an ID (A-01, S-42, N-04a) and is the contract with the client. | **Any design change.** If behaviour, scope or an assumption changes, the requirement changes with it — in the same commit. A requirement that no longer describes the system is worse than no requirement. |
| `DECISIONS.md` (this file) | **What was decided and why**, including what was considered and rejected, and what is deferred. | Anything a reader could not work out from the code: a trade-off, a constraint, a deferral, a correction. |
| `SCHEMA.md` | The database shape. | Any migration. |
| [`DEVELOPING.md`](DEVELOPING.md) | **How to run the three parts**, and the traps on this machine. | A command changes, or something costs an hour twice. |

Requirement IDs go in commit messages and in code comments. `// A-02` next to a
retry interval is what stops someone "tidying it up" a year from now.

**Anything finished is recorded here or in the SRS, not left in a conversation.**
Chat is not a record.

---

# Open items (live)

Kept current. Resolved entries are deleted, not ticked — the decision log above
is where history belongs.

**Last reconciled: 2026-09-18 (flags).**

## Where to pick up

Contacts is the current thread. The flags (S-45) are done. What remains of it,
and what comes after:

| Next | Requirement | Depends on |
|---|---|---|
| **A screen for the calls now being recorded** | A-50, A-62 | nothing — A-14 landed |
| Push flag changes to signed-in agents | S-45, A-17 | the SignalR hub, which exists |
| Export the blocked list for Issabel | S-46 | nothing — now the **only** route to PBX-level blocking |
| CDR import: abandoned calls from `Master.csv` over SFTP | S-55 | **A-14 first** — it writes `communications` rows |
| The caller's identity in the pop-up, VIP badge | A-11, A-16 | nothing |
| Mute and Hold | A-12 | nothing |
| Outbound calls, click-to-call, redial | A-20 to A-22 | nothing |
| Merging two contacts | A-63 | nothing |
| Excel/CSV import | A-64 | nothing |

**A-17 works against the real PBX**, confirmed by test calls, and since A-14 the
rejection is recorded too — a blocked call is reported with status Blocked, so it
reaches the supervisor's reports as the requirement asks. Nothing displays it
yet, which is the general gap below rather than one of A-17's.

One piece of A-17 stays open and cannot be closed in the Agent App:
**declining is not hanging up.** The PBX decides what the caller hears next, so
a queue may still hold them or pass them on. Only **S-46**, the blacklist the
Issabel administrator loads, stops the call before it enters the queue. It is a
*Should* in the SRS and worth more than that in practice.

**A-14 landed on 19 September.** Every call the Agent App sees now reaches the
server — answered, missed, rejected, blocked — with an on-disk queue behind it so
nothing is lost when the server is down. That unblocks the reports (R-01 to
R-21), the contact history (A-62), the agent's own call log (A-50), the
classification (A-40) and the call-back tasks, none of which could start before
it.

**Nothing shows any of it yet.** The data arrives and there is no screen. The
agent's call log and the contact history panel are the smallest useful next
step, and both are now possible.

**The CDR importer (S-55) is unblocked too** — it writes the same
`communications` rows — but still needs its own prerequisite first: three test
calls, then read real rows of `Master.csv` and record what marks an abandoned
call. That needs access to the Issabel box and does not depend on anything here.

**Pin `SQLitePCLRaw.lib.e_sqlite3` before A-14 if the offline buffer is part of
it.** The block-list cache deliberately avoided SQLite, so the advisory has not
been hit yet; a communications buffer that survives a network drop would hit it.

Merging and import are independent of all of this and can be done whenever.

The history panel (A-62) genuinely cannot start yet — it shows a contact's past
calls, and communications do not exist until the call work lands.

## Must fix before handover

- **No password recovery for a locked-out supervisor.** There is no self-service
  reset, the reset screen is behind supervisor login, and the seed command
  refuses once any user exists. A lockout needs direct database access. Either a
  `reset-password` command alongside `seed`, or a documented procedure. Hit for
  real on 2026-09-17.
- **The server still targets `net8.0`**, Dockerfile included, and .NET 8 leaves
  support in **November 2026**. The Agent App moved to .NET 10 on 2026-09-17.
- **Ask the client's IT about executable policy.** `dotnet run` was blocked on
  the developer's own machine with *Access is denied* while `dotnet <dll>`
  worked. The same policy would block the Agent App installer on the agents'
  laptops. Code-signing is the usual answer. See `RELEASING.md`.
- **Get the real brand colours.** Both apps use the blue from the CallPoc proof
  of concept. The orange they used before was never the restaurant's — it came
  from the initial scaffold as Tailwind's default orange under a key called
  "brand". The SRS specifies no colours at all. One value in each app.

## Questions for the telephony provider

**Mostly answered, 2026-09-19.** No inbound port to the PBX, on any port — so
AMI and database access are ruled out. The client will not have a waiting caller
answered and hung up on — so S-56 is removed. And **we now have administrative
access to the Issabel box ourselves**, so the questions about the CDR export and
the call-back extension are not questions any more: they are runbook step 8,
which we perform.

Still to confirm, and **nothing is blocked on them except the work they
describe**:

1. Is incoming customer routing a **queue or a ring group**? Decides whether
   S-57 applies instead of S-55.
2. Who administers Issabel's **Blacklist** screen, and how often will they load
   the S-46 export?
3. What **VPN uptime and support hours** are agreed, and who is called when the
   tunnel drops?
4. Can a **recording announcement** be added before ringing agents, if the
   client wants one?

Not a question for anybody — an observation we make ourselves, before the
importer is written: three test calls, then read the last rows of `Master.csv`
and record what marks an abandoned call, and whether a usable wait time is
there at all. See the 19 September CDR entry.

## Known gaps in what is built

- **Login has no database-backed test.** The suite runs without PostgreSQL by
  design, so token validation and encryption are covered and the actual login
  path is not.
- **Most of the call path still has not met a PBX.** Calls now arrive, and the
  automatic rejection of a blocked caller is confirmed working against the real
  switch. **The pop-up, Answer, Hang up, the timer, the audio and the queue
  badge are all still unverified** — every test call so far was blocked before
  it reached any of them. The block-list rule is covered by tests because it was
  deliberately put in Shared; everything downstream of an actual INVITE is not.
- **`docs/DEVELOPING.md` says to stop the apps before building, and the check
  for whether they are running is easy to get wrong.** Both run as `dotnet.exe`,
  so looking for a `CallCenter` process finds nothing while both are up. The
  command that works is
  `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"` and reading the
  command line.
- **Call logging has no database-backed test either.** The new tests cover the
  door (a supervisor is 403, an agent is not), that every outcome the app
  reports is accepted — including Blocked, which A-17 needs — and the refusals
  that answer before any query. What it then does with the row, including the
  contact matching and the update-rather-than-insert on a repeat, is untested
  for the same reason as login: the suite runs without PostgreSQL.
- **Nothing displays the calls being recorded.** The rows accumulate and no
  screen reads them, so a mistake in what is stored would not be visible to
  anybody until the reports are built.
- **The flags have no database-backed test either.** The new tests cover the
  door (an agent is 403 on every write, and gets through on the two reads the
  pop-up needs) and the three refusals that answer before any query. What the
  flags then do — finding the contact a number belongs to, creating a nameless
  one when nothing matches, and the audit rows — is untested for the same reason
  as login: the suite runs without PostgreSQL.
- **The Agent App accepts supervisor logins.** Deliberate while the phone was
  being built — it is how sign-in was tested before user management existed. The
  supervisor web app already refuses agents. Close this before handover.
- **The Agent App's `appsettings.json` has a dead `Sip` section.** Nothing reads
  `Server`, `Username` or `Password`; they arrive in the login response (A-01).
  Misleading, because it looks like where the PBX is configured.
  `RtpPortMin`/`RtpPortMax` are real and should stay.
- **The UI checks are throwaway scripts, not part of the build.** Three things
  were verified by hand during the redesign and none of them are reproducible by
  anyone else: that both Agent App language files carry the same keys, that
  every `StaticResource` a view names is defined, and that every control a view
  uses has a style in the theme. **The last of those is what would have caught
  the white contacts list** — a `ListView` left with no style falls back to
  WPF's default white, and nothing in the build complains. These belong in the
  test project.

## What running the apps has caught that the checks did not

Worth knowing when deciding how much to trust a green build. Three defects this
session were found only by opening the app:

1. Every login answered 500 — a validation attribute on the wrong half of a
   record. 176 tests were green, because none posted a login.
2. The contacts list was a white block — a file rewrite that silently never
   landed, leaving a control the new theme no longer styled.
3. English rendered right-to-left — the document direction was set only when the
   language switcher was touched, never at startup.

The build and the test suite verify structure. Nothing here can tell whether a
screen *looks* right, because the apps cannot be launched from the development
session. Screenshots from the developer are doing real work.

All three were fixed, and **the developer confirmed the fixes on 2026-09-18** by
running both apps: the contacts list renders as a dark grid with its empty state,
and English stays left-to-right across a reload.
