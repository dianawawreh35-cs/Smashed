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

The call center takes orders, cancellations, complaints and inquiries by phone through a Yeastar S20 PBX, and increasingly through messaging and delivery apps (WhatsApp, Facebook, Instagram, food-ordering apps such as Wheels). Today, agents use softphones, callers are looked up manually, calls are not classified, and there is no record of app-based communication or reporting for the supervisor.

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

- **Agent App (Windows desktop):** registers to the Yeastar S20 as the agent's SIP extensions, handles inbound and outbound calls, records calls, shows the incoming-call pop-up, holds the agent's call log, the shared contacts and the App Orders tab.
- **Server (mini PC, LAN):** PostgreSQL database, recordings storage, REST API used by the Agent Apps, nightly backups.
- **Supervisor Web App:** browser application served by the server for search, reports, statistics, recordings access and configuration of the classification form.
- **Yeastar S20 PBX (existing):** unchanged. Used as a standard SIP server; no PBX add-ons or licences required.

### 2.2 Users and roles

| Role | Permissions |
|---|---|
| Agent | Make and receive calls; classify; record; view and edit own communications (same day); view all contacts; add/edit contacts; enter app orders. |
| Supervisor | Everything an agent can see across all agents; search; reports; recordings; edit classification form; manage users, branches and extensions. |
| Administrator (technical) | Server settings, backups, updates. May be the same person as the supervisor or the developer. |

### 2.3 Extensions

Each agent has two PBX extensions: one used for inbound calls (rings in the queue/ring group) and one used for outbound calls. The Agent App registers both at login and uses the correct one automatically: incoming calls arrive on the inbound extension; clicking a number or dialling uses the outbound extension. Both are tied to the same agent account, so all calls appear in one log.

### 2.4 Assumptions and constraints

- Five agents work on four Windows 10/11 laptops with headsets; some agents share a laptop across shifts. Laptops and server are on the same LAN as the S20.
- The restaurant operates four branches; every call and app communication is assigned to a branch.
- The S20 provides standard SIP (UDP 5060) and two extensions per agent. Trunks deliver caller ID.
- No internet connection is required for daily operation. Messaging/delivery apps are handled by agents on their own devices; their content is entered manually into the App Orders tab (no automated integration in this version).
- The client is responsible for informing callers that calls are recorded (e.g., an announcement on the PBX).

---

## 3. Functional Requirements — Agent App

### 3.1 Login and SIP registration

| ID | Requirement | Priority |
|---|---|---|
| A-01 | Agent logs in with username and password issued by the supervisor. The app receives the agent's two extensions and SIP credentials from the server; the agent never types SIP details. | Must |
| A-02 | The app registers both extensions on the S20 and shows their status (Registered / Failed) at all times; re-registers automatically after network drops. | Must |
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
| A-20 | Dial from a dial box, from any phone number shown in the app (click-to-call) or from a contact. Uses the outbound extension. | Must |
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
| A-63 | Create and edit contacts; duplicate detection by phone number with an option to merge. | Must |
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
| R-20 | Abandoned and overflowed calls: count, rate (÷ inbound calls), average and maximum wait time, per hour and per day; list with call-back status. Real-time with AMI, otherwise as of the last CDR import (see 4.5). |
| R-21 | Queue service level: percentage of inbound calls answered within N seconds (N configurable), per day and per hour. Requires AMI or CDR with wait times. |

### 4.3 Dashboard

| ID | Requirement | Priority |
|---|---|---|
| S-20 | Home page with today's figures: communications by type and channel, orders and order value, complaints, missed calls, unclassified count, agents online. Charts for the selected period: communications per day (line), per type (bar/pie), per channel (bar), per hour (bar). | Must |

### 4.4 Configuration

| ID | Requirement | Priority |
|---|---|---|
| S-40 | Edit the classification form used by agents: add/remove/rename types; add custom fields (text, number, dropdown, checkbox); set required fields; the change applies to new classifications without reinstalling the Agent App. | Must |
| S-41 | Manage channels list (WhatsApp, Facebook, Instagram, Wheels, …) and branches (4 at start; add/rename/disable). | Must |
| S-42 | Manage agents: create/disable accounts, assign inbound and outbound extensions with SIP credentials, reset passwords. | Must |
| S-43 | Recording retention period (default 90 days) and storage usage view. | Must |
| S-44 | Backup now / view last backup status. | Should |
| S-45 | Mark a contact (or a bare phone number) as VIP or Blocked, with a reason and date; remove the flag at any time; list of all blocked and VIP numbers. Agents can see the flags but cannot change them. Every change is logged. | Must |
| S-46 | Export the blocked list in a format suitable for the S20 blocklist, so the client's PBX person can optionally block the numbers at the PBX level as well (see note in section 6). | Should |

### 4.5 Capture of calls that never reach an agent (queue / ring-group)

Calls that are abandoned while waiting in the queue, or that time out because all agents are busy, never ring an agent's extension and therefore cannot be seen by the Agent App. They are captured on the server side as follows.

**Primary method — real-time via PBX AMI (preferred)**

| ID | Requirement | Priority |
|---|---|---|
| S-50 | The server connects to the Yeastar S20 through AMI (Asterisk Manager Interface, TCP 5038) and records every inbound call that ends before reaching an agent: caller number, date/time, queue or ring group, wait time, and whether the caller hung up (abandoned) or was moved by the PBX at timeout (overflowed). Stored as communications with status Abandoned / Overflowed, matched to contacts, visible in search, contact history and reports within seconds. | Must |
| S-51 | Each abandoned or overflowed call creates a call-back task assigned to the supervisor (or the next available agent, configurable). The task closes automatically when an outbound call to that number is made, or manually. | Should |
| S-52 | Where the PBX offers database read access (Database Grant) instead of, or in addition to, AMI, the server may read the PBX call records directly at hang-up to obtain the same information. | Could |

