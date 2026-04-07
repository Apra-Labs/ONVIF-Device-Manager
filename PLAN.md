# ODM Sprint 3 — Auth UX Polish — PLAN

**Branch:** `feat/credentials-ui`  **Base:** `main`  **Version:** `2.2.252.2`

---

## Phase 1 — Core Logic

### TASK-1 · AccountManager: LoggedOutExplicitly flag ✅ DONE
`odm/odm.ui.views/core/AccountManager.cs`

Add `public bool LoggedOutExplicitly` (default `false`).  
`SetCurrentAccount` with non-anonymous account resets it to `false`.

---

### TASK-2 · AuthView: logout flag + remember checkbox + hyperlink + tab + startup ✅ DONE
`AuthView.xaml`, `AuthView.xaml.cs`

- `btLogout_Click`: `LoggedOutExplicitly = true` before `SetCurrentAccount(Anonymous, false)`.
- `btLogin_Click` Case 1: `remember: remember.IsChecked == true`.
- Remove `btManageCredentials` Button; add always-visible `<Hyperlink x:Name="lnkManageCredentials">` in outer Grid row.
- `AuthView_Loaded`: if `GetAllCredentials().Count > 0 && !LoggedOutExplicitly` → publish `Refresh(true)`.
- Tab order: username=0, password=1, btLogin=2, remember=3. TogglePasswordBox toggle `IsTabStop="False"`.

---

### TASK-3 · DeviceListViewModel: respect LoggedOutExplicitly
`odm/odm.ui.views/viewmodels/DeviceListViewModel.cs`

```csharp
if (acc.IsAnonymous) {
    if (!AccountManager.Instance.LoggedOutExplicitly) {
        var all = AccountManager.Instance.GetAllCredentials();
        if (all.Count > 0) acc = all[0];
    }
}
```

Done when: change compiles, logout no longer uses stored fallback.

---

### VERIFY-1 · Phase 1 checkpoint — STOP FOR REVIEW

1. Full Release x64 build — zero errors:
   ```
   powershell -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' 'C:\akhil\git\ONVIF-Device-Manager\odm.sln' /p:Configuration=Release /p:Platform=x64 /v:minimal"
   ```
2. Build test project: `dotnet build 'C:\akhil\git\ONVIF-Device-Manager\odm\odm.tests\odm.tests.csproj' -v quiet`
3. Run tests — all 31 must pass:
   ```
   & "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "C:\akhil\git\ONVIF-Device-Manager\odm\odm.tests\bin\Debug\net48\odm.tests.dll"
   ```
4. Update `progress.json` with build/test results.
5. `git add -p && git commit && git push origin feat/credentials-ui`
6. **STOP — do not continue to Phase 2.**

---

## Phase 2 — UI Polish

### TASK-4 · TogglePasswordBox: vector eye icon + IsTabStop=False ✅ DONE
`controls/TogglePasswordBox.xaml`, `TogglePasswordBox.xaml.cs`

- ToggleButton: `Width="24"`, `Focusable="False"`, `IsTabStop="False"`.
- Content: XAML Path geometry — open eye (IsChecked=False) / closed eye with strikethrough (IsChecked=True).
- Added `SelectAll()` and `FocusPasswordInput()` public methods.

---

### TASK-5 · CredentialManagerView: Delete16.png + tab nav + close behavior
`CredentialManagerView.xaml`, `CredentialManagerView.xaml.cs`, `odm.ui.views.csproj`

#### 5a — Delete16.png ✅ DONE (XAML updated)
- Copy `libs/WPFToolkit.Extended-v1.5.0/CollectionEditors/Images/Delete16.png` → `odm/odm.ui.views/images/Delete16.png`.
- Add `<Resource Include="images\Delete16.png"/>` to csproj.
- Button in delete column uses `<Image Source="/odm.ui.views;component/images/Delete16.png" Width="14" Height="14"/>`.

