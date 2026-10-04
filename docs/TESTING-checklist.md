# What still has to be tested by hand

Everything in this list is **built, committed and passing its automated tests,
and has never been seen running.** That is not the same as working. On this
project the white contacts list, the login 500s, the timezone failure and the
call-log "today" label all passed their tests and were all found by opening the
app.

The automated tests prove structure — that the right request goes to the right
endpoint with the right body. They cannot tell you whether a screen looks right
in Arabic, whether a phone rings, or whether a picture loads.

Work top to bottom. Each item says **what to do**, **what should happen**, and
**what it means if it doesn't** — the last one matters, because an unexpected
result is usually information rather than a dead end.

To start the three pieces, see `DEVELOPING.md` §3. Two reminders from §1 and §2,
because they cost a round trip almost every time: **`dotnet run` does not work
here** — run the DLL from its own output folder — and **close the server and the
Agent App before building**, or the compiler cannot replace their files.

---

## Round 1 — the supervisor web app

Nothing here needs the PBX or a phone. Start the database, the server and
`npm run dev`, sign in as `supervisor`, and go to `http://localhost:5173`.

### 1.1 The menu screen (S-59) — most recently changed, least seen

- [ ] **The menu lists 44 items with photographs.**
      *Every picture failed here on 21 September — an image tag cannot send the
      sign-in token, so all 44 came back refused and drew the same blank box as
      an item with no photograph. Fixed; this is the check that it stayed
      fixed.*
      *If the pictures are broken:* this is the first run since the pictures
      moved from the database to files. The server reads them from
      `data/menu-images` relative to wherever it was started. Check the folder
      exists with 39 `.png` files in it, and that the terminal does not say
      "recorded but missing".
- [ ] **Prices read correctly.** A burger shows `26` and `36`. An add-on shows
      `+2`, not `2`. The combination offer shows "not on the menu", not `0`.
      *If an add-on shows a bare number:* an agent would quote it as a line of
      its own instead of adding it to a burger.
- [ ] **Double-click a row.** The editor opens **under that row**, not at the
      top of the list. Try one near the bottom — that is where it used to be
      annoying.
- [ ] **Double-click the Remove button on purpose.** Nothing should happen.
      *Deliberate: otherwise a stray double-click deletes a row and then opens
      an editor for the thing it just deleted.*
- [ ] **Edit an item, change its price, save.** The table shows the new price.
- [ ] **Edit an item and choose a new photograph.** The chosen picture appears
      in the form *before* you save.
      *If it does not:* the preview is broken, and you are saving blind.
- [ ] **Save it, and look at the row.** The new photograph must appear
      **immediately**.
      *If the old one is still there:* the cache stamp is not working. This is
      the bug that would otherwise be reported as "the upload does not work" —
      the picture did upload, the browser is showing yesterday's copy. It would
      fix itself tomorrow, which is worse, not better.
- [ ] **Tick "Remove the current picture" and save.** The row shows a blank
      grey box.
- [ ] **Add a new item** in an existing category, with a picture and a meal
      price. It appears in the list.
- [ ] **Manage categories → add a category**, then add an item to it.
      *This is the one that was impossible before today.*
- [ ] **Rename a category.** Click out of the box rather than pressing Enter —
      it saves on leaving the field. The items' category column updates too.
- [ ] **Move a category up and down** with the arrows.
- [ ] **Untick "Shown" on a category.** Then check in Round 2 that an agent can
      still *find* its items by searching — hiding a category is meant to
      remove the heading, not the food.
- [ ] **Try to delete a category that has items.** It must refuse, in Arabic,
      saying to move or delete its items first.
      *If it deletes:* stop and tell me. That would take the items with it.
- [ ] **Delete an empty category.** It goes.
- [ ] **Everything above, with the interface in Arabic and right-to-left.**
      This is the real test. Numbers, the `+2`, the arrows and the table
      alignment are all places where RTL goes wrong, and all the automated
      tests run in English.

### 1.2 The delivery areas (S-58)

- [ ] 228 areas are listed, each with a branch and a price.
- [ ] **Double-click an area near the bottom of the list.** The editor opens
      there rather than at the top. With 228 rows this is the worst case, and
      the reason the change was asked for.
- [ ] Search for an area by part of its name, in Arabic.
- [ ] Add one area; add several at once.
- [ ] Edit a price; delete an area.

### 1.3 Contacts, flags and blocking (S-45)

- [ ] **Double-click a contact near the bottom of the list.** Its editor opens
      **in a row right under it**, at once (a grey placeholder for a moment,
      then the form), with no jump to the top and no column changing width.
      **Edit** does the same; **Flag…** opens its dialog under the row too.
      *Reported 24 Sep:* both opened above the table, a moment late.
- [ ] **Double-clicking a row never turns a word blue**, on contacts, a
      contact's history, or calls. Dragging across text still selects it.
- [ ] **Rest the pointer on a contact for a moment, then double-click.** The
      form opens complete, history included, with no grey block first.
- [ ] **In an open contact, double-click a call in its history.** The call's
      details and recording open **under that call**, the same panel as on the
      Calls page, and **nothing scrolls**. Its **Open** button does the same.
- [ ] Mark a contact VIP; the badge appears.
- [ ] Block a contact, giving a reason. Unblock it again.
      *You have already confirmed unblocking works end to end.*

### 1.3b Classification, the supervisor's half (S-40)

- [x] **The Classification tab lists the six types and the form's questions.**
      *Confirmed 21 September.*
- [x] **Add a question, tick a call type under "Asked for", publish.** The Agent
      App asks it on the next call. *Confirmed — a required field added here
      appeared in the Agent App without anything being reinstalled.*
- [ ] **Rename a type.** The agent sees the new label; reports are unaffected,
      because they are written against the key shown greyed beside it.
- [ ] **Try to delete a type that is in use.** The row offers hiding instead.
- [ ] **Publish a form, then publish the previous one again.** That is the way
      back from a bad change, and it is what the hint under the button promises.
- [ ] **Choose which types each form offers (S-40).** On the outgoing-calls
      card, the type question has a row "Types this form offers" with every
      type ticked. Untick Order and publish. In the Agent App, after signing in
      again, an outgoing call's form no longer offers Order; an incoming call's
      still does. The last ticked box cannot be unticked. Tick every box again
      and publish: the form offers every type, including one added later.
- [ ] **An old call whose type a form stopped offering.** Open, on the Calls
      page, an outgoing call classified as an Order before the change and
      press Edit: it still reads Order, with a note to choose another type,
      and Save waits until you do. *If Save goes through with Order:* tell me.
- [ ] **The Applications card works the same way**, and the Agent App's
      Applications section offers only the ticked types.

### 1.4 The interface itself

- [ ] **Hovering a label shows the hand, not the text I-beam**, and a label's
      text cannot be dragged and highlighted. Labels are controls: clicking one
      puts the cursor in its box.
- [ ] **No blinking cursor in the page text.** If there is one, that is the
      browser's caret browsing, not the app — press **F7**.

### 1.5 Settings

- [ ] `agent.call_log_days` is there and can be changed.
- [ ] `agent.idle_logout_minutes` reads **240**.
- [ ] The two settings that did nothing (`callback.extension`,
      `pbx.ami.enabled`) are **gone**.

### 1.6 Being signed out by the server (N-05)

Needs a second supervisor account; add one in Users if there is only one.

- [ ] **Sign in as supervisor A in one browser, and as supervisor B in
      another** (or a private window). As B, reset A's password in Users. Then,
      as A, open any page. It must go straight to the sign-in page with "You have
      been signed out…" under the form, in the interface's language.
      *If A's page just shows an error instead:* the server is an old build.
      Restart it. *If A can carry on working:* the server is not checking the
      token; stop and tell me.
- [ ] **Sign A in again with the new password.** The message goes, and A is
      back on the page they were on.
- [ ] **Reload the browser with no one signed in.** No "signed out" message.
      That line is only for someone who was thrown out while working.

### 1.7 Calls: search, open, listen (S-02, S-03, S-04)

- [ ] **Calls is in the sidebar, under Dashboard,** and lists recent calls from
      every agent, newest first, with a count and pages of 50.
- [ ] **Search by a number** three ways: `0599…`, `+970 599…`, and a piece from
      the middle. All three find the same calls. Then **by a name**, in Arabic,
      spelled with ة and then with ه. Both find the contact's calls.
- [ ] **Each filter narrows the list:** agent, branch, type, result,
      direction, a date range, notes text, order value from/to, recorded,
      classified. *If a filter seems to do nothing:* check the count changes.
      The server does the filtering, so the count is over every call.
- [ ] **Typing does not search.** Nothing changes until Search is pressed.
- [ ] **Double-click a call near the bottom of the page.** It opens **in a row
      right under it**, with no jump to the top. Its **Open** button does the
      same, and turns into **Close**. *Reported 24 Sep:* it first opened above
      the table and scrolled up to it.
- [ ] **Opening a call is smooth.** The panel fades in whole, the table's
      columns do not change width, and nothing inside it jumps as it fills
      in: the classification's grey block becomes the classification in
      place, and "Loading the recording…" becomes the player at the same
      height. *Reported 24 Sep as glitchy.*
- [ ] **Open a recorded call.** The details show the agent, extension, queue,
      times, and (if classified) type, branch, order value, notes, answers and
      who changed it when. **Play** the recording: the customer and the agent
      are both heard, the bar **glides** rather than stepping (reported 24 Sep:
      it jumped four times a second), and it seeks when dragged.
      *If there is no sound but the bar moves:* the mu-law decoding is wrong.
      Stop and tell me.
- [ ] **Both ears, then one side (S-04, 2 Oct).** With headphones, both voices
      are in **both ears**, not one person per ear. Under the player,
      *Listen to:* **Customer** carries on from the same moment with only the
      customer, in both ears; **Agent** only the agent; **Both** back to
      both. Pause, switch, and Play: it carries on from where it paused.
      *If a switch goes back to 0:00:* tell me.
- [x] **Open a call that was put on hold** *Signed off by Dia 24 September.* (put a test call on hold for ten
      seconds first). Under the bar there is an **amber mark** where the hold
      was, "On hold: 0:12–0:22" under the player, and **"On hold"** beside the
      time while playback is inside it. The same as the Agent App's player.
- [ ] **Edit a classification (S-04).** Open a classified call and press
      **Edit** beside Classification. The form opens with the call's answers,
      asks only the questions for the chosen type, and says what is missing
      before Save works. Change the order value and save: the answers update,
      and **Changes** below lists you, just now. Do it on a call from days
      ago too: a supervisor may, an agent may not.
- [ ] **Change an order to a complaint and save.** The order value is gone,
      and a **Resolved** box appears for the complaint.
- [ ] **Classify an answered call nobody classified:** its **Classify** button
      does the same. An outgoing call is asked the outgoing form's questions.
- [ ] **Download** saves a `.wav` that plays in Windows' own player.
- [ ] **A call whose recording has expired** says so, rather than "no
      recording".
- [ ] **Everything above in Arabic.** The seek bar fills from the right, and
      the hold marks must sit under the same stretch of it. *If the marks are
      mirrored against the bar:* tell me. That is the one thing RTL can get wrong
      here.

---

### 1.8 Applications, Application reports, Channels (A-70 to A-73, S-41) — never run

Built 25 Sep. Record two or three messages from the Agent App first (Round 2,
"Applications"), one of them an order with a value, one a complaint, one an inquiry, so
there is something to see.

- [ ] **Applications is in the sidebar right after Calls,** and **Application
      reports** right after it. Applications lists the messages, newest first,
      with a Channel column and no phone calls in it. **Calls has no message
      rows** and its count is unchanged. *If a message shows on Calls:* tell me
      at once; the kind filter has failed.
- [ ] **Filters:** number or name, dates, channel (Phone is not offered),
      agent, branch, type, notes text, order value from/to, classified. Each
      narrows the count. Typing does not search; Search does.
