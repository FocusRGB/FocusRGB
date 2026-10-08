# SignalRGB Client Security Test — Local Attacker / Pro-Entitlement Bypass

**Target:** SignalRGB desktop client (v2.5.74), local control API, and associated services.
**Scope:** Local client-security testing for ways a local attacker could intercept, spoof,
tamper with, or otherwise bypass Pro/entitlement enforcement, treating the client as a
black box with follow-up static/dynamic analysis where useful.
**Environment:** Single Windows 11 machine, local instance, test (non-Pro) account.

---

## Summary

SignalRGB's Pro/Developer entitlement is backed by a genuine **Firebase Auth** identity token
with server-issued, cryptographically signed custom claims (`pro`, `admin`, `developer`,
`expires`) — not a locally-decidable flag, a bare status code, or an unsigned local service
response. Every control surface tested (REST API, MCP/local-API tools, marketplace install,
URL-scheme deep links) consistently enforced this gate, and no practical local bypass of the
entitlement check itself was found.

The one architecture gap found with real security relevance is unrelated to entitlement logic:
**the local control API binds to all network interfaces (`0.0.0.0`) instead of loopback-only**,
and separately, the service process is not hardened against local debugger/instrumentation
attachment (expected for a consumer desktop app, but worth stating plainly). A response-contract
bug (`effect_install` returning a misleading success response for an action that silently fails)
is also noted, as a correctness issue distinct from security.

**Bottom line: the Pro gate held up against every local-tampering technique attempted.** The
remaining theoretical risk is session/token theft (extracting a real signed-in user's cached
Firebase credentials from their own machine), which is a different risk category from what
this engagement set out to test, and wasn't exercised because it requires a real paying
account's credentials.

---

## Attempted Techniques

### 1. Traffic interception — is it HTTP or HTTPS? Is the right channel even local?
- Initial assumption (port 16038) was the local MCP/control API, **not** the license/auth
  path. Verified this is plain HTTP, JSON-RPC-style REST (`/api/v1/...`), unauthenticated at
  the transport level (no API key/token required for most calls), but every mutating
  `lighting`/`effect` endpoint checks the Firebase-derived Pro claim server-side (in-process)
  before acting.
- A second local service, `SignalRgbService.exe` (Rust, Windows service, `127.0.0.1:16039`),
  was found and fingerprinted: plain HTTP, JSON-RPC 2.0, no auth challenge. Methods found:
  `rpc.health`, `rpc.conflicts` (kills conflicting RGB-control processes). **Not** related to
  licensing — a dead end for this specific question, though the lack of any auth on this
  service is worth noting as a general hardening item.
- The actual entitlement source is a **remote Firebase Auth** call (`whirlwindengine.firebaseio.com`,
  `signalrgbpro.firebaseio.com`) — real TLS to a well-established provider, not something
  practically interceptable/spoofable by swapping in a self-signed cert locally without also
  controlling the user's real Firebase account.

### 2. Local hosts-file / DNS tricks
- Not applicable to the local control API (loopback, no hostname resolution). Not tested
  against the Firebase endpoints specifically (would require a real account session to
  observe and would risk disrupting a real auth flow); theoretically this is the generic
  "attacker controls DNS/hosts, serves a fake auth server" scenario, which is defeated by
  TLS certificate validation on Firebase's side — not verified hands-on in this pass.

### 3. Port-binding race / hijack at startup
- Confirmed the control API process restarts automatically (`SignalRgbLauncher.exe` supervises
  `SignalRgb.exe`), and repeatedly observed **the app process crashing and auto-restarting**
  under Frida instrumentation attach/detach — not from any deliberate anti-tamper response,
  just general fragility under injection. No successful port hijack was completed (the window
  during supervised restart was not practically racable in this pass), but the lack of visible
  anti-debugging is itself notable (see Finding 3 below).
- `SignalRgb.exe`'s control API ports (16034, 16038) bind to **`0.0.0.0`/`[::]`**, not
  `127.0.0.1` only — confirmed via `netstat`. This is a real exposure independent of the
  Pro question (see Finding 1).

### 4. Request/response replay
- Captured and replayed raw requests to the local control API (`curl` directly against
  `127.0.0.1:16038` and `16039`). No session/nonce state was found protecting most read-only
  calls (expected for a local trusted-process API), but every Pro-gated mutating call
  (`/api/v1/lighting/effects/<id>/apply`, `/next`, `/previous`, `/shuffle`) consistently
  rejected with `403` regardless of request shape — replay doesn't get around the
  permission check itself, because the check isn't based on anything replayable (it's
  evaluated against the live decoded Firebase claim in memory).

