# ODM Sprint 3 — Auth UX Polish

## Scope

All work stays on `feat/credentials-ui`. These are UX and correctness fixes across
`AuthView`, `CredentialManagerView`, `TogglePasswordBox`, `AccountManager`, and
`DeviceListViewModel`. Every changed behaviour must have a unit test.

---

## REQ-1  Username → Password tab order (AuthView)

**Current:** Tab from the username field cycles inside `panelEdit` unpredictably (may
land on the TogglePasswordBox toggle button, not the password input).

**Required:** Tab from the username `TextBox` must move focus directly to the inner
`PasswordBox` of `TogglePasswordBox`. The Show/Hide toggle button inside
`TogglePasswordBox` must **not** be a tab stop (`Focusable="False"` already, verify
`IsTabStop="False"` is also set). Tab from the password field goes to the Login button.

**Files:** `AuthView.xaml`, `TogglePasswordBox.xaml`

---

## REQ-2  Enter / Login respects the "Remember" checkbox — no dialogs

**Current:** `btLogin_Click` Case 1 always calls `SetCurrentAccount(remember: true)`,
ignoring the checkbox. If both fields are empty and no stored creds exist (Case 3) it
shows a `MessageBox`.

**Required:**
- Case 1 (both fields filled): call `SetCurrentAccount(remember: remember.IsChecked == true)`.
  No dialog. No confirmation prompt. The checkbox is the answer.
- Case 2 (empty fields, store non-empty): connect silently using stored credentials.
  No dialog.
