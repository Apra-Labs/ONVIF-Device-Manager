# ODM Credentials UI — Phase 2 Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-07 18:00:00+00:00
**Verdict:** APPROVED

> See the recent git history of this file to understand the context of this review.

---

## 1. CanUserAddRows=False (Task 2.1, commit cb0b66e)

`CredentialManagerView.xaml:19` now sets `CanUserAddRows="False"` on the DataGrid. The implicit blank row at the bottom of the grid is gone. The only add affordance is the explicit "+ Add" button (see section 4). This directly addresses UX-1 requirement #5. **PASS.**

---

## 2. x delete column (Task 2.1, commit cb0b66e)

A new `DataGridTemplateColumn` (`CredentialManagerView.xaml:53-62`) is appended as the last column with `Width="30"` and empty `Header=""`. Each row renders a `Button` with `Content="&#x00D7;"` (multiplication sign, renders as x), transparent background, no border. The button's `Click` handler is `BtDeleteRow_Click`.

Code-behind (`CredentialManagerView.xaml.cs:112-119`):
```csharp
void BtDeleteRow_Click(object sender, RoutedEventArgs e)
{
    var btn = sender as Button;
    if (btn == null) return;
    var item = btn.DataContext as CredentialItem;
    if (item == null) return;
    _items.Remove(item);
    SaveAndRefresh();
}
```

The handler retrieves the `CredentialItem` from the button's `DataContext` — this is the correct WPF pattern for template-column buttons. It removes the *specific row item*, not a fixed index. Null guards on both `btn` and `item` prevent crashes if `DataContext` is somehow wrong. `SaveAndRefresh()` persists immediately.

**Done criteria check:** "x per row" present. "Removes the correct row (not a fixed index)" — confirmed, uses `DataContext` binding. **PASS.**

---

## 3. Delete key handler (Task 2.1, commit cb0b66e)

`CredentialManagerView.xaml:26` wires `KeyDown="CredGrid_KeyDown"` on the DataGrid.

Code-behind (`CredentialManagerView.xaml.cs:122-133`):
```csharp
void CredGrid_KeyDown(object sender, KeyEventArgs e)
{
    if (e.Key == Key.Delete)
    {
        var item = credGrid.SelectedItem as CredentialItem;
        if (item != null)
        {
            _items.Remove(item);
            SaveAndRefresh();
            e.Handled = true;
        }
    }
}
```

Null selection is handled gracefully — `if (item != null)` guards the removal, so pressing Delete with no row selected does nothing. `e.Handled = true` prevents the DataGrid from processing the key further (which could interfere with edit mode). The `using System.Windows.Input` import was correctly added at line 7.

**Done criteria check:** "Delete key handler removes selected row and handles null selection gracefully." Both confirmed. **PASS.**

---

## 4. Add button (Task 2.1, commit cb0b66e)

The bottom button bar was restructured from a single `StackPanel` to a `Grid` (`CredentialManagerView.xaml:66-74`). The "+ Add" button is `HorizontalAlignment="Left"`, while Move Up / Move Down / Close are in a right-aligned `StackPanel`. This layout cleanly separates the add action from the reorder/close actions.

Code-behind (`CredentialManagerView.xaml.cs:102-110`):
```csharp
void BtAdd_Click(object sender, RoutedEventArgs e)
{
    var newItem = new CredentialItem();
    _items.Add(newItem);
    credGrid.SelectedItem = newItem;
    credGrid.ScrollIntoView(newItem);
    credGrid.CurrentCell = new DataGridCellInfo(newItem, credGrid.Columns[0]);
    credGrid.BeginEdit();
}
```

Creates a blank `CredentialItem`, appends to `_items`, selects it, scrolls into view, sets the current cell to column 0 (Username), and begins edit mode. This matches the plan spec: "append new blank row and set focus to username cell."

**NOTE:** `SaveAndRefresh()` is intentionally not called here — the new blank row has an empty `Name`, so `SaveAndRefresh` would skip it (see `SaveAndRefresh` line 84: `if (!string.IsNullOrEmpty(item.Name))`). The save happens when the user completes the row edit via `CredGrid_RowEditEnding`. This is correct behavior — no empty credentials are persisted to the store.

**Done criteria check:** "Add button appends blank row and focuses username cell." Confirmed. **PASS.**

---

## 5. Remove button eliminated (Task 2.1, commit cb0b66e)

The `btRemove` button was removed from XAML. The `BtRemove_Click` handler (which included a `MessageBox.Show` confirmation dialog) was removed from code-behind. The event wiring `btRemove.Click += BtRemove_Click` was removed from the constructor. No references to `btRemove` remain.

The confirmation dialog was intentionally dropped — the x column provides per-row immediate deletion, and the plan did not call for a confirmation prompt. This is appropriate for a credential list where undo is adding the credential back (low cost).

**PASS.**

---

## 6. Build verification (Task 2.V, commit f5aadfe)

The verify commit `f5aadfe` reports `msbuild odm.sln /p:Configuration=Debug` with 0 errors. I was unable to run msbuild independently due to sandbox restrictions on this review environment, but the verify commit's progress.json notes confirm: "0 errors, warnings only (pre-existing). All four output assemblies built."

**NOTE:** Build verification is based on the doer's verify commit rather than independent execution. This is acceptable given the sandbox constraint, and the diff shows no syntax issues or missing references.

**PASS (with caveat).**

---

## 7. Phase 1 regression check

`git diff 68e43f6..f5aadfe` shows zero changes to Phase 1 files:
- `AccountManager.cs` — untouched
- `AuthView.xaml.cs` — untouched

Phase 1's `Account.Equals` fix, `SetCurrentAccount` dedup logic, and `btLogin_Click` simplification are all intact. The only files modified in Phase 2 are `CredentialManagerView.xaml`, `CredentialManagerView.xaml.cs`, and `progress.json`. **PASS.**

---

## 8. Requirements alignment

| UX-1 Requirement | Implementation | Verdict |
|------------------|---------------|---------|
| Remove bottom "Remove" button | `btRemove` and handler deleted | **PASS** |
| Add x button as last DataGrid column | `DataGridTemplateColumn` with `BtDeleteRow_Click` via `DataContext` | **PASS** |
| Handle Delete key for row removal | `CredGrid_KeyDown` with null guard | **PASS** |
| Add explicit "+ Add" button | `btAdd` with `BtAdd_Click` — appends, selects, begins edit | **PASS** |
| Set `CanUserAddRows=False` | Set on DataGrid, line 19 | **PASS** |

---

## 9. progress.json

Tasks 2.1 and 2.V are marked `"completed"` with accurate notes and commit SHA `cb0b66e`. All Phase 1 tasks remain `"completed"`. Remaining tasks (3.1 through 4.V) are `"pending"`. **PASS.**

---

## Summary

Phase 2 is clean and correct. The CredentialManagerView UX redesign addresses all five UX-1 requirements: `CanUserAddRows=False` removes the non-obvious implicit blank row; the x column provides per-row immediate deletion using the correct `DataContext` pattern (not index-based); the Delete key handler gracefully handles null selection; the "+ Add" button appends a blank row and focuses the username cell for editing; and the old Remove button with its confirmation dialog is cleanly eliminated. No Phase 1 regressions. Build reported clean by the verify commit.

**No changes required. Phase 2 is approved for implementation of Phase 3.**
