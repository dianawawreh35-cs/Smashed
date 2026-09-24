# Decision log

Decisions that shaped the repository, newest first. Each entry records what was
chosen and why, so a later reader does not have to re-derive it.

---

## 2026-09-22 — Do not disturb and auto answer (A-18)

Two check boxes in the agent's rail, beside sign-out and language.

**486 Busy for do not disturb, not the 603 Decline a block gets.** 6xx is a
global failure: RFC 3261 has the proxy stop trying other branches, which is
right for a caller a supervisor has blocked and wrong for an agent stepping away
from their desk — it would take the call away from every other agent too. 486
says "not this line", so the queue offers the call to somebody else.

**Logged Busy, not Rejected.** From the caller's side a do-not-disturb refusal
and a second call arriving mid-call are the same event, and the service figures
the supervisor reads should not show an agent who went to lunch as having
rejected customers. Rejected stays what it is: the agent saw the call and
pressed Reject.

**Do not disturb wins over auto answer.** Both set is a contradiction, and the
safe reading is the one that does not connect a live customer to an empty chair.
The auto-answer box is disabled rather than hidden while do not disturb is on,
so the agent can see why it is not doing anything.

**Remembered per laptop, in `settings.json` beside the language and the audio
devices.** A phone silently un-silencing itself at the next sign-in is its own
kind of surprise; the state survives a restart and is visible in the rail, which
is what makes it recoverable.

**The state lives in `PhonePreferences`, not on the view model.** The call
service reads it on a SIP thread while an INVITE is waiting for an answer, and
the check boxes write it on the UI thread. One locked object owns both switches;
the view model is a pass-through, so there is no second copy that could disagree
about whether the phone is off.

**A default `CheckBox` style was added to the theme.** Check boxes were
unstyled, so Windows drew their labels in its own near-black on our near-black
surface — the fifth control to fall into that hole after the contacts list, the
DatePicker, its calendar and the ComboBox. Only the label is taken over; the
Windows box itself reads fine on a dark background. This also fixes the
classification form's checkbox fields, which had the same problem.

**Not verified in the running app.** The build is clean and the behaviour is
reasoned from the SIP paths that A-17 already uses, but nobody has watched a
call arrive with either box ticked. Worth a pass with a real PBX before it is
called done.

---

## 2026-09-14 — Initial repository scaffold

### Stack (given, not chosen here)

| Layer | Choice |
| --- | --- |
| Runtime | .NET 8 SDK at the time; **.NET 10 since 19 Sep 2026** — C# `latest`, nullable + implicit usings on, `TreatWarningsAsErrors=false` |
| Server | ASP.NET Core Web API, SignalR, EF Core 8, Npgsql, Serilog |
| Agent App | WPF (.NET 8 at the time; **.NET 10 since 17 Sep 2026**), SIPSorcery + SIPSorceryMedia.Windows, EF Core SQLite offline buffer, CommunityToolkit.Mvvm |
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

**Closed 19 September 2026: the server moved to net10.0 as well.** The rest of
that paragraph is how it stood on the 17th.

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

## 2026-09-19 — Everything is on .NET 10, and the password lockout has a way out

Two of the four "must fix before handover" items, done. The other two are not
mine to do — see below.

### The server moved to .NET 10

`net8.0` → `net10.0` across the server, the shared library, both test projects and
the PBX simulator; EF Core, Npgsql, the JWT handler, the hosting packages and
`Microsoft.AspNetCore.Mvc.Testing` to their 10.x releases; and the Dockerfile's
build and runtime images to `10.0`.

**This was urgent and read as though it were not.** .NET 8 leaves support in
**November 2026** — about two months from today. A system being handed to a
client should not arrive on a runtime that stops getting security fixes in the
same quarter.

It went through without a single code change. 300 tests pass on .NET 10,
restore found no version conflicts, and `Swashbuckle.AspNetCore` 6.9.0 works
unchanged on the new runtime, so it was deliberately left alone rather than
jumped four major versions for no reason. The Agent App had already moved on
17 September, so the two halves now match.

### A useful thing the upgrade proved about the SQLite advisory

The pin to `SQLitePCLRaw` 2.1.12 was removed as a probe, to see whether EF Core
10 had fixed the problem on its own.

It has not. **EF Core 10.0.8 asks for 2.1.11, which is still affected by
CVE-2025-6965.** Upgrading the framework does not resolve this; only the pin
does. The comment in `Directory.Packages.props` now says so with the version
checked, so nobody removes it on the reasonable-sounding assumption that a newer
EF Core must have moved past it.

### `reset-password`, because there was no way back in

```
dotnet CallCenter.Server.dll reset-password --user supervisor --password 'NewPass!2026'
```

Password resets live behind supervisor login (S-42) and `seed` refuses once any
user exists, so a forgotten supervisor password meant editing the database by
hand. That happened for real on 17 September.

**Not a self-service reset**, and that is deliberate. A "forgot password" flow
needs somewhere to send a link, and this system has no email, no SMS and no
internet access by design. What it does have is a server the client controls:
anyone who can run a command on it could already edit the database directly, so
this grants nothing new — it just removes the need for a database client and the
knowledge to use one.

Three things it does beyond setting the password, each because the alternative
is a command that looks like it worked:

- **Re-enables a disabled account.** The lockout worth planning for is "the only
  supervisor was disabled by accident"; a reset that fixes the password and
  leaves the account disabled fixes nothing.
- **Closes every open session**, with reason `PasswordReset` — a new value, so a
  row of sessions all ending at once can be explained later. If the password was
  reset because it may have leaked, leaving the old tokens working defeats the
  point.
- **Lists the supervisor logins when the one asked for does not exist.** A
  lockout is often "was it `supervisor` or `admin`", and sending somebody to a
  database client to find out is the problem this command exists to remove.

Promotion to supervisor is opt-in (`--make-supervisor`): resetting a password
must not quietly change what an account can do.

Eleven tests cover the argument parsing. That is the half worth testing — it is
run once, under pressure, by somebody locked out, and a command that does the
wrong thing because an option was mistyped is worse than one that refuses.

### The other two items are not the developer's to close

**The executable policy** needs the client's IT to answer whether locally built
executables are blocked on the agents' laptops, and if so, to agree
code-signing. Nothing in the repository moves that forward.

**The brand colours** need the restaurant to say what they are. Both apps use the
blue from the CallPoc proof of concept, which was never the restaurant's colour,
and the SRS specifies none. It is one value in each app and cannot be guessed —
inventing a colour and shipping it would be worse than the placeholder, because a
placeholder is visibly provisional and a wrong brand colour is not.

Both stay on the list, now marked as waiting on somebody other than the
developer.

## 2026-09-20 — The last two must-fix items are answered, and the list is empty

Both were waiting on the client rather than on the developer, and both came back
in one go.

### The agents' laptops have no executable policy

`dotnet run` is blocked on the **developer's** machine with *Access is denied*,
and the worry was that the same policy would stop the Agent App installing on
the agents' laptops. It does not — they have no such policy.

So **code-signing is not needed for this delivery.** That was the expensive
answer (a certificate, and a signing step in the release) and it is off the
table.

What stays is a development inconvenience, not a handover risk: the developer's
own machine still requires `dotnet <name>.dll` from the output folder rather than
`dotnet run`, which is why `DEVELOPING.md` leads with it. `RELEASING.md` keeps
its code-signing section as background, since a future client may well have the
policy this one does not.

### The blue is the brand colour now

`#4F8CFF` arrived from the CallPoc proof of concept as a placeholder, and before
that the scaffold used Tailwind's default orange under a key called "brand",
which was never the restaurant's either. The SRS specifies no colours at all.

The client has chosen to keep the current theme. **That makes the blue a
decision rather than a stand-in**, and the distinction is worth recording: the
comments in `tailwind.config.js` and `Theme.xaml` described it as borrowed, so
anyone tidying up later would have read it as unfinished work and been right to
ask. They now say it was chosen, when, and — the part that actually matters — that
the same value appears in both apps and they have to change together.

No visual change. One value in each app, and both already held it.

### The must-fix list is empty

Four items on 19 September: the supervisor lockout, .NET 8 leaving support, the
executable policy, and the brand colours. The first two were fixed yesterday;
these two are answered. Nothing is left on it.

That does not mean nothing is outstanding — the open items list above it is long,
and the pop-up still has not been seen working. It means nothing on the list is
a reason the system could not be handed over.

## 2026-09-20 — The calls become visible: the agent's log and the contact history

A-50, A-52 and A-62. Calls have been recorded since yesterday and nothing could
look at them, which meant a mistake in what is stored would not have surfaced
until the reports were built. Two screens fix that.

### The agent's call log

A new first section in the Agent App's rail — first because it is what an agent
checks between calls and works through at the end of a shift.

