# ODM Credentials UI — Phase 3 Cumulative Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-07
**Scope:** Tasks 3.1 (CredentialManagerView DataGrid CRUD UI), 3.2 (AuthView integration), 3.V (verify build)
**Commits reviewed:** 084f5c0, a9120d7, 09ce16a
**Verdict:** APPROVED with 1 non-blocking finding

---

## 1. DataGrid — Editable Username + PasswordBox in DataGridTemplateColumn

**PASS.** `CredentialManagerView.xaml` correctly implements the plan:

- `DataGridTextColumn` for Username with `Binding="{Binding Name, UpdateSourceTrigger=PropertyChanged}"` — inline text editing
- `DataGridTemplateColumn` for Password with:
  - **CellTemplate:** `TextBlock` displaying bullet characters (●●●●●●) via Style setter, with a `DataTrigger` that shows empty string when `Password=""` — passwords are never displayed in plaintext in the non-editing view
  - **CellEditingTemplate:** `PasswordBox` with `PasswordBoxAssistant.BindPassword="True"` and `BoundPassword="{Binding Password, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"` — correctly reuses the existing `PasswordBoxAssistant` pattern from the codebase
- `CanUserAddRows="True"` — DataGrid shows a new-row placeholder for inline credential addition. No separate add dialog. `CredentialItem` has an implicit parameterless constructor, required by the DataGrid for creating new row instances. Correct.
- `CanUserDeleteRows="False"` — deletion is handled by the explicit Remove button with confirmation, not by the DataGrid's built-in delete. Correct per plan.
- `AutoGenerateColumns="False"`, `SelectionMode="Single"`, `SelectionUnit="FullRow"` — standard DataGrid configuration for a CRUD grid

The `CredentialItem` wrapper class implements `INotifyPropertyChanged` with proper change notification for both `Name` and `Password` properties. The null-coalescing getters (`_name ?? string.Empty`) are consistent with `Account` struct's pattern. `ToAccount()` provides clean conversion back to the domain type.

---

## 2. "Manage Credentials" Button — Child Window

**PASS.** `AuthView.xaml` line 98 adds a `Button x:Name="btManageCredentials"` in the `panelEdit` StackPanel, positioned after the "Remember me" checkbox with `KeyboardNavigation.TabIndex="4"`.

`AuthView.xaml.cs` wires the button in `Init()`:

```csharp
btManageCredentials.Click += BtManageCredentials_Click;
```

`BtManageCredentials_Click` (line 97-109):
- Creates `CredentialManagerView(eventAggregator)` — passes the Prism EventAggregator for Refresh event publishing
- Sets `win.Owner = Window.GetWindow(this)` — correct WPF pattern for getting the parent Window of a UserControl
- Calls `win.ShowDialog()` — modal child window, not a popup. Correct per plan.
- `try/catch` with `dbg.Error` — consistent with existing error handling pattern in AuthView

The `CredentialManagerView` XAML declares `WindowStartupLocation="CenterOwner"`, `ShowInTaskbar="False"`, `ResizeMode="CanResizeWithGrip"` — correct child window behavior: centered on parent, not shown independently in taskbar, resizable.

---

## 3. Quick-Login — Case-Insensitive Username Match

**PASS.** `AuthView.xaml.cs` `btLogin_Click()` (lines 111-157):

The deduplication logic when `doRemember && !string.IsNullOrEmpty(name)`:

```csharp
if (string.Equals(all[i].Name, name, StringComparison.OrdinalIgnoreCase))
```

