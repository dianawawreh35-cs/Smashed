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
- **Issabel PBX (hosted by the telephony provider):** not operated by the restaurant and not modified by this project. It accepts **no inbound connections**, so this system only ever talks to it outbound — SIP registration from the agents' laptops and from the server (4.5). Issabel's own screens, such as the Blacklist, are used by whoever administers it, not by this system.
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
| A-01 | Agent logs in with username and password issued by the supervisor. The app receives the agent's extension and SIP credentials from the server; the agent never types SIP details. A supervisor account is refused and pointed to the web app, just as the web app refuses agents. | Must |
| A-02 | The app registers the agent's extension on the Issabel PBX across the VPN and shows its status (Registered / Failed) at all times; re-registers automatically after network or VPN drops. A refusal by the PBX and a PBX that cannot be reached are shown differently, because the first needs the supervisor and the second will clear itself. | Must |
| A-03 | Audio device selection (microphone, speaker, ring device) remembered per laptop. | Must |
| A-05 | Shared laptops: the app is installed once per laptop and supports any agent logging in. On login it registers the logged-in agent's two extensions; on logout it unregisters them. Audio device choices are per laptop; everything else (calls, classifications, queued uploads) belongs to the logged-in agent. Agents must log out at the end of their shift (automatic logout after a configurable idle time). | Must |
| A-04 | If the server is unreachable, the app continues to make/receive calls and record; log entries and classifications are queued locally and synchronised when the server returns. | Must |

### 3.2 Inbound calls and pop-up

