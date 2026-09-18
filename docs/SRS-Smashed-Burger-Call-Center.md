# Software Requirements Specification

**Restaurant Call Center System — Agent Softphone, Shared Contacts, Call Classification, Omnichannel Orders and Supervisor Dashboard**

- **Version:** 1.1 (agreed scope and commercial terms)
- **Date:** 14 September 2026
- **Prepared by:** Dia Nawawreh
- **Prepared for:** Smashed Burger Restaurant — Call Center
- **Status:** For signature. Items marked [TBC] are to be confirmed by the client before installation.

> Confidential — prepared for Smashed Burger Restaurant by Dia Nawawreh.
> This is the text of the signed SRS, transcribed to Markdown so requirement IDs
> are greppable and linkable from code. The authoritative copy is the PDF
> (`SRS-Smashed-Burger-Call-Center.pdf`); where they differ, the PDF governs.
> **Sections 12.1–12.9 contain commercial terms and signatures — do not publish
> this repository publicly without removing them.**

---

## 1. Introduction

### 1.1 Purpose

This document describes what the Restaurant Call Center System must do. It is the agreement between the client and the developer on scope: everything listed here is included; anything not listed is out of scope unless added by a signed change request.

### 1.2 Background

The call center takes orders, cancellations, complaints and inquiries by phone through an Issabel PBX hosted by an external telephony provider and reached over a VPN, and increasingly through messaging and delivery apps (WhatsApp, Facebook, Instagram, food-ordering apps such as Wheels). Today, agents use softphones, callers are looked up manually, calls are not classified, and there is no record of app-based communication or reporting for the supervisor.

### 1.3 Goals

- Show the agent who is calling — with history — before the call is answered.
- Record every call and classify every communication (phone and app) by type.
- Keep one shared customer list for all agents.
- Give the supervisor search, reports and statistics for any time period.
- Run entirely on the restaurant's local network with no monthly software fees.

### 1.4 Definitions

| Term | Meaning |
|---|---|
| Agent | Call-center employee taking calls and app orders. |
| Supervisor | Manager who reviews data, reports and configuration through the web app. |
| Agent App | Windows desktop application installed on each agent's laptop: softphone + call log + contacts + app orders. |
| Server | Mini PC on the restaurant LAN hosting the database, recordings, API and supervisor web app. |
| Classification | The type (Order, Cancellation, Complaint, Inquiry, …) and notes assigned to a call or app communication. |
| Communication | A phone call or an app-based interaction (WhatsApp, Facebook, Instagram, delivery app). |
| Branch | One of the restaurant's four branches the order or call relates to (list managed by the supervisor). |

---

## 2. System Overview

### 2.1 Components

- **Agent App (Windows desktop):** registers to the Issabel PBX across the VPN as the agent's SIP extensions, handles inbound and outbound calls, records calls, shows the incoming-call pop-up, holds the agent's call log, the shared contacts and the App Orders tab.
- **Server (mini PC, restaurant LAN):** PostgreSQL database, recordings storage, REST API used by the Agent Apps, nightly backups. It also needs its own VPN connection to the PBX for the call capture in 4.5 — see the note in 2.4.
- **Supervisor Web App:** browser application served by the server for search, reports, statistics, recordings access and configuration of the classification form.
- **Issabel PBX (hosted by the telephony provider):** not operated by the restaurant and not modified by this project. Issabel is an Asterisk distribution, so it offers standard SIP, and AMI and CDR for the server-side capture in 4.5 — but each of those has to be enabled and permitted by the provider (see 12.3).
- **VPN to the provider:** what the extensions register across. Every agent laptop runs a VPN client, and the server needs one too.

### 2.2 Users and roles

| Role | Permissions |
|---|---|
| Agent | Make and receive calls; classify; record; view and edit own communications (same day); view all contacts; add/edit contacts; enter app orders. |
| Supervisor | Everything an agent can see across all agents; search; reports; recordings; edit classification form; manage users, branches and extensions. |
| Administrator (technical) | Server settings, backups, updates. May be the same person as the supervisor or the developer. |

### 2.3 Extensions

Each agent has **one** PBX extension. It makes and receives every call the agent
handles — customers, other agents and the four branches alike. The Agent App
registers it at login and uses it for everything.

**Internal calls are told apart by the number, not by the extension.** The
supervisor keeps a list of the internal extension numbers — the other agents and
the branches — in the supervisor app (S-48). A call whose other party is on that
list is an internal call.

