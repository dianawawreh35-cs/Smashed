# presence-probe

Asks the PBX to report on agents' extensions and prints what it says. It is the
test that showed, on 26 Sep 2026, that the server can tell who is online and in
a call without the Agent App (S-61, `docs/DECISIONS.md`).

It signs in as the server's own PBX extension (the one on the PBX blacklist
card in Settings), reading it and the agents' extensions from the database, and
sends a SUBSCRIBE to each extension. Every NOTIFY the PBX sends back is printed.

```powershell
cd tools\presence-probe
dotnet build -c Release
# Windows refuses to start the new .exe from OneDrive; run the DLL instead.
dotnet bin\Release\net10.0\CallCenter.PresenceProbe.dll 2001 --seconds 60
```

- No extensions named: every active agent's.
- `--presence`: ask whether a phone is connected (open / closed) instead of
  whether it is in a call (the `dialog` report, the default).
- `--trace`: print every SIP message sent and received.
- Reads the dev database unless `ConnectionStrings__Default` is set, and
  decrypts the extension's password with the dev key unless
  `Security__SipSecretKey` is set.

**Worth running on the production server before relying on S-61 there**: the
PBX's NOTIFYs have to come back in to the machine, and inside Docker that is
the part most likely to go wrong. "accepted, but NO NOTIFY arrived" in the
summary is that failure.

No answer to any SUBSCRIBE at all means the PBX is not reachable. On 26 Sep
that was the VPN being off.
