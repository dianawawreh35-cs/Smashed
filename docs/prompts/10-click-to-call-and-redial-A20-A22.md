# Click-to-call, and calling back a missed call (A-20, A-22)

First read `docs/prompts/00-house-rules.md` and follow it.

## The job

Dialling works. An agent can type a number on the Dial screen and the app places
the call, logs it as outgoing and opens the classification form on answer.

What is missing is **dialling a number that is already on the screen**. A-20
says "from any phone number shown in the app (click-to-call) or from a contact",
and today an agent looking at a missed call from a customer has to read the
number off the screen and type it back in. A-22 then asks for redial and
one-click call-back from a missed call.

Read the A-20 and A-22 rows in the SRS, and these `docs/DECISIONS.md` entries:

- "Outbound dialling (A-20, A-21), part 1: placing the call" (22 Sep) — how
  dialling works and what was deliberately left out.
- "The first outbound test call: the app was right, the PBX refused" (22 Sep).

## Read this before you start: outbound calls do not currently complete

The app dials correctly. **Issabel refuses the call with 503 after about eight
seconds of ringing tone.** That is an outbound route that does not complete, it
is a PBX question, and it is not yours to fix.

The consequence for this task: **you cannot fully test what you build.** You can
see the pop-up appear, the number dialled in the log, and the call logged as
outgoing with the right status — all of which is most of the value. You cannot
see a customer answer. Say so plainly when you report, and put the parts you
could not verify on the checklist rather than claiming them.

## What to build

**Click-to-call from the call log.** A button on each row that dials that
number. The grid already has a template column with a button in it; follow that
pattern.

**Click-to-call from a contact.** Same, on the contacts list and on the contact
being viewed. A contact can have several numbers; dial the primary one, and if
there is more than one, let the agent choose rather than guessing.

**A-22, redial and call back.** Redial is the last number this agent dialled,
remembered for the session. Call-back is the same button as click-to-call
sitting on a missed-call row — if you build click-to-call properly, A-22's
call-back half is already done and you should say so rather than building it
twice.

## How it must work

**Everything goes through `DialViewModel.DialAsync`.** It already exists, it
already decides whether dialling is possible, and it is deliberately the single
way out to the phone. Do not call `CallService` from a grid.

**A dial button must be disabled, with a reason, when a call cannot be placed** —
the phone not registered, or another call in progress. `DialViewModel` already
exposes both, including the `Hint` text. A dead button with no explanation is
the thing this project keeps writing down as the worst option.

**Numbers go through `PhoneNormalizer`.** The digits are what is dialled.

## What to leave alone

- **`CallService.cs`.** Dialling already works; this task is buttons that call
  it. If you find yourself opening that file, stop and ask me.
- **`Dialing:Prefix`.** The first test call proved the PBX accepts a plain local
  number. Leave it empty.
- **Blind transfer (A-15).** Different task, and it *does* touch `CallService`.

## A trap this project has already hit twice

Both grids you are about to add buttons to have a **column-sizing and sorting
fault**. See the 22 Sep entries "the grid was sorting itself" and "the last
column arrived cut off". The call log has been fixed; **contacts has not**.

The fix belongs in the `DataGrid` style in `Theme.xaml`:
`CanUserSortColumns="False"` and `HorizontalScrollBarVisibility="Disabled"`,
with `MinWidth` on the columns that must not be squeezed. Doing that as a small,
separate first commit will save you discovering it again with a new button
half off the edge of the card.

## When it is done

Everything in the house rules, and:

- Checklist steps for each place a number can now be dialled from.
- **Ask for a screenshot**, and say clearly which parts you could not verify
  because the PBX refuses outbound calls.