**Alternative — if AMI / database access is not available on the client's PBX**

If the client's S20 does not expose AMI or database access (to be confirmed by the client before installation, see 12.3), the following applies instead of S-50 and the real-time requirement is waived:

| ID | Requirement | Priority |
|---|---|---|
| S-55 | CDR import: the PBX stores or exports its call detail records (CDR) to a network folder on the server (S20 Storage / CDR export). The server imports them at least daily and creates Abandoned / Overflowed records for inbound calls with no answered agent leg. Reports include these calls with the delay of the import (not real-time). | Must |
| S-56 | Call-back extension: the client's PBX person sets the queue/ring-group timeout or failover destination to a dedicated extension registered by the server. The server answers, plays a short message ("all agents are busy, we will call you back"), hangs up, and immediately creates a communication record and a call-back task with the caller's number. This captures in real time every caller who waits until the timeout; callers who hang up before the timeout appear only through S-55. | Should |
| S-57 | Ring-group option (client decision): if the PBX is configured as a ring group with "ring all" instead of a queue, every inbound call rings all free agents' Agent Apps; a call that nobody answers is logged by the Agent Apps as Missed with the caller number, in real time, without AMI. Hold music and position announcements are lost in this configuration. | Info |

**Dependency statement:** real-time detection of calls that never reach an agent depends on the client's PBX providing AMI or database access. If it does not, such calls are reported from the PBX's CDR with a delay and, where the client enables the call-back extension, captured in real time for calls reaching the timeout. This is a documented dependency and not a defect of the system.

---

## 5. Non-Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| N-01 | Local operation: all components run on the restaurant LAN; no internet dependency for calls, pop-up, recording, classification or reports. | Must |
| N-02 | Performance: pop-up under 1 s; report generation under 5 s for one year of data at the expected volume (up to ~500 communications/day). | Must |
| N-03 | Capacity: 5 agents initially, designed for up to 20 without changes; one server. | Must |
| N-04 | Availability: the phone function of the Agent App never depends on the server being up (see A-04). | Must |
| N-05 | Security: role-based access; passwords hashed; recordings served only to authorised users through the API (never as shared folders); SIP credentials stored encrypted on the server and never shown to agents. | Must |
| N-06 | Audit: all edits to classifications and contacts record user and time. | Must |
| N-07 | Backup: nightly automatic backup of database and recordings to a second disk or client-provided location; restore procedure documented and tested at handover. | Must |
| N-08 | Data retention: communications and contacts kept indefinitely; recordings 90 days by default (S-43). | Must |
| N-09 | Language: Arabic (RTL) and English for both applications. | Must |
| N-10 | Platforms: Agent App on Windows 10/11; Supervisor Web App on current Chrome/Edge; server on Linux (Ubuntu Server) or Windows on a mini PC with SSD. | Must |
| N-11 | Maintainability: single installer for the Agent App; updates deployed from the server; configuration without code changes. | Should |

---

## 6. External Interfaces

- **Yeastar S20 — calls:** standard SIP registration and calls (two extensions per agent). No PBX add-on required. Caller ID as delivered by the trunks.
- **Blocked numbers — note:** rejection by the Agent App means the PBX still receives the call and, in a queue, may offer it to other agents or send it to the failover destination after all apps reject it. Blocking at the PBX level (S20 blocklist, client's PBX person, using the export in S-46) stops the call before it enters the queue and is recommended for persistent nuisance callers.
- **Yeastar S20 — server side:** AMI (TCP 5038, read-only events) for calls that never reach an agent; alternatively CDR files on a network folder and/or a call-back extension (see 4.5). No Yeastar API licence required.
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

- PBX: does the S20 show the AMI tab under Settings > System > Security? Is inbound routing a queue or a ring group? (Determines which method in 4.5 applies.)
- Exact list of channels (WhatsApp, Facebook, Instagram, Wheels, others?) and of classification types to start with.
- Should supervisors be able to see live agent status and listen to live calls? (Not included by default.)

---

## 11. Recommendations from the Developer

- **Follow-up tasks:** when a call is classified as Complaint, automatically create a follow-up for the supervisor with a due time; missed calls create a call-back task. Cheap to build, high value for customer retention.
- **Delivery notes on the contact:** a dedicated "delivery instructions" field (gate code, landmark) separate from general notes.
- **Same-as-last-order:** show the last order's notes in the pop-up so the agent can offer "the usual".
- **Call outcome for outbound:** add No answer / Busy / Wrong number outcomes to outbound classification for clean call-back reporting.
- **Recording notice:** add a short "this call may be recorded" announcement on the S20 before ringing agents.
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
- PBX (Yeastar S20) configuration and its maintenance: two working extensions per agent with SIP credentials, one spare extension for testing, caller ID delivered on inbound calls, routing of inbound calls to the inbound extensions and outbound routes on the outbound extensions, recording announcement if wanted. The PBX is configured by the client or the client's PBX provider, not by the developer.
- For capture of calls that never reach an agent (4.5): AMI enabled on the S20 (Settings > System > Security > AMI) with a username/password for the system and the server's IP in the permitted list — or, if the PBX does not offer AMI, CDR storage/export to a network folder on the server and, optionally, the call-back extension and its queue failover setting. The client confirms before installation which of these the PBX provides.
- LAN access between laptops, server and PBX; administrator rights on the laptops for installation.
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
