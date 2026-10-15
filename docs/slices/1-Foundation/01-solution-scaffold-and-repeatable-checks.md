# 01 — Solution scaffold and repeatable checks

[← All implementation slices](../../implementation-slices.md)

**Status:** Complete — PR from `slice/01-solution-scaffold`. Build passed; seven boundary/smoke
tests passed; the app opened and closed successfully; generated output stayed under `build/`.

Depends on: 00. Scope: .NET 10 solution, contracts/core/Windows/app/test projects, minimal
Avalonia window, central output paths under `build/`, and documented build/test commands.
Acceptance: restore/build succeeds, one useful scaffold smoke check runs, core/contracts have
no Windows or provider references, generated files remain ignored. Pin actual package versions.

