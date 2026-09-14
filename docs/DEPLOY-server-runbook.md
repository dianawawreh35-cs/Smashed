# Server Deployment Runbook — Restaurant Call Center System

Target: mini PC (Intel i5, SSD) on the restaurant LAN. Ubuntu Server 24.04 LTS + Docker Compose.
Example addresses used below — replace with the real ones:

| Item | Example |
|---|---|
| Server IP | `192.168.1.50` |
| Router / gateway | `192.168.1.1` |
| Yeastar S20 IP | `192.168.1.10` |
| LAN | `192.168.1.0/24` |

Estimated time: about 1 hour, plus waiting for the client's PBX person in step 8.

---

## Step 1 — Install Ubuntu Server

**What it does:** puts a clean, minimal Linux on the mini PC. No desktop, so all memory and CPU go to your system. OpenSSH lets you manage it from your laptop later; you'll never need a monitor on it again.

**Work**
1. Download Ubuntu Server 24.04 LTS ISO from ubuntu.com and write it to a USB stick (Rufus or balenaEtcher).
2. Boot the mini PC from the USB (press F12/F2/Del at power-on to pick the boot device).
3. Installer choices: language English → keyboard → **Ubuntu Server (minimized)** → network: leave DHCP for now → storage: **Use an entire disk** (the SSD) → profile: name `admin`, server name `callcenter`, username `admin`, strong password → **tick Install OpenSSH server** → skip all snaps → wait → reboot, remove USB.
4. Log in at the console and note the current IP:
```bash
ip a
```

---

## Step 2 — Fixed IP address

**What it does:** the router normally gives out a random address that can change after a reboot. The agent apps and the S20 must always find the server at the same address, so you hard-code one. Netplan is Ubuntu's network configuration.

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
      addresses: [192.168.1.50/24]
      routes:
        - to: default
          via: 192.168.1.1
      nameservers:
        addresses: [192.168.1.1, 8.8.8.8]
```
Save (Ctrl+O, Enter, Ctrl+X), then:
```bash
sudo netplan apply
ip a          # should now show 192.168.1.50
```
From your laptop, connect remotely and do everything else over SSH:
```bash
ssh admin@192.168.1.50
```
Also reserve `192.168.1.50` in the router's DHCP settings so it never gives that address to another device.

---

## Step 3 — Updates, time zone, firewall

**What it does:** brings the system up to date, sets the local clock (so call timestamps are correct), and turns on a firewall that allows only the ports you need and only from the restaurant LAN. Everything else is closed.

**Work**
```bash
sudo apt update && sudo apt upgrade -y
sudo timedatectl set-timezone Asia/Hebron
sudo apt install -y ufw curl unzip

# SSH for you; 5000/5001 for the API + supervisor web app
sudo ufw allow from 192.168.1.0/24 to any port 22 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 5000 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 5001 proto tcp
# Callback extension (SIP signalling + audio) — only if you use that feature
sudo ufw allow from 192.168.1.0/24 to any port 5060 proto udp
sudo ufw allow from 192.168.1.0/24 to any port 10000:10100 proto udp
sudo ufw enable
sudo ufw status
```
The RTP range 10000–10100 must match what you configure in the API's SIP settings.

---

## Step 4 — Install Docker

**What it does:** Docker runs each part of the system in its own isolated container — a packaged box with the program and everything it needs. You install Docker once; from then on your whole system is delivered as images that run identically on any client's machine. Adding your user to the `docker` group avoids typing `sudo` for every Docker command.

**Work**
```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker admin
newgrp docker
docker --version
docker compose version
```

---

## Step 5 — Application folder and settings

**What it does:** one place for everything. `docker-compose.yml` is the recipe listing the containers. `.env` holds all secrets and site-specific values (database password, PBX address, AMI login) so nothing sensitive is in the code. The `data/` folders live **outside** the containers, so you can update or rebuild containers without losing the database or recordings.

**Work**
```bash
sudo mkdir -p /opt/callcenter/data/postgres /opt/callcenter/data/recordings /opt/callcenter/backups
sudo chown -R admin:admin /opt/callcenter
cd /opt/callcenter
```
From your laptop, copy the deployment files from your repo:
```bash
scp docker-compose.yml .env.example backup.sh update.sh admin@192.168.1.50:/opt/callcenter/
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
SERVER_IP=192.168.1.50
PBX_IP=192.168.1.10
AMI_USER=callcenter
AMI_PASSWORD=<as set on the S20>
CALLBACK_EXT=199
CALLBACK_EXT_PASSWORD=<as set on the S20>
RTP_PORT_MIN=10000
RTP_PORT_MAX=10100
RECORDING_RETENTION_DAYS=90
TZ=Asia/Hebron
```
Generate random values with `openssl rand -base64 48`.

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
    network_mode: host          # needed for AMI + SIP/RTP without port mapping
    env_file: .env
    environment:
      ConnectionStrings__Default: Host=127.0.0.1;Database=callcenter;Username=callcenter;Password=${POSTGRES_PASSWORD}
      Recordings__Path: /data/recordings
    volumes:
      - ./data/recordings:/data/recordings
    depends_on:
      db:
        condition: service_healthy
```
Because `api` uses host networking, Postgres must publish its port to the host — add `ports: ["127.0.0.1:5432:5432"]` to the `db` service so it's reachable only locally.