Internal calls are **recorded and kept** like any other: they appear in the
agent's call log and in the contact history, and the recording and classification
rules are unchanged. They are **left out of the customer-facing reports**, so
internal chatter does not distort order counts, complaint rates, call volumes or
the service level (R-01, R-20, R-21).

Keeping the distinction in a list rather than in a second extension means a new
branch number is a settings change, not a PBX change and a visit to four laptops.

### 2.4 Assumptions and constraints

- Five agents work on four Windows 10/11 laptops with headsets; some agents share a laptop across shifts. Laptops and server are on the same restaurant LAN; the PBX is not — it is reached over the VPN.
- The restaurant operates four branches; every call and app communication is assigned to a branch.
- The provider's Issabel gives two SIP extensions per agent (see 2.3) and delivers caller ID on incoming calls. Registration and audio travel over the VPN.
- **Each agent laptop runs a VPN client.** The agent must be connected before the phone works; a laptop that is not connected can still use contacts, the call log and the App Orders tab, but cannot make or receive calls.
- **The server would need a VPN connection of its own** for the capture of calls that never reach an agent (4.5), which the server does by talking to the PBX directly — a VPN on the laptops alone does not help it. That section is deferred pending the provider's answers, so the server does not need VPN access for anything being built now. If it never gets that access, section 4.5 and reports R-20 and R-21 cannot be delivered.
- An internet connection **is** required for calls, because the PBX is reached across it (see N-01). Everything that is not telephony — contacts, call log, classification, app orders, reports — keeps working without it. Messaging/delivery apps are handled by agents on their own devices; their content is entered manually into the App Orders tab (no automated integration in this version).
- The client is responsible for informing callers that calls are recorded (e.g., an announcement on the PBX).

---

## 3. Functional Requirements — Agent App

### 3.1 Login and SIP registration

| ID | Requirement | Priority |
|---|---|---|
| A-01 | Agent logs in with username and password issued by the supervisor. The app receives the agent's extension and SIP credentials from the server; the agent never types SIP details. | Must |
| A-02 | The app registers the agent's extension on the Issabel PBX across the VPN and shows its status (Registered / Failed) at all times; re-registers automatically after network or VPN drops. A refusal by the PBX and a PBX that cannot be reached are shown differently, because the first needs the supervisor and the second will clear itself. | Must |
| A-03 | Audio device selection (microphone, speaker, ring device) remembered per laptop. | Must |
| A-05 | Shared laptops: the app is installed once per laptop and supports any agent logging in. On login it registers the logged-in agent's two extensions; on logout it unregisters them. Audio device choices are per laptop; everything else (calls, classifications, queued uploads) belongs to the logged-in agent. Agents must log out at the end of their shift (automatic logout after a configurable idle time). | Must |
| A-04 | If the server is unreachable, the app continues to make/receive calls and record; log entries and classifications are queued locally and synchronised when the server returns. | Must |

### 3.2 Inbound calls and pop-up

| ID | Requirement | Priority |
|---|---|---|
| A-10 | On an incoming call the app brings a pop-up to the front (even if minimised) showing: caller number, matched contact (name, address, notes), branch, last five communications with type and notes, and totals (orders, complaints, cancellations). | Must |
| A-11 | Unknown numbers show "New customer" with an inline form to save name, address and notes; saving links the current call to the new contact. | Must |
| A-12 | Controls: Answer, Reject, Hang up, Mute, Hold. Call timer shown while connected. | Must |
| A-13 | Phone numbers are matched regardless of format (05…, +9705…, 02…, 972…). | Must |
| A-14 | Missed and rejected calls are logged with status Missed / Rejected. | Must |
| A-15 | Transfer to another extension (blind transfer). | Should |
| A-16 | VIP callers: the pop-up shows a clear VIP badge and the contact's notes at the top so the agent adjusts immediately. | Must |
| A-17 | Blocked callers: an incoming call from a number flagged Blocked is rejected automatically by the Agent App (no pop-up, no ringing); the call is logged with status Blocked and appears in the supervisor's reports. The block list is shared by all agents and cached locally so it works even when the server is unreachable. | Must |

### 3.3 Outbound calls

| ID | Requirement | Priority |
|---|---|---|
| A-20 | Dial from a dial box, from any phone number shown in the app (click-to-call) or from a contact. All calls use the agent's one extension (see 2.3). | Must |
| A-21 | Outbound calls are recorded and classified exactly like inbound calls. | Must |
| A-22 | Redial last number; call back from a missed-call entry with one click. | Should |

