# Changelog

## 1.0.0 — 2026-10-07

First public release of Hayase BindVPN for Windows x64.

- Choose the network adapter Hayase may use, including adapters from Mullvad, WireGuard, OpenVPN, Proton VPN, and other system VPNs.
- Enable or disable binding with a switch in the Hayase plugin.
- Block fallback traffic when the selected interface is unavailable or the helper exits.
- Restore saved bindings, finish interrupted unbind operations, and retry temporary startup errors.
- Disable binding and testing with a warning when Hayase.exe cannot be found.
- Verify installed rules, selected-interface connectivity, and blocked fallback with **Test protection**.
- Open the helper with a tray click or a second launch.
- Authenticate localhost requests with a pairing code and plugin identity.
- Include a recovery shortcut that removes only this project's persistent rules.
- Provide MIT-licensed source, checksummed release packages, and GitHub build/draft-release workflows.

External IPv6 traffic is not verified on hosts without an IPv6 internet route. The runtime test reports skipped or incomplete checks explicitly.
