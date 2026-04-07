# ODM Credentials UI — Phase 2 Cumulative Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-07
**Scope:** Tasks 2.1 (credential iteration), 2.2 (per-device credential cache), 2.V (verify build)
**Commits reviewed:** 0c4f295, 004abad, 1529e39
**Verdict:** APPROVED

---

## 1. TrySessionWithCredentials — Iteration Order and Anonymous Fallback

**PASS.** The iteration order is correct:

1. **Cache check first** — `_credentialCache.TryGetValue(cacheKey, ...)` is consulted before any network call
2. **All stored credentials in order** — `FullCredentialIteration` iterates `AccountManager.Instance.GetAllCredentials()`, filtering out anonymous entries, building a `List<NetworkCredential>`
3. **Anonymous (null) appended last** — `attempts.Add(null)` after the loop ensures anonymous is the final fallback

When all credentials in the list are exhausted (`index >= credentials.Count`), `TryNextCredential` enters the exhaustion branch which creates a `NvtSessionFactory(null)` and falls back to the last URI only — preserving the original pre-Phase-2 error behavior where a failed discovered device was retried on its last URI with anonymous credentials. This backward compatibility is correct.

**Manual devices follow the same pattern.** `ManualSessionProcess` → `FullCredentialIterationManual` → `TryNextCredentialManual` mirrors the discovered-device flow. The only difference is that the manual exhaustion branch creates a full anonymous session (all URIs) instead of last-URI-only, which matches the original `ManualSessionProcess` behavior. Correct.

---

## 2. Error Handling — All Failures as "Try Next"

**PASS.** Consistent with the Task 1.0 spike finding that auth failures are indistinguishable from network errors:

- Every `Subscribe(..., err => { ... })` in the iteration chain calls `TryNextCredential(devHolder, credentials, index + 1, publishEvent)` — no exception type inspection, no early abort
- Cache hit failures call `_credentialCache.Remove(cacheKey)` then `FullCredentialIteration(...)` — evict and retry the full list
- The exhaustion branch in `TryNextCredential` (`index >= credentials.Count`) does **not** silently swallow the error — it passes the anonymous session result to `InitDeviceHolder`, which is the existing behavior for unreachable/unauthenticable devices

**No exception filtering: CORRECT.** Given the spike finding, any attempt to distinguish `FaultException` from `CommunicationException` would be unreliable. The "try next on any error" approach is the right design.

---

## 3. Credential Cache (`_credentialCache`)

### 3a. Cache Key

**PASS.** `GetDeviceCacheKey` uses `devHolder.Uris[0].DnsSafeHost` — the host portion of the first URI. This is the correct granularity:

- Multiple ONVIF services on the same device share the same host
- Different devices on different hosts get separate cache entries
- The cache dictionary uses `StringComparer.OrdinalIgnoreCase` (line 138) — hostnames/IPs are case-insensitive per RFC, correct

**Edge case — multiple devices behind NAT with same external IP:** These would share a cache key. This is acceptable: devices behind the same NAT typically share credentials, and if they don't, the cache hit will fail and full iteration runs. Documented in R6.

### 3b. Cache Eviction

**PASS.** Eviction happens in two places:

1. `TrySessionWithCredentials` line 511: `_credentialCache.Remove(cacheKey)` when the cached credential fails — then falls through to `FullCredentialIteration`
2. `ManualSessionProcess` line 233: same pattern for manual devices

After eviction, the full iteration re-populates the cache on success (`CacheCredential` is called in every success handler). This correctly handles the camera-password-changed-mid-session scenario described in R6.

### 3c. Not Persisted

**PASS.** `_credentialCache` is a plain `Dictionary<string, Account>` instance field. It is not serialized, written to disk, or referenced from any persistence code. It lives only for the application lifetime. On `Refresh()`, `_deviceFactories` is cleared (line 364) but `_credentialCache` is intentionally **not** cleared — this is correct per the plan: "Cache survives Refresh ... for app-lifetime persistence."

### 3d. Cache Population

**PASS.** `CacheCredential` is called in every success path:

- `TryNextCredential` success handler (line 555) — caches the working named credential
- `TryNextCredential` exhaustion branch (line 542) — caches `Account.Anonymous`
- `TryNextCredentialManual` follows the same pattern
- Cache hit success handlers do **not** re-cache (not needed — the entry is already present)