- [ ] **Open a message** (double-click, or Open). It opens **under its row**
      with no jump: the customer, the channel, the agent, "Message" as the
      status, no recording player, no extension or duration. Its
      classification is shown with **Edit**. Edit one and save: the row updates, and Changes lists you.
- [ ] **Application reports.** Five cards, each a table and, where numeric, a
      chart: messages per channel with a column per type; orders and value
      grouped by channel / branch / agent / day; a trend per day / week / month
      / hour; per agent (messages, orders, value); cancellations and
      complaints per channel. The figures match what you recorded. Presets
      Today / This week / This month / Custom, and channel, agent, branch,
      type, all apply as soon as they change.
- [ ] **Export CSV** on any card downloads a file that opens in Excel with the
      Arabic readable (the file carries a byte-order mark). The rows are the
      table's rows.
- [ ] **The charts in Arabic** run from the right, with the value axis on the
      right. *If a chart's bars are mirrored against its labels:* tell me.
- [ ] **Settings › Channels.** The list has Phone first and the four apps.
      **Phone has no rename box and no hide tick**, with a hint saying why.
      Rename WhatsApp to "WhatsApp Business": every message on it, in
      Applications and in a contact's history, now says the new name. Hide
      Instagram: it leaves the Agent App's dropdown at the next sign-in and the
      Applications filter, and an old Instagram message still says Instagram.
      Add "Telegram"; add it again: refused as already taken.
- [ ] **Classification page has a third card, Applications,** beside inbound
      and outbound. Remove a question from it and publish: the Agent App's
      record card stops asking it at the next sign-in, and the two call forms
      are unchanged.
- [ ] **A contact's history** (Contacts, open a customer who has a message)
      lists the message beside their calls with a **Channel** column: Phone on
      the calls, the app on the message. Open the message from there: the
      same panel as above.
- [ ] **Everything above in Arabic.** Take screenshots of Applications with a
      message open, Application reports, Settings › Channels and the
      Classification page in **each language**.

### 1.9 The dashboard, Call reports, and exporting the calls (S-20, R-01 to R-18, S-05, S-06) — never run

Built 26 Sep. `dia20`'s real calls are enough to see every card with
something in it, but not what a working restaurant looks like. For that, **add
the demo data** (`tools/demo-data/README.md`): 90 days of realistic traffic
under five demo agents, removed afterwards with one command and leaving the
real calls exactly as they were. With it, *This month* and a custom 90-day
range both have plenty; without it, choose a range from mid-September.

- [ ] **The dashboard is the first page after signing in.** Six tiles for
      today: Communications (large, with "N calls, M messages" under it),
      Orders (with "worth …"), Complaints, Abandoned calls, Missed or
      rejected rings (with "N missed, M rejected"), Unclassified calls,
      Agents online. **A ring one agent did not take is shown, not counted**
      (26 Sep). To check it: ring the queue, let it ring the first agent
      without answering, and answer on the second. Communications and the
      Phone line under Today by channel each go up by one, not two, and
      Missed or rejected rings goes up by one. Under them, today by type and by channel. Then a period
      bar (presets and dates only, no agent or branch) and four charts: per
      day (a line), per type, per channel, per hour (all 24 hours).
      *Agents online* counts agents whose Agent App has been heard from in
      the last five minutes. To check it: sign in on a laptop and the number
      goes up at once. Pull that laptop's network cable (or switch it off
      without signing out), and within five minutes it goes back down.
- [ ] **Make a call and miss it**, or record a message in the Agent App: the
      tile changes within a minute without reloading.
- [ ] **Call reports is in the sidebar right after Calls.** One filter bar,
      then six tabs: Overview, Orders and channels, Customers, Agents,
      Problems, Data quality. Changing a filter or a tab updates the cards
      with no jump.
- [ ] **Overview.** *Communications* per day / week / month: calls, messages,
      incoming, outgoing, answered, missed, blocked. **Check one day against
      the Calls page:** filter Calls to that day and count the missed and
      rejected incoming ones; the report's Missed is that number. An
      outgoing call nobody answered is **not** in it. The card under it links
      to Calls. *Calls per type* has a share column that adds to 100%.
      *Peak hours* is a grid of hours by weekday, each cell with its number,
      the busy ones a stronger blue, empty ones plain, with a "fewer … more"
      scale under it.
- [ ] **Branch filter.** Pick a branch: every card on the tab changes, and
      the Communications total is the Calls page's count for that branch and
      period (incoming + outgoing), plus messages.
- [ ] **Orders and channels.** Orders per channel (Phone and each app, with
      shares), orders over time phone vs apps, order value by channel /
      branch / agent / day, cancellation rate, and the list of cancellations
      with their notes.
