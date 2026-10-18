# SignalRGB material moved

The maintained SignalRGB plugin, capability evidence, examples, templates, and developer tools
now live in the independent public
[`MAJ224/FocusRGB-Plugin-SignalRGB`](https://github.com/MAJ224/FocusRGB-Plugin-SignalRGB)
repository. Its Slice 15 implementation is reviewed separately from the provider-neutral
FocusRGB application.

FocusRGB consumes the published `FocusRgb.PluginSdk` package contract. Core and App do not
reference or compile the SignalRGB provider project, and removing the plugin package leaves the
application operational.

Historical local security-testing notes and machine-specific observations were not republished
in the external repository; they remain available in this repository's history when needed.

Assisted-by: OpenAI Codex