---

## 4. Username Matching — Case Sensitivity

**PASS.** This check point is about Phase 1's `AccountManager.SetCurrentAccount`, which uses `StringComparison.OrdinalIgnoreCase` (line 115 of `AccountManager.cs`). Phase 2 does not introduce any new username comparison — credential iteration works on `NetworkCredential` objects and matches by authentication success/failure against the device, not by string comparison. The Phase 1 note about `Account.Equals` being case-sensitive while store dedup is case-insensitive remains accurate and unchanged.

The cache key uses `StringComparer.OrdinalIgnoreCase` for host matching, which is correct for hostnames but unrelated to username matching.

---

## 5. Single-Credential Backward Compatibility

**PASS.** When only one credential is stored:

1. `GetAllCredentials()` returns a list with one entry
2. `FullCredentialIteration` builds `attempts` = `[NetworkCredential(name, pass), null]`
3. `TryNextCredential(index=0)` tries the single credential
4. On success → session created, same behavior as before Phase 2
5. On failure → `TryNextCredential(index=1)` tries null (anonymous) → exhaustion branch falls back to last URI with anonymous — same as original `SessionProcess` error handler

The original `SessionProcess` was:
```csharp
sessionFactory.CreateSession(devHolder.Uris)
    .Subscribe(session => InitDeviceHolder(...),
               err => InitDeviceHolder(sessionFactory.CreateSession(devHolder.Uris.Last()), ...));
```

The new exhaustion branch replicates this exactly:
```csharp
var fallbackFactory = new NvtSessionFactory(null);
InitDeviceHolder(fallbackFactory.CreateSession(devHolder.Uris[devHolder.Uris.Count() - 1]), devHolder, publishEvent);
```

The only difference: the fallback factory is now explicitly `NvtSessionFactory(null)` instead of the class-level `sessionFactory` (which was also constructed with `currentAccount`, possibly null). When a single credential is stored and it fails, the fallback is anonymous — same effective behavior.

---

## 6. Thread Safety and Async Flow

**PASS (consistent with codebase).** All credential iteration happens on the UI dispatcher thread via `.ObserveOnCurrentDispatcher()`. The recursive-style `TryNextCredential` → subscribe → error → `TryNextCredential(index+1)` chain executes serially because each `Subscribe` callback runs on the dispatcher. There are no concurrent mutations to `_credentialCache` or `_deviceFactories`.

**Potential concern — multiple devices iterating concurrently:** During `LoadDevices`, `SessionProcess` is called for each discovered device in the `OnNodeLoaded` callback. Multiple devices may have in-flight credential attempts simultaneously. This is safe because:

- Each call chain operates on its own `DeviceDescriptionHolder` and its own `index` / `credentials` list (captured by closure)
- `_credentialCache` mutations are all on the dispatcher thread (single-threaded)
- `_deviceFactories` uses `DeviceDescriptionHolder` as key — different devices, different keys

**No deadlock risk.** The Rx `ObserveOnCurrentDispatcher` pattern posts to the WPF dispatcher queue. Recursive calls to `TryNextCredential` from error handlers are dispatched as new work items, not synchronous re-entrant calls. Stack overflow from deep recursion is not possible because the chain is async.

---

## 7. Code Duplication — Discovered vs. Manual Paths

**NOTE (non-blocking).** The discovered-device path (`TrySessionWithCredentials` → `FullCredentialIteration` → `TryNextCredential`) and the manual-device path (`ManualSessionProcess` → `FullCredentialIterationManual` → `TryNextCredentialManual`) are structurally identical with minor differences:

| Aspect | Discovered | Manual |
|--------|-----------|--------|
| Success handler | `InitDeviceHolder(session, devHolder, publishEvent)` | `ManualInitDeviceHolder(session, devHolder)` |
| Exhaustion fallback | Last URI only | All URIs |
| Extra parameter | `bool publishEvent` | (none) |

This duplication is acceptable for Phase 2 — the two paths have different success handlers and exhaustion semantics that justify separate methods. If Phase 3 or 4 adds more iteration complexity, consider extracting a shared iteration core that accepts a success/failure callback pair. Not blocking.

