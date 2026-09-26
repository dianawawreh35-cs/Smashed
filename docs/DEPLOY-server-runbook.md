# Server Deployment Runbook — Restaurant Call Center System

Target: mini PC (Intel i5, SSD) on the call center LAN. Ubuntu Server 26.04 LTS + Docker Compose.
Addresses used below. Rows marked *example* are still to be confirmed on site:

| Item | Value |
|---|---|
| Server IP | `192.168.1.100` (confirmed) |
| Router / gateway | `192.168.1.1` *example* |
| PBX address on the VPN | `10.8.0.1` *example* |
| Server address on the VPN | `10.8.0.20` *example* |
| LAN | `192.168.1.0/24` |

**The server is LAN-only, with outbound internet (confirmed).** Nothing reaches
it from outside the call center: the firewall admits the LAN only and the router
forwards no ports. It can reach out through the router, which is what the
Docker install, `./update.sh --pull`, the POS lookup (`smashed-ps.com`) and the
server's VPN to the PBX all rely on.

Estimated time: about 1 hour, plus waiting for the telephony provider in step 8.

> **The PBX is not on your network.** It is an Issabel system run by an external
> telephony provider and reached over a VPN. Each agent laptop runs a VPN client,
> and the server needs its own VPN connection for the abandoned-call import in
> step 8. Everything else works without it.

---

## Step 1 — Install Ubuntu Server

**What it does:** puts a clean, minimal Linux on the mini PC. No desktop, so all memory and CPU go to your system. OpenSSH lets you manage it from your laptop later; you'll never need a monitor on it again.

**Work**
1. Download Ubuntu Server 26.04 LTS ISO from ubuntu.com and write it to a USB stick (Rufus or balenaEtcher).
2. Boot the mini PC from the USB (press F12/F2/Del at power-on to pick the boot device).
3. Installer choices: language English → keyboard → **Ubuntu Server (minimized)** → network: leave DHCP for now → storage: **Use an entire disk** (the SSD) → profile: server name `smashed-callcenter`, username `smashed`, strong password → **tick Install OpenSSH server** → skip all snaps → wait → reboot, remove USB.
4. Log in at the console and note the current IP:
```bash
ip a
```

---

## Step 2 — Fixed IP address

**What it does:** the router normally gives out a random address that can change after a reboot. The agent apps must always find the server at the same address, so you hard-code one. Netplan is Ubuntu's network configuration.

**Work**
```bash
sudo nano /etc/netplan/50-cloud-init.yaml
```
Replace the contents with (use your interface name from `ip a`, e.g. `enp1s0` or `eno1`):
```yaml
network:
  version: 2
  ethernets:
    enp1s0:
      dhcp4: no
      addresses: [192.168.1.100/24]
      routes:
        - to: default
          via: 192.168.1.1
      nameservers:
        addresses: [192.168.1.1, 8.8.8.8]
```
Save (Ctrl+O, Enter, Ctrl+X), then:
```bash
sudo netplan apply
ip a          # should now show 192.168.1.100
```
From your laptop, connect remotely and do everything else over SSH:
```bash
ssh smashed@192.168.1.100
```
Also reserve `192.168.1.100` in the router's DHCP settings so it never gives that address to another device.

---

## Step 3 — Updates, time zone, firewall

**What it does:** brings the system up to date, sets the local clock (so call timestamps are correct), and turns on a firewall that allows only the ports you need and only from the call center LAN. Everything else is closed.

**Work**
```bash
sudo apt update && sudo apt upgrade -y
sudo timedatectl set-timezone Asia/Hebron
sudo apt install -y ufw curl unzip nano

# Remote administration over Tailscale (see below) - add this BEFORE enabling
# the firewall if you are connected that way, or ufw cuts off your own session
sudo ufw allow in on tailscale0

# SSH for you; 80 is the supervisor web app, 5000 the API and healthcheck
sudo ufw allow from 192.168.1.0/24 to any port 22 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 80 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 5000 proto tcp
sudo ufw enable
sudo ufw status
```
Nothing else is opened. **The server takes no part in the phone calls.** The
softphone runs on each agent's laptop, which registers with the PBX itself. Older
copies of this runbook also opened `5060/udp` and `10000:10100/udp` for a
call-back extension on the server. That feature was removed on 19 September 2026
(S-56), so don't open those ports.

