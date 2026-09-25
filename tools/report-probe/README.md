# Report probe: are the reports fast enough over a year? (N-02)

N-02: "report generation under 5 s for one year of data at the expected volume
(up to ~500 communications/day)". This fills a scratch database with a year of
synthetic calls and messages and times every report endpoint over the whole
year. Written 26 Sep 2026 for the call reports (prompt 15); the results are in
`docs/DECISIONS.md`, the entry of that date.

**Never point any of this at `callcenter`.** `fill.sql` refuses to run there.

## Run it

PowerShell, from the repository root, with the dev database container up.

```powershell
# 1. A scratch database, migrated and seeded (branches, types, channels, the 15,289 contacts).
docker exec callcenter-db-dev psql -U callcenter -d callcenter -c "DROP DATABASE IF EXISTS callcenter_probe" -c "CREATE DATABASE callcenter_probe"
$out = "$env:TEMP\cc-probe-server\"
dotnet build src\CallCenter.Server\CallCenter.Server.csproj -p:OutDir=$out
cd $out
$env:ConnectionStrings__Default = "Host=127.0.0.1;Port=5432;Database=callcenter_probe;Username=callcenter;Password=callcenter"
dotnet CallCenter.Server.dll seed --admin-user probe-supervisor --admin-password 'ProbePass!2026'

# 2. A year of calls: about 180,000 rows, a minute to write.
Get-Content -Raw <repo>\tools\report-probe\fill.sql | docker exec -i callcenter-db-dev psql -U callcenter -d callcenter_probe -v ON_ERROR_STOP=1

# 3. A server on its own port against it, hidden, as DEVELOPING.md says.
Start-Process dotnet -ArgumentList "CallCenter.Server.dll","--urls","http://127.0.0.1:5055" -WorkingDirectory $out -WindowStyle Hidden -RedirectStandardOutput "$out\probe.log"
$env:ConnectionStrings__Default = $null

# 4. Time every report.
cd <repo>
py tools\report-probe\time_reports.py --login probe-supervisor --password 'ProbePass!2026'
```

Afterwards stop the probe server (the `dotnet.exe` whose command line has
`5055`) and drop `callcenter_probe`.

## What the year looks like

`fill.sql`: 365 days back from today, about 494 communications a day, spread
over 10:00–23:59 with a lunch hump and an evening peak; 8% messages on the
seeded apps, the rest calls. Calls are 85% incoming; incoming 80% answered,
13% missed, 5% rejected, 2% blocked; outgoing 70% answered, 25% not answered,
5% failed. Three quarters of calls are from a seeded contact, the rest from
numbers nobody saved. 90% of answered calls and every message are classified,
about half of them as orders worth 20–200. Five agents, four branches.

The data is uniform where the real restaurant is not (no holidays, no growth,
no slow Tuesdays), so the numbers are about speed, not about what the reports
would show.
