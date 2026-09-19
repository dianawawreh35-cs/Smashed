# Server Deployment Runbook — Restaurant Call Center System

Target: mini PC (Intel i5, SSD) on the restaurant LAN. Ubuntu Server 26.04 LTS + Docker Compose.
Example addresses used below — replace with the real ones:

| Item | Example |
|---|---|
| Server IP | `192.168.1.100` |
| Router / gateway | `192.168.1.1` |
| PBX address on the VPN | `10.8.0.1` |
| Server address on the VPN | `10.8.0.20` |
| LAN | `192.168.1.0/24` |

Estimated time: about 1 hour, plus waiting for the telephony provider in step 8.

> **The PBX is not on your network.** It is an Issabel system run by an external
> telephony provider and reached over a VPN. Each agent laptop runs a VPN client.
> The server would need its own VPN connection for step 8 — but step 8 is
> deferred, so nothing in this runbook needs it today.

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

**What it does:** brings the system up to date, sets the local clock (so call timestamps are correct), and turns on a firewall that allows only the ports you need and only from the restaurant LAN. Everything else is closed.

**Work**
```bash
sudo apt update && sudo apt upgrade -y
sudo timedatectl set-timezone Asia/Hebron
sudo apt install -y ufw curl unzip

# SSH for you; 80 is the supervisor web app, 5000 the API and healthcheck
sudo ufw allow from 192.168.1.0/24 to any port 22 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 80 proto tcp
sudo ufw allow from 192.168.1.0/24 to any port 5000 proto tcp
# Callback extension (SIP signalling + audio) — only if you use that feature
sudo ufw allow from 192.168.1.0/24 to any port 5060 proto udp
sudo ufw allow from 192.168.1.0/24 to any port 10000:10100 proto udp
sudo ufw enable
sudo ufw status
```
The RTP range 10000–10100 must match what you configure in the API's SIP settings.

Port 80 is what lets the supervisor open the dashboard by typing the server
address on its own, with no port number after it. Nothing listens on 5001, so
it is no longer opened. None of these ports are forwarded on the router: the
system is reachable from the restaurant LAN only.

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

**What it does:** one place for everything. `docker-compose.yml` is the recipe listing the containers. `.env` holds all secrets and site-specific values (database password, PBX address, the CDR pull account) so nothing sensitive is in the code. The `data/` folders live **outside** the containers, so you can update or rebuild containers without losing the database or recordings.

**Work**
```bash
sudo mkdir -p /opt/callcenter/data/postgres /opt/callcenter/data/recordings /opt/callcenter/backups
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
SERVER_IP=192.168.1.100
PBX_IP=192.168.1.10
CDR__HOST=192.168.1.10
CDR__PORT=22
CDR__USERNAME=cdrpull
CDR__KEYPATH=/opt/callcenter/secrets/cdrpull
CDR__REMOTEPATH=/var/log/asterisk/cdr-csv/Master.csv
CDR__INTERVALSECONDS=300
RTP_PORT_MIN=10000
RTP_PORT_MAX=10100
RECORDING_RETENTION_DAYS=90
TZ=Asia/Hebron
```
Generate random values with `openssl rand -base64 48`.

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
    network_mode: host          # needed for SIP/RTP without port mapping
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
because the API image is private:
```bash
echo <your-github-token> | docker login ghcr.io -u dianawawreh35-cs --password-stdin
./update.sh v1.0 --pull
```
That pulls the image, starts it, waits for `/health`, and rolls back if it does
not come up. `postgres:16` is fetched from Docker Hub automatically.

> Even with internet on site, Option A is worth preferring for the **first**
> install: it removes any dependency on the restaurant's connection working on
> the day, and guarantees the exact `postgres:16` build you tested rather than
> whatever the tag points at that week.

Either way, wait for lines like `Migrations applied` and `Now listening on: http://0.0.0.0:80`. Ctrl+C to stop following the log (containers keep running).

---

## Step 7 — First-run seed

**What it does:** an empty database has no users. This one-time command creates the supervisor account, the four branches, the default classification types and the channels list. Then you confirm the web app works and set a real password.

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
  settings   7 created
  supervisor supervisor created
```
Safe to run twice - anything already there is left alone, and it refuses to
create a second supervisor once the users table has rows. `--branches` is
optional (it defaults to Branch 1-4) and is ignored if branches already exist.
`dotnet CallCenter.Server.dll seed --help` lists every option.

**Change the password immediately after logging in** - it was typed on the
command line and is in this machine's shell history.

From a laptop browser: `http://192.168.1.100` → log in → **change the password** → check Settings shows the branches, types and channels.

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

## Step 8 — Connect the PBX (CDR import)

**What it does:** Asterisk writes one line to a CSV file as each call ends. Your server fetches that file over SFTP every few minutes and turns the calls that never reached an agent into abandoned-call records and call-back tasks. Nothing is opened on the PBX — the connection is outbound from your server, the same direction as a phone registering.

This is the only method: AMI and direct database access need an inbound port and are ruled out, and the call-back extension was removed (SRS 4.5).

