# PLAN — ODM Credentials UI

## Branch
feat/credentials-ui (base: development)

## Exploration Summary

### Current Architecture
- **Single credential pair**: `Account` struct in `AccountManager.cs` holds one username/password
- **Storage**: Plain XML serialization to `config/account.def.xml` — **no encryption**
- **Auth UI**: `AuthView.xaml` in toolbar — username TextBox + PasswordBox + Login button + "Remember me" checkbox
- **Connection flow**: `DeviceListViewModel.Refresh()` → `LoadCurrentAccount()` → creates `NvtSessionFactory(credential)` → `CreateSession(uris)` per discovered device
- **Session factory**: `NvtSessionFactory` (F#) takes a single `NetworkCredential`, creates WCF channel factories with/without security token based on whether credentials are non-null
- **No tests exist** in the solution
- **Framework**: .NET 4.0, WPF, Prism (Unity IoC, EventAggregator), Reactive Extensions
- **Password binding helper**: `PasswordBoxAssistant.cs` — attached properties for PasswordBox data binding

### Key Constraints
- .NET 4.0 — can use `System.Security.Cryptography.ProtectedData` (DPAPI) for encryption, available via `System.Security.dll`
- `NvtSessionFactory` is constructed once per refresh with a single `NetworkCredential` — multi-credential iteration must happen at a higher level (in `DeviceListViewModel`)
- The F# session layer should not be modified if possible — credential iteration belongs in C# calling code

---

## Phase 1 — Credential Storage Foundation

### Task 1.1 — Create `CredentialStore` with DPAPI-encrypted persistence
- **Files:** `odm/odm.ui.views/core/CredentialStore.cs` (new), `odm/odm.ui.views/odm.ui.views.csproj` (add reference to System.Security)
- **Change:** Create `CredentialStore` class that:
  - Holds a `List<Account>` of credential pairs (reusing existing `Account` struct)
  - Serializes to `config/credentials.dat` using `XmlSerializer` → byte[] → `ProtectedData.Protect()` with `DataProtectionScope.CurrentUser`
  - Provides `Load()`, `Save()`, `Add(Account)`, `Remove(int index)`, `Update(int index, Account)`, `GetAll()` methods
  - Singleton pattern (like existing `AccountManager`)
  - On first load, migrates existing `account.def.xml` single credential into the new store (if present and non-anonymous)
- **Done:** `CredentialStore` compiles, can round-trip encrypt/decrypt a list of credentials, migration from old format works
- **Blocker:** Need to add `System.Security.dll` reference to csproj (should be available in .NET 4.0 GAC)
- **Tier:** standard

### Task 1.2 — Update `AccountManager` to use `CredentialStore` and support multi-credential iteration
- **Files:** `odm/odm.ui.views/core/AccountManager.cs`
- **Change:**
  - Add `IReadOnlyList<Account> GetAllCredentials()` method that delegates to `CredentialStore.Instance.GetAll()`
  - Keep `CurrentAccount` and `SetCurrentAccount` working for backward compatibility (the "active/last successful" credential)
  - Add `SetCredentials(List<Account>)` method for bulk update from UI
  - Remove old plain-XML `Save()`/`Load()` methods, delegate to `CredentialStore`
- **Done:** `AccountManager` compiles, existing code that reads `CurrentAccount` still works, new `GetAllCredentials()` returns all stored pairs
- **Blocker:** None — additive change to existing singleton
- **Tier:** standard

### Task 1.V — Verify Phase 1
- **Type:** verify
- **Steps:** Build solution (`msbuild odm.sln`), verify no compile errors, verify `CredentialStore` can be instantiated
- **Done:** Solution builds clean, no regressions in existing code paths

---

## Phase 2 — Multi-Credential Connection Logic

### Task 2.1 — Implement credential iteration in `DeviceListViewModel`
- **Files:** `odm/odm.ui.views/viewmodels/DeviceListViewModel.cs`
- **Change:**
  - Modify `Refresh()` / `LoadDevices()` flow: instead of creating one `NvtSessionFactory` with one credential, iterate through `AccountManager.Instance.GetAllCredentials()`
  - For each discovered device, try `SessionProcess` with each credential in order:
    1. Create `NvtSessionFactory(credential)` 
    2. Attempt `CreateSession(uris)`
    3. On success → use that session, stop iterating
    4. On failure → try next credential
    5. If all fail → try anonymous (null credential) as final fallback
  - Extract credential iteration into a helper method `TrySessionWithCredentials(DeviceDescriptionHolder, IList<Account>)`
- **Done:** When multiple credentials are stored, the app tries each one per device until authentication succeeds; single-credential behavior is unchanged
- **Blocker:** Need to understand error types from `NvtSessionFactory.CreateSession` to catch auth failures specifically vs. network errors. Auth failures in ONVIF are typically SOAP faults — the existing error handler in `SessionProcess` already catches all exceptions.
- **Tier:** premium

### Task 2.V — Verify Phase 2
- **Type:** verify
- **Steps:** Build solution, manually test with a device using correct credentials in position 2 of the list — verify it connects after skipping credential 1
- **Done:** Device connects successfully after iterating past wrong credentials

---

## Phase 3 — Credentials Management UI

### Task 3.1 — Create `CredentialManagerView` (XAML + code-behind) for managing credential pairs
- **Files:** `odm/odm.ui.views/views/CredentialManagerView.xaml` (new), `odm/odm.ui.views/views/CredentialManagerView.xaml.cs` (new)
- **Change:** Create a WPF UserControl with:
  - A `ListBox` or `DataGrid` displaying all credential pairs (username shown, password masked)
  - "Add" button → inline row or small dialog with username TextBox + PasswordBox
  - "Edit" button → allows editing selected credential
  - "Remove" button → removes selected credential with confirmation
  - "Move Up" / "Move Down" buttons to reorder priority
  - All changes save immediately via `CredentialStore`
  - After save, publish `Refresh` event so devices re-authenticate
- **Done:** User can add, edit, remove, and reorder credentials through the UI; changes persist across restart
- **Blocker:** Need to decide where to host this view — likely as a dialog/popup accessible from the toolbar area near AuthView
- **Tier:** standard

### Task 3.2 — Integrate `CredentialManagerView` into the application
- **Files:** `odm/odm.ui.views/views/AuthView.xaml`, `odm/odm.ui.views/views/AuthView.xaml.cs`, possibly `odm/odm.ui.views/views/ToolBarView.xaml`
- **Change:**
  - Replace or augment the existing single username/password fields in `AuthView` with a "Manage Credentials" button that opens `CredentialManagerView` as a popup/dialog
  - Keep the quick-login fields for convenience (they add/select a credential)
  - Login button behavior: if the entered credential is new, add it to the store; if it matches existing, select it as active
  - The "Remember me" checkbox controls whether the entire credential store persists (or just the current session)
- **Done:** "Manage Credentials" button appears in toolbar, opens credential management UI, credentials are saved and loaded on restart
- **Blocker:** None
- **Tier:** standard

### Task 3.V — Verify Phase 3
- **Type:** verify
- **Steps:** Build, launch app, add 3 credentials via UI, close and reopen app — verify all 3 are loaded. Remove one, verify it's gone on restart. Edit one, verify change persists.
- **Done:** Full CRUD lifecycle works end-to-end with persistence

---

## Phase 4 — Password Visibility Toggle (REQ-1)

### Task 4.1 — Add password visibility toggle to all password fields
- **Files:** `odm/odm.ui.views/controls/TogglePasswordBox.xaml` (new), `odm/odm.ui.views/controls/TogglePasswordBox.xaml.cs` (new), `odm/odm.ui.views/views/AuthView.xaml`, `odm/odm.ui.views/views/CredentialManagerView.xaml`
- **Change:**
  - Create a reusable `TogglePasswordBox` UserControl that contains:
    - A `PasswordBox` (default, visible) and a `TextBox` (hidden, for plaintext view) — toggle visibility between them
    - An eye icon `ToggleButton` that switches between show/hide states
    - A `Password` dependency property that syncs between both controls using `PasswordBoxAssistant`
  - Replace bare `PasswordBox` in `AuthView.xaml` with `TogglePasswordBox`
  - Use `TogglePasswordBox` in `CredentialManagerView.xaml` for all password fields
- **Done:** Eye icon appears next to every password field; clicking toggles between masked and plaintext; works for all credential entries
- **Blocker:** WPF `PasswordBox` doesn't support binding natively — existing `PasswordBoxAssistant` pattern handles this, reuse it
- **Tier:** standard

### Task 4.V — Verify Phase 4
- **Type:** verify
- **Steps:** Build, launch app, verify eye icon on login password field and all credential manager password fields. Click to show, click to hide. Enter new credential — verify toggle works on fresh fields.
- **Done:** All password fields have working visibility toggle

---

## Summary

| Task | Description | Tier | Est. Complexity |
|------|-------------|------|-----------------|
| 1.1 | CredentialStore with DPAPI encryption | standard | New class, DPAPI integration |
| 1.2 | Update AccountManager for multi-credential | standard | Modify singleton, add methods |
| 1.V | Verify Phase 1 | verify | Build check |
| 2.1 | Credential iteration in DeviceListViewModel | premium | Modify async connection flow |
| 2.V | Verify Phase 2 | verify | Manual test |
| 3.1 | CredentialManagerView UI | standard | New XAML + code-behind |
| 3.2 | Integrate into app (toolbar/AuthView) | standard | Wire up navigation |
| 3.V | Verify Phase 3 | verify | CRUD lifecycle test |
| 4.1 | TogglePasswordBox control + integration | standard | New control, replace PasswordBox |
| 4.V | Verify Phase 4 | verify | Visual + functional test |
