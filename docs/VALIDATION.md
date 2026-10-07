# Validation record

Development checks on October 7, 2026, used a Windows x64 host with a working Mullvad IPv4 route. Version 1.0.0 changes release metadata and documentation while retaining the tested restriction behavior.

| Check | Scope |
| --- | --- |
| Client checks | Authentication headers, endpoint, stale-app guard, unavailable helper, status, and pinned adapters. |
| Controller checks | Saved bindings, healthy rules, retries, interrupted unbind, application detection, missing paths, and recovery. |
| Real local HTTP | Origin handling, code/identity validation, pairing ownership, preflight, and authenticated test/recovery. A simulated controller prevents real changes. |
| Extension popup | Actual runtime identity in an isolated profile; pairing, switch, adapter selection, results, missing adapters/applications, and recovery. Helper responses are simulated. |
| WFP integration | Real IPv4 TCP/UDP delivery, blocked fallback, wrong adapters, stale addresses, session loss, localhost, recovery, and unaffected access from another executable. |
| Runtime protection | Installed-rule inspection and real selected-interface/fallback checks using a separate probe. |

Tests use independent fixtures and/or rules scoped to a probe. Temporary integration rules were removed afterward.

## Limits

- IPv6 rules and localhost passed. The host had no external IPv6 route, so external IPv6 delivery/blocking was not established.
- A complete Windows reboot with a saved binding was not automated.
- A live Hayase torrent session across VPN disconnect/reconnect needs a setup-specific check. The user reported the application working; that does not replace packet-level verification.
- Tray opening/window activation are implemented but not covered by automated desktop interaction.
- GitHub workflows must run after repository upload; local checks do not establish a remote run.

## Manual check

1. Enable the intended VPN adapter and run **Test protection**. Review failed/skipped checks.
2. Transfer a small torrent you are authorized to download. Disconnect the VPN and confirm downloading and seeding stop.
3. Reconnect and confirm transfer resumes on the selected adapter.
4. Exit the helper and confirm external Hayase traffic stays blocked. Reopen it and confirm restoration.
5. Restart Windows and confirm an enabled binding stays blocked until the helper starts.
6. Turn binding off or recover, and confirm ordinary connectivity returns.
