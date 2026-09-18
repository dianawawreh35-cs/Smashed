# Releasing

How code gets from this repository to the restaurant's server.

There are two different actions and it matters which one you mean:

| You say | What happens | Changes the restaurant? |
| --- | --- | --- |
| **"push"** | commit + push to GitHub. CI builds and tests it. | no |
| **"release"** / **"release v1.2"** | push, then tag a version. GitHub builds and publishes the image. | no |
| **"deploy"** | copy the image to the server and run `update.sh`. | **yes** |

Nothing reaches the restaurant until you explicitly say *deploy*.

---

## "push" — save the work

```bash
git add -A
git commit -m "<why this change exists>"
git push
```

[.github/workflows/ci.yml](../.github/workflows/ci.yml) then runs, on clean machines:

- .NET build + all tests (Windows, because the WPF agent app needs it)
- web build, lint and tests (Ubuntu)
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
3. pushes it to `ghcr.io/dianawawreh35-cs/callcenter-api:v1.2` (and `:latest`)
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
permanent version number.

---

## "deploy" — put it on the restaurant's server

**This is the only step that affects people taking orders.** Do it when the
call centre is quiet, never mid-service.

Two routes — pick whichever suits the restaurant's network.

**A. Carry it over** (no internet needed on site). On your machine:

```bash
docker pull ghcr.io/dianawawreh35-cs/callcenter-api:v1.2
docker tag  ghcr.io/dianawawreh35-cs/callcenter-api:v1.2 callcenter-api:v1.2
docker save callcenter-api:v1.2 -o callcenter-api-v1.2.tar
scp callcenter-api-v1.2.tar admin@192.168.1.50:/opt/callcenter/
```

Then on the server:

```bash
ssh admin@192.168.1.50
cd /opt/callcenter
./update.sh v1.2
```

**B. Let the server fetch it** (needs internet on site, and the login below):

```bash
ssh admin@192.168.1.50
cd /opt/callcenter
./update.sh v1.2 --pull
```

[`update.sh`](../deploy/update.sh) backs up first, keeps the running version as
`:previous`, restarts, waits for `/health`, and rolls back automatically if the
new version never becomes healthy. `./update.sh --rollback` goes back
deliberately.

Full context in [DEPLOY-server-runbook.md](DEPLOY-server-runbook.md).

### Pulling a private image needs a login

Once, on whichever machine pulls:

```bash
echo $GITHUB_TOKEN | docker login ghcr.io -u dianawawreh35-cs --password-stdin
```

---

## What is not automated, and why

**Nothing deploys on push.** A live call centre should not change because
someone saved a file. The gap between *release* and *deploy* is the point: it is
where you decide the moment.

**GitHub cannot reach the server** — it sits behind the restaurant's router on a
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

---

## Building the Agent App for the laptops

The Agent App is **not** part of the Docker release — that image is the server
and the supervisor web app. The desktop app is built and copied to the four
laptops by hand (SRS §11, N-11).

```powershell
dotnet publish src/CallCenter.AgentApp/CallCenter.AgentApp.csproj `
  -c Release -r win-x64 --self-contained true `
  -o publish/agent-app
```

**Self-contained on purpose.** The app targets `net10.0-windows`, and
`--self-contained true` bundles the runtime with it — about 185 MB and roughly
190 runtime assemblies. The alternative is installing the .NET 10 Desktop
Runtime on every laptop first, which is one more thing to get wrong on a machine
you may not be sitting at. For four laptops the larger folder is the cheaper
trade.

### Before the first install, check with the client's IT

Executables built in a user profile are blocked on some managed Windows
machines — this was hit during development on the developer's own laptop, where
`dotnet run` failed with *Access is denied* while `dotnet <dll>` worked. The
same policy would block the Agent App at install time.

Ask before installation day: *is there a policy blocking unsigned executables,
and what is the approval process for our application?* Code-signing the
executable is the usual answer, and it is not something to discover on the day.
