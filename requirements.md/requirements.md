# ODM Sprint 2 — Credentials UX Fixes

## Context

Sprint 1 delivered the credentials subsystem (CredentialStore, multi-credential iteration, CredentialManagerView, TogglePasswordBox). Post-merge testing revealed 2 functional bugs and 3 UX issues that block usability. All fixes are in existing files on `feat/credentials-ui`.

**Branch:** `feat/credentials-ui` (continue on same branch — PR #5 is open)
**Base branch:** `development`

---

## BUG-1: Username deduplication blocks legitimate entries

**File:** `odm/odm.ui.views/views/CredentialManagerView.xaml.cs`

`CredentialManagerView` deduplicates entries by username only (case-insensitive OrdinalIgnoreCase). This blocks adding `admin / password1` and `admin / password2` as separate entries — the second silently overwrites the first. This is the most common camera scenario (same default username, multiple firmware passwords).

**Fix:** Remove username-only dedup. The deduplication key must be the full `(username, password)` pair. If an exact `(username, password)` pair already exists, skip the duplicate silently. Otherwise always add.

---

## BUG-2: Credentials from Manage Credentials are not used during camera connection

**Files:** `odm/odm.ui.views/viewmodels/DeviceListViewModel.cs`, `odm/odm.ui.views/core/CredentialStore.cs`, `odm/odm.ui.views/core/AccountManager.cs`

Credentials saved via `CredentialManagerView` are not being applied when connecting to a camera. The credential iteration loop in `DeviceListViewModel.TrySessionWithCredentials()` is not reaching the stored credentials, or the store is not returning them at connect time.

**Investigation required:** Trace the call path from camera connect through `TrySessionWithCredentials()` → `CredentialStore.GetAllCredentials()`. Identify where the chain breaks. Fix so that all credentials in the store are tried in order before failing.

This is the highest-priority bug — without it, the entire credentials feature is non-functional.

---

## UX-1: Manage Credentials list UX redesign

**Files:** `odm/odm.ui.views/views/CredentialManagerView.xaml`, `odm/odm.ui.views/views/CredentialManagerView.xaml.cs`

Current UX problems:
- Adding a new row requires clicking the implicit blank row at the bottom of the DataGrid (`CanUserAddRows=true`) — this affordance is non-obvious to users.
- Removing a row requires selecting it and clicking a "Remove" button at the bottom of the window.

**Required redesign:**
1. Remove the bottom "Remove" button entirely.
2. Add an **×** button as a `DataGridTemplateColumn` — the last column in each row. Clicking it removes that row immediately.
3. Handle the **Delete key**: when a row is selected and Delete is pressed, remove it (KeyDown handler on the DataGrid).
4. Add an explicit **"+ Add"** button below the grid (or in a toolbar) that appends a new blank row and sets focus to the username cell of the new row.
5. Set `CanUserAddRows=false` on the DataGrid — the implicit blank row is gone; the Add button is the only add affordance.

---

## UX-2: Login button blocked when Name/Password empty even if CredentialStore has entries

**Files:** `odm/odm.ui.views/views/AuthView.xaml`, `odm/odm.ui.views/views/AuthView.xaml.cs` (or relevant ViewModel)

Currently the Login/Connect button is only enabled when the Name and Password fields are non-empty. This blocks connecting when the user has credentials stored in `CredentialStore` but left the fields blank (expecting the store to be used).

**Required behaviour:**
- Login button is enabled if **either**: (a) both Name and Password are non-empty, **or** (b) `CredentialStore.GetAllCredentials()` returns at least one entry.
- When both Name and Password are filled and Login is clicked: treat this as a "quick try" — attempt that credential, and if successful, optionally offer to save it to the store (a simple MessageBox "Save this credential?" is acceptable).
- When Name/Password are empty and Login is clicked: skip the Name/Password credential entirely, go straight to iterating the store.
- If both Name/Password are empty and CredentialStore is empty: keep the existing block.

---

## UX-3: Inconsistent eye icon — two password show/hide implementations

**Files:** `odm/odm.ui.views/views/CredentialManagerView.xaml`, `odm/odm.ui.views/views/CredentialManagerView.xaml.cs`

`AuthView` uses `TogglePasswordBox` (the custom UserControl from Sprint 1 with a proper eye-icon toggle button). `CredentialManagerView` has its own inline show/hide implementation (separate PasswordBox + TextBox, custom toggle logic).

**Fix:** Replace the inline implementation in `CredentialManagerView` with `TogglePasswordBox`. The DataGrid password column must use `TogglePasswordBox` so the eye icon, toggle behaviour, and styling match `AuthView` exactly.

Note: `TogglePasswordBox` may need minor adjustments to work inside a DataGrid cell — ensure the `Password` dependency property two-way binds correctly within a `DataGridTemplateColumn`.

---

## Out of scope

- IEFrame → WebView2 migration (separate issue #6, different subsystem)
- NAT edge case in credential cache (low risk, document if it surfaces)
- Persistence of per-device credential cache (intentionally in-memory only)