### 3.4 Call recording

| ID | Requirement | Priority |
|---|---|---|
| A-30 | Every answered call (inbound and outbound) is recorded from answer to hang-up, both directions. | Must |
| A-31 | Recording is uploaded to the server after the call and linked to the call record; a local copy is kept until upload is confirmed. | Must |
| A-32 | Recording failure never interrupts the call; the call is flagged "no recording". | Must |
| A-33 | Retention: recordings kept for 90 days (configurable by the supervisor, S-43), then deleted automatically; the call record and classification remain. | Must |

### 3.5 Call classification

| ID | Requirement | Priority |
|---|---|---|
| A-40 | At hang-up, the classification form opens automatically for inbound and outbound calls. The form's fields are defined by the supervisor (see S-40). Default fields: Type (Order, Cancellation, Complaint, Inquiry, Wrong number, Other), Branch (one of 4), Order value, Notes, Follow-up required. | Must |
| A-41 | The agent may skip; the call remains "Unclassified" and is highlighted in the call log until classified. | Must |
| A-42 | Agents can classify or edit the classification of their own calls for the current day only. Older entries are read-only for agents; supervisors can edit at any time. | Must |
| A-43 | Every classification change is stored with who changed it and when (audit trail). | Must |

### 3.6 Call log (agent's own calls)

| ID | Requirement | Priority |
|---|---|---|
| A-50 | List of the agent's own calls (both extensions): date/time, direction, number, contact, duration, status, type, notes, recording indicator. Filter by date and search by number/name. | Must |
| A-51 | Open any own call: view details, play the recording (play/pause/seek), and classify or edit classification subject to A-42. | Must |
| A-52 | Agents cannot see other agents' calls. | Must |

### 3.7 Contacts (shared)

| ID | Requirement | Priority |
|---|---|---|
| A-60 | Contact fields: name, phone numbers (one or more), address, notes, flags VIP and Blocked (set by the supervisor only, see S-45), created by / date. | Must |
| A-61 | All agents see all contacts saved by any agent. Search by name, number or address. | Must |
| A-62 | Contact details page: information plus the full history of communications (calls and app orders) from all agents, with type and notes. Recordings on this page are playable only for the viewing agent's own calls; supervisors can play all. | Must |
| A-63 | Create and edit contacts. A phone number already on another contact is refused, and the refusal names the contact that holds it so the agent can open it instead. A **matching name** is a warning, never a refusal: when a new contact is given a name an existing contact already has, the agent is shown those contacts and chooses — add this number to that person, or save a separate contact. Names are never matched automatically: common names are common, and silently merging two customers would mix their order histories with no way to unpick them. Merging two existing contacts is a separate, deliberate action. | Must |
| A-64 | Import contacts from Excel/CSV (initial load by the supervisor). | Should |

### 3.8 App Orders tab (WhatsApp, Facebook, Instagram, delivery apps)

| ID | Requirement | Priority |
|---|---|---|
| A-70 | Agents record communications that did not arrive by phone. Fields: Channel (WhatsApp, Facebook, Instagram, Wheels, other app — list managed by supervisor), Type (Order, Cancellation, Complaint, Inquiry, Other — same list as calls), Customer (existing contact or new: name, phone, address), Branch (one of 4), Order value, Notes, date/time (defaults to now). | Must |
| A-71 | List of the agent's own app communications for the current day, with edit; older entries read-only for agents (same rule as calls). | Must |
| A-72 | App communications appear in the contact's history alongside calls, and in all supervisor reports with Channel = app name; phone calls have Channel = Phone. | Must |
| A-73 | Quick entry: start typing a phone number to pick the customer; defaults remembered per session (channel, branch). | Should |

### 3.9 General

| ID | Requirement | Priority |
|---|---|---|
| A-80 | Interface in Arabic (right-to-left) with English option; all labels switchable. | Must |
| A-81 | Pop-up appears within one second of the call ringing. | Must |
| A-82 | Automatic updates of the Agent App from the server. | Should |
| A-83 | Agent status indicator (Available / On call / Away) visible to the supervisor. | Could |

---

## 4. Functional Requirements — Supervisor Web App

### 4.1 Access and search

