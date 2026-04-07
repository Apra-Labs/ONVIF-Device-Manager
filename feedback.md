# ODM Sprint 3 — Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-07 16:30
**Verdict:** APPROVED

---

## REQ-1: Username → Password tab order

- `AuthView.xaml`: `TogglePasswordBox` at `TabIndex="1"`, `KeyboardNavigation.TabNavigation="Local"` on `panelEdit`.
- `TogglePasswordBox.xaml`: `toggleBtn` has `Focusable="False"` and `IsTabStop="False"`.
- Tab from username lands on the password input, not the toggle button. **PASS**

---

## REQ-2: Login respects "Remember" checkbox — no dialogs

- `AuthView.xaml.cs`: `btLogin_Click` uses `LoginActionHelper.Determine()` to route into Case1/Case2/Case3.
- Case 1: `SetCurrentAccount(... remember: remember.IsChecked == true)` — checkbox value used, not hardcoded `true`. No dialog. **PASS**
- Case 2: connects silently with `stored[0]`. No dialog. **PASS**
- Case 3: shows single `MessageBox` with correct text. **PASS**

---

## REQ-3: "Manage Credentials" as always-visible Hyperlink

- `AuthView.xaml`: `<Hyperlink x:Name="lnkManageCredentials">` in `Grid.Row="1"`, outside both `panelEdit` and `panelView`. Always visible.
- `AuthView.xaml.cs`: `lnkManageCredentials.Click += BtManageCredentials_Click` opens `CredentialManagerView`. **PASS**

---

## REQ-4: Logout disconnects all cameras

- `AccountManager.cs`: `LoggedOutExplicitly` property added, default `false`. `SetCurrentAccount` resets to `false` for non-anonymous accounts.
- `AuthView.xaml.cs`: `btLogout_Click` sets `LoggedOutExplicitly = true` before `SetCurrentAccount(Anonymous, false)`.
- `DeviceListViewModel.cs`: `LoadCurrentAccount()` checks `!LoggedOutExplicitly` before falling back to stored credentials. When flag is `true`, returns `null`. **PASS**

---

## REQ-5: Keyboard navigation inside CredentialManagerView

- `CredentialManagerView.xaml.cs`: `CredGrid_PreviewKeyDown` handles Tab:
  - Col 0 (Username) → Col 1 (Password): commits edit, begins password edit, calls `FocusPasswordInput()`.
  - Col 1 (Password) → Col 2 (Delete): focuses delete button.
  - Col 2 (Delete) → next row Username or `btAdd` if no more rows.
- `TogglePasswordBox.cs`: `SelectAll()` and `FocusPasswordInput()` exposed. **PASS**

---

## REQ-6: Eye icon instead of text

- `TogglePasswordBox.xaml`: uses XAML `Path` geometry for both open eye and closed eye with strikethrough.
- No PNG, no text content. `Width="24"`, `ToolTip="Show/hide password"`.
- Visibility toggles via `DataTrigger` on `IsChecked`. **PASS**

---

## REQ-7: Delete button uses Delete16.png

- `CredentialManagerView.xaml`: `<Image Source="/odm.ui.views;component/images/Delete16.png" Width="14" Height="14"/>` in delete column.
- `odm.ui.views.csproj`: `<Resource Include="images\Delete16.png"/>` added.
- File exists at `odm/odm.ui.views/images/Delete16.png`. **PASS**

---

## REQ-8: OnClosing = logout + reconnect

- `CredentialManagerView.xaml.cs`: `OnClosing` override:
  - Sets `LoggedOutExplicitly = (storeCount == 0)` — correct: empty store = explicit logout, non-empty = allow fallback.
  - Calls `SetCurrentAccount(Anonymous, false)` — clears current account.
  - Publishes `Refresh(true)` — triggers device rescan.
- Logic verified: non-empty store → `LoggedOutExplicitly=false` → `LoadCurrentAccount` falls back to `stored[0]`. Empty store → `LoggedOutExplicitly=true` → `LoadCurrentAccount` returns `null`. **PASS**

---

## REQ-9: Startup auto-login with stored credentials

- `AuthView.xaml.cs`: `AuthView_Loaded` calls `Update()`, then checks `GetAllCredentials().Count > 0 && !LoggedOutExplicitly` → publishes `Refresh(true)`.
- Device discovery starts immediately on load when credentials exist. **PASS**

---

## REQ-10: No passwords in logs

- `AuthView.xaml.cs`: `Safe(Account)` returns `"name=..., pwd=[REDACTED]"`. Used in Case2 log line.
- `AuthLog` calls: line 92 (version only), line 150 (name + storeCount, no password), line 170 (uses `Safe()`).
- `dbg.Error` calls: only log `Exception` objects, never credentials.
- `CredentialStore.cs`: `RedactedSummary()` returns count only. `dbg.Error` calls log exceptions only.
- `AccountManager.cs`: `.Password` references are comparisons/assignments, not logged.
- Zero password values in any log statement. **PASS**

---

## Build & Tests

- **Release x64 build:** 0 errors (warnings are pre-existing, unrelated to sprint changes).
- **Test project build:** 0 errors.
- **Test run:** 39/39 passed — all 8 AuthFlowTests + all original tests.

---

## Version

- `AssemblyInfo.global.cs`: `2.2.252.2` set for `AssemblyVersion`, `AssemblyFileVersion`, and `AssemblyInformationalVersion`. **PASS**

---

## Summary

All 10 requirements verified against code. Build clean (0 errors). 39/39 tests pass. No passwords in log statements. No regressions found in previously approved work.

**APPROVED** — Sprint 3 complete, ready for merge.
