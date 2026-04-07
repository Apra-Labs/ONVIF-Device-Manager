# Sprint 2 Plan — Credentials UX Fixes

**Branch:** `feat/credentials-ui` (base: `development`)
**Items:** BUG-1, BUG-2, UX-1, UX-2, UX-3

---

## Root Cause Analysis

### BUG-2 & BUG-1 share a root cause: `Account.Equals()` compares only `Name`

`Account.Equals()` (AccountManager.cs:23–30) ignores `Password`:
```csharp
return this.Name == another.Name;  // Password is NOT compared
```

This breaks multiple things:
1. **BUG-2 — Credentials not used:** `AccountManager.CurrentAccount` setter (line 67) uses `==` which calls this broken `Equals`. Setting `admin/newpass` when current is `admin/oldpass` short-circuits — the setter returns early, `CurrentAccountChanged` never fires, no refresh propagates to devices. Stored credentials are silently collapsed: `SetCurrentAccount` finds existing entry by name and overwrites password (line 111–123).
2. **BUG-1 — Dedup blocks entries:** Same name-only dedup in `SetCurrentAccount` and in `AuthView.btLogin_Click` (line 121–146) prevents storing `admin/pass1` and `admin/pass2` as separate entries.

### UX-3 — Already resolved in Sprint 1
`CredentialManagerView.xaml` line 48 already uses `<l:TogglePasswordBox>` in the CellEditingTemplate. The display template shows bullets (standard DataGrid behavior). No separate PasswordBox + TextBox inline implementation exists. Task 4.1 is verification-only.

---

## Phase 1 — Fix Account equality & credential dedup (BUG-2 + BUG-1)

### Task 1.1 — Fix Account.Equals to compare (Name, Password)
- **File:** `odm/odm.ui.views/core/AccountManager.cs`
- **Change:** `Account.Equals()` → compare both `Name` AND `Password`. `GetHashCode()` → combine both fields. `IsAnonymous` still works correctly (empty name + empty password = Anonymous).
- **Done:** Two accounts with same name but different password are NOT equal. `CurrentAccount` setter fires change events when password changes.
- **Tier:** cheap

### Task 1.2 — Fix SetCurrentAccount and AuthView dedup logic
- **File:** `odm/odm.ui.views/core/AccountManager.cs`, `odm/odm.ui.views/views/AuthView.xaml.cs`
- **Change in AccountManager.SetCurrentAccount (lines 111–123):** Match on both `Name` AND `Password` (case-insensitive name, exact password). If exact (name, password) pair exists → skip (already stored). If name matches but password differs → ADD new entry, don't overwrite. This allows `admin/pass1` and `admin/pass2` to coexist.
- **Change in AuthView.btLogin_Click (lines 121–146):** Remove the "Update the stored password?" prompt and name-only dedup. Instead: if the exact (name, password) pair is already in the store, do nothing extra. If not, add it (when "Remember" is checked).
- **Done:** Multiple credentials with same username coexist. Login with remember adds new pairs without overwriting.
- **Tier:** standard

### Task 1.V — Verify Phase 1
- Build with `msbuild`. Confirm: no errors, `Account.Equals` compares both fields, `SetCurrentAccount` preserves distinct password entries.

---

## Phase 2 — Credential Manager list UX redesign (UX-1)

### Task 2.1 — Add × delete column, Delete key, explicit Add button
- **Files:** `odm/odm.ui.views/views/CredentialManagerView.xaml`, `odm/odm.ui.views/views/CredentialManagerView.xaml.cs`
- **XAML:**
  1. Set `CanUserAddRows="False"` on DataGrid
  2. Add `DataGridTemplateColumn` (last column) with × Button (~30px wide), `Click` bound to remove handler
  3. Remove `btRemove` from bottom StackPanel
  4. Add `"+ Add"` Button in bottom StackPanel (before Move Up)
- **Code-behind:**
  1. × button handler: get `CredentialItem` from button's `DataContext`, remove from `_items`, call `SaveAndRefresh()`
  2. `credGrid.KeyDown` handler: if `Key.Delete` + selected item → remove + `SaveAndRefresh()`
  3. "+ Add" handler: append new `CredentialItem()` to `_items`, select it, begin edit on username cell
  4. Remove `BtRemove_Click` and its wiring
- **Done:** × per row, Delete key works, explicit Add button, no implicit blank row.
- **Tier:** standard

### Task 2.V — Verify Phase 2
- Build with `msbuild`. Confirm XAML and code-behind compile, no missing handlers.

---

## Phase 3 — Login button gating (UX-2)

### Task 3.1 — Enable Login when store has entries, even if fields empty
- **File:** `odm/odm.ui.views/views/AuthView.xaml.cs`
- **Change to `btLogin_Click()`:**
  1. If both `username.Text` and `password.Password` non-empty → existing behavior: try that credential, trigger Refresh. Additionally, if "Remember" is checked and credential is NOT already in the store, show MessageBox "Save this credential?" — if Yes, add to store.
  2. If fields are empty but `CredentialStore.Instance.GetAll().Count > 0` → skip the manual credential, just trigger Refresh (devices will iterate stored credentials via `FullCredentialIteration`).
  3. If fields empty AND store empty → show info message, do nothing.
- **Change to `Init()` or add helper:** Update Login button `IsEnabled` — either replace `DelegateCommand` with `DelegateCommand` that has a `canExecute` delegate checking `(fields non-empty) || (store non-empty)`, OR add a simple check at the top of `btLogin_Click` and disable the button via binding. The `DelegateCommand.CanExecute` approach is cleanest.
- **Done:** Login button enabled when store has entries. Empty-field login goes straight to store iteration.
- **Tier:** standard

### Task 3.V — Verify Phase 3
- Build with `msbuild`. Confirm `btLogin_Click` logic covers all 3 scenarios.

---

## Phase 4 — Verify TogglePasswordBox in DataGrid (UX-3)

### Task 4.1 — Verify TogglePasswordBox binding in CredentialManagerView
- **File:** `odm/odm.ui.views/views/CredentialManagerView.xaml`
- **Status:** Sprint 1 already replaced inline show/hide with `<l:TogglePasswordBox>` in CellEditingTemplate (line 48). Display template shows bullets. This is consistent with AuthView.
- **Action:** Verify two-way binding works inside DataGrid editing template. If `Password` DP doesn't propagate edits back to `CredentialItem.Password` (e.g., DataGrid commits edit before TogglePasswordBox updates binding), add `UpdateSourceTrigger=LostFocus` or handle `CellEditEnding` to force sync.
- **Done:** Password column uses TogglePasswordBox, eye icon matches AuthView, edits propagate correctly.
- **Tier:** cheap

### Task 4.V — Verify Phase 4
- Build with `msbuild`. Full build clean, 0 errors. All 5 items addressed.

---

## Risk Register

| # | Risk | Mitigation |
|---|------|------------|
| 1 | `Account.Equals` change may break code relying on name-only equality | Grep all `==`/`!=`/`Equals` on Account. The only callers are `IsAnonymous`, `CurrentAccount` setter, and `SetCurrentAccount` — all benefit from the fix. |
| 2 | DataGrid row editing + TogglePasswordBox binding timing | CellEditingTemplate with `UpdateSourceTrigger=PropertyChanged` should work. Fallback: force sync in `RowEditEnding`. |
| 3 | Empty-field login with store iteration — UX confusion | Clear visual indication (tooltip or label) that stored credentials will be tried automatically. |