| ID | Requirement | Priority |
|---|---|---|
| S-01 | Login with supervisor account (browser on the LAN). Session expires after inactivity. | Must |
| S-02 | Global search across calls, app communications and contacts: by phone number, customer name, agent, branch, channel, type, date range, notes text, order value range, status, has recording. | Must |
| S-03 | Every communication opens with full details: agent, customer, branch, channel, date/time, duration (calls), type, notes, order value, recording player, and the audit trail of changes. | Must |
| S-04 | Play and download recordings for any agent; edit any classification at any time (logged). | Must |
| S-05 | Export any list or report to Excel/CSV. | Must |
| S-06 | Every report is shown as a table and, where the data is numeric, as a chart (bar, line, pie or heat table as appropriate). Charts update with the selected filters and can be downloaded as an image for presentations. | Must |
| S-07 | Statistics use a common set of filters available on every report: time period (day, week, month, custom range), agent, branch, channel, type. Grouping selector (per day / week / month) where applicable. | Must |

### 4.2 Reports

All filterable by time period; also by agent, branch, channel where applicable.

**Requested by the client:**

| ID | Report |
|---|---|
| R-01 | Number of communications in the period: total, calls vs app, inbound vs outbound, answered vs missed. |
| R-02 | List of all calls and communications with all related details: agent, customer, branch, channel, date, time, duration, type, notes, order value, recording. |
| R-03 | Communications per type (Order, Cancellation, Complaint, Inquiry, Wrong number, Other), with percentage share. |
| R-04 | Statistics per day, per type, per agent; recurring customers (customers with more than one communication in the period, ranked). |
| R-05 | Problems: complaints list with customer, agent, branch, notes, follow-up status; complaints per branch and per agent; repeat complainers. |

**Proposed additional reports (included unless the client removes them):**

