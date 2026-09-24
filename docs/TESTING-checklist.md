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
- [x] **Open a call that was put on hold** *Signed off by Dia 24 September.* (put a test call on hold for ten
      seconds first). Under the bar there is an **amber mark** where the hold
      was, "On hold: 0:12–0:22" under the player, and **"On hold"** beside the
      time while playback is inside it. The same as the Agent App's player.
- [ ] **Download** saves a `.wav` that plays in Windows' own player.
- [ ] **A call whose recording has expired** says so, rather than "no
      recording".
- [ ] **Everything above in Arabic.** The seek bar fills from the right, and
      the hold marks must sit under the same stretch of it. *If the marks are
      mirrored against the bar:* tell me. That is the one thing RTL can get wrong
      here.

---

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
- [ ] **Signed out by the server (N-05).** With `dia20` signed in here, reset
      `dia20`'s password in the web app's Users screen. Then do something in the
      Agent App that asks the server, such as a contacts search. It must go back
      to the sign-in screen with "You have been signed out…", in the app's
      language, and the phone status must no longer show registered.
      *If it stays on the main screen with failed lookups:* the Agent App is an
      old build. Check the `bin` date. Sign in with the new password afterwards.

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
- [ ] **An unknown number says "New customer — not on file"**, with a small
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
- [ ] **Play it.** You hear the call in the headset: customer on the left, you on
      the right. The button says **Pause** and the time counts up.
      *If it is static or a harsh buzz:* the file was not read as mu-law. Tell
      me, with the log line containing "not a mu-law WAV".
      *If it says no speaker or headset was found:* Windows has no default output
      device. Check the sound settings, not the app.
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
classification (A-40, S-40), branch management (S-41), caller identity in the pop-up (A-16, A-11), blacklist export
(S-46), click-to-call, merging contacts, and contact import
from Excel. `DECISIONS.md` holds the live list.