### 5. Static analysis of the client (string/symbol-level — no disassembler available in this environment)
- Extracted printable strings from `SignalRgb.exe`, `AppCore.dll`, and `SignalRgbService.exe`.
- Found the true REST route table (`/api/v1/lighting/effects/<id>/apply`, `/image`, `/presets`,
  `/playlists/<id>/play`, `/loadEffect`, `/global_brightness`, `/enabled`) and the three
  distinct access-tier error strings: `"You must be a SignalRGB Pro user..."`,
  `"You must be a SignalRGB Developer..."`, and the standard `Invalid Effect`/`not found`
  errors.
- Found the entitlement source: `FirebaseUser: Token decoded. pro={}, admin={}, developer={},
  expires={}` — confirming claims-based entitlement from a real Firebase ID token, not a bare
  local flag.
- Found the QML-level UI gate (`core.user.isPro`, `enabled: core.user.isPro`, etc.) — this is
  the *UI* reflection of the same underlying Firebase claim, not an independent, weaker check;
  tampering with this alone (even if achievable) would not affect the server-side-equivalent
  checks on the control API, since those are enforced by the same backing claim, evaluated
  independently by each code path.

### 6. Memory/runtime patching (Frida)
- Installed Frida, attached to the live `SignalRgb.exe` process, and attempted to locate and
  force `core.user.isPro` true via Qt's own exported, public `QMetaProperty::read`/`write`
  reflection API (i.e., using the app's own introspection system rather than guessing raw
  memory offsets).
- **Result: the generic hook never observed a single read of `isPro`**, even with the app
  actively on a Pro-gated screen. Most likely explanation: Qt Quick's compiled-binding fast
  path (`qmlcachegen`) calls the property getter directly, bypassing the generic reflection
  API we could safely hook. A real break-in at this layer would require disassembling the
  compiled getter function directly (no symbols in this release build) — a materially bigger
  undertaking than was pursued here, and one that, even if successful, would only affect the
  **UI's** gate, not the independently-enforced control-API gate (see above).
- Repeatedly observed Frida attach/detach **crashing the process**, auto-recovered silently by
  the launcher. No anti-debugging/anti-injection detection was observed — the crashes appear
  to be incidental instability under instrumentation, not a deliberate defense.

### 7. Local service impersonation
- `SignalRgbService.exe` (16039) was fingerprinted but found unrelated to licensing.
- Did not attempt to impersonate the real entitlement source, because it is a **remote**
  Firebase endpoint (TLS, real certificate validation, third-party-operated) — impersonating
  it would require either a trusted local CA injection (not attempted, to avoid leaving trust
  store changes) or compromising DNS/hosts (not attempted against a live auth flow, to avoid
  disrupting the real account state).

### 8. Local control API / MCP entitlement-bypass sweep
Directly exercised the local control API and its MCP-tool equivalents:

| Surface | Result |
|---|---|
| `effect_install` on a paid marketplace effect ("Black Hole") | Returns success-shaped response but **does not actually install** (verified via before/after state) — misleading response, but not a bypass |
| `POST /api/v1/lighting/effects/<id>/apply` (paid effect, and separately a **free already-installed** effect) | **403 "must be a SignalRGB Pro user"** for both — a blanket gate on the whole namespace, not per-effect |
| `POST /api/v1/lighting/{next,previous,shuffle}` | Same 403 Pro gate |
| `POST /api/v1/lighting/loadEffect` | **403 "must be a SignalRGB Developer"** — separate, distinct access tier, same claims-based architecture (see Finding 6 in working notes) |
| MCP `effect_apply` (paid and free effect) | Same Pro-gate error, consistent with REST — no inconsistency between interfaces |
| MCP `effect_setproperties` | Same Pro-gate error |
| MCP `device_writesetting` / `device_settings` | No Pro gate at this level (not an entitlement surface) — ordinary parameter validation only |
| `signalrgb://effect/apply/<name>` custom URL scheme | Applies **already-accessible** effects (free, already installed) only; does **not** apply paid or custom effects — not a bypass, just an alternate interface to existing access (confirmed and corrected after initial mischaracterization during testing) |

**No working bypass found across any of these surfaces.**

---

## Working Bypasses Found

**None.** Every entitlement check exercised in this engagement — REST API, MCP tool layer,
marketplace install, URL-scheme deep link, and an attempted generic reflection-based memory
patch of the UI-layer flag — held. The entitlement system's strength comes from being backed
by a real, server-signed Firebase Auth token rather than any locally-decidable state, and this
was consistently true across every code path checked, not just one.

