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
cd "src\CallCenter.Server\bin\Debug\net8.0"
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet CallCenter.Server.dll --urls http://localhost:5000
```

- `http://localhost:5000/health` — should answer `Healthy`
- `http://localhost:5000/swagger` — every endpoint, with an **Authorize** button
  that takes the `accessToken` from `POST /api/auth/login`

The server applies any pending database migrations on startup, so a schema
change needs nothing more than a restart.

### Agent App

```powershell
cd "src\CallCenter.AgentApp\bin\Debug\net10.0-windows10.0.17763.0\win-x64"
dotnet CallCenter.AgentApp.dll
```

Note the target: the Agent App is **`net10.0-windows`**, everything else is
**`net8.0`**. The long folder name is the Windows 10 1809 moniker SIPSorcery
requires. If the path does not exist, the build has not produced it yet.

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
| Agent | `dia20` | The Agent App. Extension 2001, PBX 192.168.0.27. |

The Agent App currently also accepts supervisor logins. That is deliberate and
temporary — it is how sign-in was tested before user management existed — and it
is on the list to close before handover.

If no account exists yet, seed one:

```powershell
cd "src\CallCenter.Server\bin\Debug\net8.0"
dotnet CallCenter.Server.dll seed
```

The seed command refuses to run once any user exists, so it cannot be used to
recover a forgotten supervisor password. There is no self-service reset either;
a lockout currently needs direct database access. Also on the list.

## 5. Building and testing

```powershell
dotnet build CallCenter.sln      # apps stopped first - see section 2
dotnet test                      # xUnit, no database needed
cd "src\CallCenter.Web" ; npm test   # Vitest
```

`dotnet test` deliberately runs without PostgreSQL: the test host is started
with migrations turned off, so CI needs no database service. That is also its
limit — the tests cover validation, authorization and pure logic, and anything
that reads a table is not covered.

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
