# Restaurant Call Center System

A call centre platform for the Smashed Burger restaurant group: it ties the
Asterisk PBX to a customer database so every inbound call arrives with the
caller already identified, is classified and logged by the agent who takes it,
and rolls up into supervisor reports. It is built from three components — an
**ASP.NET Core server** (REST API, SignalR realtime channel, PostgreSQL), a
**WPF agent desktop app** (softphone, screen pop, offline buffer), and a
**React supervisor web app** (Arabic-first dashboards and reports) served from
the same host.

> **Status: scaffold.** Every project builds, runs and is tested, but the
> business features are not implemented yet — no entities, endpoints or
> telephony. Those are added in the prompts under [docs/prompts/](docs/prompts/),
> each referencing requirement IDs in the SRS and tables in the schema.

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | **8.0** | Pinned in [global.json](global.json) (`latestFeature` roll-forward) |
| Node.js | **20+** | For the supervisor web app |
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

The image builds the SPA with Node 20 and copies `dist/` into the server's
`wwwroot`, so one container serves both the API and the web app. Full
instructions are in [docs/DEPLOY-server-runbook.md](docs/DEPLOY-server-runbook.md).

## Folder map

```
src/CallCenter.Shared/     phone normalisation, enums, DTO contracts (shared by server + agent app)
src/CallCenter.Server/     ASP.NET Core API, SignalR hub, EF Core, background workers
src/CallCenter.AgentApp/   WPF softphone (SIPSorcery), SQLite offline buffer
src/CallCenter.Web/        React 18 + Vite supervisor SPA (Arabic RTL default)
tests/                     xUnit test projects
tools/pbx-sim/             console app that will replay SIP events and CDR rows without a live PBX
deploy/                    Docker Compose, .env.example, backup script
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
