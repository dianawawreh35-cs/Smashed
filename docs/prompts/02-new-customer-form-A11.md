# The "new customer" form on the call pop-up (A-11)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

When a call comes from a number that isn't on file, the pop-up already says
"New customer" (see the 22 Sep entry "The pop-up finally says who is calling").
It can't do anything about it yet. A-11 asks for an **inline form on the pop-up
to save name, address and notes**, and says that **saving links the current call
to the new contact**.

Read the A-11, A-63 and A-10 rows in the SRS, and these `docs/DECISIONS.md`
entries: "Arabic name matching, and the duplicate-name warning" (17 Sep),
"Contacts: matching" (17 Sep), and "The pop-up drops the list of last calls"
(22 Sep), which is about space on the pop-up.

Code to start from: `CallPopupWindow.xaml`, `CallerViewModel`,
`ContactsViewModel` (which already creates contacts), and `ApiClient`.

## Rules the form must keep

- **The phone number is already known.** Prefill it from the call and don't
  make the agent type it.
- **A matching name is a warning, not a refusal** (A-63). Show the existing
  contacts with that name, and let the agent choose: add this number to that
  person, or save a separate contact. Reuse what the contacts screen already
  does rather than writing a second version.
- **It must not push the classification form off the screen.** On 22 Sep the
  client removed a panel for exactly that. Keep the form collapsed until the
  agent asks for it, or make it take very little height. Show me the layout
  before you polish it.
- **It never holds up the call.** Answering, hanging up and classifying all
  work whether or not the form was touched.

## The design question to settle first

**How does "saving links the current call to the new contact" work?** The call
may not have a server id yet. Calls are reported through the offline queue,
keyed on SIP Call-ID and extension. It may even be reported *before* the
contact exists, and then matched to nobody. Look at how the server matches a
call to a contact when the call is reported, and how the classification and
the note use `by-call` routes. Then propose the smallest way to make the link
reliable, including:

- the call reported before the contact is saved;
- the contact saved while the server is unreachable. Does it wait in the queue,
  or is the form simply unavailable offline? Tell me which you recommend and
  why.

Write the answer into a `docs/DECISIONS.md` entry.

## Once saved

The caller card on the pop-up should show the new contact's name straight away,
as if the number had been known.

## Tests

- Server tests for the link, including the call reported first. These are
  database-backed tests.
- Label test coverage for the new strings in both language files.
- `docs/TESTING-checklist.md` steps. This needs a real incoming call from an
  unknown number, so say so.
