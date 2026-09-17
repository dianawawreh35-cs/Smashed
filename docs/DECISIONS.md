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

---

# How this project is tracked

Three files, each with one job. Kept current as part of doing the work, not
afterwards — a change that is not written down did not happen.

| File | Holds | Updated when |
|---|---|---|
| [`SRS-Smashed-Burger-Call-Center.md`](SRS-Smashed-Burger-Call-Center.md) | **What the system must do.** Every requirement has an ID (A-01, S-42, N-04a) and is the contract with the client. | **Any design change.** If behaviour, scope or an assumption changes, the requirement changes with it — in the same commit. A requirement that no longer describes the system is worse than no requirement. |
| `DECISIONS.md` (this file) | **What was decided and why**, including what was considered and rejected, and what is deferred. | Anything a reader could not work out from the code: a trade-off, a constraint, a deferral, a correction. |
| `SCHEMA.md` | The database shape. | Any migration. |

Requirement IDs go in commit messages and in code comments. `// A-02` next to a
retry interval is what stops someone "tidying it up" a year from now.

**Anything finished is recorded here or in the SRS, not left in a conversation.**
Chat is not a record.

---

# Open items (live)

Kept current. Resolved entries are deleted, not ticked — the decision log above
is where history belongs.

### Must fix before handover

- **No password recovery for a locked-out supervisor.** There is no self-service
  reset, and the reset screen is behind supervisor login. The seed command
  refuses once any user exists. Today that means a lockout needs direct database
  access. Either a `reset-password` command alongside `seed`, or a documented
  procedure. Hit for real on 2026-09-17 when the September 14 password was not
  recoverable.
- **`SQLitePCLRaw.lib.e_sqlite3` 2.1.6 carries CVE-2025-6965** (high, memory
  corruption), arriving through EF Core Sqlite in the Agent App. Nothing reaches
  it — the offline buffer is still a README. **The offline-buffer work must not
  start without pinning this first.** Fixed versions exist from 2.1.12.
- **The server still targets `net8.0`**, Dockerfile included, and .NET 8 leaves
  support in **November 2026**. The Agent App moved to .NET 10 on 2026-09-17;
  the server is a separate, deliberate piece of work.
- **Ask the client's IT about executable policy.** `dotnet run` was blocked on
  the developer's own machine with *Access is denied* while `dotnet <dll>`
  worked. The same policy would block the Agent App installer on the agents'
  laptops. Code-signing is the usual answer. See `RELEASING.md`.

### Questions for the telephony provider

Section 4.5 and reports R-20/R-21 are deferred until these are answered
(SRS §12.3). The first is the one that decides whether they are possible at all:

1. Can the **server** have its own VPN connection to the PBX? The agent laptops'
   VPN does nothing for it, and the server is what talks to AMI or the CDR.
2. Is **AMI** available (TCP 5038) with a user for us and our server's VPN
   address permitted?
3. Read-only access to the **`asteriskcdrdb`** database, or a regular CDR export?
4. Is incoming customer routing a **queue or a ring group**?
5. What **VPN uptime and support hours** are agreed, and who is called when the
   tunnel drops?

### Known gaps in what is built

- **SIP registration is written but never verified against a real PBX.** It
  compiles and correctly reports "cannot reach the PBX" against a fake host.
  Whether it registers is unknown until it points at a real one.
- **Login has no database-backed test.** The suite runs without PostgreSQL by
  design, so token validation and encryption are covered and the actual login
  path is not.
- **The Agent App accepts supervisor logins.** Deliberate while the phone is
  being built — it is how sign-in was tested before user management existed. The
  supervisor web app already refuses agents. Close this before handover.
- **The Agent App's `appsettings.json` has a dead `Sip` section.** Nothing reads
  `Server`, `Username` or `Password`; they arrive in the login response (A-01).
  Actively misleading, because it looks like where the PBX is configured.
  `RtpPortMin`/`RtpPortMax` are real and should stay.
- **`src/CallCenter.Web/tsconfig.app.tsbuildinfo` is committed.** A build
  artifact; it belongs in `.gitignore`.
- **No automated check that the two Agent App language files agree.** A missing
  key degrades to Arabic and then to the raw key rather than crashing, but it
  would be found by eye rather than by the build.
