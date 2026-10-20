# FocusRGB source

Application source for FocusRGB, created by slice 01 in `../docs/implementation-slices.md`.
The solution file is `../FocusRGB.slnx`.

| Project | Purpose | May reference |
|---|---|---|
| `FocusRgb.Core/` | Internal host protocol types, profiles, selection, persistence, commands, fallback, and ownership | Nothing project-specific |
| `FocusRgb.Platform.Windows/` | Windows focus/process detection, later screen capture (`net10.0-windows`) | Core |
| `FocusRgb.App/` | Avalonia tray/settings UI and plugin host wiring; builds `FocusRGB.exe` (`net10.0-windows`) | Core, Platform |

Core targets portable .NET 10 without Windows APIs and is not packable. No project here
references an integration or SDK source. Production plugins live in independent
`FocusRGB-Plugin-<Name>` repositories and consume packages from `FocusRGB-Plugin-SDK`; the main
repository keeps only its raw fake conformance fixture under tests. Build output goes to the ignored
`../build/artifacts/` directory (set by `../Directory.Build.props`).
`../tests/FocusRgb.Core.Tests/DependencyBoundaryTests.cs` fails the build's tests if Core gains
a platform target, a package, or a forbidden project reference.