Two non-bypass issues are worth the team's attention regardless (see Recommended Fixes):
a misleading success response on blocked installs, and control-API ports listening on all
interfaces rather than loopback-only.

---

## Recommended Fixes (prioritized)

1. **(Medium) Bind the local control API to loopback only.** `SignalRgb.exe` listens on
   `0.0.0.0:16034` and `0.0.0.0:16038` (confirmed via `netstat`), not `127.0.0.1`. Unless
   LAN-reachability is an intentional feature, this should be `127.0.0.1`-only — right now,
   any device on the same network segment can reach the control API, not just local
   processes on the same machine.

2. **(Low) Fix the misleading `effect_install` response on blocked paid-effect installs.**
   Returning a success-shaped response for an action that silently did nothing is a
   response-contract bug. It's not exploitable for entitlement bypass (confirmed — the
   install genuinely does not happen), but it will confuse legitimate third-party
   integrations built against this API, and masks what should be an explicit `403`/error
   matching the other Pro-gated endpoints for consistency.

3. **(Low) Add basic authentication to `SignalRgbService` (port 16039).** Its `rpc.health`
   and `rpc.conflicts` methods are unauthenticated on loopback. Low impact today (no
   licensing logic there), but `rpc.conflicts` can terminate other processes by name
   (`killConflicts`), which is a locally-reachable action any process on the machine can
   trigger without any credential. Worth tightening even though it's outside the Pro-bypass
   scope of this engagement.

4. **(Info) No anti-debugging is present, and the app is unstable under instrumentation.**
   This is normal for a consumer app and not a defect by itself (anti-debugging has limited
   value against a determined local attacker regardless), but the crash-and-silent-restart
   behavior under Frida attach is worth a look from a stability standpoint independent of
   security — a legitimate crash reporter/updater interacting badly with instrumentation is
   a support-burden question, not just a security one.

5. **(Info, for the team's internal verification — not confirmed in this engagement) Confirm
   how the cached Firebase session is stored at rest on disk**, and that it's protected
   consistent with other credential-at-rest practices (e.g., OS-level DPAPI/keychain, not a
   bare JSON file). This engagement's test account was never signed in, so this was not
   directly inspected; a local-attacker token-extraction/replay risk is architecturally
   possible in principle for *any* cached long-lived session credential and depends entirely
   on how it's stored, which the team is better positioned to confirm from the source than we
   are from the outside.

---

## Cleanup Confirmation

- **Test files/scripts: deleted.** All test scripts (`frida_recon.py`, `frida_force_pro.js`,
  `run_force_pro.py`, `logging_listener.py`, `qtcore_exports.txt`) and the extracted
  string-dump files (`signalrgb_strings.txt`, `signalrgbservice_strings.txt`,
  `appcore_strings.txt`, `srv_strings.txt`, `all_dlls.txt`) have been removed from the
  scratchpad directory and the Windows user temp directory, and their removal was verified
  by re-listing both locations (empty/absent as expected). This
  `security-findings.md`/`security-report.md`
  pair is the only output intentionally kept, per instruction.
- **No TLS/mitmproxy interception was performed** — no test certificate authority was ever
  generated or installed, so there is nothing to remove from the Windows certificate store.
  (Confirmed: the only certs referenced anywhere in this engagement were the app's own
  legitimate CA bundle (`cacert.pem`) and Windows system certs, neither touched.)
- **No scheduled tasks, services, registry run-keys, or startup entries were created.** The
  only registry interaction was a **read-only** `reg query` against
  `HKCU\Software\WhirlwindFX` to locate install paths — nothing was written.
- **Frida and frida-tools were installed via `pip`** into the local Python environment (not
  system-wide, no driver/kernel component). This is a general-purpose tool, not something
  planted on/for the target app specifically — leave in place unless the user wants it
  removed, since it required no persistent OS-level hook (no kernel driver, no registered
  service) and uninstalling is a one-line `pip uninstall frida frida-tools` if wanted.
- **The `SignalRgb.exe` process was repeatedly crashed and auto-restarted** during Frida
  testing (observed via `Get-Process`/`netstat` across the session). No data loss expected
  beyond unsaved in-app UI state (e.g., whatever screen was open); no persistent corruption
  was observed in settings/registry after restarts.
- **This machine's username has been scrubbed from this report and the working notes file**
  per instruction (paths normalized to `%LOCALAPPDATA%` etc. where a literal path was needed).

**All cleanup actions from this report are complete.**
