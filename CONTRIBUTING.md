# Contributing

Bug reports and focused improvements are welcome. Read the [development guide](docs/DEVELOPMENT.md) and [architecture](docs/ARCHITECTURE.md) before changing the binding or recovery behavior.

For a bug, include the helper and plugin versions, Windows version, VPN adapter type, exact error text, and reproduction steps. Exclude pairing codes, VPN credentials, account information, and personal torrent details.

Keep changes small and preserve these behaviors:

- Binding must never fall back to another adapter automatically.
- A missing application, helper failure, or rule-update failure must not be shown as verified protection.
- Recovery must affect only Hayase BindVPN's owned rules.
- A protection test must not disable the real application's binding or reconfigure its VPN.
- Local requests must require authentication before accessing settings or changing rules.

Run the relevant checks and update the documentation and changelog. Network filtering changes also need administrator integration tests on a Windows machine with a working VPN route. Include which checks passed, failed, or were unavailable in your pull request.

Contributions are provided under the project's [MIT license](LICENSE).
