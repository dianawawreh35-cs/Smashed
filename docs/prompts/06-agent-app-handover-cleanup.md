# Agent App handover cleanup: supervisor logins and the dead Sip settings

First read `docs/prompts/00-house-rules.md` and follow it.

Two small items from "Known gaps in what is built" in `docs/DECISIONS.md`. Do
them as two commits.

## 1. The Agent App must refuse supervisor logins

Supervisors were allowed in on purpose, to test sign-in before user management
existed. That reason is gone. The supervisor web app already refuses agents.
Find out how it does that, and make the Agent App refuse supervisors the same
way.

- **Prefer enforcement on the server** over a check in the app. A check that
  lives only in the app can be bypassed, and the web app's approach may or may
  not be server-side, so look. `AuthService` already treats agents differently
  (it hands out SIP credentials). See whether the login request says which app
  is signing in, and if not, the smallest honest way for it to.
- The refusal message must be clear in both languages. Something like "This app
  is for agents; supervisors use the web app".
- **Tell me before you commit** which accounts I'll need for testing afterwards.
  I may be signing in to the Agent App as a supervisor today.

## 2. Remove the dead `Sip` settings from the Agent App

`src/CallCenter.AgentApp/appsettings.json` has `Sip.Server`, `Sip.Username` and
`Sip.Password`. Nothing reads them. They arrive in the login response (A-01).
They are misleading, because they look like where the PBX is configured.

- Confirm nothing reads them by searching the code, not just this file.
- Remove those three keys and any option classes or bindings that only exist for
  them.
- **Keep `RtpPortMin` and `RtpPortMax`**, which are real. If they now look odd
  under a section called `Sip`, leave the name alone unless renaming is
  harmless. An installed laptop's settings file would stop matching.

## When done

Remove both entries from "Known gaps in what is built". Add the refusal to
`docs/TESTING-checklist.md`. Build, run all tests, and report.
