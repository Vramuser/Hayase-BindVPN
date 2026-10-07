# Architecture

Hayase BindVPN has two components: a Chrome extension popup loaded by Hayase and an elevated Windows helper. The extension selects an adapter and displays status. The helper owns settings and Windows Filtering Platform (WFP) rules.

## Restriction model

The executable is converted to its application identity with `FwpmGetAppIdFromFileName0`. The production sublayer contains an application block and a higher-priority localhost exception at each of these layers:

- ALE_AUTH_CONNECT_V4 and ALE_AUTH_CONNECT_V6.
- ALE_AUTH_RECV_ACCEPT_V4 and ALE_AUTH_RECV_ACCEPT_V6.

Those eight filters persist. Separate dynamic-session exceptions permit the selected adapter only when its LUID and exact assigned source address match. Outbound exceptions also match the next-hop interface; inbound exceptions match the arrival interface. Unknown or unmatched traffic reaches the block.

Dynamic exceptions disappear when the helper's session ends. The persistent blocks remain. No adapter configuration, DNS setting, routing table, or Windows Firewall default is changed. The helper does not automatically switch adapters.

Rule changes use transactions. Status checks that persisted rules match the exact executable and that owned interface exceptions exist. Missing paths clear exceptions and cannot enable binding. Startup reconciles the saved switch state and retries temporary restore errors.

## Local API

The helper listens on `127.0.0.1:49736`. Pairing, status, bind, unbind, and test operations are under `/v1/`. Requests require a random per-session pairing code in an Authorization header and a valid extension identity in `X-Hayase-BindVPN-Client`.

Normal website origins are rejected. Privileged extension requests may omit Origin or serialize it as `null`; those still require the code and identity. A provided extension origin must match that identity. First valid pairing pins the client for that session. Secrets never appear in URLs or logs.

## Settings and lifecycle

`%ProgramData%\Hayase-BindVPN\settings.json` stores the path, adapter GUID, and switch state. Its directory is limited to administrators and SYSTEM. The code exists in memory and changes at restart.

A named mutex prevents duplicate rule owners. A second launch signals a named event; the first instance opens its window. Closing hides it in the tray. Exiting ends the dynamic allow session.

## Protection test

The helper embeds the probe at build time. It extracts that resource into a new administrator-only temporary directory. A random test sublayer applies only to the probe, independently of production rules.

The test checks TCP/UDP delivery on the selected adapter, then removes the probe's exceptions and verifies blocked fallback. IPv6 internet checks require a usable IPv6 address. The controller checks the real application's rules before and after testing. Changes during the test invalidate its result.

The test does not disconnect the VPN or toggle production binding. Binding mutations are refused while it runs. Temporary filters and probe files are cleaned up afterward.

## Recovery

Recovery deletes only the eight production keys derived from this project's fixed sublayer identity, then resets the saved switch when readable. It does not enumerate or delete other providers' filters. The original configuration file is unnecessary for locating owned filters.

## Upstream references

- [Hayase plugins](https://wiki.hayase.watch/extensions/plugins).
- [Hayase implementation](https://github.com/hayase-app/electron/blob/main/src/main/plugins.ts).
- [Windows ALE layers](https://learn.microsoft.com/en-us/windows/win32/fwp/ale-layers).
- [WFP arbitration](https://learn.microsoft.com/en-us/windows/win32/fwp/filter-arbitration).
- [ALE reauthorization](https://learn.microsoft.com/en-us/windows/win32/fwp/ale-re-authorization).
