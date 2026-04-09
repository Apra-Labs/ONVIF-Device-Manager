# Credentials Architecture

This document describes the design and implementation of multi-credential storage,
connection logic, and UI introduced in the credentials UI sprint (REQ-1, REQ-2).

---

## Encrypted Credential Store (`CredentialStore`)

**File:** `odm/odm.ui.views/core/CredentialStore.cs`

Credentials are stored in `config/credentials.dat` using a two-layer approach:

1. **Serialization** — A `CredentialList` wrapper (XmlSerializer) serializes the
   `List<Account>` to a UTF-8 XML string.
2. **Encryption** — The XML bytes are encrypted with `ProtectedData.Protect()` using
   `DataProtectionScope.CurrentUser`. This delegates to Windows DPAPI.

On load, `ProtectedData.Unprotect()` reverses the process. Reads and writes go
through the singleton `CredentialStore.Instance`.

### Atomic write

Saves go through a temp file to prevent corruption if the process crashes mid-write:

```
write → credentials.dat.tmp
delete → credentials.dat       (if it exists)
rename → credentials.dat.tmp → credentials.dat
```

This ensures `credentials.dat` is never left in a partial state.

### Migration from `account.def.xml`

On first load, if `credentials.dat` does not exist but the legacy plain-XML file
`config/account.def.xml` does, `CredentialStore` performs a one-time migration:

1. Deserializes the single `Account` from `account.def.xml`.
2. Writes the encrypted `credentials.dat` (atomic write, see above).
3. Only after a successful write, deletes `account.def.xml`.

Anonymous accounts (empty username) are not migrated. If migration fails for any
reason, the old file is left in place and an error is logged — no data is lost.

### Known limitations (DPAPI scope)

- `DataProtectionScope.CurrentUser` binds the encrypted blob to the **current Windows
  user account on the current machine**. The `credentials.dat` file cannot be decrypted
  by a different user, or by the same user on a different machine.
- There is no cross-machine sync or export/import mechanism. This is by design — the
  credentials are sensitive and should not leave the machine.

---

## Multi-Credential Connection Logic (`DeviceListViewModel`)

**File:** `odm/odm.ui.views/viewmodels/DeviceListViewModel.cs`

### Why iteration happens in C#, not F#

