# ODM Credentials UI — Phase 1 Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-07 04:55:00+00:00
**Verdict:** APPROVED

> See the recent git history of this file to understand the context of this review.

---

## 1. CredentialStore — DPAPI Encryption and Persistence

**PASS.** DPAPI usage is correct. `ProtectedData.Protect` and `Unprotect` are called with `DataProtectionScope.CurrentUser` and no additional entropy (`null`), which is the standard pattern for user-scoped encryption. The `System.Security` assembly reference was added to `odm.ui.views.csproj`. Credentials are serialized via `XmlSerializer` into XML, then encrypted as a byte blob to `credentials.dat` — at no point are credentials written to disk in plaintext.

**Atomic write (R4 mitigation): PASS.** `SaveInternal` writes to `_storePath + ".tmp"`, then deletes the existing file and moves the temp file into place. This prevents a crash mid-write from corrupting the store. There is a tiny window between `File.Delete` and `File.Move` where the file doesn't exist, but this is standard practice on Windows and acceptable — `File.Replace` would be marginally more atomic but has its own quirks on .NET 4.0. No data loss path exists under normal operation.

**Migration from `account.def.xml` (R4): PASS.** The migration path in `Load()` is correctly sequenced: deserialize legacy XML, build the migrated list, call `SaveInternal` to write the encrypted store, and only then `File.Delete` the legacy file. If `SaveInternal` throws, the catch block prevents the delete — the old file survives and migration retries on next launch. If the app crashes after `SaveInternal` but before `File.Delete`, the old file persists and migration re-runs, harmlessly overwriting the already-migrated encrypted store with identical content.

**Fallthrough behavior: NOTE.** If `credentials.dat` exists but is corrupt (decryption fails), the code falls through to attempt legacy migration. If no legacy file exists either, the result is an empty credential list. This is acceptable given the atomic write pattern makes corruption very unlikely, and silently returning empty is better than crashing. The `dbg.Error` call ensures the exception is logged.

---

## 2. CredentialStore — API Design and Singleton Pattern

**PASS.** The singleton pattern (`static readonly _instance`, private constructor) matches the existing `AccountManager` pattern exactly. The public API — `GetAll()`, `Add()`, `Remove(int)`, `Update(int, Account)`, `SetAll()` — matches the plan specification. Index-based `Remove` and `Update` sidestep the `Account.Equals` case-sensitivity trap (noted in prior plan reviews).

`GetAll()` returns `_credentials.AsReadOnly()`, which provides a read-only wrapper. The return type was changed from `IReadOnlyList<Account>` to `IList<Account>` in the verify commit (3474e8b) for .NET 4.0 compatibility — `IReadOnlyList<T>` was introduced in .NET 4.5. The `ReadOnlyCollection<T>` returned by `AsReadOnly()` still prevents mutation; the `IList<Account>` return type is a compile-time concession, not a runtime one.

**Thread safety: PASS (consistent with codebase).** `_credentials` is mutated without locking. The original `AccountManager` had no thread safety either. The WPF app is single-threaded (UI thread), so this is consistent with existing conventions. No change needed.

**Nested `CredentialList` class: PASS.** The `CredentialList` wrapper class is `public` — required by `XmlSerializer` in .NET 4.0 which cannot serialize non-public types. It serves only as a serialization container and is appropriately scoped inside `CredentialStore`.

---

## 3. AccountManager — Backward Compatibility

**PASS.** The `CurrentAccount` property, `CurrentAccountChanged` event, `Autorized` property (note: pre-existing typo, not introduced here), and `SetCurrentAccount(Account, bool)` method signature are all preserved. Existing callers in `ToolBarViewModel.cs:62` and `ToolBarView.xaml.cs:111` that compare `CurrentAccount == anonymous` continue to work unchanged.

The constructor now reads from `CredentialStore.Instance.GetAll()` instead of the old `Load()` method, selecting `all[0]` as `CurrentAccount` or falling back to `Account.Anonymous`. This preserves the original behavior: on startup, the app loads the saved credential (now the first in the encrypted list) as the active account.

**Behavioral change in `SetCurrentAccount` with `remember=false`: NOTE.** The original code called `Save(Account.Anonymous)` when `remember=false`, which cleared the stored credential file. The new code does nothing to the store when `remember=false` — it only sets the in-memory `CurrentAccount`. This means unchecking "Remember me" no longer wipes previously stored credentials. This is the correct behavior for a multi-credential store (a single login action should not destroy the entire credential list), but it is a semantic change from the original. In practice this is harmless: the "Remember me" checkbox controlled a single credential; now it controls whether this particular credential is added/updated in the store. Existing users who relied on uncheck-to-forget will find their previously saved credentials still present, which is reasonable.

---

## 4. AccountManager — New Methods

**`GetAllCredentials()`: PASS.** Thin delegate to `CredentialStore.Instance.GetAll()`. Returns the same read-only view. Clean passthrough — no logic, no transformation.

**`SetCredentials(List<Account>)`: PASS.** Thin delegate to `CredentialStore.Instance.SetAll()`. Will be consumed by the credential management UI in Phase 3.

**`SetCurrentAccount` upsert logic: PASS.** When `remember=true` and the account is not anonymous, the method searches for an existing credential by username using `string.Equals` with `StringComparison.OrdinalIgnoreCase`, then either updates or adds. This matches the plan's deduplication rule ("case-insensitive comparison ... never create a duplicate username entry"). The `for` loop with index tracking is .NET 4.0 compatible (no LINQ `FindIndex`).

