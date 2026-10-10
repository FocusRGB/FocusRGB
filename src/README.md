# FocusRGB source

Application source for FocusRGB. The projects are created by slice 01 in
`../docs/implementation-slices.md`; until then this directory holds only this file.

| Project | Purpose | May reference |
|---|---|---|
| `FocusRgb.Contracts/` | Provider-neutral plugin messages, capabilities, results, and resource IDs | Nothing project-specific |
| `FocusRgb.Core/` | Profiles, selection, persistence, commands, fallback, and ownership | Contracts |
| `FocusRgb.Platform.Windows/` | Windows focus/process detection, later screen capture | Contracts, Core |
| `FocusRgb.App/` | Avalonia tray/settings UI and plugin host wiring | Contracts, Core, Platform |

Contracts and Core target portable .NET 10 without Windows APIs. No project here references an
integration: provider plugins live under `../modules/<integration>/controller-plugin/`, and
tests live under `../tests/`. Build output goes to the ignored `../build/` directory.
