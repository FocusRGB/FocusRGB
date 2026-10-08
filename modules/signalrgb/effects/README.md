# effects/

Active custom SignalRGB effects only. The install script copies every `.html` and matching
`.png` in this folder into SignalRGB's user Effects folder.

This folder is currently empty because no custom effect is part of the live setup.

- Starter: `../templates/signalrgb-effect.html`
- Canvas API example: `../examples/signalrgb-canvas-event-demo.html`
- Blocked Pro experiment: `../archive/screen-ambience-deskoff-pro-required.html`

Copy a file here only when it should be installed, then run
`..\tools\install-signalrgb-content.ps1 -Effects`. Restart SignalRGB when adding a new file.