---

## 8. `_deviceFactories` Dictionary — Lifecycle

**PASS.** `_deviceFactories` maps `DeviceDescriptionHolder` → `NvtSessionFactory` so that when a device is later selected, the correct per-device factory (with the working credential) is published in `DeviceSelectedEvent`.

- **Population:** Set in every credential success handler (both discovered and manual paths)
- **Consumption:** `DeviceSelectedPublish` (line 591) and `OnSelectedDeviceChanged` (line 652) both look up the device's factory, falling back to the class-level `sessionFactory` if not found
- **Clearing:** `_deviceFactories.Clear()` in `Refresh()` (line 364) — correct, since `Refresh` recreates all `DeviceDescriptionHolder` objects, invalidating the old keys
- **Not cleared on cache eviction:** Correct — factory invalidation is tied to device recreation (Refresh), not credential cache eviction

**Object equality for dictionary keys:** `DeviceDescriptionHolder` does not override `Equals`/`GetHashCode`, so the dictionary uses reference equality. This is correct because the same holder instance is used throughout a device's lifecycle within a single Refresh cycle.

---

## 9. Build Verification

**PASS.** Task 2.V (commit 1529e39) reports `msbuild odm.sln -t:Rebuild` completed with 0 errors. Only pre-existing warnings (49 F# indentation + 1 vdproj unsupported). No new warnings introduced by Phase 2 changes.

---

## 10. Phase 1 Regression Check

**PASS.** Phase 2 commits made no changes to:

- `CredentialStore.cs` — unchanged since Task 1.1 (commit d788cd2)
- `AccountManager.cs` — unchanged since Task 1.2 (commit 4bf054b)
- `odm.ui.views.csproj` — unchanged since Task 1.1

All Phase 1 review findings remain valid:
- DPAPI encryption intact
- Atomic write pattern intact
- Legacy migration intact
- `SetCurrentAccount` upsert with `OrdinalIgnoreCase` intact
- Backward compatibility for `CurrentAccount` consumers intact

---

## 11. Plan Alignment

**Task 2.1 (credential iteration): PASS.** All plan requirements met:
- `Refresh()` / `LoadDevices()` flow modified to iterate credentials per device
- `TrySessionWithCredentials` extracted as the entry point
- Iteration order: each stored credential → anonymous fallback
- `_deviceFactories` tracks per-device factory for correct credential propagation on device selection
- `ManualSessionProcess` updated with same iteration pattern

**Task 2.2 (credential cache): PASS.** All plan requirements met:
- `Dictionary<string, Account> _credentialCache` keyed on device host (case-insensitive)
- Cache consulted before full iteration
- Cache evicted on failure, re-populated on success
- Not persisted — instance field only
- Cache survives Refresh; `_deviceFactories` cleared on Refresh

**Task 2.V (verify): PASS.** Build clean, 0 errors.

---

## Summary

**Phase 2 is approved.** All three commits (2.1 credential iteration, 2.2 credential cache, 2.V verify) are clean, correct, and aligned with the plan.

**What passed:**
- Iteration order is correct: cache → stored credentials in order → anonymous fallback
- Error handling correctly treats all failures as "try next" (per Task 1.0 spike)
- Cache key uses `DnsSafeHost` with case-insensitive comparison — correct for hostname matching
- Cache eviction on failure + re-population on success handles password-change scenario (R6)
- Cache is not persisted — in-memory only, survives Refresh, dies with app
- Single-credential behavior unchanged from pre-Phase-2
- No thread safety issues — all mutations on dispatcher thread
- `_deviceFactories` lifecycle is correct (populated on success, cleared on Refresh, consumed on device selection)
- Phase 1 code untouched — no regression
- Build is clean (0 errors)

**Notes for future phases (not blocking):**
1. Discovered/manual credential iteration paths are structurally duplicated — consider extracting a shared core if Phase 3/4 adds more iteration complexity
2. `Account.Equals` case-sensitivity note from Phase 1 review still applies — Phase 3 UI code must use explicit `OrdinalIgnoreCase` for username matching, not `Account.Equals`
3. Multiple devices behind NAT sharing a cache key is an accepted edge case — if this causes issues in practice, the cache key could be extended to include port