| ID | Report |
|---|---|
| R-10 | Peak hours: communications per hour of day and per weekday (heat table) — for staffing. |
| R-11 | Missed calls: calls not answered by an agent, calls abandoned in the queue, and calls overflowed at timeout (see 4.5) — count, list, and time until the customer was called back (or "never"). |
| R-12 | Orders by channel: Phone vs WhatsApp vs Facebook vs Instagram vs delivery apps — count and total order value; trend over time. |
| R-13 | Order value: total and average per channel, per branch, per agent, per day. |
| R-14 | Cancellation rate: cancellations ÷ orders, per branch, channel and agent; cancellation notes list. |
| R-15 | Agent productivity: calls handled, average call duration, orders taken, order value, unclassified count, missed calls on their extension. |
| R-16 | Customer base: new vs returning customers per period; top customers by orders and by order value; inactive customers (no order in N days) as a win-back list. |
| R-17 | Complaint handling: open vs closed follow-ups, time to close, complaints per 100 orders. |
| R-18 | Data quality: unclassified communications, calls from unknown numbers (not saved as contacts), duplicate contacts. |
| R-19 | Daily summary sent by e-mail to the supervisor (yesterday's totals by type and channel, complaints, missed calls) [Could — requires internet or local mail]. |
| R-20 | *(deferred with 4.5)* Abandoned and overflowed calls: count, rate (÷ inbound calls), average and maximum wait time, per hour and per day; list with call-back status. Real-time with AMI, otherwise as of the last CDR import (see 4.5). |
| R-21 | *(deferred with 4.5)* Queue service level: percentage of inbound calls answered within N seconds (N configurable), per day and per hour. Requires AMI or CDR with wait times. |

### 4.3 Dashboard

| ID | Requirement | Priority |
|---|---|---|
| S-20 | Home page with today's figures: communications by type and channel, orders and order value, complaints, missed calls, unclassified count, agents online. Charts for the selected period: communications per day (line), per type (bar/pie), per channel (bar), per hour (bar). | Must |

### 4.4 Configuration

| ID | Requirement | Priority |
|---|---|---|
| S-40 | Edit the classification form used by agents: add/remove/rename types; add custom fields (text, number, dropdown, checkbox); set required fields; the change applies to new classifications without reinstalling the Agent App. | Must |
| S-41 | Manage channels list (WhatsApp, Facebook, Instagram, Wheels, …) and branches (4 at start; add/rename/disable). | Must |
| S-42 | Manage agents: create/disable accounts, assign the agent's extension with its SIP credentials (see 2.3), reset passwords. | Must |
| S-43 | Recording retention period (default 90 days) and storage usage view. | Must |
| S-44 | Backup now / view last backup status. | Should |
| S-45 | Mark a contact (or a bare phone number) as VIP or Blocked, with a reason and date; remove the flag at any time; list of all blocked and VIP numbers. Agents can see the flags but cannot change them. Every change is logged. **A reason is required** whenever a flag is set — a block nobody can account for is one nobody later dares remove; removing a flag needs none, because the removal itself is logged with who and when. **VIP and Blocked are mutually exclusive** and setting both at once is refused: they ask the Agent App for opposite behaviour (A-16 shows a badge, A-17 rejects the call) and there is no sensible winner. A number given for flagging is matched against existing contacts the way an incoming caller is (A-13), so flagging `0599…` flags the customer already saved as `+970599…`; only a number that matches nobody creates a nameless contact to carry the flag. The supervisor can see the **full history of flag changes** on a contact, including removals. Flagging is done **from the contact itself** in the contact list, and asks for the reason at that moment; the list of flagged numbers is that same contact list filtered to VIP or Blocked, so searching and filtering compose. | Must |
| S-46 | Export the blocked list in a format the provider can load into Issabel's blacklist, so the numbers can optionally be blocked at the PBX level as well (see note in section 6). | Should |
| S-48 | Internal numbers list: the supervisor maintains the list of internal extension numbers — the other agents and the four branches (see 2.3). A call whose other party is on the list is an internal call: still recorded, still in the agent's call log and the contact history, but excluded from the customer-facing reports so it does not distort order counts, complaint rates, volumes or the service level. Entered as plain numbers, one per entry; every change is logged with who and when. | Must |
| S-47 | System settings: view and change the values the system reads at runtime — the PBX host the Agent Apps register to (SRS 2.3), the call-back extension, the idle-logout time (A-05), how long an agent may edit their own classification (A-42), the service-level threshold (R-21) and the recording retention period (S-43). Changes take effect without a redeployment: the Agent App picks up a new PBX host at the next sign-in, so a change by the provider does not need a visit to each laptop. Every change is logged with who and when. The AMI connection settings belong with section 4.5 and are deferred with it. | Must |

### 4.5 Capture of calls that never reach an agent (queue / ring-group)

> **Deferred — decision pending (16 September 2026).** How this is done, and
> whether it can be done at all, depends on answers the telephony provider has
> not given yet (see 12.3). Nothing in this section is being built until they
> are in. It is deferred, not dropped: the requirements below stand as written
> and are the starting point once the answers arrive.
>
> What is blocked with it: **R-20** and **R-21**, and the server's own VPN
> connection, which is only needed for this section.

Calls that are abandoned while waiting in the queue, or that time out because all agents are busy, never ring an agent's extension and therefore cannot be seen by the Agent App. They are captured on the server side as follows.

**Primary method — real-time via PBX AMI (preferred)**

| ID | Requirement | Priority |
|---|---|---|
| S-50 | The server connects to the provider's Issabel through AMI (Asterisk Manager Interface, TCP 5038) across the VPN and records every inbound call that ends before reaching an agent: caller number, date/time, queue or ring group, wait time, and whether the caller hung up (abandoned) or was moved by the PBX at timeout (overflowed). Stored as communications with status Abandoned / Overflowed, matched to contacts, visible in search, contact history and reports within seconds. | Must |
| S-51 | Each abandoned or overflowed call creates a call-back task assigned to the supervisor (or the next available agent, configurable). The task closes automatically when an outbound call to that number is made, or manually. | Should |
| S-52 | Where the PBX offers database read access (Database Grant) instead of, or in addition to, AMI, the server may read the PBX call records directly at hang-up to obtain the same information. | Could |

**Alternative — if AMI / database access is not available on the client's PBX**

AMI is part of Asterisk and Issabel enables it by default, but the provider still has to create a user for us and permit the server's VPN address. If the provider will not do that (to be confirmed before installation, see 12.3), the following applies instead of S-50 and the real-time requirement is waived:

| ID | Requirement | Priority |
|---|---|---|
| S-55 | CDR import: Issabel keeps its call detail records in the `cdr` table of the `asteriskcdrdb` MySQL database. The server reads them over the VPN — by read-only database access, or from an export the provider places where the server can reach it — at least daily and creates Abandoned / Overflowed records for inbound calls with no answered agent leg. Reports include these calls with the delay of the import (not real-time). | Must |
| S-56 | Call-back extension: the client's PBX person sets the queue/ring-group timeout or failover destination to a dedicated extension registered by the server. The server answers, plays a short message ("all agents are busy, we will call you back"), hangs up, and immediately creates a communication record and a call-back task with the caller's number. This captures in real time every caller who waits until the timeout; callers who hang up before the timeout appear only through S-55. | Should |
| S-57 | Ring-group option (client decision): if the PBX is configured as a ring group with "ring all" instead of a queue, every inbound call rings all free agents' Agent Apps; a call that nobody answers is logged by the Agent Apps as Missed with the caller number, in real time, without AMI. Hold music and position announcements are lost in this configuration. | Info |

**Dependency statement:** real-time detection of calls that never reach an agent depends on the client's PBX providing AMI or database access. If it does not, such calls are reported from the PBX's CDR with a delay and, where the client enables the call-back extension, captured in real time for calls reaching the timeout. This is a documented dependency and not a defect of the system.

---

## 5. Non-Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| N-01 | Network dependency: the database, recordings, API and supervisor app run on the restaurant LAN and keep working with no internet at all — call log, contacts, classification, app orders and reports are unaffected. Telephony is different: the PBX is reached across the internet over the VPN, so calls depend on the internet line, the VPN and the provider. Losing any of them stops calls; it stops nothing else. | Must |
| N-02 | Performance: pop-up under 1 s; report generation under 5 s for one year of data at the expected volume (up to ~500 communications/day). | Must |
| N-03 | Capacity: 5 agents initially, designed for up to 20 without changes; one server. | Must |
| N-04 | Availability: the phone function of the Agent App never depends on the server being up (see A-04). It does depend on the VPN and the provider (N-01), which are outside the developer's control — the agreed uptime and support hours for them are the client's arrangement with the provider (see 12.3). | Must |
| N-04a | The Agent App shows at all times whether the phone is usable, and says which part is missing — not connected to the VPN, not registered, or server unreachable — so an agent can tell a network problem from a broken application (see A-02). | Must |
| N-05 | Security: role-based access; passwords hashed; recordings served only to authorised users through the API (never as shared folders); SIP credentials stored encrypted on the server and never shown to agents. | Must |
| N-06 | Audit: all edits to classifications and contacts record user and time. | Must |
| N-07 | Backup: nightly automatic backup of database and recordings to a second disk or client-provided location; restore procedure documented and tested at handover. | Must |
| N-08 | Data retention: communications and contacts kept indefinitely; recordings 90 days by default (S-43). | Must |
| N-09 | Language: Arabic (RTL) and English for both applications. | Must |
| N-10 | Platforms: Agent App on Windows 10/11; Supervisor Web App on current Chrome/Edge; server on Linux (Ubuntu Server) or Windows on a mini PC with SSD. PBX: Issabel (Asterisk), hosted and operated by the telephony provider. | Must |
| N-11 | Maintainability: single installer for the Agent App; updates deployed from the server; configuration without code changes. | Should |

---

## 6. External Interfaces

- **Issabel — calls:** standard SIP registration and calls across the VPN (two extensions per agent). No PBX add-on or licence required. Caller ID as delivered by the provider's trunks.
- **Blocked numbers — note:** rejection by the Agent App means the PBX still receives the call and, in a queue, may offer it to other agents or send it to the failover destination after all apps reject it. Blocking at the PBX level (Issabel's blacklist, applied by the provider using the export in S-46) stops the call before it enters the queue and is recommended for persistent nuisance callers.
- **Issabel — server side:** AMI (TCP 5038, read-only events) for calls that never reach an agent; alternatively read-only access to the `asteriskcdrdb` database or a CDR export, and/or a call-back extension (see 4.5). All of it reaches the PBX over the VPN, so the server needs its own VPN connection. No licensed Issabel add-on is required — the Contact Center (Asternic) module would provide the same figures ready-made, but this project does not depend on it.
- **Messaging and delivery apps:** manual entry in this version. Automated WhatsApp Business API, Instagram/Facebook and delivery-app integrations are possible future phases (they require internet access, business verification and per-message fees).
- **POS:** not integrated in this version; order value is entered by the agent. A future phase may import customers or order totals from the POS if it offers an export or API.

---

## 7. Deliverables and Phases

| Phase | Content |
|---|---|
| Phase 1 | Server installation; Agent App with SIP (two extensions), pop-up, recording, classification, own call log, shared contacts; Supervisor Web App with search, recordings, R-01 to R-05, dashboard, classification form editor, user management. |
| Phase 2 | App Orders tab and channel reporting (R-12 to R-14); proposed reports R-10, R-11, R-15 to R-18; contact import; auto-update. |
| Phase 3 (optional) | Transfer, agent status, daily e-mail summary, POS import, WhatsApp/social integrations — quoted separately. |

**Included at handover:** installation of the server software and the Agent App on the four laptops, initial contact import, agent training (1 hour), supervisor training (1 hour), user guide (Arabic), backup and restore procedure. Support and feature-tweak terms are in section 12.

---

## 8. Acceptance Criteria

- A call from a mobile phone to the restaurant number pops up on the correct agent's screen within one second, showing the saved customer's name, address and last communications.
- The call is answered with two-way audio, recorded, and the recording is playable from the agent's call log and from the supervisor web app.
- The classification form opens at hang-up; the saved type and notes appear in the contact's history and in the supervisor's reports for the same day.
- An agent cannot open another agent's calls; an agent cannot edit yesterday's classification; the supervisor can.
- The supervisor marks a test number as Blocked: the next call from it is rejected without ringing any agent and appears in reports as Blocked. Marking it VIP shows the VIP badge in the pop-up.
- An app order entered on the App Orders tab appears in the contact's history and in the channel report.
- The supervisor changes the classification form (e.g., adds a type); the next call's form on the agent's screen shows the change.
- Reports R-01 to R-05 produce correct figures and charts for a test day with known calls, filterable by branch, and export to Excel.
- Two agents use the same laptop in sequence: after logout/login the correct extensions register and each agent sees only their own calls.
- A call that rings in the queue and hangs up before any agent answers appears in the supervisor's missed/abandoned report — immediately with AMI, or after the next CDR import in the alternative method.
- The server is switched off: agents can still receive, make and record calls; when it is switched on, the queued entries appear.
- Backup runs and a restore on a test machine brings back the data.

---

## 9. Out of Scope (this version)

- Automated integration with WhatsApp, Facebook, Instagram or delivery apps.
- POS integration; menu, pricing or delivery management.
- Remote access from outside the restaurant network (can be added with a VPN).
- IVR, queues, ring-group changes or any PBX reconfiguration beyond creating extensions.
- Mobile apps for agents.
- Payment processing.

---

## 10. Open Questions for the Client [TBC]

- **PBX provider — to confirm before installation.** The list of internal extension numbers for S-48 is also needed from the client. These answers decide whether 4.5 and reports R-20/R-21 can be delivered at all:
  1. Can the **server** have its own VPN connection to the PBX, not just the agent laptops?
  2. Is **AMI** available (TCP 5038), with a user for us and our server's VPN address in the permitted list?
  3. Can we have **read-only access to the `asteriskcdrdb` database**, or a regular CDR export we can reach?
  4. Is incoming customer routing a **queue or a ring group**? (Determines which method in 4.5 applies.)
  5. What are the agreed **VPN uptime and support hours**, and who is called when the tunnel drops?
  6. Can the provider add a **recording announcement** before ringing agents, if the client wants one?
- PBX: confirm which of each agent's two extensions is the customer one and which is the internal one, and that internal dialling to the four branches works from the internal extension (SRS 2.3).
- Exact list of channels (WhatsApp, Facebook, Instagram, Wheels, others?) and of classification types to start with.
- Should supervisors be able to see live agent status and listen to live calls? (Not included by default.)

---

## 11. Recommendations from the Developer

- **Follow-up tasks:** when a call is classified as Complaint, automatically create a follow-up for the supervisor with a due time; missed calls create a call-back task. Cheap to build, high value for customer retention.
- **Delivery notes on the contact:** a dedicated "delivery instructions" field (gate code, landmark) separate from general notes.
- **Same-as-last-order:** show the last order's notes in the pop-up so the agent can offer "the usual".
- **Call outcome for outbound:** add No answer / Busy / Wrong number outcomes to outbound classification for clean call-back reporting.
- **Recording notice:** ask the provider to add a short "this call may be recorded" announcement on Issabel before ringing agents.
- **Spare hardware:** keep a second SSD with a recent backup; the server can be rebuilt in an hour.

---

## 12. Commercial Terms

### 12.1 Price

**Total price: USD 1,000 (one thousand US dollars), fixed.** Covers the development and delivery of everything in this document (Phases 1 and 2), installation, training, and the support and feature-tweak periods below. Phase 3 items are not included.

### 12.2 Payment schedule

| # | Milestone | Amount | Condition |
|---|---|---|---|
| 1 | On signing this document | 50% — USD 500 | Work starts after receipt. |
| 2 | First agent live | 30% — USD 300 | Server installed; one agent receiving calls with pop-up, recording and classification; supervisor web app reachable. |
| 3 | Acceptance | 20% — USD 200 | All four laptops installed, five agents configured, training done. Acceptance is confirmed in writing by the client, or automatically 14 days after installation if no blocking issue has been reported. |

### 12.3 What the client provides

- All hardware: server (mini PC with SSD), agent laptops, headsets, network, UPS if desired, and any replacement of failed hardware.
- PBX (Issabel, operated by the telephony provider) configuration and its maintenance: one working extension per agent with SIP credentials (see 2.3), one spare extension for testing, caller ID delivered on incoming calls, routing of incoming customer calls to the agents' extensions, outbound routes, and internal dialling between the agents and the branches. Recording announcement if wanted. The PBX is configured by the telephony provider, not by the developer; the client owns that relationship and any charges under it.
- **The VPN**: an account and client configuration for every agent laptop, and a separate connection for the server. Supplying, licensing and supporting the VPN is the client's and the provider's responsibility, not the developer's.
- For capture of calls that never reach an agent (4.5): an AMI user on Issabel (Manager Settings in the Issabel GUI, or `manager.conf`) with the server's VPN address in the permitted list — or, failing that, read-only access to the `asteriskcdrdb` database or a CDR export the server can reach, and optionally the call-back extension and its queue failover setting. The client confirms with the provider before installation which of these is available.
- LAN access between the laptops and the server, VPN access from both to the PBX, and administrator rights on the laptops for installing the app and the VPN client.
- Customer list for the initial import (Excel/CSV), if available.
- Decisions on all [TBC] items before installation.

### 12.4 What the developer provides

- Development of the Agent App and Supervisor Web App as specified in sections 3–5.
- Installation and configuration of the server software (database, API, web app, backups) on the client's server.
- Installation of the Agent App on the four agent laptops and configuration of the five agents and their extensions.
- Initial contact import, training, user guide, backup/restore procedure.

### 12.5 Support — 12 months

From the acceptance date, the developer provides support free of charge for twelve (12) months:

- Fixing defects: any behaviour that does not match this document.
- Help with using the system, re-installing the Agent App on a replaced laptop, restoring from backup.
- Remote first (screen sharing / phone); on-site when remote is not possible.
- Response within one working day, Sunday–Thursday, 9:00–17:00. Urgent "agents cannot work" issues are handled as a priority.

**Not covered by support:** hardware failures, PBX, network or internet problems, changes made by third parties, unavailability of AMI/CDR access on the PBX (see 4.5), data loss caused by not keeping backups on the client's side, and anything listed as Out of Scope. Such work, if requested, is billed separately at USD 30 per hour (minimum one hour).

After the 12 months, support is optional at USD 250 per year (same terms) or per hour as above.

### 12.6 Free feature tweaks — 6 months

From the acceptance date, the developer implements feature tweaks free of charge for six (6) months. Tweaks are small changes to existing functionality:

- Renaming labels, fields, types or channels; adding or removing options in existing lists.
- Changing field order, defaults, colours, layout of an existing screen, or the columns shown in an existing table.
- Adding a filter or a column to an existing report, or changing an existing chart type.
- Adjusting existing rules (e.g., the same-day edit window, retention period, session timeout).

**New features are not tweaks and are quoted separately,** for example: a new screen or tab, a new report or statistic, a new role or permission model, integrations (WhatsApp/social APIs, POS, delivery apps), remote access, mobile apps, live monitoring, changes requiring new database tables. The developer decides in good faith whether a request is a tweak or a feature and informs the client before starting.

After the 6 months, tweaks are billed at USD 30 per hour (minimum one hour), or included in a support agreement if agreed.

### 12.7 Ownership and licence

- The software, its source code and design remain the property of the developer, including all improvements made during the project and the support period.
- The client receives a perpetual, non-transferable licence to use the software at the client's restaurant call center, for any number of agents up to the number in section 5 (N-03), without further licence fees.
- The client's data (contacts, communications, recordings) belongs to the client and can be exported at any time.
- The developer may reuse the software for other customers and may mention the client by name as a reference customer. No demonstration of the client's installation or access to the client's data is required or permitted without the client's separate consent.

### 12.8 Changes to scope

Any addition or change to this document is agreed in writing (a short change request with description, price and delivery time) before work starts. Items marked [TBC] are clarifications, not scope changes, as long as they stay within the described functionality.

### 12.9 Signatures

By signing, both parties confirm that this document (sections 1–12) describes the agreed scope, deliverables, responsibilities and commercial terms of the Restaurant Call Center System project.

| Party | Name | Signature | Date |
|---|---|---|---|
| Client (Smashed Burger) | ____________________ | ____________________ | ____________ |
| Developer | Dia Nawawreh | ____________________ | ____________ |
