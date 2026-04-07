# Phase 3 Code Review — APPROVED

**Reviewer:** Fleet code-review agent
**Date:** 2026-04-07
**Commits reviewed:** 530aa37, 1ebbe15

---

## Checklist

| # | Check | Verdict | Notes |
|---|-------|---------|-------|
| 1 | Three-way logic correct | PASS | Case 1 (hasFields → SetCurrentAccount + save prompt + Refresh), Case 2 (store has entries → Refresh only), Case 3 (both empty → block with MessageBox). All three branches are mutually exclusive and exhaustive. |
| 2 | CanLogin() predicate correct | PASS | Returns `hasFields \|\| hasStored`. Button enabled when either explicit credentials are entered OR the credential store is non-empty. `GetAllCredentials()` returns `IList<Account>` (never null — backed by `_credentials.AsReadOnly()`), so `.Count > 0` is safe. |
| 3 | RaiseCanExecuteChanged wiring | PASS | Fires on: (a) `username.TextChanged`, (b) `password` DependencyProperty change via `DependencyPropertyDescriptor.AddValueChanged`, (c) after `CredentialManagerView.ShowDialog()` returns, (d) after a credential is saved in Case 1. All relevant mutation points covered. |
| 4 | No null-reference risks | PASS | `username.Text` defaults to `""` (WPF TextBox). `password.Password` is a DependencyProperty with default `""` (confirmed in TogglePasswordBox). `AccountManager.Instance.GetAllCredentials()` never returns null. `Account` is a struct with `Name`/`Password` defaulting to `""`. |
| 5 | Save prompt appropriate | PASS | `MessageBox.Show` with `YesNo` + `Question` icon. Clean UX — non-blocking decision before connect. |
| 6 | No regressions in Phase 1/2 | PASS | Phase 1 (`AccountManager`, `CredentialStore`) untouched. Phase 2 (`CredentialManagerView`) untouched. Only `AuthView.xaml.cs` modified. `btLogout_Click`, `Update`, `AuthView_Loaded`, DependencyProperties all unchanged. |

## Build

msbuild on the solution produces **0 new errors**. All errors are pre-existing environment issues (missing F# toolchain, System.Reactive, C++ props) unrelated to Phase 3 changes. The C# compilation units that include `AuthView.xaml.cs` do not introduce any new warnings or errors.

## Minor observations (non-blocking)

- `DependencyPropertyDescriptor.AddValueChanged` creates a strong reference that is never removed. In this case it's fine because the `AuthView` and the `password` control share the same lifetime, but worth noting for future reference.
- The Enter-key handlers on `username`/`password` call `btLogin_Click()` directly (bypassing the `CanLogin` guard). If a user presses Enter with empty fields and no stored credentials, they'll hit Case 3's MessageBox block. Functionally correct but slightly inconsistent with the disabled-button UX. Non-blocking.

## Verdict

**APPROVED** — Phase 3 is clean. Three-way gating logic is correct, CanLogin predicate and wiring are sound, no null risks, no regressions.