- Case 3 (empty fields, empty store): show a single concise `MessageBox` ("No credentials
  available. Enter a username and password, or add entries via Manage Credentials.").
  Keep this as-is; it is the only acceptable dialog in the login flow.

**Files:** `AuthView.xaml.cs`

---

## REQ-3  "Manage Credentials" as a hyperlink — always visible

**Current:** A `Button` labelled "Manage Credentials" sits inside `panelEdit`, so it
disappears once the user is logged in.

**Required:**
- Remove the `Button`.
- Add a `Hyperlink` labelled "Manage Credentials" that is **always visible** (lives
  outside `panelEdit` and `panelView`, directly in the outer `Grid`).
- Position: right-aligned in the toolbar row, or on a second line below the auth
  controls — whichever fits without clipping on small windows.
- Clicking it opens `CredentialManagerView` (same behaviour as before).

**Files:** `AuthView.xaml`, `AuthView.xaml.cs`

---

## REQ-4  Logout disconnects all cameras

**Current:** `btLogout_Click` sets `CurrentAccount = Anonymous` then publishes
`Refresh(true)`. `DeviceListViewModel.LoadCurrentAccount()` falls back to `stored[0]`
when `CurrentAccount.IsAnonymous`, so cameras stay connected with the first stored
credential.

**Required:** Introduce `AccountManager.LoggedOutExplicitly` (bool, default `false`).

- `btLogout_Click` → sets `LoggedOutExplicitly = true` before calling
  `SetCurrentAccount(Anonymous, remember: false)`.
- `SetCurrentAccount` with any non-anonymous account → resets `LoggedOutExplicitly = false`.
- `DeviceListViewModel.LoadCurrentAccount()` → if `acc.IsAnonymous &&
  AccountManager.Instance.LoggedOutExplicitly` return `null` (no fallback to store).
- After logout, `Refresh(true)` is published. Device list re-scans with `null`
  credentials → cameras appear as unauthorized/anonymous (they remain visible in the
  list but connections fail gracefully).

**Files:** `AccountManager.cs`, `AuthView.xaml.cs`, `DeviceListViewModel.cs`

---

## REQ-5  Keyboard navigation inside Manage Credentials (CredentialManagerView)

Full tab loop through each row, then the action buttons:

| Focus | Key | Result |
|-------|-----|--------|
| Username cell | Tab | Password cell for **same row** enters edit mode; all text selected |
| Password cell | Tab | "Show" toggle button of that row |
| Show button | Enter | Toggles password visibility |
| Show button | Tab | Delete (×) button of that row |
| Delete button | Enter | Deletes the row |
| Delete button | Tab | Username cell of **next row** (or first action button if no more rows) |
| Last action button | Tab | Wraps to Username of first row |

Implementation notes:
- Username and Password cells use `DataGridTextColumn` / `DataGridTemplateColumn` — the
  tab behaviour must be implemented by handling `PreviewKeyDown` on the `DataGrid` and
  moving focus programmatically.
- Password cell in view mode (bullets) must auto-enter edit mode when focused via Tab.
- "Select all" in the Password cell edit mode: set `textBox.SelectAll()` in
  `TogglePasswordBox` when it receives focus via tab (expose a `SelectAll()` method or
  handle `GotFocus`).

**Files:** `CredentialManagerView.xaml`, `CredentialManagerView.xaml.cs`,
`TogglePasswordBox.xaml.cs`

---

## REQ-6  Show/Hide toggle: eye icon instead of text

**Current:** `TogglePasswordBox` has a `ToggleButton` with text "Show" / "Hide".

**Required:** Replace the text content with a **XAML `Path` geometry** (no PNG file
needed — vector, scales cleanly, no external asset). Use:
- **Open eye** (password hidden): a simple oval-with-pupil path.
- **Closed eye** (password visible): same oval with a diagonal strike-through line.

The button width can shrink from 40 to 24 px. ToolTip stays "Show password" /
"Hide password".

**Files:** `TogglePasswordBox.xaml`

---

## REQ-7  Delete button: use Delete16.png icon

**Current:** The delete column in `CredentialManagerView` uses `Content="×"` text.

**Required:** Replace with `Delete16.png` already in the repo at
`libs/WPFToolkit.Extended-v1.5.0/CollectionEditors/Images/Delete16.png`.

Add it as a resource in `odm.ui.views.csproj` (`<Resource>`) and reference it as a
`BitmapImage` in the `DataGridTemplateColumn` button. Button should have no visible
border and transparent background, matching the current style.

**Files:** `CredentialManagerView.xaml`, `odm.ui.views.csproj`

---

## REQ-8  Closing Manage Credentials = logout + reconnect with stored credentials

**Current:** `btClose` simply calls `Close()`. Credentials may have changed but no
reconnect happens.

**Required:** On window close (handle `Window.Closing` or override `OnClosing`):
1. Set `AccountManager.Instance.LoggedOutExplicitly = false` (ensure stored-fallback is
   enabled).
2. Set `AccountManager.Instance.SetCurrentAccount(Account.Anonymous, remember: false)`
   to clear any in-flight credential (triggers `CurrentAccountChanged` → `AuthView`
   switches back to edit panel).
3. Publish `Refresh(true)` → `DeviceListViewModel` re-scans with `LoadCurrentAccount()`
   falling back to the (now updated) stored credentials.

Close behavior depends on the store state **at the moment of closing**:

| Store after edits | `LoggedOutExplicitly` | Effect |
|-------------------|-----------------------|--------|
| Has entries | `false` | `LoadCurrentAccount()` falls back to `stored[0]`; cameras reconnect |
| Empty | `true` | `LoadCurrentAccount()` returns `null`; cameras show unauthorized (same as explicit logout) |

This means if the user clears all credentials and closes, the app treats it exactly
like an explicit logout — no stored fallback, cameras disconnect.

**Files:** `CredentialManagerView.xaml.cs`

---

## REQ-9  Startup auto-login with stored credentials

**Current:** `DeviceListViewModel` constructor calls `LoadCurrentAccount()` but
`LoadDevices()` is commented out. The device scan only starts when the user manually
clicks Login or some other Refresh publisher fires.

**Required:** In `AuthView.AuthView_Loaded`, after calling `Update()`, if
`AccountManager.Instance.GetAllCredentials().Count > 0` publish `Refresh(true)` so
device discovery starts immediately without user interaction.

**Files:** `AuthView.xaml.cs`

---

## REQ-10  No passwords in logs

**Current:** `AuthLog` logs `name=` (username) but not password. `Update()` does not log
passwords. Needs formal audit.

**Required:**
- Audit every `AuthLog(...)`, `dbg.Error(...)`, `dbg.Info(...)` call in `AuthView.xaml.cs`,
  `CredentialManagerView.xaml.cs`, `AccountManager.cs`, `CredentialStore.cs`.
- Passwords must never appear in any log line, even partially.
- Account names may be logged (they are not secret).
- Add a `CredentialStore.RedactedSummary()` helper that returns `"N credentials stored"`
  (count only) for safe logging.

**Files:** `AuthView.xaml.cs`, `AccountManager.cs`, `CredentialStore.cs` (possibly)

---

## Test Coverage Required

Every logical change must be covered by an MSTest unit test in `odm.tests`. Specific
new tests:

| Area | Test |
|------|------|
| REQ-2 | `btLogin_Click` Case1 with `remember=false` → `SetCurrentAccount(remember:false)` → not added to store |
| REQ-2 | `btLogin_Click` Case1 with `remember=true` → added to store |
| REQ-4 | After `LoggedOutExplicitly=true`, `LoadCurrentAccount()` returns null even with stored creds |
| REQ-4 | `SetCurrentAccount(nonAnonymous)` resets `LoggedOutExplicitly` to false |
| REQ-8 | `CredentialManagerView` close sets `LoggedOutExplicitly=false` before reconnect |
| REQ-9 | Startup with non-empty store → `Refresh(true)` published on load |
| REQ-10 | `AuthLog` output for any scenario never contains password string |

Existing 31 tests must all continue to pass.

---

## Version

Bump `AssemblyInfo.global.cs` to `2.2.252.2` for this deploy.