- [ ] **Customers.** Recurring customers (called more than once), new vs
      returning per month (the old system's customers are *returning*), top
      50 by orders or by value, and customers to win back with a number of
      days you can change. None of them lists a number nobody saved.
- [ ] **Agents.** Per agent: handled, incoming answered, outgoing made,
      average talk time as m:ss, orders, value, unclassified, missed.
- [ ] **Problems.** Complaints with follow-up and status; complaints per
      branch / agent with hours to resolve and per 100 orders; repeat
      complainers; missed calls per day / hour / agent with the rate; every
      missed call with its note.
- [ ] **Data quality.** Four figures, the numbers nobody saved, and the
      names more than one contact shares (about a thousand from the old
      system; that is the old data, not a fault).
- [ ] **Export CSV** on any card, opened in Excel: Arabic readable, the same
      rows as the table. On a long list (over 200 rows) the card says it shows
      the first 200 and the export has them all; check the file has more.
- [ ] **Download image** under any chart saves a PNG on the dark card
      background. Open it: the bars or lines are there, and a chart with two
      series has its legend drawn under it. *If the picture is blank or has
      no text:* tell me which chart and which browser.
- [ ] **Print one report.** On any card, **Print**: the browser's print
      preview shows **only that report**, on white, under a heading with the
      page, the tab, the period and any filters you chose (by name), and when
      it was printed. No sidebar, buttons or filter bar. The chart fits the
      page width with its legend under it; the table follows. Choose *Save as
      PDF* and open the file. Close the preview: the page is exactly as it
      was. *If the preview shows every report, or the page stays blank after
      closing:* tell me which browser.
- [ ] **Print page.** **Print page** at the top of Call reports prints
      **every report on the open tab**, one after another; a report that
      does not fit what is left of a page moves whole to the next. Peak
      hours keeps its blue shading on paper. Try it on the dashboard and on
      Application reports too. Both languages: Arabic prints right-to-left.
- [ ] **Calls page: Export all N (CSV).** Filter to a few days, press it:
      a file downloads with **every** matching call, not the 50 on the page
      (the row count is N). Headings are in the language you're reading in.
      Try it in both languages.
- [ ] **Everything above in Arabic.** Screenshots, **in each language**, of:
      the dashboard; Call reports' Overview (Peak hours visible); the
      Problems tab; and one chart's downloaded picture.

### 1.10 Phones and listening in (S-61, S-62) — built 26 Sep, needs the VPN and a phone

The server's extension must be set up (the PBX blacklist card in Settings).

- [ ] **Users page, Phone column.** An agent signed in to the Agent App shows
      **Free** within a few seconds of the page opening. One whose phone is not
      connected shows **Offline**. The demo agents (2901–2905) show **Not
      known**: the PBX does not have those extensions.
      *If every agent says Not known and a note says the PBX is not
      answering:* the server's VPN is off, or the server's extension's
      password is wrong. The server log says which (`PBX watch:` lines).
- [ ] **Ring an agent.** Their row shows **Ringing**, then **In a call** with a
      timer once they answer, then **Free** when they hang up. Each within
      about three seconds.
- [ ] **Sign out of the Agent App.** That row goes to **Offline** within a few
      seconds. Then pull a signed-in laptop's network cable instead: it goes
      Offline once the PBX notices, which may take a minute or two. Tell me how
      long it took.
- [ ] **Dashboard.** Agents online matches the Users page, and an **In a call**
      tile shows how many are talking.
- [ ] **Listen.** While an agent is on a call, press **Listen** on their row.
      A bar says "Listening to …" with a timer, and you hear both sides of the
      call, a fraction of a second behind. The agent and the customer hear
      nothing different.
      *If the bar shows an error from the PBX:* `*222` did not work from the
      server's extension. The message has the PBX's answer (404 means the code
      is not set up for that extension).
      *If the bar says Listening but there is no sound:* the call's audio is not
      getting back to the server. Tell me.
- [ ] **Stop listening** ends it at once. So must leaving the Users page, and
      closing the tab. When the call itself ends, the bar says so.
- [ ] **Listen & speak (`*223`), from a PC without the microphone setting.**
      Open the web app at `http://192.168.1.100` and press **Listen & speak**.
      A red bar says the browser will not use the microphone at this address,
      and no call is placed: the agent notices nothing.
- [ ] **The microphone setting.** On that PC, run
      `tools\supervisor-pc\allow-microphone.ps1` as administrator (runbook,
      *Supervisor PCs: the microphone*) and restart the browser. Press
      **Listen & speak** again: the browser asks for the microphone. Choose
      **Allow**.
- [ ] **Speaking.** With a headset on, the bar says "Speaking to …". Say
      something. **The agent hears you; the customer does not.** Ask someone on
      each end. The agent should hear you less than half a second late.
      *If the customer hears you too:* `*223` on this PBX is a barge, not a
      whisper. Tell me before anyone uses it.
      *If the agent hears nothing:* the server log's `PBX listen:` lines say
      which code was dialled. Tell me.
- [ ] **Mute.** Press **Mute**: the bar says your microphone is off, and the
      agent stops hearing you, but you still hear the call. **Unmute** brings it
      back.
- [ ] **Refusing the microphone.** Click the icon next to the address, set the
      microphone to Block, and press **Listen & speak**: the bar says the
      microphone was not allowed, and no call is placed. Set it back to Allow.
- [ ] **Everything above in Arabic.**

### 1.11 The 27 Sep review fixes: the web app (M-W01 to M-W10) — no phone needed

After pulling this, run **`npm ci`** in `src/CallCenter.Web` before `npm run dev`:
the build tools moved up (Vite 8, Vitest 5) and the old ones will not start
the new config. Use **Node 22.12 or later** (`node --version`; this machine has 24).

**When the server is stopped.** Start the web app, sign in, then stop the
server (not `npm run dev`).

- [ ] **Contacts: search for a number**, e.g. `0599123456`. A red box says
      *The search did not reach the server. This is not the same as no
      match*, with **Try again**. There is **no** "Nobody has … flag it anyway"
      button. Start the server and press **Try again**: the list appears.
      **Screenshot the red box in both languages.**
      *If "Nothing matched" or the flag button appears:* the failure is still
      reading as an empty list. Tell me.
- [ ] **The other screens, still with the server stopped.** Users, Delivery,
      Menu and Settings each say they could not load, with **Try again**, not
      "no users", "no delivery areas", "nothing on the menu". Settings shows
      **no form and no Save button** on any of its cards. The dashboard keeps
      the **Call queue** card, saying the queue's state could not be loaded.
      On Calls, each filter drop-down says *The list did not load.*
- [ ] **A save that fails without a field to blame.** On Settings, with the
      server running, load the page, stop the server, press **Save
      settings**: *Nothing was saved: the server did not answer…*, and what
      you typed is still there.

**Arabic.** Switch to العربية for these.

- [ ] **A contact's phone numbers.** In Contacts, find a contact with a
      `+970…` number (add one if needed: `+970 59 912 3456`). In the list the
      number reads `+970 59 912 3456`, the `+` on the left, groups in order.
      Open it: the phone boxes type left to right. Search for a number nobody
      has, press the flag button: the heading shows the number the right way
      round. **Screenshot the list and the open contact.**
- [ ] **Table headings** in Arabic have no gaps between the letters.
- [ ] **Counts of 2 and 5.** Menu → Manage categories: a category with two
      items says **صنفان**, one with five says **5 أصناف** (not "2 صنف").
      On Calls, a search with 2 results says **مكالمتان**, with 5
      **5 مكالمات**. **Screenshot a 2 and a 5.** In English: "1 item", "2
      items", never "1 items".

**How it looks.**

- [ ] **The main buttons** (Search, Save, Add, Open the queue) are a slightly
      darker blue than before, with white text; **Remove / Stop listening**
      a slightly darker red; hints under fields a little lighter. Links, the
      bar beside the selected page and the charts keep the old blue.
      **Screenshot a page with a primary button, in both languages.**
- [ ] **Users: open a row** (Set extension, or Reset password). The form opens
      on the **same dark panel** as an opened row on Calls or Contacts, not a
      white band, and it does not flicker when the pointer moves over it.
      **Screenshot it, in both languages.**
- [ ] **The font is Cairo.** Every screen's letters change shape from before
      (Segoe UI). In the browser's developer tools, Network: no request goes
      to `fonts.googleapis.com`; the `.woff2` files come from the server.
- [ ] **Delivery prices** show two decimals (`7.50`, not `7.5`); a menu
      add-on shows `+2.00` with the plus in front, in Arabic too.

**Behaviour.**

- [ ] **Remove asks twice**, on a delivery area, a menu item and a menu
      category. First click: the button turns red and says **Confirm
      remove**. Wait four seconds, or press Escape, or click elsewhere: it
      goes back. Click twice slowly: it removes. A quick double-click only
      arms it. If the server refuses (a category that still has items), a red
      line says why.
- [ ] **Searching waits for you to stop typing.** Type a name quickly in
      Contacts, Delivery or Menu: the list does not flash "Loading…" per
      letter; the old rows dim, then change. On Calls and Applications, after
      **Search** the table dims until the new results arrive.
- [ ] **Saving one menu item** refreshes that item's picture only; the other
      pictures do not blink.
- [ ] **The report tabs by keyboard.** On Call reports, Tab onto the tabs; the
      arrow keys move between them and open each (in Arabic the left arrow
      goes to the next one); Home and End go to the ends.
- [ ] **A link keeps its tab through the sign-in.** Sign out, then open
      `http://localhost:5173/call-reports?tab=abandoned` and sign in: you land
      on the **Abandoned** tab.
- [ ] **Signing out leaves nothing behind.** Open Contacts, sign out, sign in
      again: nothing from before flashes up while the page loads.
- [ ] **The Dashboard and Call reports** show "Loading…" for a moment the
      first time each is opened (their charts are fetched then). After an
      update, a tab left open shows *could not be loaded* with **Try again**,
      which reloads the page.
- [ ] **A recording that will not download.** Open a recorded call on Calls,
      stop the server, press **Download**: a red line says the recording
      could not be fetched. (It used to do nothing.)

**The sign-in rate limit (needs the server change from prompt 18).**

- [ ] **Too many attempts.** Sign in with a wrong password again and again
      until the server stops you: the message becomes **Too many sign-in
      attempts. Wait a few minutes, then try again.** / **محاولات تسجيل دخول
      كثيرة. انتظر بضع دقائق ثم حاول مرة أخرى.** **Screenshot it in both
      languages.**
      *If it keeps saying the password is wrong however many times:* the
      server's limit is not in yet, or it answers something other than 429.

**Changing your own password (needs today's server).** Signed in as a
supervisor, on **Users**, press **Reset password** on your **own** row.

- [ ] **Two boxes, not one.** Your own row shows **Your current password** /
      **كلمة المرور الحالية** above **New password**, and the hint says you
      will be signed out. **Save** stays greyed out until both are filled.
      Another person's row shows only **New password**, as before.
      **Screenshot your own row's form in both languages.**
- [ ] **A wrong current password.** Type a wrong one and a new one, Save:
      a red line says **Your current password is not correct.** /
      **كلمة المرور الحالية غير صحيحة.**, and your password has not changed.
- [ ] **The right one.** Save with your real current password: you are
      signed out, and you sign in with the **new** password; the old one is
      refused.
      *If the save is refused with "type your current one as well" although
      you did:* the web app and the server disagree about the field. Tell me.

### 1.12 Logs: the Agent Apps' logs, errors first (S-64, N-12) — built 27 Sep, never run

Needs an Agent App that has signed in at least once since N-12, so there is a
log on the server (Round 2, "The log reaches the server").

- [ ] **The page.** As a supervisor, **Logs** is in the sidebar after
      Settings. It lists this laptop, with when it last sent and its
      errors and warnings today, or a green "No errors". Sign in as an agent
      instead: there is no Logs page, and `/logs` goes to the Agent App page.
- [ ] **An error in red.** Make one: in the Agent App, stop the server and
      save a contact, or anything that fails. Start the server, wait a minute
      and refresh Logs. The laptop's count went up, it has a red edge, and the
      red line at the top counts today's errors. In its log the error is on a
      red background, open, with the lines under it; a warning (the phone not
      registered, for one) is amber; the rest are grey and folded to one line,
      with "N more lines" to open them. **Screenshot it.**
- [ ] **Filter and search.** "Errors only" leaves only the red ones;
      "Warnings and errors" adds the amber. Type a word from an error into the
      search: only entries containing it, in whatever part of them.
- [ ] **Next error.** With several errors on screen, press **Next error**:
      the list moves to the first and rings it; again, the next; after the
      last, back to the first.
- [ ] **Another day.** The page opens on today's date. Pick yesterday in
      the date box: the laptops' counts are yesterday's, and the log is that
      day's; a laptop that sent nothing then says "No log that day".
      **Today** brings it back.
- [ ] **Scrolls.** On a day with many entries the log scrolls inside its
      own box, and the filters above it stay on screen.
- [ ] **A nickname.** Above a laptop's log press **Give it a name**, type
      "Front desk" and Save: the list and the heading say Front desk, with
      the Windows name small under it. Reload, or open the page as another
      supervisor: still Front desk. **Rename**, empty the box and Save: the
      Windows name again.
- [ ] **Acknowledge.** With errors today, press **Acknowledge** on the red
      line: it goes, and a grey line says you acknowledged it and when. Open
      the page as another supervisor: no red line there either. Make one
      more error in the Agent App and wait a minute: the red line is back,
      saying "1 new error today since it was acknowledged".
- [ ] **Keeps up.** Leave the page open with "Keep up to date" ticked, and use
      the Agent App: new entries appear within a minute without refreshing.
      Untick it: they stop until you tick it or reload.
- [ ] **Arabic.** Switch the page to Arabic: the labels are Arabic and read
      from the right, and the log text stays left to right, as written.

### 1.13 Mistakes: what a branch or an agent got wrong (S-65) — built 1 Oct, never run

No phone needed. Restart the server first, so the `mistakes` table is made
(the migration runs on startup).

- [ ] **The page.** As a supervisor, **Mistakes** (الأخطاء) is in the sidebar
      after Contacts. It opens on today, with "No mistakes match". Sign in as
      an agent instead: there is no Mistakes page.
- [ ] **An agent's mistake, with a customer found by number.** Press
      **Record a mistake**. Pick a branch, leave **An agent** chosen, pick an
      agent, type a value (12.5), and in the customer box type a saved
      customer's number in another form than it was saved (+970 59… for
      059…). Under the box: "Saved customer: <their name>". Write a note and
      save. The row shows the branch, An agent (amber), the agent, the
      customer's name with the number under it, 12.50 and the note; the count
      line says 1 mistake and the total value 12.50. **Screenshot it in both
      languages.**
- [ ] **A customer found by name.** Record another, and in the customer box
      type part of a saved customer's name. A short list of matching
      customers appears, each with their number; **Save** stays greyed until
      one is picked. Pick one: their number fills the box and "Saved
      customer: …" shows under it.
- [ ] **A branch's mistake.** Record another and choose **The branch**: the
      agent box empties and greys out, with "A branch's mistake names no
      agent". Save with no value and no customer. The row says The branch
      (grey), with no agent, value or customer.
- [ ] **A number nobody has.** Record one with a number that is not a saved
      customer: under the box, "Not a saved customer. The number is kept as
      typed." The row shows "Not a saved customer" and the number.
- [ ] **Refusals.** The date box does not go past today. Save stays greyed
      without a branch, without notes, or with An agent chosen and no agent.
- [ ] **Correct and remove.** Double-click a row (or Edit): the form opens
      under it with its values. Change the note and save. Then **Remove**:
      the first click asks "Confirm remove", the second removes it.
- [ ] **Search.** Set the From box a week back and Search: older mistakes
      appear. The Branch, Responsible and Agent filters narrow it; the search
      box finds a mistake by the customer's number, by their name, or by a
      word in the notes. The count and the total value follow the filters.
- [ ] **Export to Excel.** **Export all N (Excel)** saves `mistakes-<date>.csv`.
      Open it in Excel: Arabic headings (English when the page is in English),
      the Arabic names readable, the numbers with their leading 0, and every
      mistake that matches the filters, not only the page on screen.
- [ ] **Compensated (تم التعويض, added 3 Oct).** A mistake recorded before 3
      Oct shows *Not compensated*. Edit one, tick **The customer has been
      compensated** under the value, save: the row now says *Compensated*
      (green). Record a new one: the tick starts empty. The **Compensated**
      filter set to Compensated, then Search, lists only the ticked ones, and
      the count and total value follow. The export has a Compensated column
      with Yes / No (نعم / لا).

### 1.13b The mistakes report (R-23) — built 1 Oct, never run

Record a few mistakes first (1.13): at two branches, some the branch's own
and some an agent's, a few with a value, and two for the same customer.

- [ ] **The page.** **Mistakes report** (تقرير الأخطاء) is in the sidebar right
      under Mistakes (Breaks is now one further down). It opens on Today, with
      four cards: Per branch, Per agent, Over time, Repeat customers.
- [ ] **Per branch.** Each branch with its mistakes, the branch's own, its
      agents' and the value, and a Total row; the chart has a bar for each of
      the two kinds. The counts agree with the Mistakes page for the same day.
      **Screenshot it in both languages.**
- [ ] **Per agent.** Only agents, most mistakes first. A branch's own mistake
      is not counted against anybody.
- [ ] **Over time.** Choose This month, then **Per** → Month: one row for the
      month. Per → Day: a row per day that had a mistake.
- [ ] **Repeat customers.** The customer with two mistakes is listed once,
      even if their number was typed two ways; a customer with one is not
      listed. A repeated number nobody has on file says "Not a saved customer".
- [ ] **Filters.** Choosing a branch, or an agent, changes all four cards at
      once.
- [ ] **Compensated (added 3 Oct).** Tick one or two mistakes as compensated
      first (1.13). Per branch, per agent and over time each have
      *Compensated* and *Compensated value* columns; repeat customers has
      *Compensated*. The **Compensated** filter set to Not compensated changes
      all four cards, and a printed page says "Compensated: Not compensated"
      at the top.
- [ ] **Export and print.** Export CSV on a card saves its rows, which open in
      Excel with the Arabic readable. Print on a card prints that card, with the
      period at the top.

### 1.14 Breaks: who is on break, and the break report (S-66, R-22) — built 1 Oct, never run

No phone needed, but an Agent App with the Break button (Round 2, "Break in
and Break out"). Restart the server first, so the `agent_breaks` table is made
(the migration runs on startup). Easiest with the web app and the Agent App
side by side.

- [ ] **The page.** As a supervisor, **Breaks** (الاستراحات) is in the sidebar
      after Mistakes. The top card, **Now**, lists every active agent with
      *Signed out* or *Working*, and says the daily allowance is 60 minutes.
      Sign in to the web app as an agent instead: there is no Breaks page.
- [ ] **Live.** Press **Break in** in the Agent App. Within ten seconds the
      agent moves to the top with *On break* (amber), the time of this break
      counting up every second, "since <time>", and **Break today** counting
      with it. Press **Break out**: within ten seconds *Working*, and Break
      today stops. **Screenshot the card during a break, in both languages.**
      *If it never changes:* the Agent App is an old build (check the `bin`
      date) or the break did not reach the server (its log says "queued").
- [ ] **Do not disturb (A-18, added 3 Oct).** Restart the server first, so the
      `AddSessionDoNotDisturb` migration runs, and use an Agent App built on or
      after 3 Oct. The **Do not disturb** column shows *Off* for the signed-in
      agent. Tick **Do not disturb** in the Agent App: within ten seconds it
      shows *On* (amber) with "since <time>". Untick it: *Off*. Break in turns it
      *On*, Break out *Off*. Signed-out agents show nothing. **Screenshot it with
      one agent On, in both languages.** *If it stays empty for a working agent:*
      the Agent App is an old build (check the `bin` date). *A dash (—):* the
      app on that laptop is older than 3 Oct and does not say.
- [ ] **The allowance in Settings.** In **Settings**, *Break allowance
      (minutes a day)* shows **60**, not an empty box. *If it is empty:* the
      server is older than the `SeedBreakAllowance` migration.
- [ ] **Over the allowance.** Set it to **1** and save. Without signing the
      agent out, take a break of over a minute. Break today turns red, and **Over the allowance by**
      shows the excess. Put the setting back to 60 afterwards.
- [ ] **The report.** Below, the period buttons and an **Agent** box only (no
      Branch, Channel or Type). *Break time per agent* shows today's breaks
      in minutes with a bar chart; *per agent per day* one row per agent;
      **Every break** each break with Break in, Break out, Length and *Break
      out* as how it ended. Choose an agent: all three narrow to them.
- [ ] **A break the app never ended.** Press Break in, then end the Agent App
      from Task Manager. Within five minutes the card shows the agent as *App
      not heard from*. Start the app and sign in: the break in **Every break**
      now ends at about the moment the app was killed, as *App stopped* (or
      *Sign-in ended* if you signed in again within five minutes), and the
      agent is *Working*, not on break.
- [ ] **Export.** **Export all N (CSV)** saves `breaks-<from>-<to>.csv`. Open it
      in Excel: Arabic headings (English when the page is in English), the
      names readable, the minutes as numbers, and every break in the period.

## Round 2 — the Agent App, without a phone

Sign in as `dia20`. None of this needs a call.

- [x] **Sign in as `supervisor` first.** *Signed off by Dia 24 September.* It must refuse with "This is a
      supervisor account. Supervisors use the web app in the browser", and the
      Arabic equivalent with the interface in Arabic. If a raw key such as
      `login.errors.not_an_agent` shows instead, the label is missing. If the
      app signs in, it is running an old build: check the `bin` date. Then sign
      in as `dia20` and carry on.
- [ ] **Menu tab (A-66).** Search `سماشد`, then `ماشروم`, then a misspelling of
      one. The folding is meant to find them all.
- [ ] **Search by what is in an item** — "mushroom" in the contents, not the
      name. "What has mushrooms in it?" is a real question agents get.
- [x] **Pictures load in the Agent App.** *Confirmed 21 September.* They did
      not at first: every row fetched its own picture at once, so a search fired
      39 simultaneous requests per keystroke and most timed out silently — and
      starved the delivery search, the call log and the block-list refresh with
      them. Now fetched once and kept, four at a time.
- [ ] **Search the menu quickly, several letters in a row.** The pictures
      should stay put rather than flickering or disappearing, and the delivery
      and call log tabs should still answer straight afterwards.
- [ ] **Count the boxes with no picture: there should be exactly 5**, all in
      إضافات, each saying **بلا صورة**. A box saying **تعذّر تحميل الصورة**
      is a fault — report it.
- [ ] **An item from the category you hid in 1.1 is still findable.**
- [ ] **Delivery tab (A-65).** Search an area; it names the branch and price.
- [ ] **An incoming call rings (A-10).** Ring the extension: the laptop plays
      a two-burst bell until you answer or reject, or the caller gives up. It
      stops the instant you press Answer. With Auto answer ticked it does not
      ring at all.
- [ ] **Call log tab (A-14).** It lists past calls.
- [ ] **Every column fits.** Shrink the window to its smallest: the last
      column ("Not classified" chips and notes) is still fully on the card,
      nothing is cut at the right edge, and a long customer name shows "…"
      with the full name in its tooltip. The first column is an arrow: in for
      incoming calls, out (blue) for calls you placed; hover for the word.
- [ ] **Open a date filter's calendar.** The day numbers, the Su/Mo/Tu row and
      the month name must all be readable, the chosen day is blue and today is
      tinted. *It was near-white text on a near-white panel until 21 September:
      the Calendar was styled but the panel inside it was not.*
- [ ] **Filter the log by a date older than the newest 100 calls.** This is the
      bug that was fixed but never seen: filtering used to happen in the app
      over one fetched page, so a date outside that page found nothing.
      *If an old date finds nothing, the fix did not take.*
- [x] **Classify a call while talking (A-40).** *Confirmed 21 September — three
      real calls, through the offline queue, into the database.* The form opens
      when the call is answered and stays until saved or skipped.
- [x] **Classify a call from the log, and edit one already classified (A-41,
      A-42).** *Confirmed 21 September.* Double-click a row; an already
      classified call opens filled in.
- [ ] **Double-click a call from a previous day.** It opens **read-only** with a
      message, because the agent's edit window has closed (A-42).
- [ ] **Classify with the server stopped.** The form still saves; the call and
      its classification reach the server at the next sign-in (A-04).
- [ ] **Contact history.** Open a contact; its past calls are listed.
- [ ] **Leave the app sitting idle.** With 240 minutes that is a four-hour test,
      so do it on a day you are at the desk anyway rather than blocking on it.
- [ ] **Signed out by the server (N-05).** With `dia20` signed in here, reset
      `dia20`'s password in the web app's Users screen. Then do something in the
      Agent App that asks the server, such as a contacts search. It must go back
      to the sign-in screen with "You have been signed out…", in the app's
      language, and the phone status must no longer show registered.
      *If it stays on the main screen with failed lookups:* the Agent App is an
      old build. Check the `bin` date. Sign in with the new password afterwards.

---

### Applications: recording a message (A-70 to A-73) — never run

Built 25 Sep. A message is a WhatsApp, Facebook, Instagram or Wheels
conversation the agent records by hand. None of this needs a call. The API
must be running: a message is not queued offline (Dia, 25 Sep).

- [ ] **The rail has two new sections under Call log: التطبيقات /
      Applications, then سجل التطبيقات / App logs** (split 25 Sep at Dia's
      request). **Applications** holds only the card "Record a message": App (a
      dropdown, **no "Phone" in it**, the first app pre-selected), the
      customer's number (already focused), Time (now, HH:mm), then the
      Applications classification form, and Record / Clear. **App logs** holds
      the filter row and the agent's list, empty at first. *If any label shows
      as a raw key such as `applications.record` or `nav.appLogs`:* the label is
      missing in that language; tell me which.
- [ ] **The record form uses the page.** One wide card: the app, the number
      and the time on the start side, "What was the message about?" beside
      them, and Record and Clear across the bottom. On a very wide screen it
      stops at about 1,100 pixels, at the start edge, rather than stretching.
      In Arabic the two halves swap sides.
- [ ] **Type a known customer's number** (one from Contacts) and pause. Their
      name and address appear under the box. **Type an unknown number:** "New
      customer — not in contacts" and a **Save as new customer** button
      (A-11); press it, fill the name, save: the name appears and the form
      closes. *If the lookup never answers:* the API is down or the number is
      under six digits.
- [ ] **Record a classified message.** Pick a type and branch, fill any
      required field, press **Record message**. A green "Message recorded";
      the number clears, the time resets to now, and the form redraws with the
      **same app and the same branch still selected** (A-73). Switch to **App
      logs**: the message is at the top of the list with a green "Classified"
      chip.
- [ ] **Press Record with no type chosen.** Nothing is sent, and an amber line
      says to choose what the message was about. A message is always recorded
      with its type (Dia, 25 Sep). **Record one with a type but a required
      field empty:** an amber "not complete" message and nothing is sent.
- [ ] **Set the time to a future hour** (23:59) and record: the server refuses
      with an amber message about the time. Type "abc" as the time: "Enter the
      time as HH:mm". Set it to an hour ago and record: accepted, and the row
      shows that hour.
- [ ] **In App logs, open a row** (double-click, or its Open button). Under the list,
      **without the list moving**: a details line, App / Number / Time boxes
      with Save changes, then the classification form prefilled. Change the
      time and save: "Message updated" and the row's time changes. Fix the
      classification on the "Not classified" row and save: its chip turns
      green.
- [ ] **The list reads as one row per message** on a wide screen: time, app,
      customer and number sit together at the start, then the chip and a whole
      **Open** button. A customer nobody has on file reads "Not in contacts",
      dimmed, rather than the number twice. *Reported 25 Sep:* Open was cut
      off at the bottom and the customer column stretched across the table.
- [ ] **Nothing in the list is cut, at any window width.** Shrink the window
      to its smallest: every column still shows its whole text, and the
      App logs tab scrolls sideways instead (below about 900 pixels wide). Widen it again: the scrollbar goes and the columns stay together.
      Check an unknown customer in Arabic, "غير موجود في جهات الاتصال", the
      widest text in the list. *If any cell ends in "…":* tell me which.
- [ ] **Filters.** Type part of a name or number and pause: the list narrows.
      Pick From / To dates: it narrows. "Clear filters" appears and resets.
- [ ] **Stop the API and press Record.** An amber offline message, and
      everything typed is still there. Start the API, press Record again:
      recorded. Nothing is sent twice.
- [ ] **The call log has none of these.** Switch to Call log: no message rows
      appear there, and the count of calls is unchanged.
- [ ] **Switch language.** Every label on the screen changes: rail, headings,
      column headers, chips, messages. **Take a screenshot of the section in
      each language** with one recorded message open.
- [ ] *(Another day.)* **A message from an earlier day** opens with the App /
      Number / Time boxes greyed and a note that it was written on an earlier
      day. The supervisor can still change it from the web app.

### Copying text (A-84) — built 27 Sep, never run

Paste each one into Notepad to check what was copied.

- [ ] **Nothing looks different.** Open Menu, Delivery, Call log and
      Applications, in both languages. The text is where it was, the same
      size and colour, with no box or border around it. *If any text has
      moved, grown a box, or been cut off:* screenshot it.
- [ ] **Select and copy.** In Menu, drag across an item's name: it turns
      blue. Ctrl+C, paste: the name. Double-click a price: the number is
      selected. Right-click it: **Copy** and **Select all**, dark, in the
      app's language (نسخ / تحديد الكل in Arabic). No Cut, no Paste.
- [ ] **It cannot be typed into.** Click a name in Menu and type: nothing
      changes. Press Tab from the Menu search box: the cursor does not stop
      on the names.
- [ ] **The page still scrolls.** In Menu, with the pointer over an item's
      description, turn the mouse wheel: the list scrolls.
- [ ] **The lists.** In the Call log, right-click a call's number: **Copy**
      and **Copy row**. Copy, paste: the number only. Copy row, paste: the
      direction, time, customer, number, queue, duration, status and note,
      separated by tabs. Right-click the recording icon: only Copy row.
      A single click still chooses a row, and a double-click still opens it.
      The same in App logs and Contacts.
- [ ] **The opened call.** Open a call in the Call log: its customer, number
      and time can be selected and copied. The same for an opened message in
      App logs, and the customer found in Applications.
- [ ] **An error.** Sign out, and sign in with a wrong password: the red
      message can be selected and copied.
- [ ] *(Needs the PBX.)* **The pop-up.** On a call, select the caller's
      number and name and copy them. While the text is selected, the call's
      keys still work: Ctrl+Shift+H holds, Ctrl+Shift+E hangs up.

### Break in and Break out (A-86) — built 1 Oct, never run

The server must be the 1 Oct build or later. A call is only needed for the
last step.

- [ ] **The button.** In the rail, between the phone's status and the Do not
      disturb switch, a blue **Break in** (بدء الاستراحة), with *Break today:
      0:00:00* and *Allowed: 60 minutes a day* under it. **Screenshot the rail
      in both languages, at 1366 × 768**: nothing in the rail should be cut
      off. *If a raw key such as `breaks.in` shows:* a label is missing.
- [ ] **Break in.** Press it. The card turns amber, the button becomes a green
      **Break out**, "On break since <time>" appears, Break today counts every
      second, and **Do not disturb is ticked and greyed**: clicking it does
      nothing. Auto answer is greyed too.
- [ ] **Break out.** Press it. Do not disturb is unticked and can be clicked
      again, and Break today stops where it was.
- [ ] **It adds up.** Take a second break: Break today carries on from where
      the first stopped, not from 0:00:00. Sign out and in again: it is still
      there. *If it starts from zero after signing in:* the server could not
      be asked (the log says "Today's break time could not be fetched").
- [ ] **Over the allowance.** With the allowance set to 1 minute in Settings
      (no need to sign out: Break in reads it again), take a break past a minute: a red line "You are
      over your break time by 0:00:…" appears and grows. Break out, then Break
      in again: the bar at the top says today's break time is used up, and
      the break starts anyway.
- [ ] **Signing out ends it.** Press Break in, then Log out: in the web app
      the break ends as *Signed out*. Sign in again: not on break, and Do not
      disturb is off.
- [ ] **With the server stopped.** Stop the server, press Break in and, a
      minute later, Break out: the app carries on as normal. Start the server:
      within a minute the break appears on the Breaks page with the right
      times. *If it never arrives:* the Agent App's log says why (look for
      "Break" on the Logs page).
- [ ] *(Needs the PBX.)* **No calls during a break.** On break, ring the
      agent's extension: it does not ring, the call goes to the next agent,
      and **nothing is logged for the agent on break**: no Missed row on the
      Calls page or in their call log (A-18, changed 1 Oct).

### The log reaches the server (N-12) — built 27 Sep, never run

The server's copy is in `logs\agents\` beside the server's own
`callcenter-<date>.log`: on this machine
`src\CallCenter.Server\bin\Debug\net10.0\logs\agents\` when the server is
started from its bin folder; on the restaurant's server,
`/opt/callcenter/data/logs/agents/`.

- [ ] **It arrives.** Sign in to the Agent App and wait a minute. A folder
      named after this laptop appears under `logs\agents`, with today's
      `agent-<date>.log`. Open it beside
      `%LOCALAPPDATA%\CallCenter\logs\agent-<date>.log`: the same lines, the
      server's at most half a minute behind.
- [ ] **Before sign-in goes too.** Close the app, start it, wait a minute on
      the sign-in screen, then sign in. The "Agent App started" line from
      before sign-in is in the server's copy within a minute.
- [ ] **Nothing twice, nothing lost, after the server was down.** Signed in,
      stop the server for two minutes and search Contacts meanwhile. Start it
      again and wait a minute: the lines from the minutes it was down are in
      the server's copy, each once. The laptop's log has one "The log could
      not be sent" line for those minutes, not one every half a minute.
- [ ] **No loop.** Leave the app signed in and idle for five minutes. The
      laptop's log does not grow by "Start processing HTTP request … agent-logs"
      lines every half a minute, and the server's own log
      (`logs\callcenter-<date>.log`) has no line per send.
- [ ] **The laptop keeps 3 days.** A few days later:
      `%LOCALAPPDATA%\CallCenter\logs` has at most three `agent-*.log` files.

### Installing from the web app (S-63, N-11) — built 27 Sep, never run

The installer could not be run on Dia's laptop (its policy blocks unsigned
programs under the user profile), so the first install is on an agent's
laptop. Build first: `tools\agent-app\publish.ps1`.

- [ ] **Upload.** Sign in to the web app as a supervisor. **Agent App** is
      the last item in the menu. Before any upload it says none has been
      uploaded. Choose `publish\SmashedAgentApp-Setup-<version>.exe`: the
      Version box fills in by itself. **Upload**: after a few seconds it
      says agents now download that version, and the card above shows it,
      with its size (about 70 MB) and the time. *If it says the file is not
      a Windows program, the zip was chosen instead.*
- [ ] **A wrong file is refused.** Choose the `.zip` renamed to `.exe`:
      *not a Windows program*, and the card still shows the version before.
- [ ] **An agent sees one page.** Sign out, sign in with an agent account.
      It opens on **Agent App**; the menu has that item alone, and no
      upload form. Type `/dashboard` in the address bar: it comes back to
      Agent App. The dashboard's *agents online* does not count this
      agent for the browser sign-in. *If an agent sees the dashboard,
      RequireSupervisor is not in the route.*
- [ ] **Download and install over the zip's copy.** On a laptop that has
      the app in `C:\SmashedAgentApp` from the zip, with the app open:
      **Download the installer**, open the file. Windows' blue *protected
      your PC* screen: *More info*, *Run anyway*. The installer asks to
      close the Agent App, installs with no administrator prompt, and
      offers to start it. It starts, the offline queue and the block list
      are as they were, and signing in works.
- [ ] **Only one copy on the laptop.** Search the laptop for
      `CallCenter.AgentApp.exe` (Downloads, Desktop, Documents): the only
      one is in `C:\SmashedAgentApp`. Delete any other folder, and any
      desktop shortcut that points to it. *Two copies signed in as one
      agent take each other's calls (prompt 20).*
- [ ] **The zip goes up with it.** As the supervisor, choose the installer
      and, in the second box, `publish\SmashedAgentApp-<version>.zip`, and
      **Upload**. The page now has *If the installer does not work* under
      the steps, with the zip's size (about 80 MB). Put the installer in
      the zip box instead: *not a zip*, and the installer is still offered.
- [ ] **The zip by hand works.** On a laptop, as an agent: **Download the
      zip** and follow the five steps on the page. After *Unblock*, the
      app starts from `C:\SmashedAgentApp` with no blue Windows warning,
      the offline queue and the block list are as they were, and signing
      in works. *If Windows warns on every start, the zip was not
      unblocked before extracting.*
- [ ] **Arabic.** Switch the web app to Arabic: the page reads right to
      left, and the version number reads `0.4.1`, not reversed.
- [ ] **Screenshots** of the page as an agent and as a supervisor, in
      both languages.

---

## Round 3 — the telephone

This needs the PBX at 192.168.0.27 and extension 2001. It is the part that
cannot be faked, and the part most likely to find something.

- [ ] **Signed out by the server during a call (N-05).** Answer a call as
      `dia20`, and while it is up, reset `dia20`'s password in the web app. The
      call must **carry on**: nothing happens until you hang up. Then the app
      goes to the sign-in screen with "You have been signed out…". Sign in again
      and check the call is in the call log: it was queued and sent after the
      new sign-in.
      *If the call drops at the reset:* stop and tell me. That is exactly what
      this is built not to do.
- [ ] **Call extension 2001 from another phone.** The pop-up appears, with the
      caller's number.
- [ ] **The caller's name, address and notes appear (A-10).** Call from a
      number saved as a contact. The number shows immediately and the name
      fills in a moment later.
      *If it says "New customer" for somebody who is on file:* the number is
      stored in a format that did not match (A-13). Compare the contact's saved
      number with what the pop-up showed.
- [ ] **A VIP caller shows the badge (A-16).** Flag a contact VIP in the
      supervisor app with a reason, then call from that number. The badge and
      the reason sit above everything else.
- [ ] **An unknown number says "New customer — not in contacts"**, with a small
      **Save as new customer** button under it (A-11). The classification form
      is not pushed down until the button is pressed.
- [ ] **Press it, type a name and address, Save.** The pop-up switches to the
      customer's name as if they had been known. Afterwards, in the web app,
      the contact's history shows **this call**, and any earlier call from the
      number. *If the history is missing the call:* tell me; that is the link
      A-11 asks for.
- [ ] **Type a name that already exists.** Leaving the box shows the matching
      contacts with **Add this number to them**. Pressing it puts the number
      on that contact, and the pop-up shows them.
- [ ] **Open the new-customer form on an answered call**, so it sits above the
      classification. The middle of the pop-up **scrolls**, and the caller's
      name at the top and **Mute / Hold / Hang up** at the bottom stay on
      screen while it does. *Reported 24 Sep:* the window was cut off, with no
      way to reach what was below.
- [ ] **The pop-up stays centred as it grows.** Answer a call, so the
      classification appears, then open the new-customer form: it grows up and
      down equally and stays in the middle of the screen. Drag it aside and
      open a field: it stays where you put it. The next call is centred again.
- [ ] **The scroll bar is thin and dark**, a rounded grey bar with no arrows
      that lightens under the pointer, in the pop-up and in every list (call
      log, contacts, menu). In Arabic the pop-up's bar is on the left.
- [ ] **Open the form, hang up, keep typing.** The pop-up stays until Save or
      Cancel. **Stop the server and Save:** it says the server could not be
      reached and keeps what was typed; start the server and Save again.
- [ ] **Stop the server, then call.** The pop-up must still appear with the
      number and Answer, and must say **"Could not check who this is"** — not
      "New customer". Telling an agent a regular is new is how a second record
      for the same person gets typed.
- [ ] **Two calls in a row from different numbers.** The second pop-up must
      never show the first caller's name.
- [ ] **A customer with history shows their totals (A-10).** Classify a few
      calls from one number as orders and complaints, then ring in from it. The
      pop-up shows the three counts under the name. No list of past calls
      follows them: that was removed on 2026-09-22.
      *If the totals are missing but the name is there:* the second request
      failed. The counts come separately from the name, on purpose, so this
      fails on its own. Check the log for "history could not be fetched".
- [ ] **A customer with no classified calls shows no totals at all** — not
      three zeroes.
- [ ] **An unclassified call in the list says "Not written up"**, rather than
      being left out.
- [ ] **The call log stays newest first, and column headers do nothing.**
      Click every header in the call log. Nothing should re-order.
      *A click used to sort the grid by that column's text and the sort
      survived every later refresh*, which read as the refresh button being
      broken and was fixed only by signing out and in (22 September).
- [ ] **Press Refresh twice quickly, then once more.** The list must update
      every time and the button must come back enabled.
- [ ] **Open the call log at the default window size and read the last
      column.** The "Not classified" chip must be whole, without resizing the
      window first.
      *It used to arrive clipped and repair itself when the window was dragged*
      (22 September). Check it in Arabic too, where the label is a different
      width.
- [ ] **Make the window as narrow as it will go.** The columns must still fit,
      with Customer giving up the space rather than the chip disappearing.
- [ ] **The ringing is audible.**
- [ ] **The timer counts** once answered.
- [ ] **The queue badge** is right when a second call arrives.
- [ ] **Hang up from the agent side.** The customer's phone must actually end
      the call — not keep playing a tone.
      *This was a real bug: the call closed on our side only.*
- [ ] **The log then says the agent hung up, not the caller.**
- [ ] **Block a number, then call from it.** It must be **rejected
      immediately** — no ringback, no hold tone. Then unblock it and call
      again; it must ring.
- [ ] **Call 2001 while it is already on a call.** Busy.
- [ ] **Press Reject, and watch what the caller hears.** The log now prints the
      response the app sends, so look for `SIP OUT 603 Decline`.
      *If the caller is offered again a few seconds later:* the app rejected it
      correctly and the **queue put the customer back and rang the only member
      again**. That is a dialplan question, and it is what "Reject does nothing"
      looked like on 22 September. See the DECISIONS entry of that date on 603
      versus 486.
### Call recording (A-30, A-32) — capture confirmed on a real call 24 Sep

*Both channels carried speech and alternated correctly on a 34-second test call.
The first attempt recorded the agent as silence; if that ever returns, the log
now warns and names the side.*

- [ ] **Answer a call, talk for 30 seconds with both people speaking, hang up.**
      A file appears in `%LOCALAPPDATA%\CallCenter\recordings`; the log line
      says exactly where.
      *The header and the channel layout were checked with generated tones, so
      what is untested here is the real audio path, not the file format.*
- [ ] **Play it.** It must open in Windows Media Player or VLC.
- [ ] **Both voices are there**, and in step — neither lagging the other.
      With headphones, the customer is on the left and you are on the right.
- [ ] **Put the call on hold for a few seconds.** That stretch must be silence,
      not the sound of the room. Same for mute.
      *If you can hear the office while muted, stop and tell me: that is a
      privacy failure, not a glitch.*
- [ ] **A call nobody answers leaves no file.** Missed, rejected and blocked
      calls have no conversation to record.
- [ ] **Fill the disk, or make the folder read-only, then take a call.** The
      call must connect and behave normally (A-32). The log says the recording
      could not start; nothing else changes.
- [ ] **Check the size.** Roughly 0.9 MB per minute. Far off that in either
      direction means the format is not what it should be.

- [ ] **Does taking a call reset the idle-logout timer?** Flagged and never
      checked. If it does not, an agent on a long call gets logged out
      mid-conversation.
- [ ] **Mute (A-12).** Answer, press Mute. The status line at the top must say
      "Muted" and the button must now say "Unmute". Speak: the other phone hears
      nothing. Have the other side speak: you still hear them. Press Unmute:
      both ways again, status back to "Connected".
      *If the other phone still hears you:* the microphone was not paused; check
      the log for "could not be muted".
- [ ] **Stay muted for two full minutes.** The call must survive.
      *If the PBX drops it:* Issabel has an RTP timeout switched on. The fix is
      on our side — send frames of silence instead of pausing the microphone —
      and is described in the DECISIONS entry for Mute.
- [ ] **Hang up while muted, then take the next call.** The next call must not
      start muted.
- [ ] **Hold (A-12).** Answer, press Hold. The status line must say "On hold"
      and the button "Resume". The other phone must hear the PBX's hold music
      and you must hear nothing. Press Resume: both ways again, status back to
      "Connected".
      *If the other phone hears silence instead of music:* the hold worked and
      Issabel simply has no music class on that extension — a PBX setting, not
      a bug here.
      *If the pop-up says "On hold" but the other phone still hears you:* the
      PBX refused the re-INVITE. Look in the Agent App log for a 488 or a
      warning from SIPSorcery. That would need the PBX administrator.
- [ ] **Hang up while on hold.** The other phone's call must end.
- [ ] **Hold, then have the other side hang up.** The pop-up must clear as it
      does on any hang-up.
- [ ] **Mute, then Hold, then Resume.** You must still be muted after the
      resume: status "Muted", button "Unmute".

### Hearing a recording in the call log (A-50, A-51) — never run

*Needs the server from today (part 3's `/api/recordings`) and one answered call
that was recorded and uploaded. Wear the headset: the recording plays where call
audio plays, the Windows default output device.*

- [ ] **The list marks recorded calls.** Open the Call log tab. An answered,
      recorded call shows a small **speaker** between Result and the chip
      column; hover it and the tooltip says it was recorded. A missed call shows
      nothing there.
      *If no call shows a speaker:* press Refresh first — a call that has just
      ended uploads its recording a moment later. If it still shows none, check
      the `recordings` table has a row for that call.
- [ ] **Double-click a recorded call.** A card opens **above** the
      classification form, with the customer, the number, the date and time,
      Incoming or Outgoing, the duration, the result and the queue. It says
      "Loading the recording…" briefly, then shows **Play**, a seek bar and
      `0:00 / m:ss`. The classification form below it opens exactly as before.
      *If the form no longer opens, or opens only after the recording loads:*
      that is a regression — the form must never wait on the audio.
- [ ] **The list stays on screen with a call open**, at the default window
      size. At least four rows stay visible, and the card and form below
      scroll instead of pushing the list away.
      *On the first try (24 Sep) the list shrank to a thin line.* If it does
      again, send a screenshot with the window size.
- [ ] **Play it.** You hear the call in the headset, **both voices in both
      ears** (before 2 Oct, the customer was in the left ear only and you in
      the right). The button says **Pause** and the time counts up.
      *If it is static or a harsh buzz:* the file was not read as mu-law. Tell
      me, with the log line containing "not a mu-law WAV".
      *If it says no speaker or headset was found:* Windows has no default output
      device. Check the sound settings, not the app.
- [ ] **Hear one side (2 Oct).** Under the player, *Listen to:* **Customer**
      plays only the customer, in both ears, at once and from the same
      moment; **You** only yourself; **Both** both again. The chosen button
      is orange. Open another call: it starts on **Both**.
- [ ] **Pause, then Play.** It carries on from where it stopped, not from the
      start.
- [ ] **Seek.** Click halfway along the bar: playback jumps there and the time
      matches. Drag the dot: it follows, and plays from where you let go.
- [ ] **Let it play to the end.** The button returns to **Play**; pressing it
      starts again from the beginning.
- [ ] **Every recorded call's recording reaches the server.** Make three
      short answered calls in a row: hang up yourself on one and let the
      customer hang up on another. After each, `%LOCALAPPDATA%\CallCenter\recordings`
      should empty again within a few seconds, and the call log shows the
      speaker on all three.
      *If a file stays behind:* search the Agent App log for "still waiting"
      and send me the lines around it. It should also clear by itself within a
      minute, because the queue is now retried every minute.
- [ ] **Holds are marked.** Take a call, talk, press **Hold** for about 20
      seconds, **Resume**, talk again, hang up. Open it in the call log: under
      the seek bar an amber mark sits where the hold was, and a line below says
      "On hold: m:ss–m:ss" with times close to when you pressed the buttons.
      Seek into the mark: the silence plays, and "On hold" shows beside the
      time.
      *No mark, but the silence is there:* the recording was made by an Agent
      App from before 24 Sep evening, or the hold was put on by the PBX rather
      than by the Hold button. Only the agent's own holds are marked.
      *The mark is in the wrong place:* note how far off it is and tell me.
      It should be within a fraction of a second.
- [ ] **Hang up while on hold.** Talk, press Hold, wait about 15 seconds, hang
      up without resuming. The recording runs to the hang-up, so its length
      matches the call's duration in the list, and the last hold is marked to
      the very end of the bar.
      *If the recording ends early and the last hold is missing:* the Agent
      App is older than this fix.
- [ ] **The same held call opens in any other player** (VLC, Windows Media
      Player) and plays normally. The marks are extra data those players skip.
- [ ] **A call rings while a recording is playing.** The recording **pauses at
      once**, the card says it is paused while you are on a call, and Play is
      greyed out until the call ends. Afterwards Play works again.
      *If the recording keeps playing over the caller:* stop and tell me — that
      is the one failure here that affects a customer.
- [ ] **Switch to another tab while it plays.** It pauses. Come back and Play
      carries on.
- [ ] **An answered call with no recording** (one from before uploads existed,
      such as the 11:14 test call on 24 Sep). Double-click it: the card says the
      call has no recording and mentions that a call which has only just ended
      may still be uploading. No Play button.
- [ ] **A missed, rejected, blocked or unanswered call.** Double-click it: the
      card shows the details and **says nothing about a recording** — no
      message, no Play. Only answered calls are recorded (A-30), so there is
      nothing to be missing.
- [ ] **An expired recording.** Needs one call whose recording retention has
      removed — use the "Retention actually deletes" step in Round 3a, or set
      `deleted_at` on one `recordings` row by hand on a test database. The list
      shows a **crossed-out speaker**; double-click it and the card says the call
      **was recorded** and the recording was deleted when its retention period
      ended. It must not read as "no recording".
- [ ] **Close, from any of the three buttons.** Close on the card, **Skip**
      under the classification form, and **Close** under a missed call's note
      each shut the whole call: the card, the recording (the audio stops) and
      the form or note. Nothing is left on screen below the list.
      *Before 24 Sep, Skip and the note's Close shut only their own panel and
      left the recording card standing.*
- [ ] **Save does not close it.** Saving a classification keeps the card and
      form open with "Saved", so an agent can keep listening while checking
      what they wrote.
- [ ] **Both languages.** Switch to Arabic and open the same recorded call. The
      card's labels are Arabic, the date, number and `0:00 / m:ss` read left to
      right, and the seek bar fills **from the right**. **Take a screenshot in
      each language with the player visible** — whether the bar should fill from
      the right in Arabic is a choice to settle on the screenshot.
- [ ] **A blocked call opens its details.** Double-click a Blocked row: the card
      opens alone, with no form and no note. It used to open nothing.

### Outbound dialling (A-20, A-21) — never run

- [ ] **The Dial screen is a keypad in the middle of the window.** The number
      across the top with a delete key, 1 to 9 then * 0 #, and a green Call
      button under them, all three the same width. Clicking a key fills the
      number and beeps like a phone key. Typing a digit on the keyboard does
      the same: the key on the pad lights up and beeps. Enter dials.
- [ ] **Dial your own mobile from the Dial screen.** Enter the number as it is
      held (no 48): the app adds the prefix itself, and the pop-up and the log
      show the number without it. The pop-up appears saying "Calling", with an
      **"Outgoing call"** badge, the number and a Cancel. Your phone rings,
      and **you hear ringing in the headset** while it does: one second on,
      four off. It stops the instant you answer, and also if you press Cancel
      or the number is busy.
      Answer it and the pop-up says "Connected", the badge stays, the timer
      starts, and the **outbound** classification form opens: Type, Notes and
      Follow-up by default, no Branch or Order value. On the Classification tab
      it is the second card, "Questions for outgoing calls"; add a question
      there and it appears on the next outgoing call only. The rest of the
      pop-up works exactly as it does on an incoming call (A-21).
      *Tried once on 22 September and it got as far as the PBX:* the number
      format is right, and the call was refused with **503 Service
      Unavailable** after about eight seconds of ringing tone. That is the
      PBX's outbound route, not the app. **Make this call with the speaker up**
      — Asterisk almost certainly announces the reason during those eight
      seconds, and nobody has listened yet.
      *If the log shows 404:* the PBX did not recognise the number in that
      format after all. Try `Dialing:Prefix` in the Agent App's
      `appsettings.json`.
      *If the log shows 401 or 407 repeatedly:* the PBX is challenging the call
      and the credentials are not satisfying it. That is a different fix.
- [ ] **One call, one log entry.** After any outgoing call that fails, the log
      must say "Call finished" **once**, and the server must not answer 500.
      *Two lines in the same millisecond was a real bug on 22 September:* a
      failed outgoing call ended twice and was reported twice.
- [ ] **The call appears in the call log as outgoing**, not incoming.
- [ ] **Dial, then press Cancel before answering.** Your phone must stop
      ringing. The log entry says NoAnswer.
- [ ] **Dial and let it ring out without answering.** After 45 seconds it gives
      up by itself and logs NoAnswer.
- [ ] **Dial a number that does not exist** (say 999999999). It should fail
      quickly and log Failed, not NoAnswer.
- [ ] **Mute and Hold on an outgoing call.** Both should behave exactly as they
      do on an incoming one. They act on the call, not on who started it, but
      that is a claim worth one test.
- [ ] **Try to dial while already on a call.** The Call button is disabled and
      says why.
- [ ] **Sign in with the phone unregistered** (stop the VPN) and open the Dial
      screen. The button is disabled and says the phone is not registered —
      rather than failing silently when pressed.

---

### The 27 Sep review fixes: the app no longer dies (F-01, F-02, M-A01)

Built 27 Sep. The first needs the PBX; the other two do not.

- [ ] **Save a new customer after the caller has hung up (F-01, A-11).** Ring
      2001 from a number nobody has on file. Press **Save as new customer**,
      then hang up the calling phone *before* saving. Type a name and press
      **Save customer**. The pop-up closes and **the app stays open**, still
      registered. In Contacts, the new customer is there with the number.
      *Until 27 Sep this closed the whole app,* and the phone stopped
      registering until someone reopened it. *If the app still closes:* it is
      an old build; check the `bin` date. Then do it once more with the call
      still up: the pop-up shows the customer's name, and stays.
- [ ] **Close the app while signed in (M-A01).** Sign in, wait for "Phone
      ready", close the window. It takes up to about five seconds, not longer.
      Then in the web app's Users screen, `dia20` shows as signed out at once,
      rather than when the idle timer catches it. The last lines of the log
      (`%LOCALAPPDATA%\CallCenter\logs\agent-<date>.log`) are "Signed out
      (AppClosed)" and "Agent App stopped". *If neither line is there:* the
      shutdown still is not being waited for; tell me.
- [ ] **The line across the top (F-02).** Nothing in normal use should make it
      appear, so this is a check that it never does: if an amber line saying
      "Something went wrong, and the app carried on" ever shows, the app
      survived an error that used to close it. **Send me the log from that
      minute**; it names the fault. Its **Dismiss** / **إخفاء** button hides it.

### The 27 Sep review fixes: the phone never stays busy (F-09, M-A02, M-A07, M-A08) — needs the PBX

The races themselves happen in milliseconds and cannot be forced by hand. What
can be checked is that ordinary calls still behave, and that the phone is free
afterwards every time.

- [ ] **Ten calls in a row, every kind.** Answer and let the caller hang up;
      answer and hang up yourself; Reject; let one ring out; dial out and
      Cancel; dial out and let it be answered. After each, the pop-up goes and
      **the next incoming call rings**. *If a call is ever answered "busy" with
      nothing on screen:* that is exactly what F-09 was. Send me the log.
- [ ] **Hang up on the caller the instant you press Answer.** Have the calling
      phone hang up at the same moment you click Answer, a few times. The pop-up
      goes, and the next call rings. Same with **Auto answer** on.
- [ ] **Dial, then Cancel immediately**, before the customer's phone rings. It
      must not ring at all, and the log says NoAnswer.
- [ ] **Unplug the headset during a call (M-A02).** A red line appears on the
      pop-up under the timer: "The microphone is not working…" or "The
      headset's sound is not working…", in the app's language. The call stays
      up, and the log says which device failed. Plug it back in; the next call
      starts without the red line. *If nothing appears:* the device kept
      running on the laptop's own speakers, which is not a failure. Check the
      log for "The microphone failed" or "The speaker failed".
- [ ] **The ring stops the instant you answer, every time.** Ring the extension
      and press Answer; do it five times, and once with the shortcut. *M-A08
      in the review asked for a different audio player; it turned out to be
      the one already in use, so nothing changed. If the app ever freezes as a
      ring stops:* send me the log, because that would prove the review right
      after all.

### The 27 Sep review fixes: the upload queue (F-03, F-08, M-A03, M-A04) — needs the PBX and a second agent account

- [ ] **Save is instant with the server stopped (M-A04).** Take a call as
      `dia20`, stop the server, classify the call and press Save. "Saved"
      appears at once, not after ten seconds. Start the server again: within a
      minute the call and its classification are in the web app.
- [ ] **One laptop, two agents (F-03).** Stop the server. Take a call as
      `dia20` and hang up. Sign out (it works with the server down). Start the
      server and sign in on the same laptop as a **second agent**. Wait a
      minute: `dia20`'s call must **not** appear in the second agent's call log,
      nor under them in the web app. Sign out, sign in as `dia20`: within a
      minute the call is in `dia20`'s call log. *If it turns up under the
      second agent:* the fix did not take; check the `bin` date.
- [ ] **Something the server refuses is set aside, and the rest still go
      (F-08).** This needs the server session's change (prompt 18) that refuses
      a call logged under someone else's extension. Stop the server, take a
      call as `dia20`, sign out. Start the server and, in the web app, give
      `dia20` a different extension. Sign in as `dia20` and take one more call.
      The new call reaches the call log as usual, and above the list an amber
      box says **"Could not be sent to the server (1)"**. The Call log button
      in the rail has an amber **1**. **Show** lists the call: its time,
      "Call", the number read left to right, and "Logged under another agent's
      extension". Put `dia20`'s extension back, press **Try again**: the box and
      the badge go, and the old call appears in the log. **Screenshot the box,
      open, in both languages.**
- [ ] **Sign-in with a backlog.** Stop the server, take three or four calls,
      and sign out. Start the server and sign in: the main screen comes
      straight away, and a call answered at once has its classification form.
      The backlog reaches the call log within a minute, behind you.

### The 27 Sep review fixes: shortcuts, focus, and sign-out (M-A05, M-A06) — the first two need the PBX

- [ ] **The ring does not take your typing (M-A05).** Start typing a sentence
      in Notepad (or a search in the Contacts tab), and have someone ring
      2001. The pop-up appears on top and the Agent App's taskbar button
      flashes, but **your typing carries on in Notepad**: the letters do not
      go into the pop-up. Click the pop-up and press Answer. *Before 27 Sep the
      ring took the keyboard.*
- [ ] **The shortcuts (M-A05).** With the pop-up or the main window in front:
      **Ctrl+Shift+A** answers a ringing call, **Ctrl+Shift+R** rejects one,
      **Ctrl+Shift+M** mutes and unmutes, **Ctrl+Shift+H** holds and resumes,
      **Ctrl+Shift+E** hangs up (and cancels a call you are dialling). Hover
      over each button in the pop-up: the tooltip shows its keys. Then check
      the keys do **nothing** where they do not apply: Ctrl+Shift+R during a
      connected call must not end it, and Ctrl+Shift+E while a call is only
      ringing must not answer or end it. With **Notepad** in front, the keys do
      nothing to the call: they are the app's only (Dia, 27 Sep). Try it with
      the keyboard switched to Arabic too.
- [ ] **Signing out and in again leaves nothing behind (M-A06).** Sign out and
      in five times, then switch language once. Everything follows the
      language as before, and the app is no slower. In Task Manager, the Agent
      App's memory should not climb by a step at each sign-in. *Until 27 Sep
      every sign-in left the last shift's screens listening, and they were
      redrawn at every language change for the rest of the day.*

### The 27 Sep review fixes: how it looks (tooltips, colours, numbers, 1366 × 768, menu pictures) — no phone needed

- [ ] **A tooltip in the dark theme.** Hover over **Do not disturb** in the rail:
      the hint appears as a dark grey box with light text and a thin border,
      not a pale yellow or white Windows box. The same over a long customer
      name in the call log. **Screenshot one.**
- [ ] **A right-click menu in the dark theme.** Right-click in the Contacts
      search box: Cut / Copy / Paste appear dark, with the item under the
      pointer tinted blue and the unavailable ones dimmed. *If it is white:*
      tell me, with a screenshot.
- [ ] **Nothing changed colour.** The VIP and Blocked chips in Contacts, the
      "Classified" chip in App logs, the pop-up's queue badge, the same-name
      warning, and the red box under the sign-in button (type a wrong password)
      look exactly as they did. Only where the colours are written down
      changed.
- [ ] **Numbers read left to right in Arabic.** Switch to Arabic. In Contacts,
      open a contact and type `+970 59` into the phone box: the `+` stays on
      the left. In the Call log and App logs, the Number column shows
      `0599…` the right way round, and a `+970…` number with its `+` first.
- [ ] **The window at 1366 × 768 (the agents' laptops).** On this machine, set
      the display to 1366 × 768 and Scale to 125 % (Settings → System →
      Display), or use a laptop that has it. Open the app: the whole window is
      on screen, title bar to bottom edge. Make it as narrow as it will go:
      it stops at a width that still fits the screen. In the Call log, tick
      "Needs classifying" so "Clear filters" appears: if the row is too wide,
      the end of it moves onto a second line instead of being cut off. Go
      through every rail section **in Arabic and in English** at that size and
      look for any text cut at an edge. **Screenshot the Call log and
      Contacts at 1366 × 768, in both languages.** Put the display back
      afterwards.
- [ ] **A menu picture that failed comes back.** Hard to cause on purpose: the
      menu itself needs the server. If you ever see **تعذّر تحميل الصورة /
      Picture failed to load** (the VPN blinking, a server restart), search
      the menu again a minute later: the picture should appear without
      restarting the app. *Before 27 Sep it stayed broken until a restart.*

### One phone per agent (N-05, A-05) — built 27 Sep evening, needs the PBX and two laptops

What went wrong on the evening of 27 Sep: a second copy of the app, signed in
as the same agent, took the agent's calls. These steps check each of the four
guards. They need the new server **and** the new Agent App on both laptops.

- [ ] **One copy per laptop.** Start the Agent App, sign in, and minimise it.
      Start it again from the Start menu or the desktop shortcut. The window
      that is already running comes back to the front; no second window, no
      second sign-in screen. Open Task Manager → **Details**: exactly **one**
      `CallCenter.AgentApp.exe` (or, on the dev machine, one `dotnet.exe`
      running `CallCenter.AgentApp.dll`). *If a second window opens:* the guard
      is not working; send the log. *If nothing comes forward but Task Manager
      shows one copy:* Windows would not hand over the focus; look at the
      taskbar button, which should be flashing, and tell me.
- [ ] **The laptop id.** On each laptop, the first log line after a start reads
      `Agent App started. Logs: … Laptop id DESKTOP-RMSFSIV-XXXXXX`, with a
      **different** six-character tag on each laptop. The tag stays the same
      after closing and starting the app again.
- [ ] **Signing in on a second laptop signs the first out.** Sign in as agent
      A on laptop 1. Then sign in as A on laptop 2. Within about a minute
      (at most two), laptop 1 goes back to the sign-in screen with, under the
      password box: **"You have been signed out here because you signed in on
      another laptop…"** / **"تم تسجيل خروجك هنا لأنك سجّلت الدخول على حاسوب
      آخر…"**. **Screenshot it in both languages** (switch the language on the
      sign-in screen). Meanwhile call the queue: it rings on **laptop 2**, at
      once, not on laptop 1. Keep calling for two minutes after laptop 1 has
      signed out: every call rings on laptop 2. *If laptop 2 stops ringing
      after laptop 1 signs out:* the old laptop unregistered the extension;
      send both logs.
- [ ] **Do Not Disturb is never bypassed.** On the laptop where agent A is
      signed in, tick **Do not disturb**. Call the queue several times: no
      pop-up, anywhere, for A. The log shows `turned away: do not disturb is
      on`. *This is what failed on 27 Sep: another copy with the box unticked
      showed the pop-ups.*
- [ ] **A killed app comes back at once.** Signed in, end
      `CallCenter.AgentApp.exe` in Task Manager (Details → End task). Start the
      app and sign in again straight away, then call the queue: it rings here
      at once, not after two minutes. The log shows, just after sign-in,
      `The PBX forgot every address it had for extension …` and then
      `Extension … registered`. *If it says "did not confirm forgetting the
      old addresses":* send me that line with the PBX's answer; the phone
      still works, the old address is just replaced rather than removed.
- [ ] **Signing out unregisters.** Sign out. The log shows
      `Extension … unregistered`, and on the web app's Users page (S-61) the
      extension shows **offline** at its next refresh. The same when closing
      the app with the X while signed in.
- [ ] **Send me one INVITE line.** After any call has rung, find the line
      `SIP IN  "INVITE" sip:…` in the log and send it. It shows which
      extension the PBX addressed the call to; guard 4 relies on it, and it
      has never been seen before.
- [ ] **Only this agent's calls (hard to cause, try once).** Sign in as agent
      A, then unplug the network (or drop the VPN), sign out (the un-REGISTER
      cannot reach the PBX), plug it back in, and sign in as agent B on the
      same laptop. Within two minutes, ring **A's** extension directly from
      another phone. Nothing rings on this laptop, and nothing appears in B's
      call log. The log has a warning `A call for extension <A> arrived here,
      signed in as <B>: refused 480`. *Before this change B's screen would
      have shown A's call.*

### Echo cancellation (A-87) — built 1 Oct night, needs the PBX and a mobile

What went wrong on 1 Oct: customers heard their own voice back, a quarter of a
second late, because the agent's microphone picked them up from the speaker.
Use a laptop with its **built-in speakers and microphone** (no headset): that
is where the echo is worst, so it shows whether the canceller works.

- [ ] **It switches on.** Call the queue from a mobile and answer. The log
      shows `Echo cancellation on: PCMU 8000 Hz, microphone #…, speaker #…`
      just after `Call answered`. *If it shows `could not start` or `delivered
      nothing`:* the call used the plain microphone, as before A-87; send the
      log line, with the laptop's model.
- [ ] **The mobile hears no echo.** On the mobile, talk for 20 seconds while
      the agent stays quiet, with the laptop's speaker at its usual volume.
      You hear yourself back faintly or not at all. Send me the time of the
      call: the recording shows how much of your voice came back.
- [ ] **Both talking at once.** Talk over each other for a few seconds. Both
      voices still come through; the agent's may thin out for a moment, not
      cut out.
- [ ] **Mute and hold still work.** Mute: the mobile hears nothing, the agent
      still hears the mobile. Unmute: the agent is heard again at once. Hold
      and resume the same way.
- [ ] **The switch.** In `appsettings.json` beside the program set
      `"Audio": { "EchoCancellation": false }`, restart the app and call
      again: no `Echo cancellation on` line, and the echo is back. Set it to
      `true` again.

### The call banner (A-19) — built 4 Oct, needs the PBX

A strip across the top of the main window while there is a call. It brings the
pop-up back when it has gone behind something or been closed.

- [ ] **Ringing.** Call the agent: the banner says *Incoming call*, the number
      and, for a saved customer, the name, with **Answer** and **Reject**.
      Answer from the banner: the call connects as from the pop-up.
- [ ] **Connected.** The banner shows the timer and **Mute**, **Hold**,
      **Hang up**. Mute and Hold from the banner: the banner and the pop-up
      both say *Muted* / *On hold*, and the buttons say Unmute / Resume.
- [ ] **Getting the pop-up back.** Click the main window so the pop-up goes
      behind it, then double-click the banner (not a button): the pop-up comes
      to the front with the cursor in it. Close the pop-up with its X, then
      press **Open**: it comes back, form and all.
- [ ] **A double-click on Mute** mutes and unmutes, and does not open the
      pop-up.
- [ ] **Hang up from a website or another program.** With the form not saved,
      click into Chrome or Notepad and type; hang up from the customer's side.
      The pop-up comes back **on top**, where it was, with what was typed in
      the form still there; the typing in the other program carries on
      without going into the pop-up. The banner says *Call ended. The form is
      waiting for you.*
- [ ] **The banner goes** once the form is saved or skipped, and does not come
      back between calls.
- [ ] **Outbound** (A-20): the banner says *Calling* with **Cancel**; an
      unanswered call leaves the note waiting (A-41), and the banner with it.
- [ ] **A customer on hold behind an internal call** (A-24): the banner shows
      *On hold:* and their number.
- [ ] **Both languages, at 1366 × 768 and 125 %**: the banner's text and
      buttons fit on one line; in Arabic the buttons are on the left.
      Screenshots ringing, connected and with the form waiting.

### The Websites page (S-68) — built 4 Oct, no phone needed

The supervisor's list of the tabs inside the Agent App: Websites, in the web
app's menu after Settings.

- [ ] **The POS is already there**, with "Opens the caller's cart". Its address
      is a guess (`https://smashed-ps.com/app`): open Edit and put the POS's
      real start page.
- [ ] **Add the three sites.** For the one with four accounts, four websites
      with the same address, each its own username and password, named for
      example "الطلبات ١" / "Orders 1". Tick "This site alerts with a sound".
- [ ] **The password is never shown again.** After Save the row says
      "Password stored"; Edit shows an empty password box. Save without typing
      in it, and the app still logs in (it kept the password).
- [ ] **Refusals say why:** an address without `https://`; a cart address on a
      second tab; a shared login with no username.
- [ ] **Order and hiding:** move a tab with the arrows, hide one; at the next
      sign-in the Agent App follows.
- [ ] **Remove asks twice.**
- [ ] **Both languages**, with screenshots of the list and of the form.

### Websites inside the Agent App (A-88) — built 4 Oct, needs the PBX for the call half

Sign out and in once the Websites page is filled, so the app fetches the tabs.

- [ ] **The section is there**, second in the rail, with a button per tab.
      Each opens its site.
- [ ] **The four accounts at once.** Quarters, the four tabs of the same site:
      each is signed in as its own account, by itself, with nobody typing.
      *If one stays on its login page:* note which site; its login boxes need
      the selectors under Advanced on the Websites page.
- [ ] **A wrong password stops.** Change one account's password on the
      Websites page to a wrong one, sign out and in: the tab tries once, then
      says the site refused the login. It does not try again until Reload.
- [ ] **The POS remembers each agent.** Sign in to the POS tab, let Edge save
      the password. Sign out, sign in as a different agent: their POS tab is
      not signed in. Back as the first agent: still signed in.
- [ ] **Layouts:** One, Side by side, Quarters; a tab button fills the place
      last clicked; choosing a tab already shown elsewhere swaps the two;
      zoom and mute per place. Sign out and in: the same layout and zooms.
- [ ] **Groups:** Groups → add a group with the four accounts → its button
      shows them in quarters; a group of two shows side by side. A fifth tick
      is refused. A call that rings while the Groups window is open can still
      be answered.
- [ ] **Sounds while hidden.** Leave the Websites section for the call log for
      ten minutes; an order on a site that dings is heard at once, not a minute
      later. The speaker mark is on its tab button.
- [ ] **Silent during a call.** With a site playing a sound, take a call: it
      stops from the first ring and comes back after hang-up. The customer
      hears nothing of it, and the recording has none of it.
- [ ] **The cart in the POS tab.** Answer a call from a mobile: the POS tab
      loads `…/cart/05…` and is on screen in the Websites section; the pop-up
      stays where the agent is. No browser window opens.
- [ ] **Shortcuts while typing in a site.** Click into the POS's search box
      during a call and press Ctrl+Shift+M: the call mutes, and nothing is
      typed into the POS. Ctrl+Shift+E hangs up.
- [ ] **A receipt or a link that opens a new window** opens in a small window
      of the app, signed in.
- [ ] **Memory.** Task Manager with all tabs loaded: note the app's and the
      `msedgewebview2.exe` processes' total, on a 1366 × 768 laptop.
- [ ] **Both languages**, with screenshots of the section in each layout.

### The fonts inside the app (N-10) — built 4 Oct, no phone needed for most

What went wrong on 3 and 4 Oct: on two laptops the app closed by itself just as
a call ended. Windows listed a font whose file was gone, and the app could not
draw its text. Since 4 Oct the app brings its own fonts, Cairo and Cascadia
Mono. Most laptops had no Cairo before, so the app was really showing Segoe
UI: **the letters change shape, and Cairo's lines are taller.**

- [ ] **The font is Cairo.** Sign-in screen: the letters are rounder and
      narrower than before, like the web app's. *If they look the same as
      before:* the laptop has the old version; check the version on the Users
      page.
- [ ] **Nothing is cut off, in both languages.** Cairo's lines are about 40 %
      taller than Segoe UI's. Go through the rail, Dial, Call log, Contacts,
      Menu, Delivery, Applications, App logs and the pop-up on a call: no word
      cut at the top or bottom, no button whose text does not fit, no list
      row overlapping the next. **Screenshot each, in Arabic and in English.**
- [ ] **At 1366 × 768 and 125 %** (the agents' laptops), as in the 27 Sep
      section above: the whole window and the pop-up still fit on the screen.
- [ ] **Fixed-width text stays fixed-width.** What was in the fixed-width
      font before (a number field in the classification form, the numbers in
      the call log and App logs) still is: every digit the same width, so
      the columns line up.
- [ ] **On a laptop that crashed** (lelian, or the supervisor's laptop), a
      full shift: no `Unhandled exception on the UI thread` with
      `FileNotFoundException` in App logs.

---

## Round 3a — the recording endpoints, from Swagger (A-33, S-04, S-43)

The agent's player is in the call log now (see "Hearing a recording in the call
log" in Round 3), but the supervisor's belongs to the call search (S-02), which
is not built, and the refusals are easier to see here. So they are checked from `http://localhost:5000/swagger`, using the **Authorize**
button with the `accessToken` from `POST /api/auth/login`. You need one call that
has a recording — make a test call from the Agent App, or take a call id from the
`recordings` table.

- [ ] **A supervisor plays a recording.** `GET /api/recordings/{communicationId}`
      signed in as `supervisor` answers 200 with `audio/wav`. Save the response
      and open it in any player: it should be a normal conversation, customer on
      the left and agent on the right.
      *If it is silence:* the audio was captured wrong, not served wrong — see
      the 24 September recording entries, where exactly that happened.
      *If it is 404 `recording_expired`:* the row is there and the file is not.
      Retention may have taken it, or the `Recordings:Path` folder is not the one
      it was written to.
- [ ] **The agent plays their own and nobody else's.** Signed in as `dia20`, the
      same request for one of that agent's calls answers 200; for a call
      belonging to another agent it answers **403 `not_your_call`** (A-52). That
      refusal is the one worth checking by hand, because getting it wrong leaks
      one agent's calls to another.
- [ ] **Download is the supervisor's.** `GET /api/recordings/{id}/download` as
      `supervisor` offers a `.wav` named after the call; as an agent it is 403.
- [ ] **Storage usage.** `GET /api/recordings/storage` as `supervisor` reports
      the retention days, how many recordings are kept, their size, and what the
      folder holds on disk. **Write the number down** — it is the first real
      measurement of whether 90 days is affordable, and the estimate it is being
      checked against is about 50 GB for four agents.
      *If `diskReadable` is false:* the recordings folder is not where the server
      is looking. Nothing has been lost; the database figures are still right.
- [ ] **Retention actually deletes.** Set the recording retention period to
      **1 day** on the settings screen, wait for the nightly pass or restart the
      server and wait five minutes, then check that a recording older than a day
      has lost its file and **kept its row** with `deleted_at` set — and that its
      call and classification are still there (N-08). **Set the period back to
      90 afterwards.**
      *If the row disappeared:* that is a bug, not a cleanup. The rule is that
      the file goes and the row stays.

---

## Round 3b — before any deployment

- [ ] **`npm run build` succeeds** in `src/CallCenter.Web`. This is not the same
      check as the tests: it was broken for half a day on 21 September while all
      41 tests passed, because the tests never package the app. A green test run
      does not mean the app can be installed.
- [ ] **`dotnet build CallCenter.sln`** succeeds with the apps closed.

---

## Round 4 — the things that only fail in production

These cannot be checked on this laptop, and each has burned a project somewhere.

- [ ] **Seed a genuinely empty database** and confirm you get 4 branches, 228
      delivery areas, 44 menu items, 39 picture files and one supervisor.
      *Done once on 21 September 2026 against a throwaway database. Worth
      repeating on the real server, because that is a different machine.*
- [ ] **Restore a backup onto a different machine and open it.** An untested
      backup is a hope, not a backup. It is an acceptance item in the contract.
- [ ] **Confirm the restored install has its menu pictures.** If the folder was
      not copied, run `seed` again — it puts back any missing picture — and
      then fix the backup script, because the folder should have been there.
- [ ] **The offline buffer.** Pull the network cable mid-call, make a call, plug
      it back in, and check the call reaches the server.
- [ ] **`reset-password`** actually lets you back in after a lockout.
- [ ] **The three CDR test calls**, and reading `Master.csv`. Needs Issabel
      access you do not currently have.

---

## What is not on this list because it is not built

Not testable yet, and listed so the gaps are not mistaken for failures:
classification (A-40, S-40), branch management (S-41; channels are built), caller identity in the pop-up (A-16, A-11), blacklist export
(S-46), click-to-call, merging contacts, and contact import
from Excel. `DECISIONS.md` holds the live list.