`NvtSessionFactory` (F#) accepts a single `NetworkCredential` at construction time and
creates WCF channel factories from it. The F# session layer is deliberately not modified
to keep the ONVIF session code stable. Credential iteration is the responsibility of the
C# calling code.

### `TrySessionWithCredentials`

For each device that needs a session, `TrySessionWithCredentials` is called. It:

1. Checks the **per-device credential cache** (see below).
2. If cache hit, tries that credential first.
3. On cache miss or failure, calls `FullCredentialIteration`, which:
   - Iterates `AccountManager.Instance.GetAllCredentials()` in order.
   - For each credential, creates an `NvtSessionFactory` and calls `CreateSession`.
   - On success: caches the credential (see below), sets `devHolder.Account`, continues.
   - On failure: moves to the next credential.
   - After all named credentials fail: tries an anonymous (null credential) session as a
     final fallback.

### Auth failure is indistinguishable from network error

The ONVIF session layer surfaces auth failures as generic SOAP `FaultException` or
`CommunicationException` — the same types used for network errors. There is no
`MessageSecurityException` or auth-specific error code in the WCF pipeline for this
protocol. As a result, iteration treats **all** failures as "try the next credential."
The consequence: on a truly unreachable device, every credential is attempted before
giving up, multiplying the timeout by N credentials.

### Per-device credential cache (`_credentialCache`)

```csharp
private readonly Dictionary<string, Account> _credentialCache =
    new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
```

The cache key is the **host portion** of the device's first URI (e.g. `192.168.1.50`).
When a credential succeeds, it is stored in the cache. On the next refresh, the cached
credential is tried first — skipping the full iteration if it still works.

**Eviction:** If a cached credential fails (e.g. the camera's password changed), the
entry is evicted and `FullCredentialIteration` runs from the beginning. The cache is
in-memory only — it resets on every application restart.

**NAT edge case:** Multiple cameras behind a NAT that share the same host IP (different
ports only) will share one cache slot. The last successful credential wins. This is an
accepted trade-off for the common case where host IPs are unique.

---

## Credential Manager UI (`CredentialManagerView`)

**Files:**
- `odm/odm.ui.views/views/CredentialManagerView.xaml`
- `odm/odm.ui.views/views/CredentialManagerView.xaml.cs`

`CredentialManagerView` is a **child `Window`** (not a Prism region or popup) opened
from the "Manage Credentials" button in `AuthView`. It provides:

- A **DataGrid** with `CanUserAddRows = true` for inline row addition — no separate
  "Add" dialog. New rows appear as a placeholder at the bottom of the grid.
- Inline cell editing for username and password columns.
- **Remove** button with a confirmation `MessageBox`.
- **Move Up / Move Down** buttons that reorder the `ObservableCollection<CredentialItem>`
  backing the DataGrid — priority is determined by list order.

On every edit (row commit, move, remove), `SaveAndRefresh` is called, which:
1. Flushes the collection to `AccountManager.Instance.SetCredentials(list)`.
2. Publishes a `Refresh` event so `DeviceListViewModel` immediately re-authenticates
   with the updated credential list.

### Deduplication in quick-login (`AuthView`)

When the quick-login "Login" button is clicked, the credential is added to the store
only if the username is not already present. Matching uses
`StringComparer.OrdinalIgnoreCase` — case is ignored. If the username exists, the
stored password is updated rather than creating a duplicate entry. Anonymous credentials
(empty username) are never stored.

### `CredentialItem` wrapper

The DataGrid binds to `ObservableCollection<CredentialItem>` rather than `Account`
directly. `CredentialItem` implements `INotifyPropertyChanged` so inline DataGrid edits
update the backing observable collection in real time. `ToAccount()` converts back to
`Account` for persistence.

---

## `TogglePasswordBox` Control

**Files:**
- `odm/odm.ui.views/controls/TogglePasswordBox.xaml`
- `odm/odm.ui.views/controls/TogglePasswordBox.xaml.cs`

A reusable WPF UserControl that wraps a `PasswordBox` and a `TextBox` side-by-side
with a `ToggleButton` (eye icon) to switch between masked and plaintext views.

### Why two controls instead of one

WPF `PasswordBox` does not support data binding natively (by design — the password is
held in a `SecureString` and never exposed as a `DependencyProperty`). The
`PasswordBoxAssistant` attached-property pattern works around this but still cannot
show plaintext. The solution is to keep both controls in the visual tree and toggle
`Visibility` between them.

### `Password` dependency property

```csharp
public static readonly DependencyProperty PasswordProperty =
    DependencyProperty.Register(
        "Password",
        typeof(string),
        typeof(TogglePasswordBox),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPasswordPropertyChanged));
```

External code binds to `Password` exactly as it would to `PasswordBox.Password`.
The dependency property callback (`OnPasswordPropertyChanged`) and the event handlers
for `PasswordBox.PasswordChanged` and `TextBox.TextChanged` all update both controls
and the `Password` property in sync.

### Re-entrancy guard

A `bool _updating` field prevents feedback loops: when one handler updates a control,
the other handler checks `_updating` and returns immediately. The guard is set at the
start of each handler and cleared at the end, ensuring only one propagation path runs
per user action.

### Toggle behavior

- **Checked (show):** copies `PasswordBox.Password` → `TextBox.Text`, hides
  `PasswordBox`, shows `TextBox`, moves caret to end, sets focus.
- **Unchecked (hide):** copies `TextBox.Text` → `PasswordBox.Password`, hides
  `TextBox`, shows `PasswordBox`, sets focus.

The transition is synchronous and does not mutate the `Password` dependency property —
only the display controls.

---

## Known Limitations

| Limitation | Detail |
|------------|--------|
| DPAPI scope | `credentials.dat` is bound to the current Windows user and machine. Cannot be decrypted by a different user or on a different machine. No export/import. |
| Cache resets on restart | The per-device credential cache is in-memory only. Every app start re-authenticates each device from the full credential list. |
| All failures look the same | Auth failures and network errors are both surfaced as `FaultException`/`CommunicationException`. Iteration cannot distinguish them — all failures trigger "try next credential." On unreachable devices this multiplies the connection timeout by the number of credentials. |
| NAT host collision | Two cameras sharing the same host IP (different ports) share one cache slot. The last successful credential overwrites the previous one. |
