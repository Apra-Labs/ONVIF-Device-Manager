# Sprint 6 Phase 3 Review — Connection-Refused Detector + HTTP xAddr Memoization + Fallback Factories

**Reviewer:** odm-rev
**Date:** 2026-04-16 02:58:44-0400
**Branch:** `feat/media2-support`
**Commits under review:** `c556bf5`, `236280d` (base `41460ed`)
**Verdict:** **APPROVED**

---

## Scope

Diff surface (`git diff 41460ed..HEAD --stat`):

```
 odm/odm.tests/ConnectionRefusedDetectorTests.cs | 82 ++++++++++++++++++++
 onvif/onvif.session/NvtSession.fs               | 82 ++++++++++++++++++-
 progress.json                                   | 24 +++++-
 3 files changed, 184 insertions(+), 4 deletions(-)
```

Tight, task-aligned change surface — no drift.

---

## Task 3.1 — `IsConnectionRefused` + HTTP xAddr memoization

### `NvtSessionFactory.IsConnectionRefused` — PASS

Source: `onvif/onvif.session/NvtSession.fs:518-537`

- **Static member on `NvtSessionFactory`** — PASS. `static member IsConnectionRefused (err: exn) : bool`, directly callable from C# tests as `NvtSessionFactory.IsConnectionRefused(ex)`.
- **Recursive unwrapping** — PASS.
  - `AggregateException` → `ae.InnerExceptions |> Seq.exists check` (handles multi-inner aggregates correctly).
  - `CommunicationException` → `check e.InnerException` (recurses into the wrapped transport exception).
- **True only for the two intended transport cases** — PASS.
  - `WebException` iff `Status = WebExceptionStatus.ConnectFailure`.
  - `SocketException` iff `SocketErrorCode ∈ { ConnectionRefused, ConnectionReset }`.
  - **`SecureChannelFailure` and `TimedOut` are NOT present** — matches the plan's removal requirement exactly.
- **`CommunicationException` wrapping a transport exception** — PASS (true via inner recursion).
- **`FaultException`** — PASS (returns false). `FaultException` is a subclass of `CommunicationException` and matches that arm, but its `InnerException` is null, so `check null` returns false on the entry guard `obj.ReferenceEquals(e, null) → false`.
- **Random exceptions** — PASS (fall through to wildcard `_ -> false`).
- **Null safety** — PASS (explicit null guard at entry).

Minor observation (not a defect): the implementation is fault-tolerant to `FaultException` instances that do have a non-null InnerException — if that inner were a real transport exception it would return true. This is the correct behaviour for the fallback policy (transport failure at any depth = retry HTTP), not a bug.

### `GetMedia2HttpXAddr` — PASS

Source: `onvif/onvif.session/NvtSession.fs:1081-1094`

- **Memoized** — PASS. `let comp = Async.Memoize(async {...})` is captured once in the session closure; `fun () -> comp` returns the same memoized computation per session.
- **No `FixUrl` / `UpgradeScheme`** — PASS. Returns `new Uri(service.XAddr)` directly from `GetServices()`. Raw xAddr preserved for fallback retry.
- **Null on absent service** — PASS. Two guards: `services |> IsNull` and `service |> IsNull` (the `FirstOrDefault` result) both return null.

### `GetMedia1HttpXAddr` — PASS

Source: `onvif/onvif.session/NvtSession.fs:1097-1107`

- **Memoized** — PASS (identical `Async.Memoize` pattern).
- **Raw URI from `GetCapabilities().media.xAddr`** — PASS. `new Uri(caps.media.xAddr)` with no transformation.
- **Null when media caps are null** — PASS. Both `caps |> IsNull` and `caps.media |> IsNull` branches return null.

---

## Task 3.2 — Fallback client factories — PASS

Source: `onvif/onvif.session/NvtSession.fs:1109-1129`

- **`createMedia2ClientAt` and `createMediaClientAt` exist** — PASS. Both `Uri -> Async<IMedia2>` / `Uri -> Async<IMediaAsync>` per spec.
- **Non-memoized (fresh channel per call)** — PASS. No `Async.Memoize` wrapper; every invocation runs `factory.CreateChannel(new EndpointAddress(url))`, yielding a new `IClientChannel`. Critical property for HTTP fallback retries where channel state must be reset.
- **`SetupUserNameToken` for auth** — PASS. Both call `do! SetupUserNameToken(proxy :?> IClientChannel)` before returning.
- **Scheme handling** — PASS. `useTls = url.Scheme = Uri.UriSchemeHttps` correctly selects HTTPS vs HTTP factory. (Factory selection is still memoized inside `getMedia2Factory` / `getMediaFactory`, which is correct — binding configuration can be reused.)

---

## Test coverage — `ConnectionRefusedDetectorTests.cs` — PASS

Source: `odm/odm.tests/ConnectionRefusedDetectorTests.cs:1-82`

All 5 PLAN.md cases are covered, plus 2 additional negative cases:

| Case | Test method | Expected |
|---|---|---|
| 1. `WebException(ConnectFailure)` | `IsConnectionRefused_WebExceptionConnectFailure_ReturnsTrue` | true — PASS |
| 2. `SocketException(ConnectionRefused)` in `AggregateException` | `..._SocketExceptionConnectionRefused_WrappedInAggregate_ReturnsTrue` | true — PASS |
| 3. `SocketException(ConnectionReset)` in `AggregateException` | `..._SocketExceptionConnectionReset_WrappedInAggregate_ReturnsTrue` | true — PASS |
| 4. `CommunicationException` wrapping `SocketException(ConnectionRefused)` | `..._CommunicationException_WrappingSocketException_ReturnsTrue` | true — PASS |
| 5. `FaultException` | `..._FaultException_ReturnsFalse` | false — PASS |
| +bonus — random `Exception` | `..._RandomException_ReturnsFalse` | false — PASS |
| +bonus — `WebException(Timeout)` | `..._WebExceptionTimeout_ReturnsFalse` | false — PASS (regression lock) |

The `WebException(Timeout)` test is a nice regression lock — if a future refactor re-adds `TimedOut` to the accept list, this test will catch it immediately.

---

## Build + test run — PASS

- `dotnet build odm/odm.tests/odm.tests.csproj -v quiet` — **Build succeeded**, 0 errors, 2 unrelated `NU1900` package-feed warnings (BluB0X feed unreachable from reviewer machine — not a code issue).
- `dotnet test --filter "TestCategory!=Integration" --no-build -v minimal` — **Passed! 88/88 tests, 0 failures, 12 s.**

---

## Summary

**What passed**
- `IsConnectionRefused` is correctly scoped (static, public, pure), recursively unwraps `AggregateException` and `CommunicationException`, and accepts only the two intended transport-error signatures. `SecureChannelFailure` and `TimedOut` are correctly absent.
- `GetMedia2HttpXAddr` / `GetMedia1HttpXAddr` memoize per session, bypass `FixUrl`/`UpgradeScheme`, and return null for absent services — ready for fallback consumers.
- `createMedia2ClientAt` / `createMediaClientAt` are fresh-channel-per-call, scheme-aware, and apply UsernameToken — ready for retry use.
- Tests cover all 5 PLAN.md cases plus two regression-lock negatives. Full `odm.tests` suite (88 tests) passes offline.

**What must change**
- Nothing. No findings at any severity.

**What is deferred**
- Actual wiring of these primitives into the fallback retry path (GetProfiles/GetStreamUri/etc.) lands in later Phase 4+ tasks — out of scope for this review.

Phase 3 is **APPROVED**. Safe to proceed to Phase 4.
