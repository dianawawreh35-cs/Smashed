# Blind transfer to another extension (A-15)

First read `docs/prompts/00-house-rules.md` and follow it.

## Read this first: this one touches the riskiest file in the project

A-15 is a **Should**, not a Must. Everything else outstanding in the Agent App
is a Must. **Do not start this until I have said the Musts are done**, and check
with me before you begin even then.

It also lands in `src/CallCenter.AgentApp/Services/Calls/CallService.cs`, which
is the file the whole project treats with care. **Write a plan and show it to me
before changing it.** That rule is not decoration: the SIP layer has cost three
separate bugs and a full day, and two more were found in it this week.

## The job

An agent takes a call meant for someone else — another agent, a branch, the
manager — and passes it on. Today their only options are to hang up on the
customer or to keep talking.

"Blind" means the agent transfers and lets go, without speaking to the person
they are transferring to. The customer's call leaves this extension entirely.

Read the A-15 row in the SRS, and these `docs/DECISIONS.md` entries:

- "Mute (A-12): done in the microphone, not in SIP" (21 Sep).
- "Hold (A-12): the first re-INVITE" (22 Sep) — the closest thing to this, and
  the template for how a SIP change is made here.
- The class comment at the top of `CallService.cs`, in full.

## What the library gives you

`SIPUserAgent.BlindTransfer(SIPURI, TimeSpan, CancellationToken, ...)` exists,
and `OnTransferNotify` reports what happened. A blind transfer is a SIP REFER:
this extension asks the PBX to send the call elsewhere and then steps out. The
PBX reports progress back, and **the transfer can fail after the agent has let
go** — the destination may be busy, or not exist.

That failure case is the whole design problem, and it is why this is worth a
plan rather than an afternoon.

## Questions to answer in that plan

1. **What happens to the call when the transfer fails?** Does the agent get it
   back, or has the customer gone? Find out what Issabel actually does rather
   than assuming, and say which you observed.
2. **What is the call logged as?** `CommunicationStatuses` has no `Transferred`.
   Adding one is a migration and a widened check constraint — the same shape as
   `NoAnswer` on 22 Sep, which was the right call and is written up. Decide, and
   make the case either way. Do not quietly log it as `Answered`.
3. **Does the classification form still apply?** The agent spoke to the customer
   before transferring, so there is something to classify — but the call
   continues elsewhere and may be classified again by whoever receives it.
4. **Where does the agent choose a destination?** A list of extensions the
   server knows about is better than a free-text box, but the server has no
   endpoint listing extensions today. Check before promising one.

## What to build, once the plan is agreed

A Transfer button on the connected pop-up, beside Mute and Hold. The same
shape as those: a command on `CallViewModel`, a method on `CallService`, the
state on `CallState`, labels in both languages.

**Hold the same rules Mute and Hold hold.** A failure is logged and leaves the
call exactly as it was. Nothing in the transfer path may end a call the agent
did not choose to end.

## What to leave alone

- **Attended transfer.** The SRS asks for blind only. `SIPUserAgent` offers
  attended transfer as well, and it is a different, larger feature. Do not drift
  into it.
- **Mute, hold and dialling.** They work and have been tested on real calls.

## When it is done

Everything in the house rules, and:

- **A real test call, and then another for each failure case** you identified:
  transferring to a busy extension, and to one that does not exist. I will make
  them; tell me exactly what to try and what each result means.
- Do not call this done on a green build. This is the part of the system where
  that has been worth least.
