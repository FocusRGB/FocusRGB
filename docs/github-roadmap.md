# GitHub roadmap

Created 2026-10-15. The slice documents remain the source of truth for scope, dependencies,
and acceptance criteria. GitHub issues track execution and milestones group coherent releases.

## Maintenance rules

- Keep one issue per implementation slice. Create child issues only when the slice document is
  explicitly split into lettered children.
- Give slice issues the `slice` and `enhancement` labels, assign only the repository owner, and
  link dependency issues in the body.
- Close an issue with the `completed` reason only after the roadmap marks its slice complete.
  Reopen it if accepted work is later found incomplete.
- Update this table, the slice document, and the GitHub issue together when scope, dependency,
  status, or milestone changes.
- Close a milestone only when its slice issues are complete and its integration pull requests
  have landed. Do not invent due dates.

## Milestones

| Milestone | Slices | State |
|---|---:|---|
| [v0.1.0-alpha.1 — Application foundation](https://github.com/MAJ224/FocusRGB/milestone/2) | 00–09 | Closed |
| [v0.2.0-alpha.1 — Plugin runtime and coordination](https://github.com/MAJ224/FocusRGB/milestone/1) | 10–11 | Closed |
| [v0.3.0-alpha.1 — External applications](https://github.com/MAJ224/FocusRGB/milestone/3) | 12–16 | Open |
| [v0.4.0-alpha.1 — Games and external control](https://github.com/MAJ224/FocusRGB/milestone/4) | 17–20 | Open |
| [v0.5.0-alpha.1 — Native RGB system](https://github.com/MAJ224/FocusRGB/milestone/5) | 21–29 | Open |

## Slice issues

| Slice | Issue | Milestone | State |
|---:|---|---|---|
| 00 | [#30 — Freeze the minimal data and command contract](https://github.com/MAJ224/FocusRGB/issues/30) | v0.1.0-alpha.1 | Completed |
| 01 | [#26 — Solution scaffold and repeatable checks](https://github.com/MAJ224/FocusRGB/issues/26) | v0.1.0-alpha.1 | Completed |
| 02 | [#27 — Profiles and configuration validation](https://github.com/MAJ224/FocusRGB/issues/27) | v0.1.0-alpha.1 | Completed |
| 03 | [#28 — UI shell with preview state](https://github.com/MAJ224/FocusRGB/issues/28) | v0.1.0-alpha.1 | Completed |
| 04 | [#29 — Profile mapping editor with preview state](https://github.com/MAJ224/FocusRGB/issues/29) | v0.1.0-alpha.1 | Completed |
| 05 | [#34 — Pure selection engine](https://github.com/MAJ224/FocusRGB/issues/34) | v0.1.0-alpha.1 | Completed |
| 06 | [#32 — Saved state and startup restoration](https://github.com/MAJ224/FocusRGB/issues/32) | v0.1.0-alpha.1 | Completed |
| 07 | [#31 — Profile editor and persistence](https://github.com/MAJ224/FocusRGB/issues/31) | v0.1.0-alpha.1 | Completed |
| 08 | [#33 — Desktop runtime and tray](https://github.com/MAJ224/FocusRGB/issues/33) | v0.1.0-alpha.1 | Completed |
| 09 | [#35 — Application settings and lifecycle preferences](https://github.com/MAJ224/FocusRGB/issues/35) | v0.1.0-alpha.1 | Completed |
| 10 | [#36 — Minimal plugin runtime, SDK, and fake output](https://github.com/MAJ224/FocusRGB/issues/36) | v0.2.0-alpha.1 | Completed |
| 11 | [#37 — Command coordination and fallback state](https://github.com/MAJ224/FocusRGB/issues/37) | v0.2.0-alpha.1 | Completed |
| 12 | [#40 — Windows application observations](https://github.com/MAJ224/FocusRGB/issues/40) | v0.3.0-alpha.1 | Completed |
| 13 | [#39 — Application mappings and persistence](https://github.com/MAJ224/FocusRGB/issues/39) | v0.3.0-alpha.1 | Completed |
| 14 | [#38 — SignalRGB capability investigation](https://github.com/MAJ224/FocusRGB/issues/38) | v0.3.0-alpha.1 | In review ([#71](https://github.com/MAJ224/FocusRGB/pull/71)) |
| 15 | [#43 — External SignalRGB plugin repository](https://github.com/MAJ224/FocusRGB/issues/43) | v0.3.0-alpha.1 | Planned |
| 16 | [#41 — SignalRGB acceptance gate](https://github.com/MAJ224/FocusRGB/issues/41) | v0.3.0-alpha.1 | Planned |
| 17 | [#42 — Stabilize the public SDK and contributor example](https://github.com/MAJ224/FocusRGB/issues/42) | v0.4.0-alpha.1 | Planned |
| 18 | [#45 — Local MCP commands](https://github.com/MAJ224/FocusRGB/issues/45) | v0.4.0-alpha.1 | Planned |
| 19 | [#44 — Installed game catalog and assignments](https://github.com/MAJ224/FocusRGB/issues/44) | v0.4.0-alpha.1 | Planned |
| 20 | [#48 — One game API adapter](https://github.com/MAJ224/FocusRGB/issues/48) | v0.4.0-alpha.1 | Planned |
| 21 | [#46 — Placement model and offline renderer](https://github.com/MAJ224/FocusRGB/issues/46) | v0.5.0-alpha.1 | Planned |
| 22 | [#49 — WLED plugin and native output gate](https://github.com/MAJ224/FocusRGB/issues/49) | v0.5.0-alpha.1 | Planned |
| 23 | [#47 — Layout canvas editor](https://github.com/MAJ224/FocusRGB/issues/47) | v0.5.0-alpha.1 | Planned |
| 24 | [#50 — Hybrid game layers](https://github.com/MAJ224/FocusRGB/issues/50) | v0.5.0-alpha.1 | Planned |
| 25 | [#52 — Stacked groups](https://github.com/MAJ224/FocusRGB/issues/52) | v0.5.0-alpha.1 | Planned |
| 26 | [#54 — Native screen ambience and calibration](https://github.com/MAJ224/FocusRGB/issues/54) | v0.5.0-alpha.1 | Planned |
| 27 | [#53 — One native peripheral backend](https://github.com/MAJ224/FocusRGB/issues/53) | v0.5.0-alpha.1 | Planned |
| 28 | [#51 — Startup, packaging, and recovery gate](https://github.com/MAJ224/FocusRGB/issues/51) | v0.5.0-alpha.1 | Planned |
| 29 | [#55 — SignalRGB independence gate](https://github.com/MAJ224/FocusRGB/issues/55) | v0.5.0-alpha.1 | Planned |

Assisted-by: OpenAI Codex
