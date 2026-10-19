# FocusRGB plugin protocol 1.0

This document is the normative, language-neutral contract between the FocusRGB host and a
plugin process. The JSON schemas in [`schemas/`](../schemas/) are normative machine-readable
constraints. `FocusRgb.Contracts` is the matching hand-written C# model, and
`FocusRgb.PluginSdk` is an optional convenience for .NET authors.

## Transport

- The host starts one child process per plugin package. A `.dll` entry point is started with
  `dotnet`; any other entry point is started directly.
- Standard input and output carry UTF-8 JSON-RPC 2.0. Each message occupies exactly one line and
  is terminated by `LF`; senders may emit `CRLF`. A message must not exceed 1 MiB before the line
  terminator.
- Standard output is protocol-only. Human-readable logs and exception details go to standard
  error. The host may retain only a bounded number of log lines.
- Request IDs are signed 64-bit integers. Calls may be concurrent and responses may arrive out
  of order. A response must echo its request ID.
- EOF or process exit ends the session. The host fails pending calls as `plugin-exited`, clears
  that dead process's logical authority claim, and may restart only that plugin.

## Lifecycle and time limits

The first request is `initialize`. No other request is valid until it succeeds. The manifest ID,
handshake plugin ID, and compatible contract version must match. The host then uses `status/get`,
`catalog/list`, `binding/apply`, and `binding/release` only when the declared capabilities allow
them. `shutdown` is the final request.

| Operation | Host limit | Cancellation and exit behavior |
|---|---:|---|
| `initialize` | 10 seconds | Failure or timeout rejects startup and terminates the child. |
| `status/get` | 5 seconds | Timeout marks the plugin unhealthy. |
| `catalog/list` | 5 seconds | Timeout returns no guessed catalog and marks the plugin unhealthy. |
| `binding/apply` | 5 seconds | Timeout is not proof that output was unchanged; recovery must re-query status before retrying. |
| `binding/release` | 5 seconds | Timeout leaves ownership unresolved and blocks transfer to another writer. |
| `shutdown` | 5 seconds, then 500 ms exit grace | The host kills a child that does not exit after the bounded shutdown. |

When a host stops waiting for a written request, it sends the `$/cancel` notification with that
request ID. A plugin should cancel the associated operation and return JSON-RPC error `-32800`.
Cancellation is best effort: the host still treats a timed-out mutating call as uncertain. Host
shutdown or process exit cancels all pending plugin operations.

## Requests and notifications

Every request has `jsonrpc: "2.0"`, an integer `id`, a method, and an object `params`. Every
successful response has the same `id` and a `result`; every failed response has `error.code` and
`error.message` instead.

### `initialize`

Parameters contain `contractVersion` and `host { name, version }`. The result contains
`pluginId`, `pluginVersion`, the negotiated `contractVersion`, and `capabilities[]`. Each
capability has a stable `id`, one `support` value, and an optional `reason`.

### `status/get`

Parameters are empty. The result contains `availability`, an optional `reason`, and an optional
`current`. The current value contains optional effect and layout resource references. A
resource reference contains an opaque provider `resourceId` when known and a required
`displayName`.

### `catalog/list`

Parameters contain `kind`, currently `effect` or `layout`. The result contains `support`, an
optional `refreshedAt`, `items[]`, and an optional `reason`. Each item has a stable `resourceId`,
`displayName`, `available`, and an optional reason. Unsupported enumeration returns
`support: "unsupported"`; it must not fabricate items.

### `binding/apply`

Parameters contain the profile ID, binding ID, rendering authority, device scope, optional
effect, and optional layout. Omitting layout means retain the provider's current layout. The
result contains `outcome`, an optional reason, and the actual observable selection when known.

### `binding/release`

Parameters contain the profile ID and binding ID. The result contains `outcome`. Success means
the plugin has removed changes owned by that binding; it does not by itself prove that another
physical writer may safely acquire a device.

### `shutdown`

