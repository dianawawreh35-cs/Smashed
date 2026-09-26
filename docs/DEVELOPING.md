# Developing

How to get all three parts of the system running on a development machine, and
the things about this project that will otherwise cost you an hour.

The README's quick start is the textbook version. This file is the one that
works.

---

## 1. Before anything else: `dotnet run` does not work here

On the development machine this project is built on, running a freshly compiled
`.exe` is blocked by policy — `dotnet run` fails with **Access is denied**.
`dotnet <name>.dll` is not blocked, because the file being executed is
`dotnet.exe`, which is already installed and trusted.

So everything below runs the DLL, **from its own output folder**. The folder
matters: `appsettings.json` is found relative to the working directory, not to
the DLL, so running it from the repository root starts an app with no
configuration.

This is not a quirk of one laptop. The same policy would block the Agent App's
installer on the agents' laptops, which is why code-signing is an open question
for the client's IT (see `RELEASING.md`).

## 2. Stop the apps before you build

The compiler cannot overwrite a DLL that a running process has open. A build
that fails with *the process cannot access the file* means the server or the
Agent App is still running. Close both, then build.

Worth doing first, every time — it is the single most common wasted round trip
on this project.

Checking whether they are running is easy to get wrong: both run as
**`dotnet.exe`**, so looking for a `CallCenter` process finds nothing while both
are up. What works:

```powershell
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
  Where-Object { $_.CommandLine -like "*CallCenter*" } |
  Select-Object ProcessId, CommandLine
```

## 3. The three commands

Run them in this order, each in its own terminal. PowerShell.

### Database

```powershell
docker compose -f deploy/docker-compose.dev.yml up -d
```

PostgreSQL 16 on `127.0.0.1:5432`. Leave it running; it is the only piece that
survives a reboot happily.

### Server

```powershell
cd "src\CallCenter.Server\bin\Debug\net10.0"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet CallCenter.Server.dll --urls http://localhost:5000
```

- `http://localhost:5000/health` — should answer `Healthy`
- `http://localhost:5000/swagger` — every endpoint, with an **Authorize** button
  that takes the `accessToken` from `POST /api/auth/login`

The server applies any pending database migrations on startup, so a schema
change needs nothing more than a restart.

**The POS lookup (A-67) runs only with a token.** On this machine it is in
user-secrets, outside the repository, and only the Development environment
reads it:

```powershell
dotnet user-secrets set "PosLookup:Token" "<token>" --project src\CallCenter.Server
dotnet user-secrets remove "PosLookup:Token" --project src\CallCenter.Server   # to turn it off
```

With it, the dev server asks the real POS about the dev database's recent
unknown callers, demo calls included, and creates contacts for those it knows.
Without it, the log says "The POS customer lookup is off" once at startup.

### Agent App

```powershell
cd "src\CallCenter.AgentApp\bin\Debug\net10.0-windows10.0.17763.0\win-x64"
dotnet CallCenter.AgentApp.dll
```

Note the target: everything is **.NET 10** since 19 September 2026, but the
Agent App's folder name carries the Windows 10 1809 moniker SIPSorcery requires,
so it is longer than the server's. If the path does not exist, the build has not
produced it yet.

### Supervisor web app

```powershell
cd "src\CallCenter.Web"
npm run dev
```

`http://localhost:5173`. Vite proxies `/api`, `/hubs`, `/health` and `/swagger`
to the server on port 5000, so there is no CORS or base URL to configure — but
the server has to be running, or every request 500s with nothing in the browser
console worth reading.

## 4. Signing in

| Who | Username | Notes |
| --- | --- | --- |
| Supervisor | `supervisor` | The web app. It refuses agent accounts. |
| Agent | `dia20` | The Agent App. Extension 2001, PBX 192.168.0.27. It refuses supervisor accounts. |

Each app turns away the other's accounts, so testing the Agent App needs an agent
account. Create more in the web app's Users screen.

If no account exists yet, seed one:

```powershell
cd "src\CallCenter.Server\bin\Debug\net10.0"
dotnet CallCenter.Server.dll seed
```

The same command fills an empty database with the branches, the delivery price
lists, the menu and the 15,289 customers carried over from the old ordering
system - about ten seconds, almost all of it the contacts. It is idempotent, so
running it against a database that already has them changes nothing.

That customer book is embedded in the server assembly. It is regenerated from
`docs/Contacts.xlsx` only when a newer export arrives:

```powershell
py -m pip install openpyxl
py tools\contacts-import\convert.py   # writes Data\Seed\Contacts\contacts.csv.gz - commit it
```

The seed command refuses to run once any user exists, so it cannot be used to
recover a forgotten supervisor password. `reset-password` is the way back in,
from the same folder:

```powershell
dotnet CallCenter.Server.dll reset-password --user supervisor --password 'NewPass!2026'
```

It also re-enables the account and closes its sessions. See runbook step 7.

## 5. Building and testing

```powershell
dotnet build CallCenter.sln      # apps stopped first - see section 2
dotnet test                      # xUnit; the database tests are skipped
cd "src\CallCenter.Web" ; npm test   # Vitest
```

Plain `dotnet test` needs no database. The tests that do need one are marked
`[DatabaseFact]`/`[DatabaseTheory]`, and they are reported as **skipped**, not
passed, when none was given. CI gives them one: a `postgres:16` service on Linux.

**Running the database tests on this machine**, against a scratch database in
the dev container, so your own dev data is never touched:

```powershell
# once: a database of its own, beside the dev one
docker exec callcenter-db-dev psql -U callcenter -d callcenter -c "CREATE DATABASE callcenter_test"

# every time
$env:ConnectionStrings__Default = "Host=127.0.0.1;Port=5432;Database=callcenter_test;Username=callcenter;Password=callcenter"
dotnet test tests\CallCenter.Server.Tests
Remove-Item Env:ConnectionStrings__Default
```

The test host migrates that database on startup. Every test makes its own
users, contacts and calls under random names, so running the suite a hundred
times against the same database gives the same answers as a fresh one. **The run
removes everything it made** when it ends, and again before it starts in case the
last one crashed (`TestSweeper`); recordings go to a temporary folder of the run's
own and are deleted with it. **Never point it at `callcenter`**: the tests would
fill your dev data with test agents, and the retention test runs the real job
over every recording in the database. Since 25 Sep the suite refuses to start
against a database of that name, except in CI (`CI=true`), whose throwaway
database has the same name.

## 6. What a green build does not prove

It proves the code compiles and the structures line up. It does not prove a
screen looks right, because the apps cannot be launched from a development
session with an AI assistant — only by the developer, by hand.

Three defects in one recent session were found only by opening the apps: every
login answering 500, the contacts list rendering as a white block, and English
text laid out right-to-left. All three passed the build and 176 tests.

So: after UI work, open the app and look at it. A screenshot is the evidence,
not the build log.

---

## See also

- [`SRS-Smashed-Burger-Call-Center.md`](SRS-Smashed-Burger-Call-Center.md) — what the system must do, by requirement ID
- [`DECISIONS.md`](DECISIONS.md) — what was decided and why, and the live list of what is outstanding
- [`SCHEMA.md`](SCHEMA.md) — the database shape
- [`DEPLOY-server-runbook.md`](DEPLOY-server-runbook.md) — the production install
- [`RELEASING.md`](RELEASING.md) — what push, release and deploy each mean here