| ID | Requirement | Priority |
|---|---|---|
| A-10 | On an incoming call the app **rings** (a generated two-tone bell, two short bursts then a pause, on the laptop's default output) until the call is answered, rejected or given up, and brings a pop-up to the front (even if minimised) showing: caller number, matched contact (name, address, notes), branch, and totals (orders, complaints, cancellations). *Amended 2026-09-22: the list of the last five communications was removed from the pop-up at the client's request; the caller card endpoint still returns it.* | Must |
| A-11 | Unknown numbers show "New customer" with an inline form to save name, address and notes; saving links the current call to the new contact. **As built (24 Sep 2026):** the form opens from a "Save as new customer" button, so it never pushes the classification form down; saving attaches every earlier call from the number that had no contact, the current one included, and later calls are matched as they arrive; a matching name offers "add this number to them" (A-63); with the server unreachable the form keeps what was typed and is not queued. | Must |
| A-12 | Controls: Answer, Reject, Hang up, Mute, Hold. Call timer shown while connected. **Mute** pauses the agent's microphone: the customer hears nothing and the agent still hears the customer; the pop-up shows "Muted" in place of "Connected" for as long as it lasts, and a mute ends with the call. **Hold** places the customer on the PBX's hold music, with neither side hearing the other, until the agent resumes. Neither stops the timer: the call is still in progress. A mute or hold that fails leaves the call exactly as it was and is logged; it never ends the call. | Must |
| A-13 | Phone numbers are matched regardless of format (05…, +9705…, 02…, 972…). | Must |
| A-14 | Missed and rejected calls are logged with status Missed / Rejected. | Must |
| A-15 | Transfer to another extension (blind transfer). | Should |
| A-16 | VIP callers: the pop-up shows a clear VIP badge and the contact's notes at the top so the agent adjusts immediately. | Must |
| A-17 | Blocked callers: an incoming call from a number flagged Blocked is rejected automatically by the Agent App (no pop-up, no ringing); the call is logged with status Blocked and appears in the supervisor's reports. The block list is shared by all agents and cached locally so it works even when the server is unreachable: the Agent App fetches it at sign-in, keeps a copy on the laptop, and answers from memory, so the decision costs nothing in the second before the PBX gives up on the extension. A caller id that is **withheld or unreadable is not blocked** — rejecting what cannot be identified would silently drop every withheld-number call, and this requirement is about numbers a supervisor named. | Must |
| A-18 | The agent can set their own phone to **Do not disturb** and to **Auto answer**, from check boxes in the rail beside sign-out. Do not disturb turns an incoming call away with no ringing and no pop-up, answering 486 Busy so the PBX offers the call to another agent, and the call is logged **Busy** — the same as a second call arriving during one, which is what it is from the caller's side. Auto answer connects an incoming call the moment it arrives, with the pop-up still shown first so the agent sees who they are talking to. Do not disturb wins when both are set: turning a caller away and picking them up instantly are opposite instructions, and connecting a live customer to an agent who has stepped away is the worse failure. Neither affects blocked callers (A-17), which are decided first, or outbound calls. Both are remembered per laptop with the language and audio devices (A-03), so a shift that ends with do not disturb on starts with it on and visibly ticked. | Must |

### 3.3 Outbound calls

| ID | Requirement | Priority |
|---|---|---|
| A-20 | Dial from a dial box, from any phone number shown in the app (click-to-call) or from a contact. All calls use the agent's one extension (see 2.3). One call at a time, so dialling is refused while any call is in progress and until the extension is registered. A call being placed shows the same pop-up as an incoming one, with the number and a Cancel; there is no Answer or Reject on a call the agent made. **The agent hears ringing while the customer's phone rings**: the PBX sends no early media, so the app generates the tone itself (425 Hz, one second on, four off) from the PBX's 180 Ringing until the call is answered, fails or is cancelled. If the PBX ever sends its own ringing audio, the app's tone stays off. **What the PBX expects dialled is a dialplan matter, not an application one**: the app sends the number's digits as they are held, behind a configurable prefix, and does not rewrite them into international form. | Must |
| A-21 | Outbound calls are recorded and logged like inbound calls: the same log entry with direction Out, and a classification form opening on answer. **The outbound form is its own** (see S-40): a call the agent placed is a different conversation from an order coming in, so it asks its own questions (default: Type, Notes, Follow-up required) while sharing the call types. **The pop-up marks an outgoing call** with an "Outgoing call" badge from dialling through to the form after it, so the agent knows which way the call went before speaking. The dial prefix the PBX needs (48 on this site) is added by the app and never shown: the number on screen and in the log is the customer's number as held. An outbound call the customer did not pick up is logged **NoAnswer**, never Missed — Missed means a customer rang and nobody here answered, and it is the service figure the supervisor's reports are built on. A number that could not be reached at all is logged Failed, which is a different thing an agent does something different about. | Must |
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
| A-40 | **The classification form opens as soon as the call is answered**, for inbound and outbound calls, and stays open while the agent is talking. It is filled in **during** the call, not after it: the agent is taking the order as the customer speaks, and the order value, the branch and the notes are what is being said. The form stays on screen after hang-up until it is saved or skipped, so a call that ends mid-sentence does not take the agent's typing with it. Saving is possible during the call and at any point after it. The form's fields are defined by the supervisor (see S-40). Default fields: Type (Order, Cancellation, Complaint, Inquiry, Wrong number, Other), Branch (one of 4), Order value, Notes, Follow-up required. | Must |
| A-41 | The agent may skip; the call remains "Unclassified" and is highlighted in the call log until classified. A form left untouched when the call ends is a skip, not a loss — nothing is saved until the agent saves it, and the call is simply unclassified. **Only answered calls are classified.** A missed, rejected or unanswered outbound call has no conversation to classify, is never marked "Unclassified", and instead takes a free-text note saying why, opened from the call log and editable under the same rule as A-42. When an outbound call ends unanswered, the call pop-up stays open and offers the note straight away (save or skip). Blocked and failed calls take neither. | Must |
| A-42 | Agents can classify or edit the classification of their own calls for the current day only. Older entries are read-only for agents; supervisors can edit at any time. | Must |
| A-43 | Every classification change is stored with who changed it and when (audit trail). | Must |

### 3.6 Call log (agent's own calls)

| ID | Requirement | Priority |
|---|---|---|
| A-50 | List of the agent's own calls: date/time, direction, number, contact, duration, status, type, notes, recording indicator. Filter by date and search by number/name. **The list reaches back a limited number of days, set by the supervisor (S-47, `agent.call_log_days`, one week by default).** It is a working window, not a retention rule: nothing is deleted, the contact history (A-62) still shows every call, and the supervisor's reports are unaffected. The limit is applied by the server, not by the app, and it is also what keeps the screen quick on a busy extension. | Must |
| A-51 | Open any own call: view details, play the recording (play/pause/seek), and — subject to A-42 — classify or edit the classification of an answered call, or write or edit the note on a missed, rejected or unanswered one (A-41). **Playing only:** an agent plays their own call's recording and no one else's (A-52); **downloading a copy is the supervisor's** (S-04). | Must |
| A-52 | Agents cannot see other agents' calls. | Must |

### 3.7 Contacts (shared)

| ID | Requirement | Priority |
|---|---|---|
| A-60 | Contact fields: name, phone numbers (one or more), address, notes, flags VIP and Blocked (set by the supervisor only, see S-45), created by / date. | Must |
| A-61 | All agents see all contacts saved by any agent. Search by name, number or address. | Must |
| A-62 | Contact details page: information plus the full history of communications (calls and app orders) from all agents, with type and notes. Recordings on this page are playable only for the viewing agent's own calls; supervisors can play all. | Must |
| A-63 | Create and edit contacts. A phone number already on another contact is refused, and the refusal names the contact that holds it so the agent can open it instead. A **matching name** is a warning, never a refusal: when a new contact is given a name an existing contact already has, the agent is shown those contacts and chooses — add this number to that person, or save a separate contact. Names are never matched automatically: common names are common, and silently merging two customers would mix their order histories with no way to unpick them. Merging two existing contacts is a separate, deliberate action. | Must |
| A-64 | Import contacts from Excel/CSV (initial load by the supervisor). | Should |

### 3.7a Delivery areas

| ID | Requirement | Priority |
|---|---|---|
| A-65 | Delivery lookup: the agent searches a place by name and is shown **which branch delivers there and what delivery costs**. A partial name is enough, and Arabic spellings are matched the way contact names are (الطيرة finds الطيره). Agents can read the list but never change it. | Must |
| A-66 | Menu lookup: the agent searches the menu by name or by what is in an item, and is shown its **category, contents, price and picture**. Where the menu offers a meal, both prices are shown; where an item is an add-on, the amount is shown as an addition rather than a price of its own. A partial name is enough, and Arabic spellings are matched as in A-65 — these are English words written in Arabic (سماشد, ماشروم, كرسبي) and nobody spells them the same way twice. With nothing typed the whole menu is listed in printed order. Agents can read the menu but never change it. | Must |

### 3.8 App Orders tab (WhatsApp, Facebook, Instagram, delivery apps)

| ID | Requirement | Priority |
|---|---|---|
| A-70 | Agents record communications that did not arrive by phone. Fields: Channel (WhatsApp, Facebook, Instagram, Wheels, other app — list managed by supervisor), Type (Order, Cancellation, Complaint, Inquiry, Other — same list as calls), Customer (existing contact or new: name, phone, address), Branch (one of 4), Order value, Notes, date/time (defaults to now). **As built (25 Sep 2026):** the feature is called **Applications** (التطبيقات) in both apps. A message is a `communications` row (kind App, direction None, status Logged) on the app's channel, never a table of its own. It is classified with a **third form**, "Applications", designed on its own tab of the Classification page and starting as a copy of the inbound form. The time defaults to now; an agent may set it back within the same day, a supervisor to any past time. Recording needs the server: a message is typed by the agent and is not queued offline. A message is never deleted; a wrong one is edited. **A message is always recorded with its type** (25 Sep 2026, Dia): unlike a call, it cannot be skipped, so the app reports have no "not classified" count. | Must |
| A-71 | List of the agent's own app communications for the current day, with edit; older entries read-only for agents (same rule as calls). **As built (25 Sep 2026):** its own rail section, *Applications*, separate from the Call log, which stays calls only. The list reaches back `agent.call_log_days` like the call log; the edit window is `CallEditWindow`, the same rule as calls. | Must |
| A-72 | App communications appear in the contact's history alongside calls, and in all supervisor reports with Channel = app name; phone calls have Channel = Phone. **As built (25 Sep 2026):** the history lists both with a Channel column and opens a message like a call. The supervisor has an **Applications** page (search, open, Edit, Classify; calls stay on the Calls page) and an **Application reports** page (تقارير التطبيقات): messages per channel and type, orders and value per channel/branch/agent/day, a trend per day/week/month/hour, per agent, cancellations and complaints per channel. The call reports and the combined report are not this task; the report endpoints take a `kind` so they can serve calls later. | Must |
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
| S-02 | Global search across calls, app communications and contacts: by phone number, customer name, agent, branch, channel, type, date range, notes text, order value range, status, has recording. **Built 24 Sep 2026 for calls. App communications and the channel filter joined 25 Sep 2026:** the same endpoint with `kind=App` and `channelId`, on the separate Applications page; the Calls page stays calls only. Filtered and paged by the server. | Must |
| S-03 | Every communication opens with full details: agent, customer, branch, channel, date/time, duration (calls), type, notes, order value, recording player, and the audit trail of changes. | Must |
| S-04 | Play and download recordings for any agent; edit any classification at any time (logged). **Built 24–25 Sep 2026:** playback with holds marked, download, and Edit/Classify on any answered call from its details, using the call direction's form. | Must |
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
| R-11 | Missed calls: calls not answered by an agent, and calls abandoned in the queue (see 4.5) — count, list, and time until the customer was called back (or "never"). Calls the agents' apps saw are immediate; abandoned calls come from the CDR import and appear within its interval (S-55). |
| R-12 | Orders by channel: Phone vs WhatsApp vs Facebook vs Instagram vs delivery apps — count and total order value; trend over time. |
| R-13 | Order value: total and average per channel, per branch, per agent, per day. |
| R-14 | Cancellation rate: cancellations ÷ orders, per branch, channel and agent; cancellation notes list. |
| R-15 | Agent productivity: calls handled, average call duration, orders taken, order value, unclassified count, missed calls on their extension. |
| R-16 | Customer base: new vs returning customers per period; top customers by orders and by order value; inactive customers (no order in N days) as a win-back list. |
| R-17 | Complaint handling: open vs closed follow-ups, time to close, complaints per 100 orders. |
| R-18 | Data quality: unclassified communications, calls from unknown numbers (not saved as contacts), duplicate contacts. |
| R-19 | Daily summary sent by e-mail to the supervisor (yesterday's totals by type and channel, complaints, missed calls) [Could — requires internet or local mail]. |
| R-20 | Abandoned calls: count, rate (÷ inbound calls), average and maximum wait time, per hour and per day; list with call-back status. Sourced entirely from the CDR import (S-55), so figures are current as of the last import — **not real time** — and a call abandoned at 19:05 appears in the report at the next import after it. Wait time is subject to the CDR supplying one; see R-21. |
| R-21 | Queue service level: percentage of inbound calls answered within N seconds (N configurable), per day and per hour. As of the last CDR import, not live. **Conditional:** it needs a usable per-call wait time from `Master.csv`. Whether the CDR on this PBX supplies one has not been checked — it is part of the S-55 prerequisite. If it does not, this report degrades to counts of answered and abandoned calls without a service-level percentage, and the client is told before the work starts. |

### 4.3 Dashboard

| ID | Requirement | Priority |
|---|---|---|
| S-20 | Home page with today's figures: communications by type and channel, orders and order value, complaints, missed calls, unclassified count, agents online. Charts for the selected period: communications per day (line), per type (bar/pie), per channel (bar), per hour (bar). | Must |

### 4.4 Configuration

| ID | Requirement | Priority |
|---|---|---|
| S-40 | Edit the classification forms used by agents: add/remove/rename types; add custom fields (text, number, dropdown, checkbox); set required fields; the change applies to new classifications without reinstalling the Agent App. **There are three forms, for incoming calls, outgoing calls and application messages (A-70)**, edited on the same page and published separately. The list of call types is shared; **each form offers the types the supervisor ticks for it** (25 Sep 2026, Dia), every type when none is chosen. The server refuses a type the form does not offer, for supervisors too. | Must |
| S-41 | Manage channels list (WhatsApp, Facebook, Instagram, Wheels, …) and branches (4 at start — رافات, بطن الهوى, ايكون, نابلس; add/rename/disable). Renaming is safe: everything that refers to a branch holds its id, never its name. **Channels built 25 Sep 2026:** add, rename, reorder, hide; never delete. Phone is a system channel and can be neither renamed nor hidden. Branches are still read-only. | Must |
| S-42 | Manage agents: create/disable accounts, assign the agent's extension with its SIP credentials (see 2.3), reset passwords. | Must |
| S-43 | Recording retention period (default 90 days) and storage usage view. | Must |
| S-44 | Backup now / view last backup status. | Should |
| S-45 | Mark a contact (or a bare phone number) as VIP or Blocked, with a reason and date; remove the flag at any time; list of all blocked and VIP numbers. Agents can see the flags but cannot change them. Every change is logged. **A reason is required** whenever a flag is set — a block nobody can account for is one nobody later dares remove; removing a flag needs none, because the removal itself is logged with who and when. **VIP and Blocked are mutually exclusive** and setting both at once is refused: they ask the Agent App for opposite behaviour (A-16 shows a badge, A-17 rejects the call) and there is no sensible winner. A number given for flagging is matched against existing contacts the way an incoming caller is (A-13), so flagging `0599…` flags the customer already saved as `+970599…`; only a number that matches nobody creates a nameless contact to carry the flag. The supervisor can see the **full history of flag changes** on a contact, including removals. Flagging is done **from the contact itself** in the contact list, and asks for the reason at that moment; the list of flagged numbers is that same contact list filtered to VIP or Blocked, so searching and filtering compose. | Must |
| S-59 | Menu: add, edit, remove and hide menu items and the categories that group them, each with its contents, price, meal price where there is one, and a picture (A-66). A category holding items cannot be removed until they are moved or deleted, so it is hidden instead; **hiding a category does not hide its items**, which remain findable by search — what it controls is whether the category appears as a heading when the menu is read in printed order. Categories can be reordered to match the printed menu. Pictures are PNG, JPEG or WebP up to 2 MB, are shown before saving so the wrong photograph is caught by eye, and can be removed as well as replaced. Prices distinguish three cases the printed menu uses: a price, **no price** (an offer the menu does not price), and an **addition** to another item (the menu writes these as "+2"). | Must |
| S-58 | Delivery areas: add, edit, remove and deactivate the places the restaurant delivers to, each with the branch that covers it and the delivery price (A-65). **Adding many at once is required, not a convenience**: the lists run to hundreds of rows per branch and live in the branches' own spreadsheets, so the supervisor picks a branch and pastes two columns — name and price, tab or comma separated. The import reports every line it could not use, with the line number and the reason, and either applies wholly or not at all. An area already listed under a different branch is reported rather than moved, because moving it silently would leave an area served by whichever branch was pasted last. A price of **0** is valid and means free delivery. | Must |
| S-46 | Export the blocked list in a format that can be loaded into Issabel's blacklist, so numbers are blocked at the PBX level as well (see note in section 6). With no inbound port to the PBX (4.5) this export is the **only** route to PBX-level blocking — there is no automatic path — and it is what makes a blocked caller genuinely rejected rather than declined by each Agent App in turn. Loaded by whoever administers Issabel. | Should |
| S-48 | Internal numbers list: the supervisor maintains the list of internal extension numbers — the other agents and the four branches (see 2.3). A call whose other party is on the list is an internal call: still recorded, still in the agent's call log and the contact history, but excluded from the customer-facing reports so it does not distort order counts, complaint rates, volumes or the service level. Entered as plain numbers, one per entry; every change is logged with who and when. | Must |
| S-47 | System settings: view and change the values the system reads at runtime — the PBX host the Agent Apps register to (SRS 2.3), the idle-logout time (A-05), how long an agent may edit their own classification (A-42), the service-level threshold (R-21), the recording retention period (S-43), the CDR import interval (S-55) and how far back an agent's own call log reaches (A-50, `agent.call_log_days`, 1–90 days). Changes take effect without a redeployment: the Agent App picks up a new PBX host at the next sign-in, so a change by the provider does not need a visit to each laptop. Every change is logged with who and when. There are no AMI settings and no call-back extension: both were ruled out or removed (4.5), and the settings they used were deleted on 21 September 2026 rather than left on the screen doing nothing. | Must |

### 4.5 Capture of calls that never reach an agent (queue / ring-group)

> **Settled — 19 September 2026, revised.** Two constraints, both final.
>
> **No inbound port to the PBX can be opened, on any port.** AMI (S-50) and
> direct database access (S-52) are **ruled out, not deferred** — both need to
> connect *in* to the PBX.
>
> **A waiting caller is not to be answered and hung up on.** The client has
> rejected that behaviour, which removes S-56 (the call-back extension)
> entirely. It is not deferred or downgraded; a requirement describing
> behaviour the client has refused would be wrong.
>
> **S-55, the CDR import, is therefore the only method, and is a Must.** It is
> also a better fit than S-56 ever was: it captures *every* abandoned call,
> including the caller who hangs up after five seconds, which S-56 structurally
> could not see.
>
> **The cost is latency.** Abandoned calls appear within the import interval,
> not instantly. R-11, R-20 and R-21 say so in their own definitions.

Calls that are abandoned while waiting in the queue, or that time out because all agents are busy, never ring an agent's extension and therefore cannot be seen by the Agent App. They are captured on the server side as follows.

**Method — CDR import over SFTP**

| ID | Requirement | Priority |
|---|---|---|
| S-55 | CDR import: Asterisk's `cdr_csv` module appends one row to `/var/log/asterisk/cdr-csv/Master.csv` as each call ends, independently of the `asteriskcdrdb` database. The server fetches that file over **SFTP, outbound, key-based authentication**, on a short interval (5 minutes is the working assumption, configurable), and creates communications with status Abandoned for inbound calls that never reached an agent, matched to contacts and visible in search, contact history and reports. Reading is **resumed from the byte offset last reached**, so a file that grows all year is not re-downloaded; a file smaller than the stored offset means the log has rotated, and the offset resets to zero and the event is logged. Rows are keyed by Asterisk's `uniqueid` against `communications.pbx_unique_id` and its unique index, so re-reading the same lines is harmless. **`cdr.conf` must have `loguniqueid=yes`** — without it there is no stable key and duplicate protection is lost; this is a prerequisite, not a preference. | Must |
| S-51 | Each abandoned call creates a call-back task assigned to the supervisor (or the next available agent, configurable). The task closes automatically when an outbound call to that number is made, or manually. Tasks appear with the latency of the import. | Should |
| S-57 | Ring-group option (client decision): if incoming routing is a ring group with "ring all" rather than a queue, every inbound call rings all free agents' Agent Apps, and a call nobody answers is logged by the Agent Apps themselves as Missed, in real time, with no server-side capture at all. The cost is hold music and queue position announcements, which a ring group does not have. | Info |

**Identifying an abandoned call — provisional, to be confirmed before implementation**

`disposition = 'NO ANSWER'` is **not** a sufficient test. Where an inbound route or IVR answers the call before it reaches the queue, Asterisk records `ANSWERED` even though no agent ever spoke. The better test is expected to be `lastapp = 'Queue'` with a zero or near-zero `billsec`, **but the rule must be confirmed against real rows from this PBX before any parser is written.**

**Prerequisite (S-55):** three test calls — one answered by an agent, one hung up while ringing, one left to the queue timeout — then read the last rows of `Master.csv` and record what distinguishes them. That observation, not this paragraph, is what the importer is written against.

**Ruled out**

| ID | Requirement | Status |
|---|---|---|
| S-50 | Real-time capture through AMI (Asterisk Manager Interface, TCP 5038). | **Ruled out (19 Sep 2026).** No inbound port to the PBX. |
| S-52 | Reading the PBX call records directly through a database grant. | **Ruled out (19 Sep 2026).** No inbound port to the PBX. |
| S-56 | Call-back extension: the server registers an extension the PBX sends timed-out callers to, answers, plays "all agents are busy, we will call you back" and hangs up. | **Removed (19 Sep 2026).** Answering a waiting caller and hanging up on them is not a customer experience the client will accept. The ID is retired and not reused. |

**Dependency statement:** the client's PBX accepts no inbound connections, and a waiting caller is not to be answered and hung up on. Calls that never reach an agent are therefore captured from the PBX's own CDR file, fetched outbound by the server on a short interval. **Real-time capture is not available.** Coverage is complete — every abandoned call is seen, whenever the caller gave up — but each appears within the import interval rather than instantly. This is a documented characteristic of the solution given the client's constraints, not a defect.

---

## 5. Non-Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| N-01 | Network dependency: the database, recordings, API and supervisor app run on the restaurant LAN and keep working with no internet at all — call log, contacts, classification, app orders and reports are unaffected. Telephony is different: the PBX is reached across the internet over the VPN, so calls depend on the internet line, the VPN and the provider. Losing any of them stops calls; it stops nothing else. | Must |
| N-02 | Performance: pop-up under 1 s; report generation under 5 s for one year of data at the expected volume (up to ~500 communications/day). | Must |
| N-03 | Capacity: 5 agents initially, designed for up to 20 without changes; one server. | Must |
| N-04 | Availability: the phone function of the Agent App never depends on the server being up (see A-04). It does depend on the VPN and the provider (N-01), which are outside the developer's control — the agreed uptime and support hours for them are the client's arrangement with the provider (see 12.3). | Must |
| N-04a | The Agent App shows at all times whether the phone is usable, and says which part is missing — not connected to the VPN, not registered, or server unreachable — so an agent can tell a network problem from a broken application (see A-02). | Must |
| N-05 | Security: role-based access; passwords hashed; recordings served only to authorised users through the API (never as shared folders); SIP credentials stored encrypted on the server and never shown to agents. A password reset, a disabled account or a change of role takes effect on existing sign-ins at once, not when they expire. | Must |
| N-06 | Audit: all edits to classifications and contacts record user and time. | Must |
| N-07 | Backup: nightly automatic backup of database and recordings to a second disk or client-provided location; restore procedure documented and tested at handover. | Must |
| N-08 | Data retention: communications and contacts kept indefinitely; recordings 90 days by default (S-43). | Must |
| N-09 | Language: Arabic (RTL) and English for both applications. | Must |
| N-10 | Platforms: Agent App on Windows 10/11; Supervisor Web App on current Chrome/Edge; server on Linux (Ubuntu Server) or Windows on a mini PC with SSD. PBX: Issabel (Asterisk), hosted and operated by the telephony provider. | Must |
| N-11 | Maintainability: single installer for the Agent App; updates deployed from the server; configuration without code changes. | Should |

---

## 6. External Interfaces

- **Issabel — calls:** standard SIP registration and calls across the VPN (two extensions per agent). No PBX add-on or licence required. Caller ID as delivered by the provider's trunks.
- **Blocked numbers — note:** rejection by the Agent App means the PBX still receives the call and, in a queue, may offer it to other agents or send it to the failover destination after all apps reject it — so the caller may experience being held rather than cut off, and nothing the Agent App sends can change that. Blocking at the PBX level, through Issabel's own **Blacklist** screen, stops the call before it enters the queue: no ringing, no queue, no agent involved. With no inbound port to the PBX there is no way to write that list automatically, so it is loaded by hand from the S-46 export. **Recommended for every persistent nuisance caller**, not merely optional.
- **Issabel — server side:** the PBX accepts **no inbound connections**, so AMI and database access are out (4.5). The server instead **fetches Asterisk's CDR file over SFTP** — `/var/log/asterisk/cdr-csv/Master.csv`, outbound, key-based, on a short interval — and reads from the byte offset it last reached (S-55). The only PBX-side requirements are `cdr_csv` enabled with `loguniqueid=yes` in `/etc/asterisk/cdr.conf`, and a restricted SFTP account scoped to that directory. The server reaches the PBX over the VPN, outbound only. No licensed Issabel add-on is required — the Contact Center (Asternic) module would provide the same figures ready-made, but this project does not depend on it.
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
- A call that rings in the queue and is abandoned — whether the caller gives up after five seconds or waits to the timeout — appears in the supervisor's abandoned-call report with a call-back task **within one import interval** (S-55). Verified by making both kinds of call and checking they appear after the next import, with the caller's number and, where the CDR supplies it, the wait time.
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

- **PBX — largely answered as of 19 September 2026.** No inbound port can be opened, on any port (so S-50 and S-52 are ruled out); a waiting caller is not to be answered and hung up on (so S-56 is removed); and **we now have administrative access to the Issabel box ourselves**, so several items that were questions for the provider are now work we do. See 4.5 and 12.3.
- **Before the CDR importer is written (S-55) — an observation, not a question.** Make three test calls: one answered by an agent, one hung up while ringing, one left to the queue timeout. Then read the last rows of `/var/log/asterisk/cdr-csv/Master.csv` and record what distinguishes them. The abandoned-call rule is written against those rows. Also confirm at the same time whether the CDR carries a usable per-call **wait time**, which decides whether R-21 is a service-level percentage or only counts.
- **Still to confirm with the client / provider.** The list of internal extension numbers for S-48 is also needed:
  1. Is incoming customer routing a **queue or a ring group**? (Determines whether S-57 applies instead.)
  2. Who administers the Issabel **Blacklist** screen, and how often will they load the S-46 export?
  3. What are the agreed **VPN uptime and support hours**, and who is called when the tunnel drops?
  4. Can a **recording announcement** be added before ringing agents, if the client wants one?
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
- PBX (Issabel) configuration and its maintenance: one working extension per agent with SIP credentials (see 2.3), one spare extension for testing, caller ID delivered on incoming calls, routing of incoming customer calls to the agents' extensions, outbound routes, and internal dialling between the agents and the branches. Recording announcement if wanted. Trunk and routing configuration remains the telephony provider's work; the client owns that relationship and any charges under it.
- **The VPN**: an account and client configuration for every agent laptop, and a separate connection for the server. Supplying, licensing and supporting the VPN is the client's and the provider's responsibility, not the developer's.
- For capture of calls that never reach an agent (4.5): **administrative access to the Issabel box for the developer**, sufficient to read `/var/log/asterisk/cdr-csv/`, verify `/etc/asterisk/cdr.conf`, create a restricted SFTP account scoped to that directory, and inspect queue settings. Nothing is opened on the PBX — the server connects outbound only. The client grants this access before installation; the developer performs the setup (runbook step 8).
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

**Not covered by support:** hardware failures, PBX, network or internet problems, changes made by third parties, unavailability of the CDR file or the SFTP account on the PBX (see 4.5), data loss caused by not keeping backups on the client's side, and anything listed as Out of Scope. Such work, if requested, is billed separately at USD 30 per hour (minimum one hour).

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
