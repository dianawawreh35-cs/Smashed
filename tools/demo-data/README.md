# Demo data: something to look at in the reports, removable afterwards

The development database has a few dozen real test calls, which is too little
to see what the dashboard and the call reports look like with a working
restaurant behind them. `add.sql` writes a stretch of realistic traffic;
`remove.sql` takes exactly that away again. **Development only** — never run
`add.sql` against a database with real customers' calls in it.

```powershell
# Add 90 days, about 200 communications a day (a couple of minutes).
Get-Content -Raw tools\demo-data\add.sql | docker exec -i callcenter-db-dev psql -U callcenter -d callcenter -v dev=yes

# Or choose: -v days=30 -v per_day=500

# Take it all away again.
Get-Content -Raw tools\demo-data\remove.sql | docker exec -i callcenter-db-dev psql -U callcenter -d callcenter
```

`-v dev=yes` is required: without it `add.sql` refuses, so it cannot be run by
accident. It also refuses when demo data is already there (remove it first)
and when the database has not been seeded. `remove.sql` is safe to run twice.

## What it adds

- **Five demo agents**, logins `demo-agent-1` to `demo-agent-5`, shown as
  "سارة (تجريبي)" and so on. Their passwords are random and thrown away:
  nobody can sign in as them. Three have an open sign-in, so the dashboard's
  *Agents online* shows them.
- **Calls and messages** over the chosen days, in the restaurant's own hours
  (10:00–23:59, a lunch hump and an evening peak, Friday and Saturday busier).
  About 10% are messages on the apps; calls are mostly incoming, mostly
  answered, with missed, rejected, blocked, unanswered and failed ones among
  them, and notes on some. Most are from the old system's customers, a few
  hundred of whom call again and again; the rest are from about 300 numbers
  nobody saved.
- **Classifications**: every message, every answered outgoing call and about
  nine answered incoming calls in ten; about half are orders worth 25–200.
  Complaints carry a reason, some a follow-up, and about half are resolved by
  the supervisor. The rest are unclassified, so R-18 has something to show.
- **About 120 new customers** ("زبون تجريبي 1" …), saved by the demo agents
  during the period, each with a call or three, so R-16's *new* column is not
  empty.
- If it runs before the restaurant opens, **a couple of dozen communications
  since midnight**, so the dashboard's *today* has figures.

Branches, channels and types are the real ones.

## How it stays removable

Everything hangs off the five demo agents. Every demo call and message has one
as its agent; every demo customer was saved by one; the sign-ins are theirs.
`remove.sql` deletes those calls and messages (their classifications, change
history and notes go with them), those customers, those sign-ins and the
agents — and nothing else. Your own calls, `dia20`'s and the old system's
15,289 customers are never touched. If you edit or classify a demo call while
testing, the edit goes with it.

Two things to know while it is there:

- The demo agents are in the **Users** screen and the reports' agent filter.
  Don't give one a real extension or a password; remove the demo data instead.
- A real call from one of the demo customers' numbers (0599 88xxxx) would
  match that customer. `remove.sql` keeps such a call and only unlinks it.