Port 80 is what lets the supervisor open the dashboard by typing the server
address on its own, with no port number after it. Nothing listens on 5001, so
it is no longer opened. None of these ports are forwarded on the router: the
system is reachable from the call center LAN only. The restaurant branches are
on separate networks and don't need it.

**Remote administration: Tailscale.** The server is also in the developer's
Tailscale network as `smashed-callcenter`, and advertises `192.168.1.100/32`
as a subnet route. So from the office, `ssh smashed@192.168.1.100` and
`http://192.168.1.100` reach it through Tailscale, not through the router. That
traffic arrives on the `tailscale0` interface from a `100.x` address, not from
`192.168.1.x`, which is why it gets its own `ufw` rule above. Only devices in
that Tailscale account can use this path. Nothing is opened to the internet.
In the Tailscale admin console, **disable key expiry** for
`smashed-callcenter`. Otherwise the remote path drops after 180 days until
someone signs it in again at the call center.

---

## Step 4 — Install Docker

**What it does:** Docker runs each part of the system in its own isolated container — a packaged box with the program and everything it needs. You install Docker once; from then on your whole system is delivered as images that run identically on any client's machine. Adding your user to the `docker` group avoids typing `sudo` for every Docker command.

**Work**
```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker smashed
newgrp docker
docker --version
docker compose version
```

---

## Step 5 — Application folder and settings

**What it does:** one place for everything. `docker-compose.yml` is the recipe listing the containers. `.env` holds all secrets and site-specific values (database password, signing keys, the POS token) so nothing sensitive is in the code. The `data/` folders live **outside** the containers, so you can update or rebuild containers without losing the database, the recordings or the menu photographs.

**Work**
```bash
sudo mkdir -p /opt/callcenter/data/postgres /opt/callcenter/data/recordings /opt/callcenter/data/menu-images /opt/callcenter/backups
sudo chown -R smashed:smashed /opt/callcenter
cd /opt/callcenter
```
From your laptop, copy the deployment files from your repo:
```bash
scp docker-compose.yml .env.example backup.sh update.sh smashed@192.168.1.100:/opt/callcenter/
```
Back on the server:
```bash
cp .env.example .env
nano .env
```
Fill in:
```
POSTGRES_PASSWORD=<long random>
JWT_SECRET=<long random, 64+ chars>
SIP_SECRET_KEY=<long random, 32+ chars>
RECORDING_RETENTION_DAYS=90
POS_LOOKUP_TOKEN=<the token the POS issued>
TZ=Asia/Hebron
```
Generate random values with `openssl rand -base64 48`.

`POS_LOOKUP_TOKEN` is the bearer token for the POS's customer lookup. With it,
every few minutes (the supervisor sets how often, five by default) the server asks the POS about recent callers nobody has on
file, and creates or fills in their contacts (A-67). It is not random: it is
the value the POS side gave you, and it goes in the password manager beside the
others. Left blank, the lookup stays off and the log says so once at startup.
The server needs outbound HTTPS to `smashed-ps.com` for it.

**The PBX address isn't in this file.** It's the `pbx.host` setting in the
supervisor app, and it's set in step 7. Older copies had `PBX_IP` or `PBX_HOST`
here, and nothing ever read either of them.

`JWT_SECRET` signs the tokens agents and supervisors hold; changing it signs
everyone out. `SIP_SECRET_KEY` encrypts the agents' SIP secrets in the database,
so a database dump does not hand over the extensions' passwords — keep both in
the password manager. The API refuses to start if either is missing.

Reference `docker-compose.yml` (adjust image name):
```yaml
services:
  db:
    image: postgres:16
    restart: unless-stopped
    environment:
      POSTGRES_DB: callcenter
      POSTGRES_USER: callcenter
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      TZ: ${TZ}
    volumes:
      - ./data/postgres:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U callcenter"]
      interval: 10s
      retries: 10

  api:
    image: callcenter-api:latest
    restart: unless-stopped
    network_mode: host          # uses the host's VPN route to the PBX (step 8)
    env_file: .env
    environment:
      ConnectionStrings__Default: Host=127.0.0.1;Database=callcenter;Username=callcenter;Password=${POSTGRES_PASSWORD}
      Recordings__Path: /data/recordings
      MenuImages__Path: /data/menu-images
    volumes:
      - ./data/recordings:/data/recordings
      - ./data/menu-images:/data/menu-images
    depends_on:
      db:
        condition: service_healthy
```
Because `api` uses host networking, Postgres must publish its port to the host — add `ports: ["127.0.0.1:5432:5432"]` to the `db` service so it's reachable only locally.