**A-52 is enforced by the endpoint's shape, not by the screen.** `GET
/api/communications/mine` takes no agent id and accepts none; the token decides.
There is no request this app could make that would return another agent's calls,
which is a stronger guarantee than a screen that simply does not offer the
option.

Filtering happens in the app rather than on the server. One agent's shift is a
small list, and a filter that answers as they type beats one that makes a round
trip per keystroke. When the list outgrows that the filter moves server-side and
the screen does not change.

### "Not classified" is a property of answered calls only

A-41 wants unclassified calls highlighted until dealt with. The flag is
`!IsClassified && Status == Answered`.

Missed, rejected and blocked calls are deliberately excluded. There was no
conversation to classify, and marking them would hand every agent a list of work
they can never clear — which is the fastest way to make a highlight meaningless.

### The contact history

Under the contact's details in the supervisor app (A-62): every agent's calls
with that customer, newest first, with the outcome, duration, queue and who took
it. Not just the viewer's calls — the panel exists to show the customer's whole
relationship with the restaurant, and half of it would mislead.

Colour is by outcome rather than by call: answered is green, missed and abandoned
are red. Scanning that column shows how often this customer has failed to get
through, which is the question the panel is really for.

Recordings are the part A-62 restricts to the viewing agent's own calls. They do
not exist yet, so nothing here has to enforce it.

### Two presentation rules, applied in both screens

**A call that was never answered shows a blank duration, not 0:00.** Zero reads
as a call that connected and was silent.

**A call with no contact shows its number where the name would be**, rather than
an empty cell. The agent needs something to recognise the caller by, and a blank
reads as a broken row.

### What this did not include

A-51 — opening a call to see its details, play the recording and classify it — is
not built. It needs the classification form and recordings, neither of which
exists. The log lists calls; it does not open them yet.

## 2026-09-20 — Every call log was 500ing: DateTimeOffset and PostgreSQL

The first real call to reach the new call log failed, and every one after it.
Found in the server log:

```
Cannot write DateTimeOffset with Offset=03:00:00 to PostgreSQL type
'timestamp with time zone', only offset 0 (UTC) is supported.
```

Npgsql refuses a `DateTimeOffset` carrying a non-zero offset. Everything the
server writes uses `UtcNow`, so nothing had ever hit this. The Agent App stamps
a call with `DateTimeOffset.Now` — which is right, it is describing a moment in
the agent's day — and Palestine is +03:00, so every insert failed.

### Fixed in the context, not in the service that hit it

A value converter in `ConfigureConventions` normalises every `DateTimeOffset` to
UTC on the way to the database. The alternative — converting in
`CommunicationsService` — fixes today's caller and leaves the next one to
rediscover it: the classification endpoint would have taken a timestamp from the
same app and failed the same way.

Nothing is lost by converting. The instant is identical, and `timestamptz` never
stored the offset anyway.

### What it cost, and what it proved

Five calls failed before it was noticed, because the only symptom on the agent's
side is a log line — the pop-up works, the call log is simply empty.

**Nothing was lost.** All five sat in the offline buffer with their attempt
counts rising, and go through on the next flush. That is the first time the
queue has earned its keep, and it did so against a bug on the server rather than
a network outage, which is not the failure it was designed for.

### Two things this exposes

**No database-backed test would have caught it either.** The suite runs without
PostgreSQL by design, so a rule enforced by the driver — not by the model — is
invisible to it. This is the third defect in the "green build, broken system"
family, after the login 500 and the white contacts list.

**The Agent App's SQL logging is too loud.** Finding this meant reading past
every `SELECT` and `INSERT` the buffer runs. On a laptop that detail is noise,
and it will bury the lines that matter once calls are flowing.

## 2026-09-20 — Unblocking a number now takes effect without signing out

The block list was fetched at sign-in and never again. The gap was written down
on 19 September as something to fix "with the call work"; it bit during testing
today instead, and cost twenty minutes of a call that would not come through
after the number had been unblocked.

### Why it only matters in one direction

Blocking somebody late is harmless — they get through once more. **Unblocking
somebody late is not.** Every agent already signed in keeps rejecting that
customer until they next sign out, which on a normal shift is hours. The
customer cannot order all evening, and nobody in the restaurant can explain why,
because from the agent's side nothing happens at all.

### A timer, not a SignalR push

The hub exists and would be instant, which is why it was the first idea.
Rejected on reliability: a push fails silently when the connection drops, and
the connection dropping is exactly the circumstance in which the list goes
stale and nobody notices. A poll that repeats every two minutes cannot fail
that way — the worst case is being two minutes behind.

Two minutes is the staleness the client is accepting. Short enough that a
supervisor unblocking somebody sees it work while they are still at their desk;
long enough to be invisible.

A push could still be added later as an optimisation, with the timer underneath
it as the guarantee. That is the right order; doing the push first would have
left the guarantee missing.

### Quieter about it

A refresh that changes nothing now logs at Debug. At one refresh every two
minutes an unchanging list would otherwise write thirty lines an hour saying
nothing happened, and bury the ones that matter. A change logs the count before
and after at Information, because a block list that suddenly empties is worth
seeing in a log.

## 2026-09-20 — The first call all the way through, and where a hang-up stops being ours

```
10:05:44  INVITE from 0569498581 via queue smashed-002
10:05:54  Call answered
10:06:03  Call finished
```

Pop-up, answer, two-way audio, hang-up. Everything built since 18 September had
been unverified against real hardware; most of it now is not.

### The caller is not hung up on, and that is the dialplan's doing

The agent puts the phone down, and the caller keeps hearing a tone rather than
being released.

The app is correct here: it sends `BYE`, the PBX answers `200 OK`, and the leg
between this extension and the PBX is gone. There is no further thing a phone
can send — SIP has no way to say "and release the other leg too". That leg
belongs to the PBX.

What happens to the caller is decided by what follows `Queue()` in the dialplan
for `smashed-002`. With a `Hangup()` after it the caller is released; without
one they fall through to whatever is next, which is what is being heard. **A
dialplan fix on the Issabel side, not an app one**, and worth recording because
the symptom points squarely at the app.

This is the same shape as the blocked-caller-on-hold question: the Agent App
controls its own leg and nothing beyond it.

### A log line that said the opposite of the truth

```
Call finished: the other party hung up
SIP OUT "BYE"
```

The agent hung up. `SIPUserAgent.OnCallHungup` fires for **both** ends — it is
raised from inside our own `Hangup()` as well as when a BYE arrives — and the
handler assumed remote.

Harmless to the call and expensive to a person: it is the kind of line somebody
believes for an hour while debugging something else, and it appeared in the log
of the exact session where the hang-up was being investigated. A flag set around
`Hangup()` now tells the two apart.

### And the audio was closed a moment too early

`HangUp()` finished the call before the BYE had left, so the RTP session was torn
down while the BYE was still being built. Nothing observed went wrong, but the
order was backwards; the BYE goes first now and the audio closes after.

## 2026-09-20 — The busy path covers direct dials, not queued customers

Raised by the developer while planning the test for it, and it corrects
something written as though it were broader than it is.

A **queue does not offer a call to a member who is already talking**, so a
second INVITE never arrives at a busy agent from `smashed-002`. The app's busy
handling fires only on a **direct dial** to the extension — an internal call
from a branch or another agent.

### What that means for the reports

A customer who rings while every agent is busy **never touches the Agent App**.
They wait in the queue, and either get through when somebody frees up or give
up. Nothing in this system sees them.

Those are the abandoned calls, and they are precisely what section 4.5 exists
for: they arrive from the PBX's CDR file (S-55), with the latency of the import.
So the coverage stands as already written — this does not open a new gap — but it
is worth being exact that "Missed because we were busy" in the call log means an
internal caller, not a customer.

The code comments said busy "lets the PBX offer the caller to somebody else",
which reads as though the queue would re-offer it. Corrected in place: it is the
queue's own skipping that keeps a busy agent out of the rotation, before any of
this runs.

## 2026-09-20 — The call log filters on the server, because filtering a page lies

Asked what would happen if an agent filtered by date and the match was older
than the hundred calls fetched. The answer was that the screen would say **"no
calls match"** — and mean "none in the part I looked at".

The first version fetched the agent's last hundred calls and filtered them on
the laptop. The comment defending that said the filter would move to the server
"when the list outgrows a hundred". A busy agent takes fifty to a hundred calls
a day, so it had outgrown it before it was written; the flaw was already live in
the name and number search, not merely waiting on the unbuilt date filter.

Searching for a customer spoken to two hundred calls ago answered "no calls
match", which reads as "you never called them". **A screen that reports nothing
when it means nothing-here is worse than one that cannot answer at all**, because
the first is believed.

### Now

`GET /api/communications/mine` takes `from`, `to`, `q` and `unclassified`, and
every one is applied in the query. The text search covers the number as dialled,
the normalised number and the contact's name, so `0599` finds a call stored as
`970599…`. `to` is a date rather than an instant and includes the whole of that
day — "to Tuesday" that excluded Tuesday's calls would be the same class of quiet
wrongness.

The cost is a round trip per change, so the text box waits for a pause in typing.
And each fetch cancels the one before it: a slow answer for `05` landing after
the answer for `0599` would put the wrong rows on screen, which looks exactly
like the filter being broken.

### A-50's date filter, which was the question

Built at the same time, because building it over a client-side filter would have
shipped the bug rather than the feature. From and To date pickers, and a Clear
button that appears only when something is set.

### The DatePicker needed a style, and that is a repeat

WPF's `DatePicker` is light-themed by default, and a control with no style in
`Theme.xaml` falls back to that default — which is precisely how the contacts
list came out as a white block on 17 September. The calendar popup is a separate
control with its own default, so `DatePicker`, `DatePickerTextBox`, `Calendar`,
`CalendarDayButton` and `CalendarButton` are all styled.

The open items have listed "every control a view uses has a style in the theme"
as a check that exists only as a throwaway script. That check would have caught
the white contacts list, and it would have caught this. It is still not in the
test project.

## 2026-09-20 — An agent's call log reaches back a week, and the supervisor decides

The agent's call log had no lower bound. Asked to limit it — first to a day, then
a week, then settled as a setting, which is the right answer: the useful window
depends on how the restaurant works, and guessing it in code means a code change
to correct the guess.

`agent.call_log_days`, default **7**, range **1–90** (S-47).

### A working window, not a retention rule

Nothing is deleted. The contact history (A-62) still shows every call a customer
ever made, and the supervisor's reports see everything. What the setting bounds
is one screen: how far back an agent can look at their own calls.

Worth saying plainly in the SRS and in the supervisor's hint, because "call log
window" reads like data expiry and would be alarming if it were.

### Enforced by the server, or it is not a limit

The window is applied in the query, not by the screen. The method takes whatever
`from` the caller asks for and moves it forward if it reaches past the window, so
an agent cannot see older calls by editing a request — and the app does not have
to be trusted to enforce a rule that is the supervisor's.

### The speed question, which was asked directly

The concern was lag once there are many calls. Three things carry it:

**The window bounds the scan.** One agent, one week, rather than a walk back
through the year.

**The index matches the query exactly.** `ix_comm_agent_started` is
(`agent_id`, `started_at DESC`), which is precisely the filter and precisely the
sort, so PostgreSQL walks the index backwards from the window's edge and stops
after the page limit. It never reads what it is not returning.

**Nothing is tracked.** These are reads, and EF was building a change snapshot of
every row for a list nobody edits. `AsNoTracking()` on the call log, the contact
history and the three lookups behind them.

The text search is the one part that cannot use an index — `ILIKE '%text%'` never
can — but it runs after the agent and window filters, so it only ever examines
one agent's week.

The 90-day ceiling exists for the same reason. It is the one setting where a
generous value costs performance on every agent's screen, so it is bounded rather
than open.

### Caught by a test, which is worth noting

Adding the setting broke `Settings_are_the_keys_the_schema_lists` — the seed and
the schema document must agree. That is the check working as intended: the seeded
settings are the ones a fresh installation gets, and a new one that reached
production unseeded would be invisible until somebody wondered why the default
was not applying.

## 2026-09-20 — Delivery areas: a feature the SRS did not have

Asked for by the client and not in the requirements: an agent needs to know
**which branch delivers to a place and what delivery costs**, and the supervisor
needs to maintain that list. Added as **A-65** and **S-58**, with the two
spreadsheets the restaurant already keeps seeded as the starting data.

Written into the SRS in the same commit as the code, as everything here is. A
feature the contract does not mention is a feature nobody agreed to pay for.

### One area, one branch

The unique index is on the area name alone, not on the pair with the branch.
The agent's question is "who delivers to Kafr Aqab?", and an area listed under
two branches has no answer to it — the screen would show two prices and the
agent would pick one.

The restaurant's own lists work this way: 228 areas across four branches, none
shared. Should that ever change, it is a schema change and a deliberate one,
which is the right amount of friction for something that would make every lookup
ambiguous.

### Arabic names get the contact-name treatment

These are place names copied from handwritten lists by several people, so
<c>الطيرة</c> and <c>الطيره</c> have to be the same place. They run through
`NameNormalizer`, the same folding contact names get (17 September). Without it
an agent searches, finds nothing, and quotes the wrong price — which is worse
than finding nothing, because they will quote *something*.

Search is a substring match rather than exact: an agent hears half a name and
types what they caught.

### Pasting a list is the requirement, not a convenience

166 rows for Nablus alone. A form that takes them one at a time is a form the
supervisor stops using, and then the agents quote last year's prices — the
feature fails quietly rather than visibly.

So: pick a branch, paste two columns, save. Tab-separated first because that is
what a paste out of Excel gives, comma accepted for a list typed by hand. The
price is read from the **last** field rather than the second, so an area name
containing a comma still works.

Three decisions inside it:

**Parse everything, then write.** The whole import is one transaction. A paste
that is half-rejected must not leave the branch half-updated, because the
supervisor's next move is to fix the file and paste it again, and they need the
first attempt to have changed nothing.

**Every rejected line comes back with its number and a reason.** "154 of 168
added" leaves somebody to find the other fourteen by eye, which is how a list
ends up quietly incomplete.

**An area belonging to another branch is reported, not moved.** Moving it
silently is how an area ends up served by whichever branch was pasted last, and
nobody would see it happen.

`Replace` empties the branch first, so a pasted list becomes the whole list. Off
by default and spelled out in full on the screen, because it is the one
destructive thing here.

### The branches got their real names

They were still `Branch 1` to `Branch 4` — placeholders from the initial seed
that nobody had renamed. An area cannot belong to "Branch 3", so the seed now
creates رافات, بطن الهوى, ايكون and نابلس, and the four in the
development database were renamed in place.

Safe to rename at any time: everything that refers to a branch holds its id. The
seed data is the one place a branch is named, and only to create it.

### What the spreadsheets needed

Three things, each recorded in `SeedData` so a later reader comparing the code
against the files does not think rows were lost:

- One Ramallah row spelled the Rafat branch **رفات** — the same name without its
  alif. Merged.
- The Nablus list held one area **twice**, identical branch and price. Imported
  once.
- **Three areas are priced 0**, all beside the Rafat branch. That is free
  delivery for the street outside, not missing data, and nothing may treat it as
  unset. The CHECK allows zero and refuses negative; the supervisor's screen
  shows "Free" rather than a bare 0, which reads as missing.

### A read-only branches endpoint, deliberately narrow

The supervisor's screen needs a branch dropdown and there was no branches API at
all. `GET /api/branches` returns the list and nothing else. **S-41** — creating,
renaming and disabling branches — is still unbuilt; this is not a start on it,
it is the one read that was blocking S-58, and building S-41 replaces this
controller rather than working around it.

### What is not built

The price does not appear on the call pop-up. That needs the caller's address
matched to an area, and address matching does not exist — A-13 matches phone
numbers, not places. The agent looks it up on the Delivery tab, which is what
was asked for.

## 2026-09-21 — Two settings that did nothing are gone

`callback.extension` and `pbx.ami.enabled` were still seeded and still on the
supervisor's Settings page. Both belonged to approaches ruled out on 19
September: AMI needs an inbound port to the PBX, and the call-back extension
answered a waiting caller and hung up on them.

Noticed by the developer reading the list of what a fresh server gets, and
removed. **A setting somebody can type into that changes nothing is worse than a
missing one** — a supervisor would enter a call-back extension, save, and wait
for behaviour that no longer exists anywhere in the system.

Removed from the catalogue, the seed, the seeded-settings tests, the schema
document and both language files. `ValidateBoolean` is now unused and was kept
with a note: the boolean kind is supported the whole way through, including a
control on the supervisor's screen, so the next yes/no setting needs that line
rather than a rewrite.

This had been recorded twice as a follow-up "to be done with the CDR importer".
It took five minutes and did not need the importer at all, which is the usual
shape of a deferred tidy-up.

## 2026-09-21 — The menu: a second feature the SRS did not have

Asked for straight after the delivery areas and built the same way: an agent
mid-call is asked "what comes in the Overdose?" and "how much is a double as a
meal?", and the answer today is a printed sheet beside the laptop that goes
stale the moment a price changes. **A-66** and **S-59**, seeded from the
restaurant's own menu spreadsheet — 44 items, 12 categories, 39 photographs.

### Four price shapes, and why they are four columns and not one string

The printed menu writes prices four ways and each means something different:

| The menu says | Stored as |
|---|---|
| `26 (sandwich) / 36 (meal)` | price 26, meal price 36 |
| `25` | price 25 |
| `+2` | price 2, `is_surcharge` |
| `FREE` | price 0, `is_surcharge` |
| `not shown on menu` | price **null** |

Keeping the original string and letting the screen print it would have been
less work and wrong: reports cannot add up a string, and the supervisor's form
could not validate one.

**Null and zero are deliberately different.** Null is "the menu prints no
price" — there is exactly one such item, a combination offer. Zero is a free
extra. An agent has to be able to tell "free" from "ask the branch", and a
single number cannot say both.

**`is_surcharge` earns its column.** Without it, "إضافة الجبنة — 2" reads as a
portion of cheese costing 2, and an agent would quote it as a line of its own
instead of adding it to a burger. Both apps render it as "+2" and never as a
bare number.

### Arabic only, as instructed

The spreadsheet carries English names and contents too. Not stored: the menu is
Arabic, the agents speak Arabic to customers, and a second set of names is a
second thing to keep correct and a second thing to forget. The English stays in
`docs/smashed_menu.xlsx` if it is ever wanted.

Names are folded by `NameNormalizer` as everything else is, and it matters more
here than anywhere. These are English words written in Arabic — سماشد, ماشروم,
كرسبي — and nobody spells them the same way twice. The description is searched
as well as the name, because "what has mushrooms in it?" is a question agents
are asked and the answer is in the contents.

### The pictures are files on disk — a decision reversed the same day

39 photographs, 1.2 MB. Built first as `bytea` in the row, on the reasoning that
**the backup already covers the database** and a folder of images is one more
thing to back up separately and forget. Dia asked "why not move to file from
now?", and the answer is that the reasoning was wrong twice over.

**The folder was already backed up.** Step 9 of the runbook has rsynced
`data/recordings/` since it was written. Menu pictures beside them add a line to
a script that already exists — there was never an extra thing to remember.

**And this is the part that decides it: `rsync` is incremental, `pg_dump` is
not.** Menu photographs never change. On disk the nightly backup copies them
once, ever. In the database they were re-dumped and re-copied *every single
night, for ever* — 1.2 MB a night now, and more as the menu grows. Postgres also
carries the bytes through its write-ahead log and its vacuum, for data that is
never read by a query and never joined to anything.

The cost of switching was at its lowest the hour after it was written, which is
the other half of why it was worth doing rather than noting as a future task.

**What files cost, honestly.** A row and its file cannot be written in one
transaction, so two things can go wrong. A file left behind after its item is
deleted wastes a few kilobytes. A row whose file is missing shows without a
picture and answers 404 for it. Neither loses anything an agent needs, which is
what makes the trade acceptable — this would be a different argument for call
recordings, which are evidence.

The missing-file case has a fix rather than a shrug: the pictures still ship as
embedded resources in the server assembly, and running `seed` again puts back
any seeded picture whose file has gone. That covers both a restore that brought
the database back without the folder and the migration that moved them out.
Files are named after the item's id, never after anything typed, so there is no
path to traverse.

Served from their own endpoint rather than inside the list, and cached for a
day — unchanged, and now streamed from disk instead of buffered out of a query.
44 items carrying 1.2 MB on every keystroke would make the search crawl over a
VPN; a changed photograph taking a day to appear is a fair trade for a picture
of a burger.

### What the spreadsheet needed

Nothing, which is unusual. Every item had an Arabic name, the categories were
unambiguous, and the only oddities were structural — the trailing provenance
note is not a category, and the five add-ons have no photographs because the
printed menu does not photograph them.

### The tests worth having are about the transcription

The endpoint tests cover the door, as usual. The ones that matter more check
the seed data itself, because a transcription error would be quoted to a
customer without anybody blinking:

- every item names a category that exists
- no item is listed twice within a category
- no price is negative
- **a meal costs more than the sandwich alone** — a meal price at or below the
  sandwich is a typo, and an agent would read it out
- **only add-ons are marked as surcharges** — a burger marked wrongly would be
  quoted as "+26"

The schema test caught the two new tables before anything else did, as it did
for the delivery areas.

## 2026-09-21 — The menu screen finishes: categories, and pictures that can be taken away

Dia asked for the supervisor to be able to add and edit menu items — name,
price, picture. Most of that was already built and had simply never been opened:
the form adds, edits, hides and removes items, and uploads a picture. Three
things were genuinely missing, and all three were missing in the same way — they
only bite the second time you use the screen.

### You could not add a category

The form picked from a list of twelve seeded categories and there was no way to
make a thirteenth. Adding "حلويات" to the menu was impossible from the screen,
which makes "add a menu item" false for any item that does not fit what the
spreadsheet happened to contain. The API had create and delete; renaming needed
a new endpoint (`PUT /api/menu/categories/{id}`).

Kept behind a **Manage categories** button rather than sitting on the page.
Categories change once a season and items change weekly; a screen that shows
both at once makes the rare thing as loud as the common one.

**Hiding a category does not hide its items.** A category holding items cannot
be deleted — the foreign key refuses it and so do we, with a message that says
why — so hiding is what a supervisor actually reaches for. An agent searching
"كرسبي" mid-call should still find it; what hiding controls is whether the
category appears as a heading when the menu is read in printed order. Recorded
in S-59 because it is the kind of thing that gets "fixed" later by someone who
assumes the opposite.

Order is nudged with arrows rather than dragged. Twelve categories reordered
once a season does not justify a drag-and-drop library.

### You could not tell whether an item already had a picture

The edit form showed an empty file input and nothing else. So a supervisor
opening an item could not tell whether they were adding a picture or replacing
one, and **there was no way to remove a wrong picture at all** — only to cover it
with another. The client already had `setMenuImage(id, null)`; nothing called it.

The form now shows the current picture, shows the chosen file before saving, and
offers a **Remove the current picture** box when there is one. Picking a file
and asking to remove one are made mutually exclusive in the UI rather than left
to whichever the save code checked first.

### The new picture would not have appeared for a day

This one would have been reported as "the upload does not work". Pictures are
served with `Cache-Control: max-age=86400`, which is right for agents and wrong
for the supervisor who just replaced one: the browser would go on showing
yesterday's photograph. Every picture URL on this screen now carries a stamp
that changes on each save, so the browser is asked for a URL it has never seen.
The agents' day-long cache is untouched.

### Tests

Six on the screen, covering what would otherwise be quoted wrongly to a
customer: a surcharge shown as "+2" and not "2", a blank price sent as **null**
and not zero, the picture uploaded *after* the item exists (it needs the id),
the picture removable, and categories renamed and added. Plus the door on the
new endpoint, which is supervisor-only like the rest.

`duplicate_category` and `category_not_found` are new error codes. The rename
path was about to tell a supervisor renaming a category that "that category
already has an item with this name".

## 2026-09-21 — A list of what has never been run

Dia asked how to test the things that were built but never opened. The honest
answer was that quite a lot had accumulated: the call pop-up, the call log tab,
contact history, the delivery tab, both menu screens and the offline buffer had
all been written, tested and never once seen working.

`docs/TESTING-checklist.md` is now the list, ordered so that the things needing
nothing but a browser come first and the ones needing the PBX come last. Each
item says what should happen **and what it means when it does not**, because on
this project an unexpected result has usually been information rather than a
dead end.

Also deleted, again: four stale `net8.0` build folders under the Agent App. They
come back whenever an old branch is built, and a stale folder means running
yesterday's code and debugging a bug that was fixed — which has already cost one
round trip here.

## 2026-09-21 — Every menu picture in the supervisor app was a blank box

Found within a minute of Dia opening the screen for the first time, which is the
whole argument for opening screens. The menu page was asked "why do not all
photos show?" — the expected answer was "five add-ons have no photograph, the
printed menu does not photograph a slice of cheese". The actual answer was
**more than five**, and none of them were loading.

### The cause

`<img src="/api/menu/{id}/image">`. The endpoint requires a signed-in user; a
browser loading an image tag issues its **own** request, carrying none of our
headers, so the token never arrived and every picture came back **401**.

The Agent App was never affected. It fetches pictures through its API client,
which sends the token — so the same feature worked in one app and not the other,
and the one it worked in was the one being demonstrated.

### Why it was invisible

A failed picture and an item with no picture rendered **the same empty grey
box**. The screen looked entirely plausible while showing nothing. The tests
passed because they checked that the right requests were sent, and this request
was never sent by us at all — the browser sent it.

Three lessons, all of which this project has now learnt twice:

- A **green build proves structure, not that a screen looks right.** Same
  category as the white contacts list and the call-log "today" label.
- **Absence must look different from failure.** The box now says "بلا صورة" or
  "تعذّر تحميل الصورة". Had it said either from the start, the bug would
  have announced itself.
- **Two clients, one feature, one of them wrong.** Worth checking both whenever
  a feature spans them.

### The fix, and the one that was rejected

`MenuImage` fetches the bytes with the token attached and hands the tag an
object URL. The browser's HTTP cache still applies — it is an ordinary GET — so
a picture marked good for a day is still fetched once.

**Rejected: putting the token in the query string.** There is a precedent for it
in this codebase — SignalR cannot set headers either, and `/hubs` already accepts
`?access_token=`. It would have been one line. But a hub is one connection per
session, while this is 44 image URLs per page load, each carrying a twelve-hour
token into the browser history, the server access log and any proxy in between.
Not worth it for pictures of burgers.

**Also rejected: making the endpoint anonymous.** Simplest of all, and the
pictures are not secret. But it quietly loosens an access rule, which is not a
thing to do to fix a display bug.

Two tests now cover it: the picture request must carry the token, and an item
with no picture must not be asked for one — 44 pointless 404s on every keystroke
is the other way to get this wrong.

**Where this will bite again:** call recording playback (not yet built) is the
same shape — a protected file, in a media tag that cannot authenticate. Use
`requestBlob`.

## 2026-09-21 — The Agent App fetched 39 photographs at once, on every keystroke

Dia opened the menu tab and asked why not all the photos showed. The expected
answer was "five add-ons have no photograph". The real one was that most of them
were failing.

Each row fetched its own picture the moment it was built, with nothing holding
the result and nothing limiting how many went at once. Typing rebuilds the rows,
so a search fired **39 simultaneous requests per keystroke**. The log for one
session: **121 image requests started, 44 responses received.** The rest timed
out after ten seconds and were discarded in silence.

Worse than the missing pictures: they starved everything else. The same log has
the delivery search, the call log and the **block-list refresh** all timing out
while an agent was doing nothing but browsing the menu. A block list that fails
to refresh is a barred caller getting through.

Fixed with `MenuImageCache`: fetched once per picture for the life of the app,
at most four in flight, failures logged rather than swallowed. And the empty box
now says which kind of empty it is — "بلا صورة" for the five add-ons the printed
menu does not photograph, "تعذّر تحميل الصورة" when one was promised and did
not arrive. Those two drew the identical grey box before, which is the entire
reason a screenful of failures passed for a menu of add-ons. Second time today
that exact confusion has hidden a bug; the first was the web app's 401s.

### Open, and genuinely unresolved: the server collapses under concurrency here

Chasing the above turned up something I could not explain and should record
rather than pretend about. On this development machine, **authenticated requests
that touch the database collapse when they arrive together**, while everything
else is fine:

| 39 at once | result |
|---|---|
| `/health` (no auth, no database) | 39/39 in 0.07s |
| `/api/menu` **without** a token (401, no database) | 39/39 in 0.03s |
| `/api/menu` **with** a token | 7/39 in 25s |

One at a time each takes **10 ms**. Confirmed with two independent clients
(Python and curl), so it is not the measuring tool.

Ruled out: Postgres itself (10 concurrent queries inside the container, 0.3s),
Docker's port forwarding (39 TCP connects from the host, 0.03s), IPv6 versus
IPv4 for `localhost`, the log volume (same collapse at `MinimumLevel=Warning`),
OneDrive (same collapse with the whole server copied outside it), and
sync-over-async in our own code (there is none).

The odd detail: during the collapse `pg_stat_activity` shows **one or two
connections**, so the requests are not reaching the database — they are waiting
somewhere before it. Individual `DbCommand` entries take 1–47 ms, with
**eleven-second gaps between them where nothing is logged at all**.

**Leading hypothesis, untested:** `EnableRetryOnFailure()` on the Npgsql
provider. If connections fail transiently under load, EF's execution strategy
retries with exponential backoff, which would produce exactly these 10–30 second
action times and the near-empty connection count. Worth testing by turning it
off and re-measuring.

**Whether production is affected is unknown.** The deployed server runs on Linux
with `network_mode: host` and a different connection path, so this may be local
only — but that is a guess, and the honest position is that a call centre with
several agents is exactly a burst of concurrent authenticated requests. It
belongs on the list to settle before handover.

## 2026-09-21 — The editors open where you are, not at the top of the list

Dia asked for double-click to edit, and for the edit form to appear where the
list is scrolled to rather than above everything.

Both lists had the same flaw: the form rendered above the table, so editing an
item near the bottom of 44 menu items — or 228 delivery areas — opened a form
the supervisor then had to scroll up to find, and scroll back down from. The
longer the list, the worse it got, which is backwards.

The Users screen already did the right thing and had done since it was written:
the panel opens in a row beneath the row it belongs to. Menu and delivery now
follow it, so there is one pattern across the three rather than two.

**Double-click is a shortcut, never the only way in.** The Edit button stays.
A control that exists only on double-click cannot be reached from a keyboard and
is invisible to anyone who does not already know it is there.

**The double-click stops at the action buttons.** Without that, two quick clicks
on Remove would delete the row and then open an editor for the thing that had
just been deleted.

A new item still opens above the list, where the button that asked for it is:
there is no row for it to sit under yet.

## 2026-09-21 — Labels looked editable, and `tsc --noEmit` was not the build

Dia asked why a cursor appears next to the labels on the supervisor screens, and
said it was two things at once: the mouse showing the text I-beam over a label,
and a blinking caret sitting in the label text.

**The first is ours.** Nothing said a label is a control. Clicking one focuses
or toggles the input it names, but it drew the I-beam and its text could be
dragged and highlighted like a paragraph — so it read as "this text is
editable", which is an invitation to try to type into it. Labels now take the
hand cursor and are not selectable; the input inside keeps the I-beam and stays
selectable, and checkboxes, selects and file pickers take the hand.

**The second is not ours, and could not be.** A blinking caret in plain text
means the browser has **caret browsing** on — Edge and Chrome toggle it with
**F7**, which sits next to the function keys people actually reach for, and the
confirmation dialog is easy to dismiss without reading. Nothing in the app can
put a caret in a label: there is no `contentEditable` and no `tabIndex` on any
text in the codebase, which is what made this diagnosable rather than a guess.

### The more important finding: the production build was broken

Checking this turned up that `npm run build` had been **failing since the menu
picture fix that morning** — a type error in `requestBlob`. It had been
committed, and it had passed everything that was run at the time.

The gap: `npx tsc --noEmit` uses the root config, while `npm run build` runs
`tsc -b` across the project references, which is stricter. Unit tests do not
type-check the build either, and Vitest compiles through esbuild, which strips
types without checking them. So the full suite was green against a tree that
could not be built for deployment.

**`npm run build` is now the check that counts** before committing web changes.
A green test run is not evidence the app can be shipped — the same lesson as the
green build that hid the white contacts list, arriving from a different
direction.

## 2026-09-21 — The date filter's calendar was unreadable

Dia asked for the date filter's text colour to be fixed. The box itself was
fine — near-white on near-black. The problem was the calendar that drops out of
it.

`Calendar` had a style setting `Foreground` and `Background`, and that looked
like enough. It was not. The panel that actually draws a month is a
**`CalendarItem`**, an inner control with its own default template, and that
template keeps Windows' light chrome whatever the `Calendar` around it says. So
the day numbers were painted in our near-white text on a near-white panel, and
the Su/Mo/Tu row in a dark grey the default template writes for itself. Two
different ways to be invisible in one dropdown.

Properties could not fix it because the colours live **inside** the template,
where a `Setter` cannot reach. `CalendarItem`, `CalendarDayButton` and
`CalendarButton` are now templated rather than set.

Third time this exact trap has bitten: the contacts list that came out white on
17 September, the `DatePicker` styled just after it, and now the calendar behind
that same `DatePicker`. **An unstyled WPF control does not inherit a dark theme
— it falls back to a light one.** Styling the outer control is not enough when
the inner one has its own template. Worth checking the remaining popups
(`ComboBox` dropdowns, `ToolTip`, `ContextMenu`) before handover rather than
waiting to be told.

Two API details cost a build each, and are written down so the next person does
not repeat them: `DisplayMode` belongs to `Calendar`, not `CalendarItem` —
`CalendarItem` shows and hides its own month and year views — and
`CalendarButton` is a month or a year, so it has `HasSelectedDays` rather than
`IsSelected`.

## 2026-09-21 — Classification, part 1: the server

The biggest hole in the system, and the one that made everything else look
finished when it was not. Four tables, four entities, six types and form
version 1 were all designed, migrated and seeded on 14 September. The API folder
was created the same day and left **empty**. So every call carried an
"unclassified" chip, the Agent App had a filter to find them, and there was no
way to classify a single one — **0 classifications recorded** across the whole
database.

It is also what the reports need. Nearly every number the supervisor asks for is
a count of classifications, so until this existed the reports had nothing to
count.

### The form is versioned, never edited in place

Publishing a change (S-40) writes a **new row** and moves the current flag. A
complaint classified in January still reads back with the questions asked in
January.

The alternative — editing the definition in place — is less code and quietly
rewrites history: a field removed today would erase what agents recorded under it
last month, and a renamed field would relabel answers nobody gave. The schema
already assumed versioning; this honours it. It also means a supervisor who
breaks the form can be put back by re-publishing an older definition.

The client sends back **the version it drew**, rather than the server assuming
its own current one. A form filled in during the seconds a change is published
is stored against the questions that were actually on screen.

### The form is validated, not trusted

A definition that cannot be drawn stops **every** agent classifying **every**
call until somebody notices. So the server refuses one: every field needs a key
and a kind the clients know, no two fields may share a key, and there must be a
type field — without it a call is classified as nothing in particular and every
report loses its grouping.

### Name and label are different things

The type's **name** (`Order`, `Complaint`) is the stable key the reports and the
form's `showWhenType` rules are written against. The **labels** are what agents
read, in both languages. A supervisor renaming "Complaint" to "Issue" changes
the label and nothing else moves — the same rule S-41 states for branches.

**Nothing in use is ever deleted.** A type any call carries can be hidden but not
removed, or those calls would describe nothing and the history could not be
rebuilt. The six the system was built around cannot be deleted at all. Same
pattern as menu categories.

### The edit window is the agent's day, not the server's

A-42 lets agents edit their own classifications for the current day. Measured
against the **call's start** and in **local time**: a call taken at 23:55 and
classified at 00:05 belongs to the evening it happened, and a shift ending after
midnight UTC is still the same evening in Hebron. Measuring in UTC would cut
agents off mid-shift.

### Classifying a call the server has never heard of

The open question from earlier, now settled. A call taken while the server was
down is queued on the laptop and has **no server id**, but the agent still
classifies it at hang-up. `PUT /api/classifications/by-call` keys on the SIP
Call-ID plus the extension — both already stored on the communication — so the
classification can be queued beside the call and land correctly once both
arrive. A 404 there is not an error an agent sees: it means the call has not
synced yet, and the app keeps trying.

The alternative was to refuse classification until the call had synced, which is
simpler and tells an agent who just finished a call to come back later. Given
A-04 promises the app keeps working with the server down, that was not a real
option.

### One thing the type check caught

`[Required] Guid TypeId` does **not** refuse a missing type. A non-nullable Guid
arrives as all-zeros when the client omits it, and the attribute sees a value
rather than a gap — the call would have been "classified" as nothing and passed
validation. It is `Guid?` now. A test covers it, because this is the sort of
thing that is invisible until a report comes out wrong.

### Verified against the real database, not just the tests

Classified a real call, edited it, read the audit trail back (before and after,
with who), published a version 2 of the form with a conditional complaint-reason
field, and confirmed the four refusals: an undrawable form, a form with no type
field, an unknown call, and deleting a system type. **The dev database is now on
form version 2**, which is deliberate — it gives the Agent App work a form with
a `select` and a `showWhenType` rule to draw.

### Still to build

Part 2, the Agent App: the form at hang-up (A-40) and classifying from the call
log. Part 3, the supervisor's form editor (S-40).

## 2026-09-21 — The classification form opens on answer, not at hang-up (A-40)

Dia, on being told slice 2 would open the form at hang-up: *"the form opens on
call accept not on hangup"*. Right, and the reason is the business, not the
software.

A-40 said "at hang-up" and that was written as if classifying were paperwork
done after the fact. It is not. **The agent is taking the order while the
customer is speaking.** The order value, the branch, the notes — those are the
call, not a summary of it. A form that appears once the customer has gone asks
the agent to remember what they were just told, which is slower and wrong more
often.

A-40 now reads: the form opens **as soon as the call is answered**, stays open
while the agent talks, and **stays on screen after hang-up until saved or
skipped** — so a call that ends mid-sentence does not take the agent's typing
with it. A-41 gains a sentence saying an untouched form is a skip, not a loss:
nothing is stored until the agent saves.

### The consequence that matters for the build

**During a call, the server has no record of it yet.** The call is logged when it
ends, so for the whole time the form is on screen there is no communication id
to attach a classification to.

That is exactly the situation the offline route was built for this morning —
`PUT /api/classifications/by-call`, keyed on the SIP Call-ID and the extension.
It was designed as the exception for a server that was down. It is now **the
normal path**, and the server-id route is the special case used when editing an
older call from the log.

Worth noting as a piece of luck rather than foresight: the endpoint exists
because Dia asked for offline working, and the design that answered that
question turned out to answer this one too. Had the form only ever opened at
hang-up, the id-based route would have looked sufficient and this change would
have needed new server work.

## 2026-09-21 — Classification, part 2: the form the agent fills in

The form is on screen and working end to end: three real calls classified by
`dia`, on form version 2, with Arabic notes and branches, from the pop-up
through the offline queue into the database.

It is built from whatever the supervisor has defined rather than written into
the screen — seven kinds of question, each its own small class so WPF picks a
template by type and nothing decides what to draw with a switch. A field the app
does not recognise is skipped with a warning rather than crashing the form: a
supervisor on a newer server must not be able to stop every agent classifying.

### Four bugs, all found by opening the screen

None of these would have been caught by any test worth writing, and all four
came from a single screenshot or one sentence from Dia.

**The queue deadlocked.** The worst of them, and it made saving silently do
nothing. The classification is queued **before** its call — obvious in hindsight,
because the agent saves while still talking and the call is not reported until
hang-up, so the classification always gets the lower id. The flush loop stopped
at the first refusal, so the classification sat at the front being refused for a
call that did not exist yet, blocking the very call it was waiting for. Two
calls and two classifications were stuck behind each other.

The commit that introduced it asserted the opposite in its own message: *"one
sequence guarantees the call goes first"*. It was wrong, and it was wrong
**because of the A-40 change made an hour earlier** — moving the form to the
answer inverted the order, and the queue was not rethought. A refused
classification is now skipped rather than stopped at, and one extra pass sends
it as soon as its call has gone.

**`ComboBox` had no dark style**, so Windows drew it light: a white list with
near-white text. Fourth control to fall into this hole after the contacts list,
the `DatePicker` and the calendar behind it — and this afternoon's entry had
named dropdowns as the likely next one and said to check them before handover.
They were not checked. Both `ComboBox` and `ComboBoxItem` are templated now;
`ToolTip` and `ContextMenu` remain unchecked.

**`IsVisible` collided with WPF's own.** The field's "does this apply to the
chosen type" flag was called `IsVisible`, and the item container bound its
Visibility to it — resolving to the container's **own** `IsVisible` and looping.
Notes vanished and the complaint field appeared when nothing was selected.
Renamed `AppliesNow`.

**Save was dead with no explanation.** It re-evaluated only when the type
changed, so choosing the branch — also required — never re-enabled it. Now every
field re-checks, and a line underneath names what is still missing, because a
disabled control that will not say why is worse than no control.

**A branch printed as `FormBranchDto { Id = 8f3c…, Name = ايكون }`.** The type
and select lists went through wrappers that print their label; the branch bound
straight to a record, and a record prints all of its contents. Second bug of the
day caused by one item in a list being treated differently from its neighbours.

### What the screenshots were worth

Two screenshots and two sentences found five defects in about forty minutes.
Every one of them was invisible to the build, the tests and the type checker.
That is now the fourth separate way this project has demonstrated the same
thing, and it is worth stating plainly in the record: **on this project, code
that compiles and passes its tests is evidence of very little. Open the screen.**

## 2026-09-21 — Classification, part 3: the supervisor's form editor (S-40)

The half of the morning's request that was still missing. The server could
already publish a form — it was done from a script that afternoon — but there was
no screen to do it from, so the questions agents answer were whatever had been
seeded.

### Types and questions on one screen

They are one decision, not two. A question asked only for complaints has to name
the complaint type, so editing them apart would mean holding one in your head
while changing the other.

### What the screen refuses to let a supervisor do

Each of these is a way to break the reports quietly, months later:

- **The type's key cannot be edited.** It is shown, greyed, beside the labels.
  Reports and every `showWhenType` rule are written against it; renaming is what
  the labels are for. Same rule S-41 states for branches.
- **A type in use cannot be deleted** — the row offers hiding instead, and says
  why. Only an unused, non-system type shows a Remove button.
- **The type and branch questions cannot be removed or retyped.** They have
  their own columns and the reports group by them; the server would refuse a
  form without a type field anyway, but a screen that offers something the
  server refuses is a screen that wastes the supervisor's time.
- **A field's key and a choice's value are generated, never typed.** Answers are
  stored against them, so a supervisor editing a label must not silently orphan
  every answer already given under it.

### Publishing, and undoing

Publish writes a new version; existing classifications keep theirs. The hint
under the button says so, and says the way back: publish the old one again. A
supervisor who has just broken the form for forty agents should not have to ask
what to do.

The button is disabled until something actually changes, and a Discard appears
beside it once it has — so a half-finished edit is one click from gone rather
than something to reload the page over.

### Tests

Five, all about what would corrupt data rather than what would look wrong: the
key is shown and not editable, a type in use offers hiding rather than deletion,
the built-in questions cannot be removed, publishing needs a change, and — the
one that matters most — **the show-when rule is written against the type's name,
not its label**.

## 2026-09-21 — Classification, part 4: the way back to a skipped call

A-41 lets an agent skip, and the call log has said "unclassified" on those calls
since it was built. Until now that chip pointed at work nobody could do: there
was no way back in. Double-clicking a row in the call log now opens the form for
it, the same gesture the contacts, menu and delivery lists use for their
editors.

### The form view model had to stop being shared

It was a singleton, held by the call pop-up. Giving the call log the same
instance would have meant **an incoming call wiping out whatever the agent was
typing in the log** — the pop-up calls `Begin` on answer, which rebuilds the
fields from scratch.

Split in two:

- **`ClassificationCatalog`**, shared, holds the one definition the supervisor
  published and fetches it at sign-in.
- **`ClassificationFormViewModel`**, one per screen, is a form being filled in.

One definition, several forms. Obvious once written down, and it was only
noticed because the second screen forced the question — with one screen a
singleton looked right.

### Two ways to save, for a real reason

A call **in progress** has no server id: it is keyed on the SIP Call-ID and
queued behind the call. A call **from the log** came from the server, so its id
is known and the classification goes straight there — which also means a refusal
can be shown to the agent immediately rather than failing quietly in a queue.

Those refusals now say what they mean: "this call can no longer be changed, ask
a supervisor" for a closed edit window (A-42), and "that call belongs to another
agent". The server was already returning both; nothing was reading them.

Saving from the log refetches the list, so the chip clears on the row it belongs
to rather than lingering until the next search.

## 2026-09-21 — Classification, part 5: opening a call that already has an answer

Three fixes, all found by Dia opening the screen, and the middle one was the
serious one.

**The form came out as a list of class names.** The field templates were defined
in `CallPopupWindow`'s own resources, so the call log — the second screen to draw
the same form — could not see them, and an `ItemsControl` with no template for
its items falls back to printing the type name. Moved into `Theme.xaml`. An hour
earlier the *data* had been split to be shared and the *drawing* was left behind;
this was the other half of one job. **A form drawn in two places belongs to
neither of them.**

**An already-classified call opened blank.** Worse than unhelpful: pressing Save
would have replaced a real answer with nothing, and the agent would have had no
way of knowing — the audit trail would have faithfully recorded them wiping their
own work. The form is now fetched and prefilled before it is shown.

Three details that came with it:

- A call the agent may no longer edit (A-42) opens **read-only**, showing what it
  says, with the reason. Before, they would have filled the whole form in and
  found out on Save.
- A type the supervisor has since **hidden** is still shown on the calls that used
  it. Blanking it would misrepresent what the agent recorded.
- An answer to a question the supervisor has since **removed** has nowhere to go
  and is skipped, rather than throwing.

**Changing the type crashed the app.** `BoolToVisibility` was declared in
`App.xaml` *after* the merged theme, and a `StaticResource` can only see what has
already been declared — so the templates inside the theme pointed at nothing. It
only fell over when one of those templates was first drawn. The converters now
live at the top of `Theme.xaml`, above everything that uses them.

Second crash today from moving code without moving what it depends on; the
calendar styles this afternoon were the same shape. Both were invisible to the
compiler, because XAML resolves resources by name at runtime. **For this app,
"it builds" says nothing about whether it runs.**

`b8fd8f7` was pushed carrying a "NOT YET VERIFIED BY HAND" line in its own
message, because Dia asked for the push before testing it. **Verified by hand
shortly after** — a classified call opens filled in, and changing the call type,
the step that crashed, holds. The caveat in that commit message no longer
applies; this entry is the record of it.

With that, classification (A-40 to A-43, S-40) is complete and every part of it
has been seen working: the form during a call, the supervisor's editor, and
classifying or editing from the call log.

## 2026-09-21 — What comes next, reordered: the phone before the archive

I proposed call recording as the next feature, on the grounds that it is the
biggest unbuilt Must. Dia asked what about mute, hold and outbound dialling. The
right question, and the order changes.

Weigh what each gap costs on a shift today:

| Missing | Cost now |
|---|---|
| **Hold** (A-12) | An agent who needs to check something must keep talking or hang up |
| **Mute** (A-12) | Everything in the room goes down the line |
| **Dial** (A-20) | **An agent cannot phone a customer at all** — no callbacks, no returning a missed call |
| **Recording** (A-30) | A supervisor cannot review calls — and could not today regardless, because there is no screen to play them on |

The first three are missing every shift. Recording is missing for someone who has
no way to reach a recording either way, since the supervisor app still has no call
search (S-02, S-03).

**They also live in the same file.** `CallService.cs` holds the call controls, the
dialling that does not exist yet, and the media session where recording's audio
tap has to go. Going in with small, testable changes first — and learning that
code — beats opening with the riskiest one. The SIP layer already cost three bugs
and a day to get right the first time.

**Order from here:** mute and hold (A-12), then outbound dial and click-to-call
(A-20), then recording (A-30 to A-33), then the supervisor's call search (S-02,
S-03) — which is what makes recordings reachable at all, and so belongs beside
recording rather than long after it.

Redial and blind transfer (A-22, A-15) are **Should**, not Must, and can follow.

## 2026-09-21 — Mute (A-12): done in the microphone, not in SIP

The first of the two missing call controls. Mute is the smaller one, and it goes
first deliberately: it is the safest way into `CallService.cs`, the file that
took a day to get right, before Hold touches the SIP dialogue itself.

**Mute is not a SIP feature.** A desk phone that is muted simply stops feeding
the microphone into the stream; the phone system is never told. The same here:
the microphone object SIPSorcery gives us has `PauseAudio()` and `ResumeAudio()`,
and mute is exactly those two calls. The customer's audio is untouched, so the
agent keeps hearing them — which is the point of mute over hold.

**One difference from a desk phone, noted rather than fixed.** A paused source
produces no samples at all, so no RTP leaves the laptop while muted. A desk phone
keeps sending frames of silence. Asterisk only cares if its RTP timeout is
switched on, which it is not by default; if a long mute ever drops a call on the
real PBX, the change is to send silence instead of pausing. Tested by muting for
two minutes, on the checklist.

**Muted is a flag on a connected call, not a new status.** `CallState` gains
`IsMuted`; `Status` stays `Connected`. Everything that asks "is the call
connected" — the timer, the form opening on answer, the outcome at hang-up —
keeps working without knowing mute exists. The flag dies with the media session,
which is created per call, so it cannot leak into the next one.

**The state is shown where the agent looks.** The status line at the top reads
"Muted" instead of "Connected" for as long as it lasts. The button reads the
*action* — Mute, then Unmute — not the state. An agent who has forgotten they are
muted is talking to nobody, and a small pressed-looking button is not enough to
tell them.

**Unmute is automatic at the end of the call**, and there is no remembered mute
across calls: a customer who rings next should never be greeted by silence
because the last call ended muted.

Hold follows in its own commit, with its own test call, because it sends a
re-INVITE and is the first thing in this app to change a call after it is
answered.

---

## 2026-09-21 — Hold (A-12): the first re-INVITE

The second call control, and the first time this app changes a call after it
has been answered. Mute never spoke to the PBX; Hold does.

**What a hold is on the wire.** A second INVITE inside the same call — a
*re-INVITE* — carrying a media description marked `a=sendonly`: "I will send but
not receive". That is the standard way (RFC 3264) and every real PBX understands
it. Asterisk, which Issabel runs on, reads it as hold: it plays its hold music to
the customer and stops sending us their voice. Resume is another re-INVITE with
`a=sendrecv`. SIPSorcery does both in `PutOnHold()` and `TakeOffHold()`, so the
change in `CallService` is small; the risk was never the size of the code but
that it is the first message sent into a live dialogue.

**The microphone is paused too.** `sendonly` still allows sending, and SIPSorcery
keeps transmitting the microphone during a hold. Asterisk ignores it, but the
room's audio should not leave the laptop while the agent believes nobody can
hear. On resume the microphone comes back **only if the agent had not muted
before the hold** — mute and hold are separate flags, and a mute survives a hold.
Pressing Mute while on hold flips the flag alone; the microphone is paused
regardless, and Resume reads the flag to decide.

**The PBX's answer is not waited for.** The re-INVITE is sent and its response
handled inside SIPSorcery, on another thread, so the app cannot know at the
moment of pressing Hold whether the PBX accepted. The state is shown as on hold
at once. If the PBX ever refused (a 488), the library logs it and the call
carries on unchanged, and the pop-up would be wrong until Resume is pressed.
Accepted rather than engineered around: hold is in the SIP standard, a PBX that
refuses it is misconfigured, and the log is where that would be found. This is
one of the two risks named before the work started and the test call is what
retires it.

**Remote hold is logged, not shown.** The PBX can hold *us* — it does during a
transfer — and SIPSorcery raises an event for it. The pop-up has nothing to
offer the agent about a hold they did not choose, so it is a log line only.

**Status line priority: on hold, then muted, then connected.** Hold is the larger
fact — nobody hears anybody — so it wins. The buttons read the next action, Hold
then Resume, as Mute does.

**Hang up while on hold** needs nothing special: the BYE goes inside the same
dialogue whether or not a re-INVITE preceded it. The other side hanging up while
on hold is the same BYE as ever. Both on the checklist regardless, because "needs
nothing special" is a claim about the library and not about this PBX.

**Both worked first time on a real call to 2001** — mute, hold, hold music,
resume. The 488 refusal and the RTP-timeout drop, the two risks named before the
work started, did not happen. Hold is in the SIP standard and Issabel is
configured normally; the caution was worth having and cost nothing.

### And the screenshot found a fifth thing, again

The pop-up's **window title was a fixed "Incoming call"** for the whole life of
the call, so the title bar and the taskbar button announced an arriving call
while it had been connected, and muted, for over a minute. Bound to the status
line now, so it reads Incoming call, Connected, Muted or On hold like the card
does.

Nothing could have caught this but a human looking at the window: it compiles, it
has no test that could fail, and the card in the middle of the screen was right
all along. **That is five defects from three screenshots on this project.** The
rule stands — when UI work is finished, ask for a screenshot before committing.

## 2026-09-22 — Outbound dialling (A-20, A-21), part 1: placing the call

Until today the app could only answer. An agent who needed to ring a customer
back had to use a desk phone or their own mobile, and nothing about that call
reached the system.

**Dialling is its own state, not a reuse of Ringing.** `CallStatus.Dialling`
sits beside `Ringing` and `Connected`. Reusing Ringing would have been fewer
lines and would have put **Answer and Reject on screen for a call the agent
placed**, which is nonsense the first time an agent sees it. A call being placed
offers one thing: Cancel.

**Cancel is not Hang up.** A call still being set up has no dialogue, so there is
no BYE to send — SIPSorcery's `Hangup()` is documented as ending an *established*
call. It is `Cancel()`, which sends a SIP CANCEL. Getting this wrong would have
left the PBX ringing a customer nobody was waiting for, and it would have looked
like it worked from inside the app.

**The number is sent as it is held.** What Issabel wants dialled is a dialplan
question that cannot be answered from the code: a Palestinian mobile might want
`0569498581`, or the country code, or a digit in front to reach an outside line.
Every wrong guess fails identically, with a 404 and no ring. So the app strips
everything that is not a digit — a number displayed as `059-949-8581` is not
routable — puts a configurable prefix in front, and sends that.
`Dialing:Prefix` in `appsettings.json`, empty by default. **This is the one
thing about outbound calls that a test call had to settle rather than a
review**, and it is now a question for the provider below.

### NoAnswer, and why it was worth a migration

An outbound call nobody picks up needed a status. The obvious choice was the
existing **Missed**, and it would have been wrong.

Missed means *a customer rang this call centre and nobody answered*. It is the
service failure the supervisor's reports exist to count. A customer who was out
when an agent rang them is not that, and recording the two as the same thing
would have quietly made the most important number in the system meaningless —
and it would have looked correct in every test, because it is a valid value in a
valid column.

So `NoAnswer` is a new status, which cost one migration to widen a CHECK
constraint. **Failed** keeps its own meaning: the call could not be placed at
all. The difference is not pedantry — it is what tells an agent whether trying
again is worth anything, which is exactly what A-22's call-back needs.

Dia was asked and chose the migration over reusing a value.

**Two guard tests caught what the compiler could not.** The status list is tied
to a C# enum and to the database constraint by tests in `CallCenter.Shared`, and
both failed the moment the string was added and the enum was not. That is the
first time on this project that a test found something before a screenshot did,
and it is worth saying so: the guard was written by someone who had been bitten.

### What is deliberately not in this commit

**Click-to-call from the call log and from contacts** (the rest of A-20) is the
next commit. Dialling itself had to be known to work against the real PBX first;
adding buttons that call code nobody has proved is how a small failure arrives in
three places at once.

**Dialling a blocked number is allowed.** A-17 is about calls *arriving*. An
agent ringing a blocked customer is far more likely to be resolving the complaint
that got them blocked than making a mistake, and a phone that silently refuses to
dial would be worse than one that does as it is told.

**"Any phone number shown in the app"** is read as the places a number is
actually shown today: the call log, a contact, and the dial box. Making every
piece of number-shaped text clickable is a larger and vaguer job with no user
asking for it.

## 2026-09-22 — The first outbound test call: the app was right, the PBX refused

Dia dialled his own mobile. Three things came back, and only one of them is ours.

### The number format was never the problem

The expectation was a 404 and an argument about dialling rules. Instead:

```
10:43:42 Dialling sip:0569498581@192.168.0.27
10:43:43 The far end is ringing: "SessionProgress"
10:43:50 The outgoing call did not connect: "ServiceUnavailable"
```

The PBX **accepted the number**, answered 183 Session Progress and started early
media, then gave up after eight seconds with 503. So the dialplan understands
`0569498581` perfectly well and `Dialing:Prefix` stays empty.

**503 after early media is an outbound route that did not complete** — no trunk,
a trunk that refused, or extension 2001 not permitted to use one. Those eight
seconds of early media are almost certainly Asterisk *saying the reason out
loud*, so the next test call should be made with the speaker up: the PBX is
likely announcing what is wrong before it hangs up.

This is a PBX question, not an app one, and it is now question 3c for whoever
administers Issabel.

### Cancel works

```
10:44:06 Dialling sip:00970569498581@192.168.0.27
10:44:11 SIP OUT "CANCEL"
10:44:11 The agent gave up on the outgoing call
10:44:11 Call finished: nobody answered
```

CANCEL rather than BYE, logged NoAnswer, one report. Exactly as designed.

### And a bug of mine, which only an outgoing call could have found

```
10:43:50.674 Call finished: the PBX answered ServiceUnavailable
10:43:50.674 Call finished: the caller hung up
```

**The same call was finished twice, in the same millisecond**, and so reported
twice. The server refused the second copy on `ux_comm_sip_call` and the agent's
log showed a 500 from a call that had otherwise behaved.

A failed outgoing call raises two things at once: the failure that `DialAsync`
is waiting for, and a hang-up from SIPSorcery. `Finish` guarded against that
with "return if the call is already idle" — but it marked the call idle at the
*end* of the method, after reporting. Both threads read "not idle" before either
wrote it, so both went through. The guard read like a lock and was a suggestion.

Fixed by claiming the call inside the same lock that reads it: the state goes to
Idle there, and whoever arrives second returns having done nothing.

**This existed before outbound calls and could not be reached.** Inbound has one
path in: either the agent hangs up, which sets a flag that silences the library's
event, or the caller does, which raises only that event. Adding a second way for
a call to end turned a latent race into a reproducible one — and it took a real
PBX refusing a real call to produce it, because it needs two endings inside the
same millisecond.

Two flushes of the upload queue also ran concurrently, which is how the duplicate
reached the server rather than being absorbed. That the queue can be flushed
twice at once is a **separate** weakness, related to the unresolved concurrency
item below, and is not fixed here.

## 2026-09-22 — "Reject does not work", and the one message the log never showed

Dia reported that Reject does nothing. The log says otherwise, and the
interesting part is what it could not say.

```
11:51:20.918  SIP IN  "INVITE" from 0569498581
11:51:26.103  Call ended: rejected by the agent
11:51:26.110  SIP IN  "ACK"
```

**The app did reject it.** An ACK arrives only in answer to a final response, so
one went out. The call was reported once, with status Rejected, and the server
took it.

**But the response itself is invisible.** The trace wired up
`SIPRequestIn`, `SIPRequestOut` and `SIPResponseIn` — and not
`SIPResponseOut`. So every message in the conversation is logged except the ones
this app sends in reply: the 180 Ringing, the 200 OK on answer, and the 603
Decline on a reject. The one message the complaint is about was the one message
with no line in the log, and the only evidence it existed at all was the PBX
acknowledging something.

That is now fixed — three lines — and the docstring that claimed to log "every
SIP message in and out" is true again. **This is the second time on this project
that a missing log line, rather than a bug, was what cost the time**; the first
was the trace itself being absent when a call never arrived.

### What is probably actually happening

Rejecting is not the same as getting rid of the caller, and with a queue it
usually is not. `Reject` sends **603 Decline**, chosen for A-17 because 6xx is a
global failure that asks a proxy to stop trying anywhere. What Asterisk does with
it for a **queue** call is its own business: the normal behaviour is to keep the
customer in the queue and offer them to the next available member — which, with
one agent, is the agent who just declined. From that seat it looks exactly like a
button that does nothing.

Two things follow, and neither can be settled without one more test call now that
the response is logged:

1. **Is 603 the right answer for an agent reject at all?** 603 says "do not try
   anywhere else", which for a queue arguably means hanging up on a customer who
   has been waiting. **486 Busy Here** says "not this extension, try another",
   which is what a call centre with more than one agent would want. The reasoning
   for 603 is written down for *blocked* callers (A-17) and was never revisited
   for A-12's Reject — the two were given the same answer because the same method
   sends both.
2. **What the caller should hear after a reject is a dialplan decision**, exactly
   as it is for a hang-up. The app can only choose which refusal it sends.

Nothing is changed in what Reject sends yet. Guessing at a SIP response code
against a live PBX, with no log of what was sent, is how the last three telephony
bugs happened.

## 2026-09-22 — The pop-up finally says who is calling (A-10, A-16)

Until today an agent answering saw a phone number and nothing else, which is
most of what the pop-up exists for. The lookup was the oldest open item on the
list and everything it needed was already built: `FindContactByPhoneAsync` has
been sitting in the Agent App's API client since the contacts work, with a
comment saying "the lookup the incoming-call pop-up will use", and nothing ever
called it.

What is on the pop-up now: the **VIP badge and its reason** at the very top
(A-16), then the queue, the number, the **customer's name** at the size of a
name rather than a footnote, their **address**, and their **notes** in a box of
their own. The name the PBX sent hides itself once the real contact is known —
two names for one caller, one of them whatever the switch felt like sending, is
worse than either alone.

### "New customer" and "could not check" are not the same thing

The one real decision here. The API client folded every non-2xx answer into
`ServerError`, so a 404 — *this number belongs to nobody* — was indistinguishable
from the server being unwell.

Left alone, that would have put **"New customer"** on the screen whenever the
server was down, in front of an agent talking to a regular. The agent types the
customer in again, and now there are two records for one person, each with half
a history. That is not a display bug; it is data loss that takes a merge (A-63)
to undo.

So `ApiStatus.NotFound` is now its own thing, and the pop-up says one of three
things: **looking**, **not on file**, or **could not check**. Never blank, and
never the wrong one of the three.

### It fills in underneath the call, and never holds it up

The lookup starts when the call appears and is not awaited. The number, the
queue and the Answer button are on screen from the first moment whatever the
server is doing — A-04 requires the phone to work with the server unreachable,
and a ringing phone does not wait for HTTP. A failure is a log line and a
message, never an exception that reaches the pop-up.

**The lookup is cancelled when its call goes away.** Without that, a slow answer
for the last caller lands on the next caller's pop-up and the agent greets
somebody by a stranger's name. Rare, horrible, and almost impossible to
reproduce on purpose, which is why it is handled rather than waited for.

### Outbound calls get it too

A-10 says "on an incoming call", but the same lookup runs when the agent dials
out (A-20): knowing who you are ringing is not less useful than knowing who is
ringing you, and it is the same code path. Recorded here because it is slightly
more than A-10 asks for.

### And then the rest of it: the history and the totals

A-10 asks for more than a name — the **last five communications with type and
notes**, and **totals** for orders, complaints and cancellations. Both are in
now, under the name.

**A new endpoint rather than the existing history one.**
`GET /api/communications/by-contact/{id}` already returns a contact's calls, and
it was not enough: `CommunicationDto` carries no classification, and A-10 asks
for the type and the notes. Those are the whole point. "Three calls last week"
tells an agent nothing; "complaint, cold food, unresolved" tells them how to
open their mouth. So `GET /api/contacts/{id}/card` returns the three counts and
the last few calls, each with what it was about and what the agent wrote.

**Two requests, not one.** The name, address and VIP badge come back from the
small, cheap lookup and appear first; the card joins classifications and fills
in behind them. A single combined endpoint would have been one round trip and
would have made the agent wait for the slower half to see the customer's name,
which is the one thing they need before they speak.

**The counts are of classified calls only, and they only ever understate.** A
call nobody wrote up is not an order that did not happen; it is a call nobody
wrote up. Counting it as anything would invent history. A customer with nothing
counted shows **no totals at all** rather than three noughts, because three
noughts read as a verdict on the customer instead of on our records.

**An unclassified call still appears in the list**, labelled "not written up".
Hiding it would hide the fact that somebody did not do the paperwork, and that
is worth an agent seeing.

**Direction is not filtered.** A customer this branch rang and one who rang in
are the same relationship, and an agent about to speak wants the last five times
anybody spoke to them (A-21). Outbound rows are marked.

### Two things the language-file check caught

`NoAnswer` went into the statuses this morning **with no label in either
language file**, so the call log would have shown a missing key the first time
an outbound call was not answered. Added. And the history rows needed a label
for an unclassified call. Both found by the key-parity check between `ar.json`
and `en.json` — the throwaway script that DECISIONS has been complaining about
not being part of the build. It has now earned its place twice.

### Not in this commit

**The inline "new customer" form** (A-11) is its own piece: the pop-up now says
a number is not on file, and cannot yet do anything about it.

## 2026-09-22 — "Why is 10:44 above 16:40?" — the grid was sorting itself

Dia sent a screenshot of the call log with one row visibly out of order, and
separately that **the refresh button sometimes did nothing and only signing out
and back in showed recent calls**. Two complaints, one cause.

**The data was never wrong.** Checked against PostgreSQL directly: every row is
stored correctly and the server's query orders by `started_at` descending on the
index built for exactly that. The server would have returned them newest first.

**WPF makes every column header a sort button unless told otherwise**, and
`CanUserSortColumns` had never been set, so it defaulted to true. One click on
the Number header — easy to do by accident while reaching for a row — sorted the
grid by number. And since every column here binds to a **formatted string**, not
a value, that sorted the *text*: `00970569498581` sorts above `0569498581`,
because the third character is `0` against `5`. Every other row carried the same
number, tied, and kept its newest-first order underneath. Which is precisely the
screenshot: one outbound call on top, everything else in time order.

Sorting by the time column would have been no better. `"21/09 21:00"` and
`"16:40"` compared as text put yesterday above today.

**And the sort survives a refresh**, because it lives on the collection *view*
rather than the collection. `Calls.Clear()` and re-add does not touch it. So
every later refresh quietly dropped its new rows into that old order — the
refresh worked perfectly and looked broken. Signing out and in built a new grid,
which is the only thing that cleared it. That is the whole of "I have to log out
and log in".

Sorting is now off on this grid. Not a simplification: the list is **the last
100 calls, newest first**, chosen by the server, and re-sorting a truncated page
presents a ranking of the page as a ranking of everything.

### The second half: a cancellation source disposed underneath its own run

Hunting the first one turned up a real intermittent fault beside it. `Refresh`
replaces a slower in-flight request with a newer one — correct, and there for a
good reason — but it **disposed** the previous `CancellationTokenSource` the
instant it cancelled it. The run that owned that source was still inside the
method and about to read `current.Token`, and reading `.Token` on a disposed
source throws `ObjectDisposedException` — which is not an
`OperationCanceledException`, so it sailed straight past the catch written for
exactly this moment.

Each run now disposes its own source, and the token is read once at the top,
before anybody can take it away. This one fires only when two refreshes overlap,
which is why it was "sometimes".

### The same trap is in three other grids

Contacts, menu and delivery all bind formatted strings to sortable columns over
a server-truncated list. Nobody has reported it and it is the same defect.
**Left for a commit of its own** rather than folded in here, because the fix
belongs in the DataGrid style in `Theme.xaml`, which another session is editing
right now.

## 2026-09-22 — `callLog.unclassified` on a chip, and the guard that should have existed

Dia's screenshot showed the call log's "Not classified" chip reading
**`callLog.unclassified`** — the key, not the label — and said it was happening
in more than one place.

**Why it showed the key.** `Localizer` is built to do exactly that. When a label
is missing it writes a warning and returns the key itself, so the app carries on
and the agent sees something rather than nothing. Right at runtime, and the
reason nothing else ever caught it: the compiler cannot see a JSON key and the
type checker cannot see a XAML binding. A missing label is invisible to every
automated check this project had, and visible to anyone who opens the screen.

**Why it was missing.** The list of last calls was removed from the pop-up
earlier today (entry above), and with it its label, `caller.unclassified`. The
call log's chip uses `callLog.unclassified` — same last word, different section,
nothing to do with the list — and it went too. Removing one label took a
same-named neighbour with it. The DECISIONS entry for that change lists what was
removed and this key is not on it, so it was collateral, not intent.

**"More than one place."** Today's log has the warnings, and there are exactly
two keys that ever rendered raw: `callLog.unclassified`, 38 times, and
`callLog.status.NoAnswer`, 22 times. The second was mine — `NoAnswer` went into
the statuses this morning with no label in either language, fixed this afternoon
— and the running app in the screenshot predates that fix. Both are the same
class of defect: a label file that drifted from the code that reads it.

**This is the fourth time.** The key-parity check has existed since the redesign
as a throwaway script, and this file has asked three times for it to be in the
test project. It now is — `AgentAppLabelsTests` in `CallCenter.Shared.Tests` —
and it checks more than parity:

- Arabic and English carry the same keys.
- No label is blank in either language.
- **Every label the app asks for exists**, by scanning the Agent App's XAML and
  C# for `Localizer[section.key]` references. A dot is required in the key,
  which is what tells a literal apart from a variable such as
  `Localizer[StatusKey]`.
- Every value in `CommunicationStatuses.All` has a `callLog.status.*` label,
  because the call log builds that key at runtime and the scan cannot see it.
  This is the check that would have caught `NoAnswer` this morning.

**The guard was seen to fail before it was trusted.** The label was removed on
purpose, two of the four tests failed naming `callLog.unclassified` and the file
that asks for it, the label was restored, all four passed. A guard that has only
ever passed proves nothing — the same reason a screenshot is asked for after UI
work.

The test reads the app's source tree, which a unit test normally would not. It is
a check on data files rather than code, and the alternative was a fifth incident.
The two remaining throwaway checks — every `StaticResource` a view names exists,
and every control a view uses has a style — are still scripts, and are the ones
that would have caught the white contacts list.

## 2026-09-22 — The last column arrived cut off, and only a resize fixed it

The call log's "Not classified" chip came up clipped to "Not cl". Widening the
window and pulling it back fixed it, and it was wrong again on the next start.
"Wrong until something forces a second layout" is a shape worth recognising: it
is almost never the thing that looks wrong.

**A star-width column cannot be measured against infinite space.** The theme
gives every DataGrid `HorizontalScrollBarVisibility="Auto"`, which lets the
grid's own ScrollViewer offer unlimited width on the first layout pass. The
"Customer" column is `Width="*"` — a share of what is available — and there is
no share of infinity. So it could not be resolved, the columns after it were
laid out against a width nobody had settled, the total came out wider than the
card, and the rightmost column was the one pushed over the edge. Dragging the
window forced a second pass with a real width, which is why resizing appeared to
repair it.

It was never the chip, and never the label — the label was a separate fault
fixed an hour earlier, and this one had been waiting behind it.

**Disabled, not Auto, for this grid.** The columns now have to fit the space
that exists, so the star column yields instead of the last column being pushed
out. Two `MinWidth`s keep that honest: the chip always has room for its longest
label in either language, and Customer cannot be squeezed to nothing paying for
it.

### Where this really belongs, and why it is not there yet

The setting is in the shared DataGrid style in `Theme.xaml`, and **Contacts has
exactly the same shape** — two `Width="Auto"` template columns of flag chips
sitting after star columns. Nobody has reported it and it is the same defect
waiting.

The one-line fix in the theme would settle every grid at once. It is not in this
commit because another session has `Theme.xaml` open for the do-not-disturb
work, and quietly changing a file somebody else is editing is how the menu
pictures got swept up this morning. **Left as its own commit, deliberately**,
for the moment that session is finished.

### The pattern, for next time

Three faults today were invisible to the build, the tests and the type checker,
and every one showed itself the moment somebody looked at a screen: the window
title that never changed, the grid that sorted itself, and this. The count of
defects found by screenshot on this project is now eight.

## 2026-09-23 — CI gets a database, because one test always needed one

CI went red on `A_missing_picture_is_a_404_rather_than_an_empty_body`: 500 where
the test wanted 404, after a 32-minute run.

**It was never about pictures.** Before the endpoint can decide a picture is
missing it looks the item up in the database. CI has no database, the query
cannot connect, and the request dies as a 500. The picture code is fine and does
return 404 for a genuinely missing file.

**It passed locally for the worst possible reason:** Docker happened to be
running on the developer's laptop. Reproduced both ways before changing
anything — the same test fails in 73 seconds against an unreachable database and
passes in 4 seconds against a live one. CI's failure took 1m25s, which matches.

**And that is where the 32 minutes went.** The connection is configured with
`EnableRetryOnFailure`, which is right on a real server where the database might
be restarting, and pure cost here: every test that touches the database waits
out the full retry schedule before failing. The run was mostly waiting for
something that was never coming.

### Two jobs where there was one, and the split is forced

GitHub can attach a database to a job for its lifetime, **but only on Linux** —
service containers do not run on Windows machines. The Agent App is
`net10.0-windows` and builds nowhere else. So:

* **build (Windows)** — `dotnet build CallCenter.sln`. Its only job is to prove
  the Agent App still compiles.
* **test (Linux)** — a `postgres:16` service, and the two test projects by name
  rather than the solution, because the solution contains the Agent App.
  Neither test project references it, which is what makes this possible at all.

A health check on the service is not decoration: without it the steps can start
before PostgreSQL is accepting connections, and the first test to touch it fails
for a reason that has nothing to do with the code. That is a worse failure than
the one being fixed, because it is intermittent.

### The schema, which is the part that is easy to miss

The database GitHub provides is **empty**. The test host had migrations turned
off — correctly, while there was never a database — so pointing it at a fresh
PostgreSQL would have replaced "cannot connect" with "relation does not exist".

`CallCenterApiFactory` now runs migrations **when, and only when, a connection
string is present in the environment**, by the same configuration key production
uses. No connection string, no migrations, and the suite behaves exactly as it
always has. That keeps one switch rather than a test-only flag that could drift
from what the server actually does.

### Verified, not assumed

Against a throwaway database created empty for the purpose, so nothing of the
developer's own was touched: 21 tables and 9 migrations built from nothing, all
236 server tests and all 124 shared tests green, then the database dropped. The
workflow itself was parsed rather than eyeballed, and nothing depended on the
old job name.

### What this does not fix

**A laptop with no PostgreSQL at all still fails those few tests**, exactly as
before — unchanged, not improved. The standing claim that "the suite runs
without PostgreSQL by design" was already untrue for a handful of endpoint tests
that reach the database before they can answer; CI is now the place that runs
them honestly. Making them skip themselves when no database is reachable is a
separate, smaller job.

**The four gaps this unblocks are still gaps.** Login, call logging, the contact
flags and the classification write path all have "no database-backed test" next
to them in the list below, and the reason given each time was that CI has no
database. That reason has gone. The tests themselves still have to be written.

## 2026-09-24 — Call recording, part 1: capturing the audio (A-30, A-32)

The table, the entity, the folder in the deployment, the backup that copies it
and the 90-day setting have all existed for days. Not one line of code read any
of them, and nothing tapped the audio. It looked far more finished than it was.

Capture is now written. Nothing is uploaded yet, and **no real call has been
through it** — the PBX is down. Committed anyway rather than left loose, and
flagged here so nobody mistakes it for proven.

### Both voices, on separate channels, in one file

Dia chose this over mixing them, which would have halved the disk. One stereo
file per call: **customer on the left, agent on the right**. It plays as an
ordinary conversation, and a supervisor reviewing a complaint can turn one side
down when the two talked over each other — which is what the calls worth
reviewing sound like. Mixing throws that away permanently, and a complaint is
exactly where somebody later disputes who said what.

**Stored as the phone system sends it.** The audio arrives as G.711, so it is
written as G.711 inside a WAV container rather than expanded to plain PCM. Same
bytes, same quality, half the size, and it still opens in anything. About 0.9 MB
a minute, so 90 days of four agents lands near 50 GB — bounded, and the number
to size the disk on. Compression would cut that fivefold and costs a dependency
and a conversion per call; better measured than guessed.

### What is recorded is what crossed the line

The customer's side is taken from the network frames, not from the speaker, so
it is what arrived rather than what the laptop managed to play. The agent's side
is taken from the microphone feed the call itself is using — raw samples, which
state their own rate, where the encoded event does not.

That choice makes mute and hold honest for free. Both pause the source, so
nothing arrives, so those stretches record **silence rather than the office**.
A recording that captured the room while the agent believed they were muted
would be a genuine privacy failure, not a bug.

### The two channels are kept in step by padding, not by hope

Audio only arrives while somebody is speaking. A hold, a mute, or ten seconds of
listening all produce a gap in one direction, and without something to fill it
the next thing that person says would be heard on top of what the other party
said ten seconds earlier. Each channel is padded with silence when it falls more
than 200 ms behind the clock.

**G.711 silence is 0xFF, not zero.** Zero is a loud buzz. That is the sort of
thing discovered by playing back a call that was on hold, and it is written down
here so nobody has to discover it twice.

### A-32 held hardest

Every way in is wrapped. A full disk, a file that will not open, a codec nobody
expected — each costs the recording and nothing else. A write that fails logs
once and never again, because a full disk will not empty itself mid-call and a
line per frame would bury the log. A half-written file is deleted rather than
attached, because a recording that exists and plays as noise is worse than an
honest absence.

### Verified synthetically, because a bad audio file is silent about it

A malformed WAV is a nasty failure: the file exists, the size looks right, and
it simply will not play. So the recorder was driven with a 400 Hz tone on one
side and 800 Hz on the other, and the result read back the way a player would.
Every header field correct, and both tones returned at full strength on their
correct channels — 399 Hz left, 799 Hz right. Silence, swapped channels or a
wrong sample rate would each have shown up.

That is not the same as a real call, and the checklist says so.

### Still to come

Upload and attach to the call record (A-31), the retention job (A-33), the
endpoint that serves a recording (S-04) — and the supervisor's call search
(S-02, S-03), without which there is nowhere to play one from. The upload half
can be built and tested with a generated file while the PBX is down; only the
capture needs a phone.

**Recording and classification do not link to each other.** Both hang off the
call, named by the same SIP Call-ID the classification already uses. That pair
has worked since classification shipped, so the recording rides a road that is
already proven.

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

## 2026-09-22 — The pop-up drops the list of last calls (A-10)

Later the same day, with the pop-up running against real numbers, the client
asked for the **"Last few calls" list to go**, leaving the caller card above it
(number, name, address, notes, the three totals) and the classification form
below.

Their screenshot made the case better than the argument for the panel did. Five
rows, every one "Not written up — Missed" or "Not written up — Rejected", each
row taller than the counts above it, and the classification form pushed below
the fold. In practice most of a customer's history is calls nobody classified,
so the list was mostly telling the agent about gaps in our own records while
costing them the form they actually have to fill in.

**What changed.** The `ItemsControl` and its heading are out of
`CallPopupWindow.xaml`; `CallerViewModel` no longer keeps a `Recent`
collection; `CallerHistoryRow` is deleted; the `caller.recent` and
`caller.unclassified` strings are gone from both language files. The SRS row
for A-10 is amended rather than rewritten, so the original ask stays on record.

**What did not change, and why.** `GET /api/contacts/{id}/card` still returns
the last few calls in `CallerCardDto.Recent`, and the server-side join that
builds them stays. The app simply ignores that part of the response. Removing
it would have meant touching the shared contract and the server tests for a
list the client may yet want back somewhere quieter — a "history" tab, say, or
the contact's page in the admin panel. Dropping a field from a response is
cheap later; putting one back is a redeploy of both halves.

**Open.** Whether the notes from past calls belong anywhere on the pop-up. The
argument for them ("complaint, cold food" tells the agent how to open) has not
gone away; it just lost to the form. If the client wants them back, one line
showing only the most recent *classified* call's type and note, with the
missed and unwritten ones filtered out, would carry most of the value in a
tenth of the height.

## 2026-09-22 — The old system's 15,000 customers ship inside the server

The shop has been taking orders for two years, and the customer book from the
old ordering system is the most valuable thing it has: a call from a number
nobody recognises is a call where the agent asks for the address again. So the
export — `docs/Contacts.xlsx`, 15,358 rows — is now part of the seed. A server
installed from scratch answers its first call with the customer's name already
on the screen.

**Where the data lives.** A gzipped CSV, embedded in `CallCenter.Server.dll` as
`Data/Seed/Contacts/contacts.csv.gz`. 393 KB, against 3.2 MB of plain CSV and
11 MB of spreadsheet. This is the same choice as the menu photographs and for
the same reason: installing the server is one image and nothing beside it, so a
file that has to be copied to the right folder before seeding is a step that
will be forgotten at two in the morning.

Nothing in the server reads Excel. `tools/contacts-import/convert.py` does the
conversion once, by hand, and its output is committed — the alternative was a
spreadsheet parser as a permanent dependency of the API for something that runs
once per installation.

**What was kept, and what was dropped.** Five columns of fifteen: name, both
numbers, address and the shop's note. The order statistics the export carries
(first order, last order, number of orders, totals) have nowhere to live in this
schema, and inventing a place for numbers that stop being true the moment the
new system takes a call would be worse than losing them. The `BL` and `Reason`
columns are empty in every row of this export, so nothing arrives blocked.

The city was its own column and this schema has no such field, so it is appended
to the address: `الماصيون - رام الله`. 1,369 of these customers are in
Jerusalem, and an address that does not say so is not deliverable. Known
misspellings are folded to one form each — `Ramallah`, `رامالله`, `را م الله`
all become `رام الله` — and anything unrecognised is passed through as the shop
typed it rather than guessed at.

**The numbers go through `PhoneNormalizer`, at seed time, in C#.** The export
stores `598214351`; the PBX announces `+970598214351`. Had the converter done
the normalising there would be two implementations of the rules — one in Python
for these rows, one in C# for every number an agent types — and the day they
disagreed, caller matching would quietly stop finding the oldest customers.
Confirmed against a real database: a lookup on `last9` for `+970598214351`
returns the contact the first exported row describes.

**Duplicates.** `ux_contact_phones_normalised` refuses the same number twice,
and the export has 110 repeats — the same customer entered once in Arabic and
once in English, `Adam Sadaqa` and `ادم صدقة`. The seeder drops the repeated
number rather than failing the install. When that leaves a row with no number at
all, and 69 rows are like that, the row is skipped: it is a duplicate of a
contact that has already been inserted, and a nameless, numberless contact would
only be clutter in the search. A fresh database ends with **15,289 contacts and
15,529 numbers**.

This is a merge that could be done properly — matching those 69 against the
contact that took their number, and filling in an address or a note the other
one lacks. That is A-63's job, and doing a rough version of it here would leave
a second implementation to keep honest.

**It is the same seed as everything else: skipped entirely once any contact
exists.** These are starting contents, not a sync. A supervisor who has fixed an
address, or flagged a caller, must not have it undone by somebody running `seed`
again after a half-finished install.

Inserted in batches of 500 with EF's change tracking off. Tracking thirty
thousand entities in order to insert them once costs more than the inserts do;
as it stands the contacts add about eight seconds to a run that was instant.

**What is tested, and what is not.** The suite has no PostgreSQL, so what is
checked is the file: that it is in the assembly and parses, that every row has a
name and a number that normalises, that virtually all the numbers still look
Palestinian, and that the duplicate counts are exactly the 110 and the 69 the
seeder's remarks claim. That last one is the one that matters — it is what will
notice when a newer export is converted and the numbers move.

The seed itself was run end to end against a scratch database on the development
machine: 15,289 contacts in about eight seconds, the right counts in
`contacts` and `contact_phones`, `last9` generated, and a second run creating
nothing.


## 2026-09-24 — Only answered calls are classified; missed, rejected and unanswered ones take a note

Double-clicking a missed or rejected call in the agent's call log opened the
full classification form, the same as an answered one. Nobody spoke on those
calls, so there is no order, complaint or branch to record, and a
classification on one would put an "order" into the reports that never
happened. What is worth knowing is **why** it was missed or rejected, and that
is a sentence, not a form.

**What the log does now.** Answered → the classification form, as before.
Missed, Rejected or NoAnswer (an outbound call the customer did not pick up) →
a small notes box ("Why was this call missed?"). Blocked and Failed → nothing
opens: nobody chose anything, or the number could not be dialled. The saved note shows in the last column of the
log, trimmed, with the full text in the tooltip, and in the supervisor app's
contact history under the status.

**The note lives on the call, not on a classification.** A new nullable
`communications.notes` column (migration `AddCallNotes`, max 4000 like the
classification's notes). Putting it in `classifications` would have meant a
classification row with no type — every report counts those rows, so every
report would have had to learn to skip them.

**The server enforces it, not just the screen.** `PUT /api/classifications/{id}`
(and `by-call`) now answers **409 `not_answered`** for any call that is not
Answered. `PUT /api/communications/{id}/notes` answers **409 `notes_not_taken`**
for any call that is not Missed or Rejected. Which statuses take a note is one
function, `CommunicationStatuses.TakesNotes`, used by both the server and the
app.

**The pop-up asks too, for an outbound call nobody picked up.** When a call the
agent placed ends as NoAnswer, the pop-up stays on screen with a notes box
("The customer didn't answer. Add a note?") and Save note / Skip, instead of
closing. That is the moment the agent knows "tried twice, call after 6". The
pop-up learns the outcome from `CallService.CallFinished`, which is raised just
before the state goes idle, so it knows to stay. A new call arriving closes the
note unsaved, as it does an untouched classification.

The pop-up has no server id for the call, so the note goes through the offline
queue as a new kind, `Notes`, keyed on SIP Call-ID and extension, and is sent to
`PUT /api/communications/by-call/notes` behind its call — the same route a
classification takes. No change to the laptop's buffer schema: the queue was
built generic for exactly this. A 404 `call_not_found` waits for the call; the
server's definite refusals (`notes_not_taken`, `not_your_call`,
`edit_window_closed`, and `not_answered` for classifications) are now dropped
rather than retried forever.

**One edit window for both.** The A-42 rule (own calls, same day unless the
supervisor sets `agent.edit_window` to Always; supervisors always) moved out of
`ClassificationService` into `CallEditWindow`, which both the classification
and the note use. Two copies would drift, and an agent would find one editable
the morning after and the other not.

**Failed outbound calls do not get the pop-up note.** Failed means the number
could not be dialled at all, and nothing about the customer was learned.

**Verified:** server, agent app and web build; 131 shared and 241 server tests
pass, including new ones for which statuses classify and which take a note, and
the door and validation checks on both notes endpoints. **Not yet seen
running** — neither notes box has been opened against a real call, and the
migration has not been applied to a real database.

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
| **Classification: the form and the supervisor's designer** | A-40, S-40 | nothing — A-14 landed |
| Opening a call from the log: details, classify | A-51 | classification (A-40) |
| Export the blocked list for Issabel | S-46 | nothing — now the **only** route to PBX-level blocking |
| CDR import: abandoned calls from `Master.csv` over SFTP | S-55 | **A-14 first** — it writes `communications` rows |
| The inline "new customer" form on the pop-up | A-11 | identity — **done** |
| Click-to-call from the log and from a contact | A-20 | dialling — **done**, needs its test call |
| Redial, call back from a missed call | A-22 | click-to-call |
| Merging two contacts | A-63 | nothing |
| Excel/CSV import *(the supervisor's own import; the one-off seed of the old system's 15,358 customers is done)* | A-64 | nothing |
| **Concurrency: authenticated requests collapse when they arrive together** | — | **unresolved.** Reproduced locally, cause not found, production impact unknown. Test `EnableRetryOnFailure()` first. Settle before handover. |
| Branch management: create, rename, disable | S-41 | nothing — a read-only `GET /api/branches` exists |
| Delivery price on the call pop-up | A-65, A-10 | address matching, which does not exist |

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

**The calls are visible as of 20 September**: the agent's own call log in the
Agent App (A-50) and the contact's history in the supervisor app (A-62). Neither
has been seen running — both are unverified UI, like the pop-up.

**Classification is the next substantial piece** (A-40, S-40), and the biggest
remaining feature: a server side, the supervisor's form designer, and a form in
the Agent App that renders fields it has never seen. One design question to
settle first — a classification attaches to a call record, and a call made while
the server was down has no id yet, so the classification endpoint should accept
the SIP Call-ID and extension as an alternative key. Both already ride in the
offline buffer together.

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

**Empty as of 2026-09-20.** All four items are closed — see the decision entries
for 19 and 20 September. Nothing on this list is a reason the system could not be
handed over; the open items above are work, not blockers.

Add to this list only what would make a handover irresponsible, not what is
merely unfinished. It was useful because it stayed short.

## For whoever administers the Issabel PBX

- **A caller is not released when the agent hangs up.** Check what follows
  `Queue()` in the dialplan for `smashed-002`: a `Hangup()` after it releases the
  caller, and without one they fall through to whatever is next and keep hearing
  a tone. Confirmed on 2026-09-20 that the Agent App sends `BYE` and is answered
  `200 OK`, so its own leg ends correctly — the remaining leg is the dialplan's.
- **Blocking at the PBX (S-46)** is the only way to stop a blocked caller
  entering the queue at all; the Agent App can only decline its own leg.

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
3b. ~~What does the dialplan expect an agent to dial?~~ **Answered 22 Sep by
   the first test call:** plain `0569498581` is accepted and routed.
   `Dialing:Prefix` stays empty.
3c. **Can extension 2001 place external calls at all, and through which trunk?**
   The first outbound test was answered 183 Session Progress, given about eight
   seconds of early media, then **503 Service Unavailable**. That is an outbound
   route that did not complete. Listening to those eight seconds should say why,
   because Asterisk usually announces it. **Outbound calling (A-20) cannot be
   signed off until this is settled**, and nothing in the app can fix it.
4. Can a **recording announcement** be added before ringing agents, if the
   client wants one?

Not a question for anybody — an observation we make ourselves, before the
importer is written: three test calls, then read the last rows of `Master.csv`
and record what marks an abandoned call, and whether a usable wait time is
there at all. See the 19 September CDR entry.

## Known gaps in what is built

- **Login has no database-backed test.** Token validation and encryption are
  covered and the actual login path is not. **The reason has now gone**: since
  23 September CI runs the server tests on Linux against a real `postgres:16`
  service, so a database-backed test is possible. It has not been written yet.
  The same applies to every "no database-backed test" note below.
- **The call path has met a PBX and works.** Confirmed on 2026-09-20: a call
  arrives through queue `smashed-002`, the pop-up appears, Answer connects
  two-way audio, and Hang up ends the leg cleanly. The automatic rejection of a
  blocked caller was confirmed earlier. **Still unverified:** the queue badge and
  the timer as they appear on screen, the agent's call log, and the contact
  history panel — all built but not yet seen running. The block-list rule is covered by tests because it was
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
- **Two of the three UI checks are still throwaway scripts.** The label check
  became a real test on 22 September (`AgentAppLabelsTests`), after the fourth
  label to reach the screen as a raw key. The other two are not reproducible by
  anyone else yet: that every `StaticResource` a view names is defined, and that
  every control a view uses has a style in the theme. **The last of those is what
  would have caught the white contacts list** — a `ListView` left with no style
  falls back to WPF's default white, and nothing in the build complains. Both
  belong beside the label test.

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
