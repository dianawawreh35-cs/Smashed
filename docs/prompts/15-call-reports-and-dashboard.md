# Call reports and the dashboard (R-01 to R-21, S-05 to S-07, S-20)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

**The largest piece of the system still missing.** The supervisor can search
calls and play them, but has no figures. The dashboard (`DashboardPage.tsx`) is
a placeholder saying reports are coming, and none of the call reports exist.

Read, in the SRS: section **4.2 Reports** (R-01 to R-21, the client's own list
and the proposed ones); **S-05** (export), **S-06** (table and chart), **S-07**
(common filters), **S-20** (the dashboard), **N-02** (a year of data in under
5 seconds); section **7**, the phases (Phase 1 is R-01 to R-05 and the
dashboard, Phase 2 is R-10 to R-18); and the acceptance line that says *"Reports
R-01 to R-05 produce correct figures and charts for a test day with known calls,
filterable by branch, and export to Excel."* That line is the test this task has
to pass.

Then read the `docs/DECISIONS.md` entries on the call search (24 Sep),
Applications (25 Sep, commit `87b9453`) and the App logs split (25 Sep).

## Reuse the application reports; don't build a second set of machinery

The Application reports page was built on 25 Sep **so this task could reuse
it**. Start there:

- `ApplicationReportsService`: `Filter` is already S-07's common set, with a
  `Kind` that defaults to `App`. Call reports pass `Call`. The local-time rule
  (the restaurant's day, not UTC's) is written there once. Keep it one rule.
- `ReportCard.tsx`: one report as a table, a chart (Recharts, already a
  dependency) and an **Export CSV** button.
- `lib/csv.ts`: CSV with a byte-order mark, so Excel opens Arabic correctly.
- `ApplicationReportsPage.tsx`: the filters bar and page pattern.
- `CallSearchService`: R-02 ("all calls with all details") **is** the Calls
  page's search, plus an export of the whole filtered result. Not a second list.

If a report needs a query `ApplicationReportsService` can't express, extend the
shared service or pull the common part out. Don't copy it.

## What to build, in order

**Phase 1 first**, finished and tested, before anything from Phase 2:

1. **R-01**: communications in the period: total, calls vs app, inbound vs
   outbound, answered vs missed.
2. **R-02**: the full list, from the call search, exportable in full.
3. **R-03**: per type, with percentage share.
4. **R-04**: per day, per type, per agent; recurring customers, ranked.
5. **R-05**: problems: complaints with customer, agent, branch, notes and
   follow-up status; per branch, per agent; repeat complainers.
6. **S-20, the dashboard** (the home page): today's figures (communications by
   type and channel, orders and order value, complaints, missed calls,
   unclassified, agents online) and the four charts for a chosen period (per
   day, per type, per channel, per hour).

**Then Phase 2:** R-10 peak hours (a heat table by hour and weekday), R-11 missed
calls and time until called back, R-12 orders by channel, R-13 order value, R-14
cancellation rate, R-15 agent productivity, R-16 customer base, R-17 complaint
handling, R-18 data quality.

**Not in this task:** R-19 (daily e-mail, a *Could*). **R-20 and R-21, and the
abandoned half of R-11,** need the CDR import (S-55), which doesn't exist. Build
nothing that pretends otherwise (see the decisions below).

Every report is filterable by S-07's set (period, agent, branch, channel, type,
and a per day / week / month grouping where it applies), shown as a table and a
chart where the figures are numeric (S-06), and exportable (S-05). Charts: follow
the `dataviz` skill.

## Definitions to write down before the numbers

A report is only as right as what its words mean. Settle each of these in a
`docs/DECISIONS.md` entry and name it on the report itself, where a supervisor
would wonder:

- **Missed**: inbound `Missed` only, or `Rejected` too? **Never `NoAnswer`**:
  that's an outbound call nobody picked up (A-21).
- **Answered**, and **average call duration**: over answered calls only, and
  `duration_sec` is talk time (answered to ended).
- **An order**: type `Order`; **order value**: from the classification.
- **Unclassified**: an answered call with no classification (A-41).
- **Called back** (R-11): the next outbound call to the same normalised number
  after the missed one, by anyone, within what window?
- **Recurring / new / returning customer** (R-04, R-16): by contact, and a call
  from an unknown number is not a customer.
- **Agents online** (S-20): an open `agent_sessions` row, or more than that?
- **Duplicate contacts** (R-18): the same normalised name? The same number
  can't happen (unique index).

## Decisions to ask me before building

1. **Calls only, or calls and applications together where the SRS compares
   them?** Dia keeps Application reports separate. But R-01 asks for "calls vs
   app", R-12 compares Phone with WhatsApp and the other apps, R-13 has "per
   channel", and the dashboard shows "by type and channel". Recommend: call
   reports are calls only, except those few whose point is the comparison.
   Confirm.
2. **Layout:** one *Reports* page with the reports grouped (Overview, Customers,
   Agents, Problems, Data quality), or one page per report? Where it sits in
   the sidebar next to Application reports. Arabic names.
3. **Missed**: `Rejected` included or not, and R-11's call-back window.
4. **R-20, R-21 and abandoned calls**: leave them off until S-55, or show them
   greyed with "available once the PBX's call records are imported"? Recommend
   the second, so the supervisor knows they're coming, not missing.
5. **Excel**: CSV (opens in Excel, Arabic safe via the byte-order mark), or real
   `.xlsx`? The acceptance line says "Excel". Recommend CSV unless the client
   insists, since `.xlsx` is a library on the server for a file Excel opens either
   way.
6. **Agents online** on the dashboard: what counts.

## Speed (N-02): measure it, don't assume it

A year at about 500 communications a day is about 180,000 rows. The application
reports currently read rows and group them in memory, which is fine for today's
handful of messages and unknown at a year of calls.

**Before building the Phase 2 reports,** fill a scratch database (never
`callcenter`) with a year of synthetic calls in realistic shapes: busy evenings,
a few agents, most calls answered, some classified as orders. Time each report
endpoint over the full year. Anything over 2 seconds is grouped in SQL instead,
or given the index it lacks (migration and `SCHEMA.md`). Put the measurements in
the decision entry. `tools/load-probe/probe.py` shows the shape of a probe.

## When it is done

Everything in the house rules, and specifically:

- **The acceptance test, as a test.** A database-backed test builds one known day
  (a few agents, both branches, answered, missed, orders with values,
  complaints, one app message) and checks R-01 to R-05's figures exactly,
  **filtered by branch**, and that the export holds the same rows.
- A database-backed test per Phase 2 report against rows you created, including
  each definition's edge (a `NoAnswer` not counted as missed; an unknown number
  not counted as a customer).
- Web tests for the dashboard and the reports page: figures render, filters are
  sent to the server, export contains what the table shows.
- The speed measurements, written down.
- Checklist steps; **ask for a screenshot** of the dashboard and the reports in
  both languages.
- Shared files (`AppLayout.tsx`, the web language files, the reports service if
  another session is using it) are staged by name, and a commit says which parts
  are yours.
