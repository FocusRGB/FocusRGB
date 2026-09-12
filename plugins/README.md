# plugins/

One `.js` per USB device. SignalRGB matches the file to hardware by `VendorId()` + `ProductId()`.
A user plugin here overrides the built-in one with the same IDs, so you can also patch
SignalRGB's own Razer plugin by copying it from
`%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\Plugins\` and editing.

- `TemplateDevice.js`  skeleton with every export SignalRGB expects

Workflow for a new device: capture USB traffic from the vendor app with Wireshark + USBPcap,
find the color packet format, then fill in `Validate`, `Initialize`, and `sendColors`.
See https://docs.signalrgb.com/developer/plugins/tutorial/
