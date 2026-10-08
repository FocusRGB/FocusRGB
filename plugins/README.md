# plugins/

These are **SignalRGB USB device plugins**, not the planned controller's plugin system.
For app-detection and external-app/RGB adapters, see [controller architecture](../docs/plugin-architecture.md).

One `.js` per USB device. SignalRGB matches the file to hardware by `VendorId()` + `ProductId()`.
A user plugin here overrides the built-in one with the same IDs, so you can also patch
SignalRGB's own Razer plugin by copying it from
`%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\Plugins\` and editing.

This folder is currently empty because the keyboard and mouse use SignalRGB's built-in
Razer plugin and WLED needs no user plugin.

Start new device work from `../templates/signalrgb-device-plugin.js`, then copy the completed
plugin here and run `..\tools\install-signalrgb-content.ps1 -Plugins`.

Workflow for a new device: capture USB traffic from the vendor app with Wireshark + USBPcap,
find the color packet format, then fill in `Validate`, `Initialize`, and `sendColors`.
See https://docs.signalrgb.com/developer/plugins/tutorial/
