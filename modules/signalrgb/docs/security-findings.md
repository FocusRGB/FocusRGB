# SignalRGB Client Security Test — Working Notes

**Scope:** Local client-security testing of SignalRGB desktop app (WhirlwindFX), focused on
Pro/entitlement enforcement — local process attack surface, memory tampering, and local
control/MCP API. All testing performed against the local running instance on this machine.

---

## 1. Architecture discovered

- `SignalRgb.exe` — main Qt6/QML app (`%LOCALAPPDATA%\VortxEngine\app-2.5.74\Signal-x64\`).
  Pro gating is a QML-exposed property `core.user.isPro` (bool), referenced throughout the UI
  (`enabled: core.user.isPro`, `visible: !core.user.isPro && isProExclusive`, etc).
  `core.user` also exposes `signedIn`, `uid`, `subscriptions.invoices` — consistent with
  entitlement being sourced from a remote account/subscription API (`signalrgb.com`,
  `marketplace.signalrgb.com`), not a local-only check.
- `SignalRgbService.exe` — separate Rust service (Windows service `SignalRgb.Service`),
  listens on `127.0.0.1:16039`. Plain HTTP, JSON-RPC 2.0, **no auth challenge observed**.
  Only methods found: `rpc.health`, `rpc.conflicts` (kills conflicting RGB-control processes).
  **Not** the license/Pro check — a dead end for that specific question.
- `SignalRgb.exe` itself also listens on `16034`/`16038` (bound to `0.0.0.0`, not just loopback —
  noted as a separate finding below) and `16036`/`16037` (loopback) — this is the local
  control/MCP API surface.

## 2. Findings so far

| # | Finding | Severity (prelim) | Status |
|---|---|---|---|
| 1 | `SignalRgb.exe` control API ports 16034/16038 bind `0.0.0.0`/`[::]`, not `127.0.0.1` only | Medium | Confirmed via `netstat` |
| 2 | `SignalRgbService` JSON-RPC (16039) has no auth on health/conflict methods | Low | Confirmed, but no Pro-relevant methods found |
| 3 | No anti-debugging/anti-injection — Frida attaches freely | Info | Confirmed |
| 4 | Frida attach/detach repeatedly crashes `SignalRgb.exe`; `SignalRgbLauncher` silently auto-restarts it | Info/Low | Confirmed (incidental fragility, not a deliberate control) |
| 5 | Generic reflection-based property tampering (hooking exported `QMetaProperty::read/write`) did not catch `isPro` — Qt Quick's compiled-binding fast path likely bypasses the generic exported API | Info | Inconclusive — needs real binary RE to confirm/exploit, not a quick win |
| 6 | Local control API `effect_install` on a marketplace **"not free"** effect ("Black Hole") did NOT actually install it (verified via `read_effects` before/after) | — (control working) | Confirmed — this specific vector did *not* bypass the paywall |

## 3. Prior findings supplied by user, with correction

| # | Surface | Behavior observed | Implication |
|---|---|---|---|
| A | `effect_apply` via MCP tools / REST API, on Pro-gated effects | Returns `"requires SignalRGB pro"`; `GET /api/v1/lighting` and `GET /api/v1/lighting/effects` return HTTP 403 "must be a SignalRGB Pro user" | Server/API-side gate IS enforced for this path |
| A — URL-scheme route | `signalrgb://effect/apply/<EffectName>` (launched via `SignalRgbLauncher.exe --url=signalrgb://effect/apply/<EffectName>?-silentlaunch-`; `%20` for spaces; handled via the `HKCU\Software\Classes\signalrgb` registry handler; documented in SignalRGB's integration docs, and the route string exists in `SignalRgb.exe`) | Applied free/already-installed effects (Rainbow, Aurora, Screen Ambience). **Did not** apply a paid effect (Dota 2) or a custom effect. | **Not a bypass.** ~~Initially reported as "the one free way around" the Pro check~~ — retracted by the user after further testing. It's an unlocked way to switch between effects already accessible to the account, nothing more. |
| B | `effect_install` on paid catalogue effects (148/383 paid, incl. Fortnite, Valorant, Dota 2) | Returns a **false "OK"** (misleading success response) but does not actually install; URL route also does not bypass this one | Consistent with our own "Black Hole" test — this specific gate holds |
| C | Game integrations that read the screen (e.g. Witcher 3 health/toxicity bars) | Installs free, but running it prompts for Pro at runtime | Separate runtime-gate, not just an install-time gate |
| D | Screen-mirroring / video lightscripts on third-party devices (WLED) | Custom Screen Ambience copy blocked; built-in Screen Ambience works | Documented Pro tier restriction, device/integration-type based |

**Note on finding B's "false OK":** a local API call reporting success for an action that silently
did nothing is worth flagging on its own, independent of the Pro question — it's a correctness/API-contract
issue (callers, including legitimate integrations, can't trust the response), separate from whether
the underlying entitlement check holds (it does, here).

## 4. REST/MCP `effect apply` — correct route found and verified

The real route (found via binary string search, not guessed) is:

```
POST /api/v1/lighting/effects/<effectId>/apply
```

(also present: `/api/v1/lighting/effects/<id>`, `/<id>/image`, `/<id>/presets`,
`/lighting/playlists/<id>/play`, `/lighting/next`, `/lighting/previous`, `/lighting/shuffle`,
`/lighting/loadEffect`, `/lighting/global_brightness`, `/lighting/enabled`. Also present but
untested: `"You must be a SignalRGB Developer to use this endpoint"` — a separate dev-tier gate.)

**Test result:** `POST .../lighting/effects/<id>/apply` returns `403 "You must be a SignalRGB
Pro user to access this endpoint"` for **every** effect tried — including `Rainbow.html`, which
is free and already installed. This is a **blanket Pro gate on the whole `/api/v1/lighting/*`
REST namespace**, not a per-effect paid/free check.

Cross-checked the MCP tool path (`effect_apply`) against the same two cases (paid DOTA 2, free
installed Rainbow) — **both correctly rejected** with the same "requires SignalRGB pro" message.
**No inconsistency found between the MCP tool interface and the raw REST API** — they enforce the
same gate. This specific interface pair does not offer a bypass.

## 5. Further REST/MCP surface testing

| Call | Result | Pro-gated? |
|---|---|---|
| `POST /api/v1/lighting/next` | 403 "must be SignalRGB Pro user" | Yes — consistent with `/apply` |
| `POST /api/v1/lighting/previous` | 403 "must be SignalRGB Pro user" | Yes |
| `POST /api/v1/lighting/shuffle` | 403 "must be SignalRGB Pro user" | Yes |
| `POST /api/v1/lighting/loadEffect` | 403 **"must be a SignalRGB Developer"** | Different tier — a third access level beyond free/Pro, not yet explored further |
| MCP `effect_applyprevious` | generic `"Failed to execute action"` | Inconclusive — no active effect history in this session, not clearly a Pro check |
| MCP `effect_applypreset` | generic `"Failed to execute action"` | Inconclusive — likely missing required preset name param, not clearly a Pro check |
| MCP `effect_setproperties` | "requires SignalRGB pro" | Yes |
| MCP `device_writesetting` (mouse, `enabled`) | "No matching properties found" | Not Pro-related — wrong property key, not an entitlement check |
| MCP `device_settings` (WLED device `wled-desk`) | Returned settings fine (`LightingMode`, `forcedColor`, `turnOffOnShutdown`) | No Pro gate visible at this level — the WLED/third-party video-lightscript Pro restriction (finding D) is presumably enforced at effect-application time, not in device settings |

**Summary of this pass:** every mutating `/api/v1/lighting/*` route and its MCP equivalent enforces
the Pro gate consistently, including a third "Developer" tier on `loadEffect`. No bypass found via
the REST or MCP control-API surface in this round of testing. The one non-security issue worth
keeping in the report is `effect_install`'s misleading "OK" response (section 2, finding 6 /
section 3, item B) — a response-correctness bug, not an auth bypass.

## 6a. Registry enumeration (read-only) — no entitlement flag found anywhere

Read through `HKCU\Software\WhirlwindFX\SignalRgb` and subkeys (`catalog`, `conflicts`, `UI`,
`UI\Dev`, `UI\DeveloperPanel`, plus the full key list supplied separately covering addons,
alerts, devices, effects, layouts, lighting, Macroblocks, Monitoring, WLED, states, tours,
video, window). **No key resembling `isPro`/`isDeveloper`/`entitlement`/`subscription`/`claims`
exists anywhere in this tree.** Everything found is ordinary app/UI state (binary-serialized
`QVariant` catalog data, panel layout prefs, per-device settings).

One real, separate finding from this pass: **`conflicts\whitelisted_processes` is user-editable**
(`REG_MULTI_SZ`, currently `RazerAppEngine.exe`) and is additive to whatever the
`SignalRgbService` allowlist already permits — a local user can add process names to their own
kill-allowlist. Low impact (still restricted to what the user adds for themselves, not arbitrary
processes system-wide), but worth the team confirming this list is bounded/sane server-side too,
not purely trusted from this registry value.

**Conclusion: entitlement state is not persisted in the registry.** This closes out the
specific "find a local pro flag and flip it" line of inquiry — consistent with the Firebase
claims-based architecture in section 6 below (the claim lives in the signed-in session's
in-memory/SDK-cached token, not a WhirlwindFX-written settings key).

## 6. Root-cause finding: entitlement architecture (Firebase Auth custom claims)

Resolved why nothing tested found a bypass. The app uses the real **Firebase C++ SDK**
(`https://whirlwindengine.firebaseio.com`, `https://signalrgbpro.firebaseio.com`,
`https://signalrgbpartners.firebaseio.com`), not a custom/local auth scheme.

- Log line found verbatim in app logs: `FirebaseUser: Token decoded. pro={}, admin={}, developer={}, expires={}`
- The `FirebaseUser` object's properties — `isPro`, `isDeveloper`, `isAdmin`, `signedIn`,
  `inRedemption`, `emailVerified`, `developer_fx`, `purchased_fx` — are **all** decoded from
  custom claims on a Firebase Auth ID token issued and cryptographically signed server-side
  by Firebase/Google for the signed-in account.
- `issueDeveloperAccessRequest` implies Developer status is requested/approved server-side
  (a custom claim grant), not a local toggle — **same trust architecture as Pro, not a weaker
  side door.**
- This machine's test account is **not signed in** (`"no cached Firebase user; waiting for
  auth listener"` in app logs), confirming there is a local cache for a *real* signed-in
  session, but nothing to extract/replay here — testing actual token extraction/replay would
  require a real paying account's credentials, which is out of scope for this engagement.

**Conclusion:** This explains the clean sweep of "no bypass found" across every API/MCP surface
tested. A locally-forged `isPro: true` is not practically achievable without either (a) a valid
Firebase ID token for an actual Pro/Developer account, or (b) compromising Firebase's signing
key — neither of which is a "local client tampering" bypass in the sense originally scoped. The
one remaining theoretical local-attacker angle is **token-at-rest extraction/replay** (stealing
a real signed-in user's cached Firebase session from their own machine) — a credential-theft
question, not an entitlement-logic flaw, and not something to test without a real account in
scope.