Parameters are empty and the result is an empty object. The plugin stops accepting work, cleans
up app-owned temporary state where possible, writes the response, and exits.

### Notifications

- `status/changed` has empty parameters and tells the host to call `status/get`; it carries no
  authoritative state itself.
- `$/cancel` contains `{ "id": number }` and is sent by the host for an abandoned request.

## Support, outcomes, and reasons

Capability and catalog support is exactly `supported`, `unverified`, or `unsupported`.
`unverified` means the implementation exists but visible or environmental acceptance has not
been established. It must not be presented as verified success.

Binding outcomes currently use `applied`, `released`, `rejected`, `unavailable`, `unsupported`,
or `unverified` as appropriate to the operation. A `reason` contains a stable machine `code` and
an actionable human `message`. Established host codes include `contract-incompatible`,
`plugin-absent`, `plugin-exited`, `plugin-incompatible`, `timeout`, `request-cancelled`, and
`malformed-response`. Plugins use specific reason codes such as `capability-unsupported` rather
than relying on prose matching.

The plugin SDK uses standard JSON-RPC errors: `-32700` parse error, `-32600` invalid request,
`-32601` unknown method, `-32602` invalid parameters, `-32603` internal error, and `-32800`
request cancelled. Exception details go to standard error, not the JSON-RPC error message.

## Manifest and package

The package root contains `focusrgb-plugin.json`, validated by
[`plugin-manifest.schema.json`](../schemas/plugin-manifest.schema.json). Schema version 1 requires:

- `id`: lowercase letters, digits, and hyphens, at most 64 characters;
- semantic `version` with three numeric parts and `contractVersion` with two numeric parts;
- an entry point contained inside the package;
- unique supported operating systems, architectures, roles, and declared resources;
- a non-empty SPDX license expression; and
- optional absolute HTTP(S) `source`.

The host rejects path traversal, a missing entry point, an unsupported platform or architecture,
an unexpected plugin ID, and an incompatible contract major before launching code. Plugins run
with the user's permissions and therefore require explicit installation and enablement.

The manifest resource vocabulary is `process.read`, `process.start`, and `filesystem.read`;
declare only access the plugin actually needs, or use an empty array. New resource names require
a manifest-schema update before plugins may use them.

## Versioning and compatibility

Contract versions are `major.minor`; package versions are semantic `major.minor.patch`.

- A major contract change may remove or reinterpret wire behavior. The host rejects a different
  major with `contract-incompatible` before or during initialization.
- A minor contract change adds optional methods, fields, or capabilities. A plugin must negotiate
  a version it supports and must not require behavior newer than the host's offered minor.
- Patch releases change implementation or documentation without changing the wire contract.
- Stable 1.x fields and methods are not removed before 2.0. Deprecations remain supported for at
  least the rest of the current major and are documented before removal.
- Unknown optional capabilities are ignored. Required behavior is invoked only after capability
  negotiation, leaving room for game events and native frame output in later 1.x versions.

During the SignalRGB migration only, host 1.0 also accepts contract 0.1 when both the manifest
and handshake declare 0.1. The UI and log identify it as legacy. Remove this exception after the
SignalRGB plugin publishes its coordinated 1.0 package.

## Conformance

Conformance does not require .NET. A conforming executable communicates only through the
transport above, validates initialization, implements every capability it reports, handles
cancellation and shutdown, keeps logs off standard output, and agrees with
[`plugin-protocol.schema.json`](../schemas/plugin-protocol.schema.json).

The language-neutral runner checks the successful initialize, status, catalog, apply, release,
and shutdown lifecycle. The repository's host and SDK tests separately cover package
reproducibility, an external package consumer, manifest and handshake incompatibility, malformed
and oversized messages, timeouts, cancellation, concurrent calls, process exit, stderr/stdout
separation, and shutdown. Schema/type drift tests fail when the normative schemas, method
constants, contract version, or hand-written C# property names, requiredness, and types diverge.