---

## Step 6 — Load the image and start

**What it does:** `docker load` brings your built API image onto the machine from a file you copied (no internet needed). `docker compose up -d` reads the recipe and starts the database and API in the background with restart-on-failure, so after a power cut everything comes back by itself. On first start the API applies database migrations, creating all tables.

Images are built and published by CI, not by hand — see
[RELEASING.md](RELEASING.md). Tagging a version publishes
`ghcr.io/dianawawreh35-cs/callcenter-api:<version>`, which is what you install.

**Option A — carry the images (works with no internet on site).** On your laptop:
```bash
docker pull ghcr.io/dianawawreh35-cs/callcenter-api:v1.0
docker tag  ghcr.io/dianawawreh35-cs/callcenter-api:v1.0 callcenter-api:v1.0
docker save callcenter-api:v1.0 -o callcenter-api-v1.0.tar

# the database image too - otherwise the server downloads it on first start
docker pull postgres:16
docker save postgres:16 -o postgres16.tar

scp callcenter-api-v1.0.tar postgres16.tar smashed@192.168.1.100:/opt/callcenter/
```
On the server:
```bash
cd /opt/callcenter
docker load -i postgres16.tar
docker load -i callcenter-api-v1.0.tar
docker tag callcenter-api:v1.0 callcenter-api:latest
docker compose up -d
docker compose logs -f api
```

