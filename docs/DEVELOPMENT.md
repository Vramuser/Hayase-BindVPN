# Development and releases

## Build

Use Windows x64 with .NET Framework and Node.js (24 recommended). No .NET SDK or production npm dependencies are needed.

```powershell
npm test
npm run build
```

The build checks version consistency, compiles the helper and embedded probe, verifies root manifests in both installable ZIPs, and creates a clean source ZIP and SHA-256 checksums. Output is `dist/v<version>/`.

The Windows package contains the helper, plugin, recovery and launch shortcuts, quick-start README, license, and changelog. The plugin ZIP is directly importable in Hayase but requires the matching helper. The source ZIP includes `.github` and excludes research clones, generated tests, browser profiles, binaries, and local state.

## Checks

```powershell
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Controller.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Native.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Api.ps1
```

The native script without `-Integration` checks ABI, identity handling, and adapter enumeration. Controller fixtures do not access real settings or filters. HTTP tests use a separate local port and a simulated controller. A sandbox that blocks HTTP.sys may require an unsandboxed run.

For live restriction checks, open a Windows terminal as administrator:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Native.ps1 -Integration
```

Integration uses temporary rules scoped to a test executable. A working external TCP route and reachable DNS resolver are required. It does not change Hayase's real rules, VPN configuration, or adapters. Read [the validation record](VALIDATION.md) for coverage and limits.

Optional popup checks need Playwright and a Chromium browser. A temporary installation can be used without changing the project's dependencies:

```powershell
npm install --no-save --package-lock=false playwright
npx playwright install chromium
node scripts/Verify-UI.mjs playwright --extension
```

The script uses an isolated browser profile under `test-output/`, real extension identity, and simulated helper responses. `BINDVPN_BROWSER` can specify an existing Chromium executable. It does not use your personal browser profile.

## Prepare a release

1. Update `package.json`, `plugin/manifest.json`, and both constants in `helper/ReleaseInfo.cs`. The assembly version appends `.0` to the release version.
2. Update the README version badge/output path, quick-start title, changelog, and `docs/releases/v<version>.md`.
3. Run checks and build. Review the ZIP contents, checksums, and known limitations.
4. Commit the source to [Vramuser/Hayase-BindVPN](https://github.com/Vramuser/Hayase-BindVPN).
5. Push a matching tag, such as `v1.0.0`, or run **Draft release** with that existing tag.
6. Review the generated draft on GitHub and publish it when ready.

The **Windows checks** workflow runs offline/local checks and builds artifacts on branch pushes and pull requests. It does not run tests that depend on a public network route. **Draft release** verifies the tag matches the source version, runs checks, builds all assets, and creates a draft. It can refresh an existing draft but refuses to replace a published release.

For manual upload, create a release with tag `v1.0.0`, title **Hayase BindVPN 1.0.0**, and the body in [the release notes](releases/v1.0.0.md). Attach:

- `Hayase-BindVPN-Windows.zip`.
- `Hayase-BindVPN-plugin.zip`.
- `Hayase-BindVPN-source.zip`.
- `SHA256SUMS.txt`.

The README's latest-download link uses the Windows ZIP's stable filename. Keep that filename for future releases. Enable private vulnerability reporting in GitHub's security settings before public release.

## GitHub repository details

Suggested description: **Bind Hayase to a VPN network adapter on Windows, with fallback blocking, a protection test, and targeted recovery.**

Suggested topics: `hayase`, `vpn`, `windows`, `network-interface`, `wireguard`, `mullvad`, `bittorrent`.

The supplied source bundle can be extracted and uploaded to an empty repository. Preserve its `.github` folder and dotfiles. There is no GitHub Pages website to deploy; the project landing page is the repository README.