#### 5b — Keyboard tab navigation
`PreviewKeyDown` on DataGrid:
- Username → Tab → Password cell (enter edit, call `FocusPasswordInput()`)
- Password → Tab → Delete button of same row
- Delete → Tab → Username of next row (or `btAdd` if no more rows)
- `e.Handled = true` to suppress default DataGrid tab behaviour.

#### 5c — OnClosing override
```csharp
protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
{
    base.OnClosing(e);
    var storeCount = CredentialStore.Instance.GetAll().Count;
    AccountManager.Instance.LoggedOutExplicitly = (storeCount == 0);
    AccountManager.Instance.SetCurrentAccount(Account.Anonymous, remember: false);
    _eventAggregator.GetEvent<Refresh>().Publish(true);
}
```

Done when: file copy done, csproj updated, tab nav works, closing triggers reconnect/logout.

---

### VERIFY-2 · Phase 2 checkpoint — STOP FOR REVIEW

1. Full Release x64 build — zero errors.
2. Build test project.
3. Run all tests — all must pass.
4. Update `progress.json`.
5. `git add -p && git commit && git push origin feat/credentials-ui`
6. **STOP — do not continue to Phase 3.**

---

## Phase 3 — Tests + Security + Version

### TASK-6 · Security audit: no passwords in logs
`AuthView.xaml.cs`, `AccountManager.cs`, `CredentialStore.cs`

- Add to `AuthView.xaml.cs`: `static string Safe(Account a) { return "name=" + a.Name + ", pwd=[REDACTED]"; }`
- Replace any log line concatenating `.Password` with `Safe(account)`.
- Add to `CredentialStore.cs`: `public string RedactedSummary() { return GetAll().Count + " credential(s) stored"; }`
- Zero hits from: `grep -n "\.Password" AuthView.xaml.cs AccountManager.cs CredentialStore.cs` in log contexts.

---

### TASK-7 · Unit tests: AuthFlowTests.cs
`odm/odm.tests/AuthFlowTests.cs` (new file)

8 tests using MSTest + reflection:

| Test | Assertion |
|------|-----------|
| `LoginCase1_RememberChecked_AddsToStore` | After SetCurrentAccount(remember:true), store has the entry |
| `LoginCase1_RememberUnchecked_DoesNotAddToStore` | After SetCurrentAccount(remember:false), store unchanged |
| `Logout_SetsLoggedOutExplicitly` | `LoggedOutExplicitly = true` after setting it |
| `Logout_LoggedOutExplicitly_BlocksStoredFallback` | When flag=true and store has entries, business logic returns no stored credential |
| `Login_AfterLogout_ClearsLoggedOutExplicitly` | `SetCurrentAccount(nonAnon)` resets flag to false |
| `CloseDialog_EmptyStore_SetsLoggedOutExplicitly` | If store empty → `LoggedOutExplicitly = true` |
| `CloseDialog_NonEmptyStore_ClearsLoggedOutExplicitly` | If store has entries → `LoggedOutExplicitly = false` |
| `Safe_Helper_RedactsPassword` | `Safe(account)` output contains "[REDACTED]", not the actual password |

All 31 existing tests must still pass. Total after: 39.

---

### TASK-8 · Version bump
`odm/~cfg/AssemblyInfo.global.cs`: change all three `2.2.252.1` → `2.2.252.2`.

---

### VERIFY-3 · Final checkpoint — STOP FOR REVIEW

1. Full Release x64 build — zero errors.
2. Build test project.
3. Run ALL tests — **39/39 must pass**.
4. Password audit: `grep -rn "\.Password" odm/odm.ui.views/core/AccountManager.cs odm/odm.ui.views/views/AuthView.xaml.cs odm/odm.ui.views/core/CredentialStore.cs` — must have zero hits in log statements.
5. Update `progress.json` with final results.
6. `git add -p && git commit && git push origin feat/credentials-ui`
7. **STOP — sprint complete pending PM/reviewer sign-off.**
