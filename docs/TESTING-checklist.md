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

- [ ] Double-click a contact row — its details open.
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

---

## Round 2 — the Agent App, without a phone

Sign in as `dia20`. None of this needs a call.

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
- [ ] **Call log tab (A-14).** It lists past calls.
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

---

## Round 3 — the telephone

This needs the PBX at 192.168.0.27 and extension 2001. It is the part that
cannot be faked, and the part most likely to find something.

- [ ] **Call extension 2001 from another phone.** The pop-up appears, with the
      caller's number.
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

### Outbound dialling (A-20, A-21) — never run

- [ ] **Dial your own mobile from the Dial screen.** The pop-up appears saying
      "Calling", with the number and a Cancel. Your phone rings. Answer it and
      the pop-up says "Connected", the timer starts, and the classification form
      opens exactly as it does on an incoming call (A-21).
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
classification (A-40, S-40), branch management (S-41), opening a call from the
log (A-51), caller identity in the pop-up (A-16, A-11), blacklist export
(S-46), click-to-call, merging contacts, and contact import
from Excel. `DECISIONS.md` holds the live list.