**Option B — let the server fetch them (needs internet on site).** Log in once,
because the API image is private. Git isn't needed on the server. Creating the
read-only token it logs in with is covered in
[RELEASING.md](RELEASING.md#pulling-a-private-image-needs-a-login):
```bash
read -s T && echo "$T" | docker login ghcr.io -u dianawawreh35-cs --password-stdin; unset T
chmod +x update.sh backup.sh
./update.sh v1.0 --pull --no-backup
```
That pulls the image, starts it, waits for `/health`, and rolls back if it does
not come up. `postgres:16` is fetched from Docker Hub automatically.
`--no-backup` is for this first install only. The script backs up before every
update, and with no database yet (and no backup disk until step 9) that backup
would fail and stop the install. Leave it off every later update.

> Even with internet on site, Option A is worth preferring for the **first**
> install: it removes any dependency on the call center's connection working on
> the day, and guarantees the exact `postgres:16` build you tested rather than
> whatever the tag points at that week.

Either way, wait for lines like `Migrations applied` and `Now listening on: http://0.0.0.0:80`. Ctrl+C to stop following the log (containers keep running).

---

## Step 7 — First-run seed

**What it does:** an empty database has no users. This one-time command creates the supervisor account, the four branches, the default classification types, the channels list, the delivery price lists, the menu, and the 15,289 customers carried over from the old ordering system. Then you confirm the web app works and set a real password.

**Work**
```bash
docker compose exec api dotnet CallCenter.Server.dll seed \
  --admin-user supervisor --admin-password 'TempPass!2026' \
  --branches "Branch 1,Branch 2,Branch 3,Branch 4"
```
It prints what it created:
```
  branches   4 created
  channels   5 created
  types      6 created
  form v1    created
  settings   6 created
  delivery   228 created
  menu       44 created
  contacts   15289 created
  supervisor supervisor created
```
The contacts are the slow part - about ten seconds; everything else is instant.
Safe to run twice - anything already there is left alone, and it refuses to
create a second supervisor once the users table has rows. `--branches` is
optional (it defaults to Branch 1-4) and is ignored if branches already exist.
`dotnet CallCenter.Server.dll seed --help` lists every option.

**Change the password immediately after logging in** - it was typed on the
command line and is in this machine's shell history.

From a laptop browser: `http://192.168.1.100` → log in → **change the password** → check Settings shows the branches, types and channels, and that the contacts list is not empty.

Then, in **Settings**, set **`pbx.host`** to the PBX's address on the VPN (`10.8.0.1` in the example). It's what every Agent App registers to. While it's blank, agents sign in but the phone stays offline.

### If the supervisor password is ever lost

There is no self-service reset: the system has no email or SMS to send a link
to. The way back in is from the server's own command line.

```bash
docker compose exec api dotnet CallCenter.Server.dll reset-password   --user supervisor --password 'NewPass!2026'
```

It also re-enables the account if it had been disabled, and closes every session
it had open. If the login is wrong it prints the supervisor logins that do
exist, so you do not need a database client to find out.

If there is no supervisor account left at all, promote an agent:
```bash
docker compose exec api dotnet CallCenter.Server.dll reset-password   --user dia20 --password 'NewPass!2026' --make-supervisor
```

**Change the password again in the app afterwards** - what you type here stays
in this machine's shell history.

---

## Step 8 — Connect the PBX (abandoned calls)

**What it does:** the PBX's web interface has a report, Call Center → Reports → Calls Detail, that lists every queue call and marks the ones the caller gave up on as **Abandoned**. Your server logs in to that web interface like a person would, downloads the report every minute, and saves the abandoned calls so they show in the reports and the call list. Nothing is installed or opened on the PBX — the connection is outbound from your server over HTTPS, the same direction as a phone registering.

This is the only method: AMI and direct database access need an inbound port and are ruled out, and the call-back extension was removed (SRS 4.5). An earlier plan pulled Asterisk's CDR file over SFTP; it was replaced by this report on 26 Sep 2026 and never built, so there is no SFTP account or key to set up.

**Prerequisite — the server's VPN connection**
The PBX is only reachable over the VPN, so set this up first and confirm it:
```bash
ping -c 3 10.8.0.1                                  # the PBX on the VPN
curl -sk -o /dev/null -w '%{http_code}\n' https://10.8.0.1/index.php   # its web interface: 200
```
Make the VPN start on boot, so a power cut does not leave the server silently cut off from the PBX.

### 8.1 A PBX web user for the server

In the Issabel web interface, as admin, create a user for the server — `callcenter-reports`, say — rather than lending it a person's login: if that person changes their password, the import stops.

- **Language: English.** The report's column headings follow the user's language, and the server reads the English ones. A user set to another language gets a clear error on the settings screen, not an empty import.
- **Access: Call Center reports.** Check by logging in as that user in a browser and opening Call Center → Reports → Calls Detail. If you can see the calls, so can the server.

Store the username and password in your password manager.

### 8.2 Enter it in the app

Supervisor app → **Settings** → **Abandoned calls from the PBX**:
- **PBX web address:** `https://10.8.0.1` — as you would open it in a browser.
- **PBX username** and **PBX password:** the user from 8.1. The password is stored encrypted with `SIP_SECRET_KEY` (step 5) and never shown again; the screen only says one is saved.
- **Check every:** 1 minute is the default.

Save, then **Check now**. It should answer with the number of calls it downloaded and how many were abandoned. The first check reaches back 30 days.

The PBX's HTTPS certificate is its own, self-signed; the server accepts it for this address only.

**Verify once it is running**
Settings shows the last check and, if it failed, why — a wrong password, an unreachable PBX, a user not set to English.
```bash
docker compose logs api | grep -i "abandoned\|PBX" | tail
```
Test: call the restaurant number and hang up while it is still ringing. It should appear under Call reports → **Abandoned** within a minute — not instantly. The Abandoned tab's **Fetch from PBX** button downloads the chosen period at once.

---

## Step 9 — Backups

**What it does:** dumps the database to a file and copies the recordings and the menu photographs to a second disk or network share every night, scheduled by cron (Linux's scheduler). A backup you have never restored is a hope, not a backup — so you test a restore on your own PC before handover (it's an acceptance item in the contract).

**Work**
Mount the backup disk (example: second SSD/USB at `/mnt/backup`):
```bash
sudo mkdir -p /mnt/backup
lsblk                                  # find the device, e.g. /dev/sdb1
sudo blkid /dev/sdb1                   # copy the UUID
echo 'UUID=<uuid>  /mnt/backup  ext4  defaults,nofail  0 2' | sudo tee -a /etc/fstab
sudo mount -a
```
`backup.sh` (in `/opt/callcenter`, make executable with `chmod +x backup.sh`):
```bash
#!/bin/bash
set -e
D=$(date +%F)
cd /opt/callcenter
docker compose exec -T db pg_dump -U callcenter callcenter | gzip > backups/db-$D.sql.gz
rsync -a --delete data/recordings/ /mnt/backup/recordings/
rsync -a --delete data/menu-images/ /mnt/backup/menu-images/
cp backups/db-$D.sql.gz /mnt/backup/
find backups -name 'db-*.sql.gz' -mtime +30 -delete
find /mnt/backup -name 'db-*.sql.gz' -mtime +30 -delete
echo "$D backup OK"
```
Schedule nightly at 03:30:
```bash
crontab -e
30 3 * * * /opt/callcenter/backup.sh >> /opt/callcenter/backups/backup.log 2>&1
```
Run it once by hand and check `backups/backup.log`.

**Restore test (on your own PC with Docker):**
```bash
docker run -d --name restoretest -e POSTGRES_PASSWORD=x -p 5433:5432 postgres:16
gunzip -c db-<date>.sql.gz | docker exec -i restoretest psql -U postgres -c "CREATE DATABASE callcenter" && \
gunzip -c db-<date>.sql.gz | docker exec -i restoretest psql -U postgres -d callcenter
```
Point a local API at it and confirm calls and contacts are there.

---

## Step 10 — Agents and laptops

**What it does:** creating agents in the supervisor app stores each one's two extensions and SIP passwords centrally, so agents never type SIP details. Installing the Agent App from the server's download link means every laptop gets the same version and future updates come from the same place. The Windows Firewall prompt matters: deny it and you get registered-but-silent calls.

**Work**
1. Supervisor app → Users → add 5 agents: name, login, customer extension + SIP password, internal extension + SIP password, default branch. (Customer = the one the queue rings and that calls customers; internal = agents and branches. SRS 2.3.)
2. On each of the 4 laptops: browse to `http://192.168.1.100/downloads/AgentApp-Setup.exe`, install, first-run: server address `192.168.1.100`, choose microphone/speaker, log in as an agent.
3. Windows Firewall prompt → **Allow** on private networks. If missed: Windows Security → Firewall → Allow an app → tick the Agent App.
4. Test on each laptop: internal call between two agents (pop-up, audio both ways, recording plays back, classification form opens), then a real call from a mobile through the trunk.
5. Log out and log in as a different agent on the same laptop; confirm the other agent's extensions register and only their calls show.

---

## Step 11 — Handover

**What it does:** leaves the client able to operate without you and leaves you able to rebuild the server from scratch if the SSD dies.

**Work**
- Give the supervisor a one-page sheet: server IP, web address, how to reboot (just power on — everything auto-starts), where backups go, your contact and support hours.
- Store in your password manager: `.env` contents, admin password, the PBX web user the server logs in as (step 8), the VPN accounts for the server and each laptop, the router reservation.
- Commit `docker-compose.yml`, `.env.example`, `backup.sh` and this runbook to the repo (never the real `.env`).
- Walk the supervisor through the dashboard for 1 hour; agents 1 hour.

---

## Updating later

**What it does:** swaps the API container for the new version in seconds. Database, recordings and menu photographs are untouched; migrations bring the schema up to date; agent apps update themselves at next launch. Phones never stop because they don't depend on the server.

Use `update.sh` rather than doing this by hand — it backs up first, verifies the
new version answers `/health`, and rolls back automatically if it does not.

The image comes from CI — `git tag v1.2 && git push --tags` publishes it (see
[RELEASING.md](RELEASING.md)). You never build a release by hand.

**Option A — carry it over.** On your development machine:
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

**Option B — the server pulls it.** Nothing to copy; needs internet on site and
the one-time `docker login` above:
```bash
ssh smashed@192.168.1.100
cd /opt/callcenter
./update.sh v1.2 --pull
```

It runs `backup.sh`, tags the running image `callcenter-api:previous`, loads the
new one, restarts the API, and waits up to 90 seconds for `/health`. If the new
version never becomes healthy it prints the last 40 log lines, retags
`:previous` back to `:latest`, restarts, and confirms the old version is serving
again — so a bad build costs seconds, not an evening.

To go back deliberately after a successful but unwanted update:
```bash
./update.sh --rollback
```

Copy `update.sh` alongside the other files in step 5 (`chmod +x update.sh`).
The tag on the saved image **must** match the version you pass — the script
refuses if `callcenter-api-v1.2.tar` does not contain `callcenter-api:v1.2`.

---

## Quick health checks

```bash
docker compose ps                     # all services "running"
docker compose logs --tail 50 api     # recent API log
df -h /opt/callcenter /mnt/backup     # disk space
tail -5 /opt/callcenter/backups/backup.log
```
Supervisor app → Settings → PBX status and Storage usage cover the same at a glance.
