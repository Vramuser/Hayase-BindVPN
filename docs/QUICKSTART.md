# Hayase BindVPN 1.0.0

Choose the network adapter Hayase may use. Supports VPNs that provide a Windows system adapter, including Mullvad, WireGuard, OpenVPN, and Proton VPN.

Project and updates: https://github.com/Vramuser/Hayase-BindVPN

## Install

1. Extract the Windows package into a folder you intend to keep.
2. Open **Start-Helper.cmd** or **Hayase-BindVPN-Helper.exe** and accept administrator access.
3. Check the detected **Hayase.exe**. Use **Browse** if necessary.
4. In Hayase's **Settings → Plugins**, import **Hayase-BindVPN-plugin.zip**. The full Windows ZIP is also importable.
5. Open the popup. Copy the pairing code from the helper, paste it, and choose **Connect helper**.
6. Connect your VPN, choose its **Network interface**, and turn **Bind Hayase** on.

Keep the helper running. Closing its window leaves it in the tray; one click reopens it. Start it again after a Windows restart. A new helper session requires its new pairing code.

## Test

With binding on and the selected interface connected, choose **Test protection**. It checks installed rules, selected-interface TCP/UDP connectivity, and blocked fallback through a separate probe. It does not disconnect the VPN or turn off your binding.

Review every result. Skipped IPv6 checks and incomplete connectivity tests do not verify that part of the connection. The test contacts Cloudflare IP addresses and sends an example.com DNS query.

## Update

Exit the old helper from the tray, start the new helper, remove/re-import the plugin, and pair with the new code. Your saved application and adapter selection remain. Update both components together.

## Remove or recover

Turn binding off, or choose **Remove binding** in the helper. If it cannot start, exit any running instance and open **Recover.cmd**.

An enabled binding stays blocked if the helper stops. Removing the plugin alone does not remove blocking rules. Recovery removes only this project's eight persistent rules; it does not reset Windows Firewall or change adapter settings.

## Scope

Windows 10/11 x64 and administrator access are required. Traffic attributed to the selected Hayase.exe is restricted, including its torrent worker. Localhost remains available. Other applications and adapter settings are unchanged.

The VPN must route Hayase's traffic. Browser-only VPN extensions are unsupported. External players and separate system services, such as Windows DNS, are outside the restriction. LAN casting through another adapter is blocked.

## Support

Issues: https://github.com/Vramuser/Hayase-BindVPN/issues

Include the helper/plugin version, Windows version, adapter type, exact error text, and reproduction steps. Do not share pairing codes or VPN credentials.

MIT license. Copyright (c) 2026 Vramuser. See LICENSE.