**Account.Equals case sensitivity mismatch: NOTE.** `Account.Equals` compares `Name` with `==` (case-sensitive, ordinal), but `SetCurrentAccount` deduplicates with `OrdinalIgnoreCase`. This means `Account.Equals("Admin") != Account.Equals("admin")` but the store treats them as the same user. This is actually correct — the store-level deduplication should be case-insensitive (usernames are typically case-insensitive), while `Account.Equals` is used for change detection in `CurrentAccount.set` (where exact match is fine). However, Phase 3 implementers should be aware that `Account` equality and store deduplication use different case rules. Not a bug, but worth tracking.

---

## 5. Code Style and Conventions

**PASS.** The new code follows existing codebase patterns:
- Singleton via `static readonly` + private constructor (matches `AccountManager`)
- `dbg.Error(err)` for exception logging (matches `AccountManager.Load/Save`)
- `XmlSerializer` usage for serialization (matches original `AccountManager`)
- `AppDefaults.ConfigFolderPath` for file paths (matches original `settingsPath`)
- No LINQ usage in `CredentialStore.cs` (the file doesn't import `System.Linq`; LINQ is available in the codebase but the new code uses explicit loops, which is consistent with the simpler helpers)
- `using utils;` for `dbg` class access (added in verify commit, consistent with other files in `core/`)

**Whitespace changes: PASS.** The BOM was removed from `AccountManager.cs` and trailing whitespace was cleaned up in a few places. These are minor formatting normalizations that don't affect behavior.

**No hardcoded secrets: PASS.** Searched all changed files — no credentials, keys, tokens, or sensitive values are hardcoded anywhere.

---

## 6. Plan Alignment

**Task 1.0 (spike): PASS.** Spike findings are documented in PLAN.md with the specific exception types (`FaultException`, `CommunicationException`) and the implication for Task 2.1. No code was written — correct for a read-only spike. Progress.json notes capture the key finding.

**Task 1.1 (CredentialStore): PASS.** All plan requirements met:
- `List<Account>` storage with `Account` struct reuse
- DPAPI encryption with `CurrentUser` scope to `credentials.dat`
- `Load()`, `Save()`, `Add()`, `Remove(int)`, `Update(int, Account)`, `GetAll()` methods present
- Singleton pattern
- Legacy migration from `account.def.xml`
- `System.Security.dll` reference added to csproj

**Task 1.2 (AccountManager update): PASS.** All plan requirements met:
- `GetAllCredentials()` delegates to `CredentialStore.Instance.GetAll()`
- `CurrentAccount` and `SetCurrentAccount` preserved for backward compat
- `SetCredentials(List<Account>)` added for bulk update
- Old `Save()`/`Load()` methods and `settingsPath` field removed
- Plan said `IReadOnlyList<Account>` but implementation uses `IList<Account>` — this was a necessary .NET 4.0 fix caught in Task 1.V. Acceptable deviation.

**Task 1.V (verify): PASS.** Build confirmed clean. Two .NET 4.0 compatibility fixes applied in the verify commit: `IReadOnlyList` → `IList`, and `using utils;` added. Both are correct fixes for legitimate build failures.

---

## 7. Build Verification

**PASS.** `msbuild odm.sln -p:Configuration=Release` completes with 0 errors. Warning count is consistent with pre-existing warnings (all in unrelated files: `SynesisAnalyticsConfigView`, `TimeSettingsView`, `ToolBarView`, etc.). No new warnings introduced by the Phase 1 changes.

---

## 8. Requirements Alignment

**PASS.** Phase 1 delivers the storage foundation for REQ-2:
- **"Credentials must be stored securely"** — DPAPI encryption, not plaintext. Met.
- **"Credentials persist across application restarts"** — `credentials.dat` written to disk, loaded on startup. Met.
- **"Credentials are not stored in plaintext"** — enforced by `ProtectedData.Protect`. Met.

The remaining REQ-2 acceptance criteria (UI for add/edit/delete, iteration on connect) are correctly deferred to Phases 2-3.

---

## Summary

**Phase 1 is approved.** All four commits (1.0 spike, 1.1 CredentialStore, 1.2 AccountManager, 1.V verify) are clean, well-structured, and aligned with the plan and requirements.

**What passed:**
- DPAPI encryption is correctly implemented with `CurrentUser` scope
- Atomic write pattern prevents data corruption (R4 mitigation)
- Legacy migration is safely sequenced (write-before-delete)
- Backward compatibility preserved — existing callers of `AccountManager.CurrentAccount` and `SetCurrentAccount` are unaffected
- No secrets hardcoded, no new warnings introduced
- Code style consistent with existing .NET 4.0 / WPF / singleton patterns
- Build is clean (0 errors)

**Notes for future phases (not blocking):**
1. `Account.Equals` is case-sensitive but store deduplication is case-insensitive — Phase 3 UI code should use explicit case-insensitive comparison for username matching, not rely on `Account.Equals`
2. `SetCurrentAccount(account, remember=false)` no longer clears the stored credential — this is correct for multi-credential but is a behavioral change from the original single-credential flow
3. The `IList<Account>` return type on `GetAll()`/`GetAllCredentials()` exposes mutating methods at compile time even though the runtime object is read-only — callers should treat it as read-only (enforced by `ReadOnlyCollection` at runtime)
