# Releasing

How code gets from this repository to the call centre's server.

There are two different actions and it matters which one you mean:

| You say | What happens | Changes the call centre? |
| --- | --- | --- |
| **"push"** | commit + push to GitHub. CI builds and tests it. | no |
| **"release"** / **"release v1.2"** | push, then tag a version. GitHub builds and publishes the image. | no |
| **"deploy"** | copy the image to the server and run `update.sh`. | **yes** |

Nothing reaches the call centre until you explicitly say *deploy*.

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

**This is the only step that affects people taking orders.** Do it when the
call centre is quiet, never mid-service.

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
version keeps serving. Make a new token and log in again.

---

## What is not automated, and why

**Nothing deploys on push.** A live call centre should not change because
someone saved a file. The gap between *release* and *deploy* is the point: it is
where you decide the moment.

**GitHub cannot reach the server** — it sits behind the call centre's router on a
private address. So even with `--pull`, the server only ever fetches when you
tell it to; nothing can be pushed to it from outside. Daily operation needs no
internet at all (SRS N-01); `--pull` needs it only at the moment of an update,
which is why Option A exists.

When a server exists and there is reason to automate further, the options are a
self-hosted GitHub runner on the mini PC (it dials out, so no inbound access is
needed) or a cron job that polls for a new image. Neither is worth building
before there is a machine to test against.

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
**Agent App**, choose the `SmashedAgentApp-Setup-<version>.exe` file (the
version fills itself in from the name) and **Upload**. It replaces the
version before, and every agent is offered it at once.

**On each laptop:** the agent signs in to the web app with their own account,
sees only the Agent App page, downloads the installer and runs it. It installs
into `C:\SmashedAgentApp\`, closes the app first if it is open, clears the old
version's files, and makes the desktop shortcut. No administrator rights are
needed. The app's own data (the offline call queue, the block list, the logs)
is under `%LOCALAPPDATA%\CallCenter` and is kept.

**Without the browser**, the zip still works: unzip it over
`C:\SmashedAgentApp\` with the app closed. Never into a second folder: two
copies signed in as one agent take each other's calls (prompt 20).

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
