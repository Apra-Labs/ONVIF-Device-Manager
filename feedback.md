# ODM Credentials UI — Phase 1 Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-07 15:30:00+00:00
**Verdict:** APPROVED

> See the recent git history of this file to understand the context of this review.

---

## 1. Account.Equals and GetHashCode (Task 1.1, commit 80dac2b)

`Account.Equals` now compares both `Name` and `Password` (`AccountManager.cs:29`):

```csharp
return this.Name == another.Name && this.Password == another.Password;
```

This directly fixes the root cause shared by BUG-1 and BUG-2. Previously, setting `CurrentAccount` to `admin/newpass` when the current was `admin/oldpass` short-circuited — the setter's `if (_currentAccount == value) return` treated them as equal, so `CurrentAccountChanged` never fired and devices never refreshed.

`GetHashCode` (`AccountManager.cs:43-46`) correctly combines both fields using the standard `unchecked { (Name.GetHashCode() * 397) ^ Password.GetHashCode() }` pattern. Both `Name` and `Password` properties null-coalesce to `string.Empty`, so there is no `NullReferenceException` risk in the hash computation.

`IsAnonymous` (`AccountManager.cs:21`) checks `Anonymous.Equals(this)` where `Anonymous` has empty Name and empty Password. With the new equality both fields must match, so `IsAnonymous` remains correct — no regression.

The `==` and `!=` operators delegate to `Equals`, so all equality paths are covered.

**Done criteria check:** "Two accounts with same name but different password are NOT equal. `CurrentAccount` setter fires change events when password changes." Both satisfied. **PASS.**

---

## 2. SetCurrentAccount dedup logic (Task 1.2, commit 31bee48)

`SetCurrentAccount` (`AccountManager.cs:108-129`) was rewritten from name-only lookup-and-overwrite to exact (name, password) pair matching:

- Old behavior: found existing entry by name → overwrote its password. This collapsed `admin/pass1` and `admin/pass2` into a single entry.
- New behavior: scans for exact `(name, password)` match. If found (`exactMatch = true`), skips the add. If not found — even if the same name exists with a different password — adds as a new entry.

The name comparison uses `StringComparison.OrdinalIgnoreCase` while `Account.Equals` uses case-sensitive `==`. This asymmetry is intentional and correct: store-level dedup is case-insensitive for usability (a user typing "Admin" shouldn't create a duplicate of "admin/pass1"), while identity comparison (`Account.Equals`) is exact for correctness in the `CurrentAccount` setter's change detection.

**NOTE:** Password comparison in the dedup loop uses exact `==`, matching `Account.Equals` behavior. This is correct — passwords are case-sensitive.

**Done criteria check:** "Multiple credentials with same username coexist. Login with remember adds new pairs without overwriting." Satisfied. **PASS.**

---

## 3. AuthView.btLogin_Click simplification (Task 1.2, commit 31bee48)

The 30-line block in `btLogin_Click` (`AuthView.xaml.cs:118-147` old) that prompted "Update the stored password?" has been completely removed. The method now simply calls `SetCurrentAccount` and publishes a `Refresh` event (`AuthView.xaml.cs:119-121`).

This removal is correct because:
1. The "overwrite" prompt was the UI-facing manifestation of name-only dedup — with (name, password) pair matching, there is no overwrite scenario to prompt for.
2. The dedup responsibility is now centralized in `SetCurrentAccount`, eliminating the duplicated store-scanning logic that existed in both `SetCurrentAccount` and `btLogin_Click`.
3. The plan explicitly called for this removal, and the plan review (§2) noted that Task 3.1 will later rebuild `btLogin_Click` with new gating logic — so keeping it minimal now is the right call.

**Done criteria check:** Prompt removed; dedup is centralized. **PASS.**

---

## 4. Build verification (Task 1.V, commit 100fa9d)

`msbuild odm.sln -p:Configuration=Debug` produces 0 errors. All four output assemblies built successfully:
- `odm.ui.views.dll`
- `odm.ui.activities.dll`
- `odm.extensibility.dll`
- `odm.exe`

Warnings are all pre-existing (CS0108 hides-member, CS0105 duplicate-using, CS0169 unused-field, MSB3270 arch-mismatch, etc.). No new warnings introduced by Phase 1 changes. **PASS.**

---

## 5. Requirements alignment

| Requirement | Phase 1 Coverage | Verdict |
|-------------|-----------------|---------|
| **BUG-1:** Dedup key must be (username, password) pair | `SetCurrentAccount` matches on both fields; prompt removal eliminates the UI-level name-only check | **PASS** |
| **BUG-2:** Stored credentials must be tried during camera connect | `Account.Equals` fix unblocks `CurrentAccount` setter → `CurrentAccountChanged` fires → device refresh propagates | **PASS** |

**Plan review note follow-up:** The plan review (§11) flagged that BUG-2 may have a second root cause beyond `Account.Equals`. The 1.V commit notes acknowledge that "end-to-end manual test required: add credential in CredentialManagerView -> connect -> verify credential is tried." This is the correct response — Phase 1 fixes the identified root cause and defers end-to-end validation to manual testing before Phase 2 begins.

---

## 6. Regression check

No previously approved phases exist in Sprint 2 (this is the first code phase). Sprint 1 code was not modified — all changes are confined to `AccountManager.cs` and `AuthView.xaml.cs`. The `CredentialStore`, `CredentialManagerView`, `TogglePasswordBox`, and `DeviceListViewModel` are untouched. **PASS.**

---

## 7. progress.json

Tasks 1.1, 1.2, and 1.V are all marked `"completed"` with accurate notes and commit SHAs. Remaining tasks (2.1 through 4.V) remain `"pending"`. **PASS.**

---

## Summary

Phase 1 is clean and correct. The `Account.Equals` fix is the minimal, precise change needed to unblock both BUG-1 and BUG-2. `GetHashCode` is consistent with `Equals`. The `SetCurrentAccount` rewrite correctly transitions from name-only overwrite to (name, password) pair dedup. The `btLogin_Click` prompt removal eliminates duplicated logic and leaves the method in a clean state for Phase 3's rebuild. Build passes with 0 errors and no new warnings.

**No changes required. Phase 1 is approved for implementation of Phase 2.**
