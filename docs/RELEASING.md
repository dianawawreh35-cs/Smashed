# Releasing

How code gets from this repository to the call centre's server.

There are two different actions and it matters which one you mean:

| You say | What happens | Changes the call centre? |
| --- | --- | --- |
| **"push"** | commit + push to GitHub. CI builds and tests it. | no |
| **"release"** / **"release v1.2"** | push, then tag a version. GitHub builds and publishes the image. | **yes, at 04:30 that night**, unless the tag says `deploy: manual` |
| **"deploy"** | install it now, by hand: copy the image to the server and run `update.sh`. | **yes, now** |

Since 29 Sep 2026 the server installs a new release by itself at night
([auto-update.sh](../deploy/auto-update.sh)). So a release is a promise that
the version can go live without anyone there, and what it needs is said in its
tag message (below).

---

## "push" — save the work

```bash
git add -A
git commit -m "<why this change exists>"
git push
```

[.github/workflows/ci.yml](../.github/workflows/ci.yml) then runs, on clean machines:

- .NET build of everything (Windows, because the WPF Agent App builds only
  there), and a check that no NuGet package has a known vulnerability
- the Shared and Server tests (Linux, because only Linux machines can have a
  PostgreSQL attached; the tests run against it, in `TZ=Asia/Hebron` as the
  server does, and give up after 20 minutes rather than six hours)
- web install (`npm ci`), audit (no high or critical advisory in what ships),
  lint, build and tests, on Node 22
- Docker image build, discarded afterwards — this only proves the Dockerfile works

Green means the repository builds from nothing but what is committed.

**Before pushing, check the file count.** `git show --stat` on a commit that
claims 1,200 files when you changed two means something was swept in that
`.gitignore` should have caught.

---

## "release" — publish a versioned image

```bash
git tag -a v1.2 -m "<what is in this version>"
git push --tags
```

[.github/workflows/release.yml](../.github/workflows/release.yml) then:

1. runs the tests — **publishing depends on them**, so a failing version is never stored
2. builds the image from the tagged commit
3. pushes it to `ghcr.io/dianawawreh35-cs/callcenter-api:v1.2`, and moves
   `:latest` to it (a pushed tag only; see below)
4. prints the deploy commands in the run summary

The image is private — it inherits this repository's visibility.

### The tag message tells the server what the release needs

The server's nightly update reads two lines from the tag message:

```bash
git tag -a v1.2 -m "Crispy burgers on the menu

deploy: seed"
```

| Line in the tag message | What the server does at 04:30 |
| --- | --- |
| *(neither)* | installs it |
| `deploy: seed` | installs it, then runs the seed once (a release that adds menu items) |
| `deploy: manual` | **does not install it.** It stops at the version before, and says so in its log every night until someone deploys it by hand |

Use `deploy: manual` when the release needs something the server cannot do for
itself:

- a new value in `.env` (a secret, a token);
- the laptops updated the same day, because the new server and the old Agent
  App do not work together (as with v0.5.0's one-sign-in rule);
- anything you want to watch go live.

Changes to `docker-compose.yml`, `update.sh`, `backup.sh` or `auto-update.sh`
do **not** need it: each image carries its own copies of them, and the nightly
update puts them on the server first. That starts with the first release after
29 Sep 2026; older images carry none.

A tag without a message (`git tag v1.2`) has neither line, so it installs.
Only plain versions (`v1.2.3`) install by themselves; a test build
(`v1.2-test`) never does.

### Version numbers

`vMAJOR.MINOR.PATCH`, and the tag must start with `v` or the workflow will not fire.

- **PATCH** (`v1.2.1`) — a bug fix, nothing new
- **MINOR** (`v1.3.0`) — a new feature, nothing broken
- **MAJOR** (`v2.0.0`) — something changed that needs action on install

Tie these to the SRS phases: Phase 1 delivery is `v1.0.0`, Phase 2 is `v2.0.0`.

### Publishing without tagging

The workflow also accepts a manual run with a version of your choosing
(Actions → Release → Run workflow), for a test build that should not become a
permanent version number. It must look like a version (`v1.2-test`), and a
manual run **does not move `:latest`**, so a test build is never what a plain
pull fetches (27 Sep 2026).

---

## "deploy" — put it on the call centre's server

Usually nothing to do: the server installs a release by itself at night
(below). Deploy by hand when a release is marked `deploy: manual`, or when a
fix cannot wait for 04:30.

**This affects people taking orders.** Do it when the call centre is quiet,
never mid-service.

Two routes — pick whichever suits the call centre's network.

**A. Carry it over** (no internet needed on site). On your machine:

```bash
docker pull ghcr.io/dianawawreh35-cs/callcenter-api:v1.2
docker tag  ghcr.io/dianawawreh35-cs/callcenter-api:v1.2 callcenter-api:v1.2
docker save callcenter-api:v1.2 -o callcenter-api-v1.2.tar
scp callcenter-api-v1.2.tar smashed@192.168.1.100:/opt/callcenter/
```

Then on the server:

```bash
ssh smashed@192.168.1.100
cd /opt/callcenter
./update.sh v1.2
```

**B. Let the server fetch it** (needs internet on site, and the login below):

```bash
ssh smashed@192.168.1.100
cd /opt/callcenter
./update.sh v1.2 --pull
```

[`update.sh`](../deploy/update.sh) backs up first and prints the dump's name,
keeps the running version as `:previous`, restarts, and waits up to three
minutes for `/health/ready`, which also asks the database. If the new version
never becomes ready it rolls back automatically, **but only if the database
is as it was before**. If the new version has already applied a migration, it
stops. It says so, and prints the pre-update dump to restore first (runbook
step 9). `./update.sh --rollback` goes back deliberately, under the same rule.

### By itself, at night

At 04:30 every night (an hour after the 03:30 backup) a timer on the server runs
[`auto-update.sh`](../deploy/auto-update.sh). In plain terms: the server asks
GitHub "is there a newer version than mine?", and if there is, it runs the same
`./update.sh <version> --pull` you would.

Step by step, it:

1. reads the version running, and lists the versions in the image store;
2. reads the tag message of every newer one, oldest first, and stops below the
   first marked `deploy: manual`;
3. copies that version's deploy files to `/opt/callcenter`, keeping the old
   ones in `backups/deploy-<old version>-<time>/`;
4. runs `./update.sh <version> --pull`: backup, restart, health check, rollback;
5. if the update rolled back, puts the old deploy files back too;
6. runs the seed if any of the versions it passed says `deploy: seed`.

It skips several versions at once if the server missed some. Everything
it did is in `/opt/callcenter/backups/auto-update.log`:

```bash
tail -30 /opt/callcenter/backups/auto-update.log
```

**What it cannot do:** tell anyone. A night that failed shows only in that log,
and in the web app still showing the old version. Look after each release.

To see what tonight's run would do, without installing anything:

```bash
cd /opt/callcenter && ./auto-update.sh --check
```

Setting the timer up, changing its hour, and switching it off are in the
runbook, *Updating by itself at night*. It uses the server's `docker login`
below, so **a token with an expiry date stops the nightly updates on that
date.**

### A migration must not remove what the previous release reads

Rolling back swaps the image, never the database (M-D01, 27 Sep 2026). A
migration that drops or renames a column the previous version still reads
leaves nothing to roll back to except the dump, and restoring the dump loses
whatever happened since the update. So a migration **only adds**: new
tables, new columns (nullable or with a default), new indexes. Removing or
renaming something takes two releases. The first stops reading it. The next,
once the first has run in production, removes it. Say which kind a migration is
in its DECISIONS entry.

Full context in [DEPLOY-server-runbook.md](DEPLOY-server-runbook.md).

### Pulling a private image needs a login

Neither git nor the GitHub CLI is needed on the server. Docker only needs a
token that lets it download the image.

**Your laptop** is already signed in to GitHub through `gh`. Give that sign-in
package access once (it opens the browser), then hand it to Docker:

```bash
gh auth refresh -s read:packages
gh auth token | docker login ghcr.io -u dianawawreh35-cs --password-stdin
```

**The server** gets its own token, so it never holds the laptop's, which can
push code:

1. On GitHub: Settings → Developer settings → Personal access tokens →
   **Tokens (classic)** → Generate new token. Name it `callcenter-server`,
   tick **only `read:packages`**, and choose an expiry (no expiry means
   updates never stop working because a date passed). Fine-grained tokens
   don't work with the image store yet, so it has to be a classic one.
2. Copy it into the password manager.
3. On the server, type it where it won't land in the shell history:

```bash
read -s T && echo "$T" | docker login ghcr.io -u dianawawreh35-cs --password-stdin; unset T
```

Paste the token at the blank prompt and press Enter. It should say `Login
Succeeded`. Docker remembers it from then on. If the token expires or is
revoked, `./update.sh --pull` fails at the download step and the running
version keeps serving, and the nightly update logs `could not sign in to
ghcr.io` every night. Make a new token and log in again.

Log in as `smashed`, not with `sudo`: the nightly update runs as `smashed` and
reads that user's login.

---

## What is not automated, and why

**Nothing deploys on push.** A live call centre should not change because
someone saved a file. Only a tagged release installs, and only at night.

**Nothing installs during a shift.** The nightly update runs at 04:30 and at no
other time. A server that was off at 04:30 does not catch up when it is switched
on, because that would be mid-service; it waits for the next night.

**GitHub cannot reach the server** — it sits behind the call centre's router on a
private address. So the server asks instead: once a night it dials out to the
image store. Daily operation still needs no internet (SRS N-01); without it the
nightly check fails, logs why, and the running version carries on.

**The laptops are not updated.** The Agent App is installed from the web app's
Agent App page (below), not by the nightly update. A release the old Agent App
cannot work with is marked `deploy: manual`.

**A failure is not reported.** It is in `backups/auto-update.log` only. A
failed update that had already changed the database leaves the call centre down
until someone restores the dump (runbook step 9). The 04:30 start leaves the
hours before opening to do it.

---

## Release history

| Version | Date | Contents |
| --- | --- | --- |
| `v0.1.0` | 2026-09-14 | Scaffold — structure, config and placeholder code. No business features. Published to verify the pipeline. |
| `v0.2.0` | 2026-09-26 | **Never published.** The release job had no database, the database-backed tests hung, and the run was cancelled. Use `v0.2.1`. |
| `v0.2.1` | 2026-09-26 | The first version for the server: everything built to date, including calls, contacts, classification, the reports with printing, the POS contact lookup and the abandoned calls from the PBX. The release job now runs the tests against Postgres, as CI does. |
| `v0.2.2` | 2026-09-26 | The Smashed logo as the supervisor app's browser-tab icon, and as the Agent App's icon (the Agent App is built separately, see below). |
| `v0.3.0` | 2026-09-26 | The PBX's blacklist follows the Blocked flag (S-46) and the dashboard opens and closes the queue (S-60). Each agent's phone as the PBX sees it, offline, free, ringing or in a call, on the Users page and the dashboard (S-61), and listening in on a call with `*222` (S-62). Agents online counts only apps heard from lately; the dashboard counts a call passed between agents once. One migration (`agent_sessions.last_seen_at`). The Agent App is unchanged. |
| `v0.3.1` | 2026-09-26 | Log lines only, to find why the first listen-in on the server was silent: where the PBX sends a listen-in's audio, and how much sound arrived. The PBX watch also says when it is off because the server's extension is not set up. |
| `v0.3.2` | 2026-09-27 | A listen-in ends by itself when the call does. The PBX's `*222` stays on the line after the call it listens to, so the server now ends it once the PBX watch shows the agent free or offline. |
| `v0.4.0` | 2026-09-27 | The fixes from the 27 Sep code review, in all three apps. **Server:** the delivery price paste works; R-16 counts POS customers as new; only a supervisor closes a complaint; "today" is right on clock-change days; a call must be logged under the agent's own extension and cannot be rewritten after its day; sign-in is rate-limited (429); placeholder keys refuse to start; logging out ends the Agent App's token; the PBX address must be https; the CSV export runs no formulas; `/health/ready`. Two migrations (`AddContactSource`, `ProtectClassificationHistory`). **Deploy files changed:** `backup.sh`, `update.sh` and `docker-compose.yml` must be copied to the server (DECISIONS, 27 Sep part 5). **Web:** failures say so, Arabic numbers read the right way, lighter pages, the current password for your own change. **Agent App:** no more crashes after hang-up or on a UI error, the phone never stays busy, the upload queue loses nothing and files nothing under the wrong agent, call shortcuts; **update the laptops the same day as the server.** |
| `v0.4.1` | 2026-09-27 | The dashboard and both report pages work again: in v0.4.0 they crashed on opening, because of how the web build split the chart library into files (DECISIONS, 27 Sep). Web app only; no migration, no new deploy files. |
| `v0.5.0` | 2026-09-27 | **Agents install the Agent App from the web app** (S-63, N-11): a supervisor uploads the installer and its zip on a new Agent App page, and an agent who signs in sees only that page. **Logs page** (S-64, N-12): the Agent Apps send their logs to the server, and supervisors read them with errors first. **One phone per agent** (N-05, A-05), after the 27 Sep evening's missed calls: one copy of the app per Windows sign-in, one Agent App sign-in per agent, and the PBX forgets old addresses at sign-in. Text in the Agent App can be selected and copied (A-84). No migration. **Deploy files changed:** `docker-compose.yml` (the `data/agent-app` mount) must be copied to the server. **Agent App:** build it after this tag and upload it on the Agent App page; update the laptops the same day as the server, since the one-sign-in rule needs both. |
| `v0.5.1` | 2026-09-27 | **Four crispy burgers on the menu** (A-66): سنجل and دبل كرسبي برغر, ناشوز and بافلو كرسبي برغر, in two new categories, with their pictures. The seed now adds missing menu items to a server that already has a menu, and changes none that exist. **After the update, run `seed` once** (runbook, *Updating later*); it should print `menu 4 created`. Everything in `v0.5.0` is in it, so its deploy notes apply if the server is still on `v0.4.1`. No migration, no new deploy files, no Agent App change. |
| `v0.6.0` | 2026-09-29 | **The Logs page, for everyday use** (S-64): it opens on **today**, with a date box in place of each laptop's list of days, and the entries scroll in their own box. Supervisors can give a laptop a **nickname**, shown in place of its Windows name. The red line about today's errors has **Acknowledge**: it goes for every supervisor until a laptop has new errors, and then counts only those. No migration, no new deploy files. **Agent App:** unchanged; stays at 0.5.3. |
| `v0.7.0` | 2026-10-01 | **The POS lookup asks again at every run** (A-67): every few minutes it asks the POS about every number from the last two days with no contact, not once an hour, so a customer whose order is typed in after the call is added within minutes. A **POS customer lookup** card on the Settings page shows the last check and has **Check now**. **Every date filter in the web app opens on today**: the dashboard's charts, both report pages, Calls and Applications. Also since `v0.6.0`: `backup.sh` works without the second disk (N-07), and the image carries `auto-update.sh` and its timer for the nightly update, **not yet switched on on the server**. No migration, nothing new in `.env`. **Agent App:** unchanged. |
| `v0.7.1` | 2026-10-01 | **The POS lookup looks at recorded messages too** (A-67): a WhatsApp or other app customer the POS knows is added like a caller. In `v0.7.0` it looked only at calls, so ريم صالح, recorded as a message, was never asked about. No migration, nothing new in `.env`. **Agent App:** unchanged. |
| `v0.7.2` | 2026-10-01 | **The Calls page shows whether each call is classified**: a *Classified* column, green when it is, amber *Not classified* on an answered call nobody classified, and nothing on a missed or abandoned call. Web app only; no migration, nothing new in `.env`. **Agent App:** unchanged. |
| `v0.8.0` | 2026-10-01 | **Did not build; nothing was published.** Its commit registered `MistakeReportsService` in `Program.cs` before the class was committed (another session's line, staged with the breaks). Superseded by `v0.8.1`. |
| `v0.8.1` | 2026-10-01 | **Breaks** (A-86, S-66, R-22): **Break in / Break out** in the Agent App's rail turns do not disturb on and holds it, then off again, and counts the agent's break time for the day, adding up across breaks. A **daily allowance** on the Settings page (*Break allowance*, 60 minutes) warns past it and never stops a break. A **Breaks** page in the web app: who is on break now, live, and the break report per agent, per day and every break, with CSV. **Mistakes page** (S-65): mistakes made by a branch or an agent, with value and customer, searchable and exported; and the **Mistakes report** (R-23): per branch, per agent, over time, repeat customers. Three migrations (`AddMistakes`, `AddAgentBreaks`, `SeedBreakAllowance`), all additive; nothing new in `.env`. **Agent App: 0.8.1**, built after the tag and uploaded on the Agent App page; laptops on 0.5.3 or later offer *Update now*. The old Agent App keeps working, without the button. |
| `v0.8.2` | 2026-10-01 | **Do not disturb no longer logs a Missed call** (A-18): a call it turns away, during a break too, is not reported, so the queue's every try at an agent on a break stops counting as a call they missed. The 144 such rows already logged were cleared on the server with `tools/clear-dnd-missed/clear.sql`, which keeps a copy; run it again once every laptop is on 0.8.2. **Agent App: 0.8.2**, built after the tag and uploaded on the Agent App page. The server is unchanged; no migration, nothing new in `.env`. |
| `v0.8.3` | 2026-10-01 | **The phone comes back by itself when the PBX asks for its password again** (A-02): once an extension has registered on a sign-in, a later *401 Unauthorized* makes the app log in again, after 30 s, then 1, 2 and every 5 min, instead of staying off until the agent signs out and in (extension 2010, 1 Oct 20:01). A wrong password at sign-in still stops at once. **Agent App: 0.8.3**, built after the tag and uploaded on the Agent App page. The server is unchanged; no migration, nothing new in `.env`. |
| `v0.8.4` | 2026-10-01 | **Each agent's Agent App version on the Users page** (S-42): an *App version* column, from the agent's latest sign-in to the app, with **Update to x** when the Agent App page offers a newer one. **The app asks for a new version every minute**, not every 15 (A-82), from 0.8.4 on. No migration, nothing new in `.env`. **Agent App: 0.8.4**, built after the tag and uploaded on the Agent App page; it carries 0.8.2's and 0.8.3's fixes, so it is the only one to upload. |
| `v0.8.5` | 2026-10-02 | **Echo cancellation** (A-87): the agent's microphone goes through Windows' own echo canceller, so a customer no longer hears their voice come back from the agent's speaker. If it cannot run, or goes quiet for half a second, the call carries on with the plain microphone; `Audio:EchoCancellation` in the app's appsettings.json turns it off for one laptop. **Agent App: 0.8.5**, built after the tag and uploaded on the Agent App page. The server is unchanged; no migration, nothing new in `.env`. |
| `v0.8.6` | 2026-10-02 | **The dashboard splits today's calls into incoming and outgoing** (S-20): an *Incoming calls* block with Answered and Abandoned under it, the figure to set beside the PBX's own call report, and an *Outgoing calls* block with Answered and Not answered. The *Abandoned calls* tile is now the Incoming block's line. Server and web only; no migration, nothing new in `.env`. **Agent App: unchanged**, nothing to upload; 0.8.5 stays the one to install. |
| `v0.8.7` | 2026-10-02 | **A printed report shows all its columns**: on paper a wide table no longer stops at the edge of its scroll box, and it is set tighter so every column fits the page; the chart legend's swatches print small again. Every printed table gains it: both report pages, Calls, Applications, Mistakes and Breaks. Web only; no migration, nothing new in `.env`. **Agent App: unchanged**, nothing to upload; 0.8.5 stays the one to install. |
| `v0.8.8` | 2026-10-02 | **The supervisor's filters take several choices at once** (S-02, S-07): agent, branch, type, result and channel take several ticks on the Calls, Applications, reports, breaks and mistakes pages; any of them matches. Server and web together; no migration, nothing new in `.env`. **Agent App: unchanged**, nothing to upload; 0.8.5 stays the one to install. |
| `v0.8.9` | 2026-10-02 | **Listen & speak** (S-62): a second button beside Listen on the Users page. The server dials `*223`, the PBX's whisper, so the supervisor also speaks to the agent and the customer does not hear them; **Mute** turns the microphone off without hanging up. Before the first use: `*223` allowed on the server's extension (runbook step 8.3), and `tools/supervisor-pc/allow-microphone.ps1` run once on each supervisor PC, since the browser offers no microphone to `http://` otherwise (runbook, *Supervisor PCs: the microphone*). Without either, Listen & speak says why and Listen works as before. Server and web together; no migration, nothing new in `.env`. **Agent App: unchanged**, nothing to upload; 0.8.5 stays the one to install. |
| `v0.9.0` | 2026-10-02 | **App messages: the channel can be changed on any day, and a supervisor deletes one recorded by mistake** (A-71). On the Applications page, an opened message has *Change* beside its channel and *Delete message* (asked twice); what a deleted message said is kept in the audit log. In the Agent App's App logs, the channel of an older message can still be changed; the number and the time stay locked. **The side menu can be hidden** (S-67) with the button at the start of the header, for a report that wants the whole width. No migration, nothing new in `.env`. **Agent App: 0.9.0**, built after the tag and uploaded on the Agent App page; an older Agent App keeps working, with older messages fully read only. |
| `v0.9.1` | 2026-10-02 | **The Agent App's X really closes it** (A-05): since 18 Sep, X only hid the window, and the app went on running, signed in and taking calls, until Task Manager ended it. Now X signs the agent out and the phone stops ringing; **tell the agents to minimise the app, not close it, to stay on calls.** **Agent App: 0.9.1**, built after the tag and uploaded on the Agent App page in place of 0.9.0 (it carries 0.9.0's changes). The server is unchanged from `v0.9.0`; no migration, nothing new in `.env`. |
| `v0.9.2` | 2026-10-03 | **Recordings play both voices in both ears** (S-04, A-51): before, the customer was heard in the left ear only and the agent in the right. Under the player, *Listen to: Both / Customer / Agent* (the Agent App says *You*) plays one side alone, for a moment they talked over each other. The saved recordings and the download are unchanged. Web and Agent App; no migration, nothing new in `.env`. **Agent App: 0.9.2**, built after the tag and uploaded on the Agent App page in place of 0.9.1 (it carries 0.9.1's changes); an older Agent App keeps working, with the old one-voice-per-ear playback. |

---

## Building the Agent App for the laptops

The Agent App is **not** part of the Docker release — that image is the server
and the supervisor web app. The desktop app is built here, uploaded to the
server on the web app's **Agent App** page, and each agent installs it from
there (N-11, S-63).

```powershell
powershell -ExecutionPolicy Bypass -File tools\agent-app\publish.ps1
```

That publishes into `publish\agent-app-<version>\`, zips it as
`publish\SmashedAgentApp-<version>.zip`, and builds the installer,
`publish\SmashedAgentApp-Setup-<version>.exe` (about 70 MB, 20 seconds). The
installer needs Inno Setup on the build machine, once:
`winget install --id JRSoftware.InnoSetup -e --scope user`. The version comes from the latest git
tag, so build it after tagging a release. `publish\` is git-ignored.

**The server address is written in by the script.** The app has no screen for
it. It reads `Server:BaseUrl` from the `appsettings.json` beside the program,
and the repository's copy says `http://localhost:5000` for development. The
script puts `http://192.168.1.100` into the published copy only. For another
site, pass `-Server http://<address>`.

**Put it on the server:** sign in to the web app as a supervisor, open
**Agent App**, choose the `SmashedAgentApp-Setup-<version>.exe` file and,
in the second box, `SmashedAgentApp-<version>.zip` from the same build (the
version fills itself in from the names), and **Upload**. They replace the
version before, and every agent is offered them at once. The zip is the
fallback for a laptop the installer will not run on; it is optional, but
without it an agent has nothing to try when the installer fails.

**From 0.5.3 on, the app offers it itself (A-82).** Within a minute of the
upload (15 minutes on versions before 0.8.4), or at the next sign-in, every
laptop on 0.5.3 or later shows a bar:
*A new version of the app is ready*, with **Update now**. The agent presses
it between calls (it is off during one); the app downloads the installer,
closes, installs it silently and opens again, and the agent signs in. No
Windows prompt comes up, since the app fetched the file and not a browser.
If a laptop does not update, its install log is in
`%LOCALAPPDATA%\CallCenter\updates\install-<version>.log`, and the app's log
says why. Only the copy in `C:\SmashedAgentApp\` updates itself.

**On each laptop, the first time** (or on one older than 0.5.3): the agent signs in to the web app with their own account,
sees only the Agent App page, downloads the installer and runs it. It installs
into `C:\SmashedAgentApp\`, closes the app first if it is open, clears the old
version's files, and makes the desktop shortcut. No administrator rights are
needed. The app's own data (the offline call queue, the block list, the logs)
is under `%LOCALAPPDATA%\CallCenter` and is kept.

**If the installer will not run**, the page offers the zip under it, with
the steps: close the app, unblock the zip, empty `C:\SmashedAgentApp\`,
extract into it, start it. **Without the browser**, the same zip carried
over works the same way. Never into a second folder: two copies signed in
as one agent take each other's calls (prompt 20).

**Self-contained on purpose.** The app targets `net10.0-windows`, and
`--self-contained true` bundles the runtime with it — about 185 MB and roughly
190 runtime assemblies. The alternative is installing the .NET 10 Desktop
Runtime on every laptop first, which is one more thing to get wrong on a machine
you may not be sitting at. For four laptops the larger folder is the cheaper
trade.

### Executable policy — asked and answered for this client

Executables built in a user profile are blocked on some managed Windows
machines. This was hit during development on the developer's own laptop, where
`dotnet run` failed with *Access is denied* while `dotnet <dll>` worked, and the
worry was that the same policy would block the Agent App at install time.

**Answered on 2026-09-20: the agents' laptops have no such policy.** The Agent
App installs unsigned, and **code-signing is not required for this delivery.**
The developer's own machine still has the restriction, which is a development
inconvenience only — see `DEVELOPING.md`.

**Ask again for any other client, before installation day:** *is there a policy
blocking unsigned executables, and what is the approval process for our
application?* Code-signing the executable is the usual answer, it costs money and
adds a step to every release, and it is not something to discover on the day.