---

## Step 6 — Load the image and start

**What it does:** `docker load` brings your built API image onto the machine from a file you copied (no internet needed). `docker compose up -d` reads the recipe and starts the database and API in the background with restart-on-failure, so after a power cut everything comes back by itself. On first start the API applies database migrations, creating all tables.

**Work**
On your laptop, after building:
```bash
docker save callcenter-api:latest -o callcenter-api.tar
scp callcenter-api.tar admin@192.168.1.50:/opt/callcenter/
```
On the server:
```bash
cd /opt/callcenter
docker load -i callcenter-api.tar
docker compose up -d
docker compose logs -f api
```
Wait for lines like `Migrations applied` and `Now listening on: http://0.0.0.0:5000`. Ctrl+C to stop following the log (containers keep running).

---

## Step 7 — First-run seed

**What it does:** an empty database has no users. This one-time command creates the supervisor account, the four branches, the default classification types and the channels list. Then you confirm the web app works and set a real password.

**Work**
```bash
docker compose exec api dotnet CallCenter.Api.dll seed \
  --admin-user supervisor --admin-password 'TempPass!2026' \
  --branches "Branch 1,Branch 2,Branch 3,Branch 4"
```
From a laptop browser: `http://192.168.1.50:5000` → log in → **change the password** → check Settings shows the branches, types and channels.

---

## Step 8 — Connect the PBX (client's PBX person)

**What it does:** enabling AMI on the S20 opens the event feed your server listens to for calls that never reach an agent; the permitted IP limits who may connect to only your server. The callback extension is an ordinary extension your server registers as a phone, so the S20 can send timed-out queue calls to it. The status page proves the whole chain.

**Work — on the S20 (client side)**
1. Settings > System > Security > **AMI**: Enable; Username `callcenter`; Password = `AMI_PASSWORD` from `.env`; Permitted IP `192.168.1.50` / `255.255.255.255`. Save & Apply.
2. Create extension `199` "Callback" with the password from `.env`.
3. Queue / ring group: set **Failover / timeout destination** → extension 199 (if the callback method is used).
4. Confirm inbound routing type (queue or ring group) and that caller ID reaches extensions today.

**Work — verify on the server**
Supervisor app → Settings → PBX status: `AMI: connected`, `Callback extension: registered`.
```bash
docker compose logs api | grep -i ami | tail
```
Test: call the restaurant number from a mobile, let it ring until the queue times out. It should appear in the dashboard as **Abandoned/Overflowed** within seconds.

If the S20 has no AMI tab, skip 1 and configure the alternative: Settings > System > Storage → CDR to a network drive pointing at the server's share (set up Samba on the server), or a scheduled CDR export. The API's CDR importer watches `/data/cdr-import/`.

---

## Step 9 — Backups

**What it does:** dumps the database to a file and copies recordings to a second disk or network share every night, scheduled by cron (Linux's scheduler). A backup you have never restored is a hope, not a backup — so you test a restore on your own PC before handover (it's an acceptance item in the contract).

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
1. Supervisor app → Users → add 5 agents: name, login, inbound extension + SIP password, outbound extension + SIP password, default branch.
2. On each of the 4 laptops: browse to `http://192.168.1.50:5000/downloads/AgentApp-Setup.exe`, install, first-run: server address `192.168.1.50:5000`, choose microphone/speaker, log in as an agent.
3. Windows Firewall prompt → **Allow** on private networks. If missed: Windows Security → Firewall → Allow an app → tick the Agent App.
4. Test on each laptop: internal call between two agents (pop-up, audio both ways, recording plays back, classification form opens), then a real call from a mobile through the trunk.
5. Log out and log in as a different agent on the same laptop; confirm the other agent's extensions register and only their calls show.

---

## Step 11 — Handover

**What it does:** leaves the client able to operate without you and leaves you able to rebuild the server from scratch if the SSD dies.

**Work**
- Give the supervisor a one-page sheet: server IP, web address, how to reboot (just power on — everything auto-starts), where backups go, your contact and support hours.
- Store in your password manager: `.env` contents, admin password, the S20 AMI credentials, the router reservation.
- Commit `docker-compose.yml`, `.env.example`, `backup.sh` and this runbook to the repo (never the real `.env`).
- Walk the supervisor through the dashboard for 1 hour; agents 1 hour.

---

## Updating later

**What it does:** swaps the API container for the new version in seconds. Database and recordings are untouched; migrations bring the schema up to date; agent apps update themselves at next launch. Phones never stop because they don't depend on the server.

Use `update.sh` rather than doing this by hand — it backs up first, verifies the
new version answers `/health`, and rolls back automatically if it does not.

**On your development machine** — build and save with the version as the tag:
```bash
docker build -f src/CallCenter.Server/Dockerfile -t callcenter-api:v1.2 .
docker save callcenter-api:v1.2 -o callcenter-api-v1.2.tar
scp callcenter-api-v1.2.tar admin@192.168.1.50:/opt/callcenter/
```

**On the server:**
```bash
ssh admin@192.168.1.50
cd /opt/callcenter
./update.sh v1.2
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
