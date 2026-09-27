# Restaurant Call Center System

A call centre platform for the Smashed Burger restaurant group: it ties the
Asterisk PBX to a customer database so every inbound call arrives with the
caller already identified, is classified and logged by the agent who takes it,
and rolls up into supervisor reports. It is built from three components — an
**ASP.NET Core server** (REST API, SignalR realtime channel, PostgreSQL), a
**WPF agent desktop app** (softphone, screen pop, offline buffer), and a
**React supervisor web app** (Arabic-first dashboards and reports) served from
the same host.

> **Status (27 Sep 2026): in use on the restaurant's server, before handover.**
> Released as `v0.3.2` ([docs/RELEASING.md](docs/RELEASING.md)). Built: the Agent
> App's softphone with the caller pop-up, classification and the offline queue;
> contacts, the call log and search; the call and message reports and the
> dashboard; the POS customer lookup; the abandoned calls from the PBX; the PBX
> blacklist, the queue switch, the agents' phone states and listening in. What
> is left, and in what order, is under *Where to pick up* and *Open items* at
> the end of [docs/DECISIONS.md](docs/DECISIONS.md). Each feature was built from
> a prompt under [docs/prompts/](docs/prompts/), citing the SRS's requirement IDs.

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | **10.0** | Pinned in [global.json](global.json) (`latestFeature` roll-forward) |
| Node.js | **22.12+** | For the supervisor web app (`engines` in its `package.json`) |
| Docker + Compose | v2 | PostgreSQL 16 in development, the full stack in production |
| Windows 10 1809+ | — | Only to build/run the WPF agent app |

## Run for development

> **On a machine that blocks running locally-built executables** — `dotnet run`
> answering *Access is denied* — use
> [docs/DEVELOPING.md](docs/DEVELOPING.md) instead. It gives the commands that
> work, and the ones below will not.

```bash
docker compose -f deploy/docker-compose.dev.yml up -d   # 1. PostgreSQL 16 on 127.0.0.1:5432
dotnet build CallCenter.sln                             # 2. build everything
dotnet run --project src/CallCenter.Server              # 3. API on http://localhost:5000
cd src/CallCenter.Web && npm install && npm run dev     # 4. SPA on http://localhost:5173
dotnet run --project src/CallCenter.AgentApp            # 5. WPF agent shell (Windows only)
```

- `http://localhost:5000/health` — liveness probe (returns `Healthy`)
- `http://localhost:5000/swagger` — API explorer (Development only)
- `http://localhost:5173` — SPA; Vite proxies `/api`, `/hubs`, `/health` and
  `/swagger` to the API, so there is no CORS or base-URL configuration to do.

Run the tests with `dotnet test` (xUnit) and `npm test` in
`src/CallCenter.Web` (Vitest).

### Production image

```bash
docker build -f src/CallCenter.Server/Dockerfile -t callcenter-api:latest .
cp deploy/.env.example deploy/.env    # fill in every secret
docker compose -f deploy/docker-compose.yml up -d
```

The image builds the SPA with Node 22 and copies `dist/` into the server's
`wwwroot`, so one container serves both the API and the web app. Full
instructions are in [docs/DEPLOY-server-runbook.md](docs/DEPLOY-server-runbook.md).

## Folder map

```
src/CallCenter.Shared/     phone normalisation, enums, DTO contracts (shared by server + agent app)
src/CallCenter.Server/     ASP.NET Core API, EF Core, the PBX features (SIP from the server's own extension), background workers
src/CallCenter.AgentApp/   WPF softphone (SIPSorcery), SQLite offline buffer
src/CallCenter.Web/        React 18 + Vite supervisor SPA (Arabic RTL default)
tests/                     xUnit test projects: Shared, Server (against PostgreSQL), Agent App
tools/agent-app/           publish.ps1: builds the Agent App for the laptops, pointed at the server
tools/contacts-import/     convert.py: the old system's customer export into the seed file
tools/demo-data/           add.sql / remove.sql: removable demo traffic for the dev database's reports
tools/icons/               make_icons.py: the apps' icons from the Smashed logo
tools/load-probe/          probe.py: many requests at once, for the concurrency work
tools/presence-probe/      asks the PBX about extensions' states (S-61's first test)
tools/report-probe/        a year of synthetic calls, and the time each report takes (N-02)
tools/pbx-sim/             placeholder for a PBX simulator; not built
deploy/                    Docker Compose, .env.example, backup.sh, update.sh
docs/                      requirements, schema, runbook, decisions, prompt history
```

## Documentation

- [docs/SRS-Smashed-Burger-Call-Center.md](docs/SRS-Smashed-Burger-Call-Center.md) — requirements and the IDs every feature prompt cites
- [docs/SCHEMA.md](docs/SCHEMA.md) — database tables, columns and CHECK constraints
- [docs/DEPLOY-server-runbook.md](docs/DEPLOY-server-runbook.md) — production install, backup and recovery
- [docs/DECISIONS.md](docs/DECISIONS.md) — stack choices and the reasoning behind them
- [docs/DEVELOPING.md](docs/DEVELOPING.md) — how to actually run the three parts on a development machine
- [docs/RELEASING.md](docs/RELEASING.md) — push vs release vs deploy, and how each works
- [docs/THIRD-PARTY-LICENSES.md](docs/THIRD-PARTY-LICENSES.md) — every dependency and its licence