This correctly uses `StringComparison.OrdinalIgnoreCase` — **NOT** `Account.Equals`, which uses case-sensitive `==` on `Name`. This addresses the Phase 2 review note (#2) about `Account.Equals` case sensitivity.

The flow is:
1. Iterate `CredentialStore.Instance.GetAll()`
2. If username matches (case-insensitive) **and** password differs → prompt "Update the stored password?"
   - User says **No** → `SetCurrentAccount(account, false)` + Refresh + return (credential used for session only, not persisted)
   - User says **Yes** → `break` out of loop, fall through to `SetCurrentAccount(account, doRemember)` which calls `CredentialStore.Update(existing, account)` — password updated in store
3. If username matches and password is the same → `break`, fall through to `SetCurrentAccount` which finds and "updates" the existing entry (no-op in effect)
4. If no match → loop completes, `SetCurrentAccount(account, doRemember)` calls `CredentialStore.Add(account)` — new entry added

All paths correctly publish `Refresh` event. **No duplicate username entries can be created.** The `AccountManager.SetCurrentAccount` (line 115 of `AccountManager.cs`) also uses `StringComparison.OrdinalIgnoreCase` — both layers are consistent.

---

## 4. Refresh Event After CredentialStore Changes

**PASS.** The `Refresh` event (`_eventAggregator.GetEvent<Refresh>().Publish(true)`) is published in all mutation paths:

- **CredentialManagerView:** `SaveAndRefresh()` is called after every mutation:
  - `CredGrid_RowEditEnding` (on row commit) — via `Dispatcher.BeginInvoke` to defer until after DataGrid binding completes
  - `BtMoveUp_Click` and `BtMoveDown_Click` — after `_items.Move()`
  - `BtRemove_Click` — after `_items.Remove()`
- **AuthView btLogin_Click:** Published on every exit path (line 140 for "No" response, line 151 for normal path)

The `Refresh` event triggers `DeviceListViewModel.Refresh()` which re-runs `LoadDevices()` → `TrySessionWithCredentials()` for each device. Devices will re-authenticate using the updated credential list. Correct.

The `Dispatcher.BeginInvoke` pattern in `CredGrid_RowEditEnding` is a well-known WPF technique for DataGrid — the event fires *before* the DataGrid commits the edit to the binding source, so deferring to `DispatcherPriority.Background` ensures `SaveAndRefresh` reads the committed values. Correct.

---

## 5. "Remember Me" Checkbox Semantics

**PASS (with note).** The plan states the checkbox "controls whether the entire credential store persists (or just the current session)." The implementation is slightly more nuanced:

- **Checked (`doRemember=true`):** The quick-login credential is upserted into `CredentialStore` via `AccountManager.SetCurrentAccount(account, true)`. Existing store entries are unaffected.
- **Unchecked (`doRemember=false`):** The credential is set as `CurrentAccount` for the current session only. The store is not modified — existing entries remain.

This interpretation is **more correct for a multi-credential store** than the literal plan text. Unchecking "remember me" should not wipe all stored credentials — it should only prevent *this particular login* from being saved. The implementation achieves this. The credential manager (accessed via "Manage Credentials") always persists directly, regardless of the checkbox state, which is the correct separation of concerns.

---

## 6. Move Up / Move Down Priority Reordering

**PASS with finding (F1, non-blocking).**

`BtMoveUp_Click` and `BtMoveDown_Click` use `ObservableCollection.Move()` to reorder entries, then call `SaveAndRefresh()` to persist the new order and trigger device re-authentication.

Boundary checks:
- **Move Up:** `if (idx <= 0) return` — prevents moving the first item up. Correct.
- **Move Down:** `if (idx < 0 || idx >= _items.Count - 1) return` — prevents moving the last item down or operating on no selection. Correct.

**F1 (non-blocking): Move Up on DataGrid new-item placeholder row.** When `CanUserAddRows=True`, the DataGrid appends a special "new item placeholder" row at the bottom. If the user selects this placeholder and clicks "Move Up", `credGrid.SelectedIndex` returns `_items.Count` (one past the last real item). The guard `idx <= 0` does not catch this, so `_items.Move(_items.Count, _items.Count - 1)` would throw `ArgumentOutOfRangeException`.

**Fix:** Add an upper-bound check to `BtMoveUp_Click`:
```csharp
if (idx <= 0 || idx >= _items.Count) return;
```

Move Down is already safe: `idx >= _items.Count - 1` returns early for the placeholder row. Remove is also safe: `credGrid.SelectedItem as CredentialItem` returns null for the placeholder.

**Severity:** Low — the placeholder row is visually distinct and users rarely select it without editing. The exception is caught by WPF's unhandled dispatcher exception handler, so it won't crash the app. But it's a trivial one-line fix.

---

## 7. Remove Button Confirmation

**PASS.** `BtRemove_Click` (line 120-136):

- Casts `credGrid.SelectedItem as CredentialItem` — returns null if no selection or if the new-row placeholder is selected. Safe.
- Shows `MessageBox.Show` with `MessageBoxButton.YesNo` and `MessageBoxImage.Question`
- Only removes on `MessageBoxResult.Yes`
- Calls `SaveAndRefresh()` after removal

The confirmation message includes the username: `"Remove credential for '{0}'?"` — helps the user identify which credential they're deleting.

---

## 8. Plaintext Passwords in Memory

**ACCEPTABLE.** Passwords exist as `System.String` in:

- `CredentialItem._password` — while the CredentialManagerView dialog is open
- `CredentialStore._credentials` list — for the application lifetime (same as the original `Account` struct in `AccountManager`)
- `Account.Password` property — same as pre-Phase-1 behavior

.NET 4.0's `SecureString` could theoretically reduce the attack surface, but WPF `PasswordBox` returns `string` from its `Password` property, WCF `NetworkCredential` takes `string`, and the `XmlSerializer` operates on `string`. Converting to `SecureString` at any single point would be security theater without end-to-end support. The existing approach (DPAPI encryption at rest, `string` in memory) is consistent with the codebase's threat model and .NET 4.0 constraints.

The `CredentialManagerView` does not persist `CredentialItem` objects beyond the dialog's lifetime — when the window closes, the `ObservableCollection<CredentialItem>` becomes eligible for GC. The canonical in-memory store is `CredentialStore._credentials`, which exists for the app lifetime regardless of the UI.

---

## 9. Build Verification

**PASS.** Task 3.V (commit 09ce16a) reports MSBuild Release build completed with 0 errors. Pre-existing warnings only (262 total — F# indentation warnings + vdproj unsupported). No new warnings introduced by Phase 3. The csproj correctly includes both `Compile` and `Page` entries for `CredentialManagerView`.

---

## 10. Phase 1 Regression Check

**PASS.** Phase 3 commits made no changes to:

- `CredentialStore.cs` — unchanged since Task 1.1 (commit d788cd2)
- `AccountManager.cs` — unchanged since Task 1.2 (commit 4bf054b)
- `odm.ui.views.csproj` — only additive changes (new Compile + Page entries for CredentialManagerView), no existing entries modified

All Phase 1 review findings remain valid:
- DPAPI encryption intact
- Atomic write pattern intact
- Legacy migration intact
- `SetCurrentAccount` upsert with `OrdinalIgnoreCase` intact

---

## 11. Phase 2 Regression Check

**PASS.** Phase 3 commits made no changes to:

- `DeviceListViewModel.cs` — unchanged since Task 2.2 (commit 004abad)

All Phase 2 review findings remain valid:
- `TrySessionWithCredentials` → cache → `FullCredentialIteration` → `TryNextCredential` chain intact
- Per-device credential cache with eviction intact
- Manual device iteration path intact
- `_deviceFactories` lifecycle intact

The Phase 3 `SaveAndRefresh()` correctly uses `AccountManager.Instance.SetCredentials(list)` which calls `CredentialStore.Instance.SetAll(credentials)` — this updates the store that `AccountManager.GetAllCredentials()` reads from, so the next `Refresh` cycle in `DeviceListViewModel` will pick up the new credential list. The integration is correct.

---

## 12. Plan Alignment

**Task 3.1 (CredentialManagerView): PASS.** All plan requirements met:
- DataGrid with editable Username (TextColumn) and masked Password (DataGridTemplateColumn with PasswordBox)
- `CanUserAddRows=True` for inline add — no separate dialog
- Inline editing via DataGrid's built-in cell editing
- Remove button with confirmation (MessageBox YesNo)
- Move Up / Move Down buttons for priority reordering
- All changes save immediately via `CredentialStore`
- After save, publishes `Refresh` event

**Task 3.2 (AuthView integration): PASS.** All plan requirements met:
- "Manage Credentials" button added to `panelEdit` in AuthView
- Opens `CredentialManagerView` as a child Window (ShowDialog with Owner set)
- Quick-login deduplication uses `StringComparison.OrdinalIgnoreCase` — not `Account.Equals`
- If username matches and password differs, prompts to update — no duplicate entries created
- "Remember me" controls persistence of the quick-login credential (reasonable interpretation for multi-credential store)

**Task 3.V (verify): PASS.** Build clean, 0 errors.

---

## Findings Summary

| # | Severity | Type | Description | Location |
|---|----------|------|-------------|----------|
| F1 | Low (non-blocking) | Bug | Move Up on DataGrid new-item placeholder row could throw `ArgumentOutOfRangeException`. Fix: add `idx >= _items.Count` guard. | `CredentialManagerView.xaml.cs:104` |

---

## Verdict

**Phase 3 is APPROVED.** All three commits (3.1 CredentialManagerView, 3.2 AuthView integration, 3.V verify) are clean, correct, and aligned with the plan.

F1 is non-blocking — it's a low-severity edge case with a trivial fix that can be addressed in Phase 4 or as a follow-up. It does not affect the core CRUD functionality or credential persistence.

**What passed:**
- DataGrid CRUD with inline editing, PasswordBox in template column, CanUserAddRows — correct per plan
- CredentialManagerView opens as modal child window with Owner set — correct
- Case-insensitive username matching uses `OrdinalIgnoreCase` throughout (AuthView + AccountManager) — addresses Phase 2 review note
- Deduplication prevents duplicate username entries — prompt on password mismatch, silent match on same password
- Refresh event published on every credential mutation — devices re-authenticate correctly
- Move Up/Down reordering with immediate persist and Refresh
- Remove with MessageBox confirmation
- "Remember me" semantics are sensible for a multi-credential store
- Password masking in view mode (bullet chars), PasswordBox in edit mode — no plaintext display
- Build clean (0 errors)
- No regression in Phase 1 (CredentialStore, AccountManager) or Phase 2 (credential iteration, cache)

**Notes for Phase 4:**
1. F1 fix (Move Up guard) — trivial, can be included in the Phase 4 commit
2. Phase 4 will replace the bare `PasswordBox` in both AuthView and CredentialManagerView with `TogglePasswordBox` — the DataGridTemplateColumn's CellEditingTemplate will need updating