**Prerequisite — the server's VPN connection**
The PBX is only reachable over the VPN, so set this up first and confirm it:
```bash
ping -c 3 10.8.0.1                 # the PBX on the VPN
nc -vz 10.8.0.1 22                 # SSH/SFTP port reachable
```
Make the VPN start on boot, so a power cut does not leave the server silently cut off from the PBX.

### 8.1 Check the PBX is writing the CDR file

On the Issabel box:
```bash
cat /etc/asterisk/cdr.conf | grep -Ev '^\s*(;|$)'
ls -l /var/log/asterisk/cdr-csv/
```
You need `[csv]` enabled and, in it:
```ini
[csv]
usegmtime=no
loguniqueid=yes
loguserfield=yes
```
**`loguniqueid=yes` is not optional.** It puts Asterisk's `uniqueid` in every row, which is the key the importer uses to avoid inserting the same call twice. Without it there is no stable key and duplicate protection is lost.

If you change `cdr.conf`:
```bash
asterisk -rx "module reload cdr_csv"
asterisk -rx "cdr show status"
```

### 8.2 Create a restricted SFTP account on the PBX

An account that can read the CDR directory and nothing else. On the Issabel box, as root:
```bash
useradd -r -m -d /home/cdrpull -s /sbin/nologin cdrpull
mkdir -p /home/cdrpull/.ssh
chmod 700 /home/cdrpull/.ssh
usermod -a -G asterisk cdrpull          # read access to the CDR directory
```
Then in `/etc/ssh/sshd_config`, restrict it to SFTP only:
```
Match User cdrpull
    ForceCommand internal-sftp
    PasswordAuthentication no
    AllowTcpForwarding no
    X11Forwarding no
```
```bash
systemctl restart sshd
```

Read-only and key-only on purpose: this account exists to pull one file, and it should not be able to do anything else if the key ever leaks.

### 8.3 Generate the key on your server and install it on the PBX

On **your server**:
```bash
ssh-keygen -t ed25519 -f /opt/callcenter/secrets/cdrpull -N '' -C 'callcenter cdr import'
chmod 600 /opt/callcenter/secrets/cdrpull
cat /opt/callcenter/secrets/cdrpull.pub
```
Copy that public key onto the **PBX**, into `/home/cdrpull/.ssh/authorized_keys`:
```bash
chown -R cdrpull:cdrpull /home/cdrpull/.ssh
chmod 600 /home/cdrpull/.ssh/authorized_keys
```
The private key never leaves your server.

### 8.4 Verify the connection

From your server:
```bash
sftp -i /opt/callcenter/secrets/cdrpull cdrpull@10.8.0.1
sftp> ls -l /var/log/asterisk/cdr-csv/
sftp> bye
```
You should see `Master.csv` and its size. If it refuses, the usual causes are the key not installed, the group membership missing, or `sshd_config` not reloaded.

### 8.5 Settings the server needs

In `/opt/callcenter/.env`:
```ini
CDR__HOST=10.8.0.1
CDR__PORT=22
CDR__USERNAME=cdrpull
CDR__KEYPATH=/opt/callcenter/secrets/cdrpull
CDR__REMOTEPATH=/var/log/asterisk/cdr-csv/Master.csv
CDR__INTERVALSECONDS=300
```
`docker compose up -d` after any change to `.env`.

The import interval is also a supervisor setting (S-47) once the app is running; the `.env` value is the starting point.

### 8.6 Before the importer is written — read real rows

**Do this before any parsing work.** The rule for "this call was abandoned" cannot be guessed: `disposition = 'NO ANSWER'` is not sufficient, because an inbound route or IVR that answers before the queue makes Asterisk record `ANSWERED` even though no agent ever spoke.

Make three calls to the restaurant number:
1. one **answered by an agent**
2. one **hung up while still ringing**
3. one **left until the queue timeout**

Then:
```bash
tail -n 20 /var/log/asterisk/cdr-csv/Master.csv
```
Write down what distinguishes the three — `lastapp`, `billsec`, `disposition`, and whether any column carries a usable **wait time**. The wait time decides whether report R-21 can show a service-level percentage or only counts.

That observation is what the importer is written against.

**Verify once it is running**
Supervisor app → Settings → PBX status: last import time and rows read.
```bash
docker compose logs api | grep -i cdr | tail
```
Test: call the restaurant number and hang up while it is still ringing. It should appear in the dashboard as **Abandoned** within one import interval — not instantly.

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
- Store in your password manager: `.env` contents, admin password, the `cdrpull` SSH key pair (step 8), the VPN accounts for the server and each laptop, the router reservation.
- Commit `docker-compose.yml`, `.env.example`, `backup.sh` and this runbook to the repo (never the real `.env`).
- Walk the supervisor through the dashboard for 1 hour; agents 1 hour.

---

## Updating later

**What it does:** swaps the API container for the new version in seconds. Database and recordings are untouched; migrations bring the schema up to date; agent apps update themselves at next launch. Phones never stop because they don't depend on the server.

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
