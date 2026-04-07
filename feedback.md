# ODM Credentials UI — Plan Review

**Reviewer:** odm-rev
**Date:** 2026-04-07 00:00:00+00:00
**Verdict:** CHANGES NEEDED

> See the recent git history of this file to understand the context of this review.

---

## 1. Clear "Done" Criteria — PASS

Every task has a "Done:" line with testable conditions. Task 1.1 specifies "round-trip encrypt/decrypt" and "migration works"; Task 2.1 specifies "tries each credential per device until authentication succeeds; single-credential behavior is unchanged"; Task 3.1 specifies full CRUD lifecycle with persistence. These are specific enough to unambiguously verify completion.

One minor note: Task 1.V's done criteria ("Solution builds clean, no regressions in existing code paths") is weaker than the others since "no regressions" can't be mechanically verified without tests, but this is acceptable given the project has no test suite.

---

## 2. Cohesion and Coupling — PASS

Tasks are well-decomposed along architectural boundaries:
- Phase 1 separates storage (1.1) from the adapter layer (1.2)
- Phase 2 isolates the connection-retry logic in its own phase
- Phase 3 separates view creation (3.1) from integration wiring (3.2)
- Phase 4 is a self-contained UI control

Coupling between phases flows in one direction (storage → logic → UI → polish), which is correct.

---

## 3. Key Abstractions in Earliest Tasks — PASS

`CredentialStore` (the shared storage abstraction) is Task 1.1, and the `AccountManager` multi-credential API surface is Task 1.2. All later tasks depend on these, and they come first. The `TogglePasswordBox` reusable control is correctly placed before its consumers in Phase 4.

---

## 4. Riskiest Assumption Validated Early — FAIL

The plan identifies credential iteration error handling as a blocker concern in Task 2.1: *"Need to understand error types from NvtSessionFactory.CreateSession to catch auth failures specifically vs. network errors."* The plan then hand-waves this away with *"the existing error handler in SessionProcess already catches all exceptions."*

This is the riskiest technical assumption in the entire plan. Catching all exceptions is not the same as distinguishing auth failures from network/timeout errors. If the code cannot distinguish these, the iteration logic may:
- Silently skip past the correct credential on a transient network error
- Waste 30+ seconds per credential on TCP timeouts before trying the next one

The `NvtSessionFactory.CreateSession` in `NvtSession.fs` uses async race across multiple URIs with timeout logic. Understanding the failure modes (SOAP fault codes for 401-equivalent vs. connection refused vs. timeout) needs to happen before committing to the iteration design in Task 2.1.

**Required change:** Add a spike or investigation step in Phase 1 (e.g., Task 1.3) that reads `NvtSession.fs` lines 468+, identifies the specific exception types thrown on auth failure vs. network failure, and documents the error-discrimination strategy. Task 2.1 should then reference that finding. If errors cannot be distinguished, the plan needs a different approach (e.g., parallel attempts with short timeouts, or a user-visible "testing credentials..." progress indicator).

**Doer:** fixed in commit 26d735a — Added Task 1.0 spike with resolved finding: auth failures are indistinguishable from network errors (both FaultException/CommunicationException). Task 2.1 blocker updated to reference Task 1.0 and use "try all, fallback to anonymous" strategy.

---

## 5. Later Tasks Reuse Early Abstractions (DRY) — PASS

Task 3.1 uses `CredentialStore` from 1.1. Task 3.2 uses `AccountManager` APIs from 1.2. Task 4.1's `TogglePasswordBox` is used in both `AuthView` and `CredentialManagerView`. The `PasswordBoxAssistant` pattern (existing, in `odm.ui.controls`) is correctly identified for reuse rather than reinvention.

---

## 6. Phase Structure (2-3 Tasks + Verify) — PASS

Phases 1 and 3 have 2 work tasks + verify. Phases 2 and 4 have 1 work task + verify. Single-task phases are acceptable here because each contains a cohesive, non-trivial unit of work (async retry logic and a reusable control, respectively). Splitting them further would create artificial boundaries.

---

## 7. Each Task Completable in One Session — PASS

All tasks are scoped to 1-3 files with clear boundaries. Task 2.1 (credential iteration in an Rx/async pipeline) is the most complex but is focused on a single method modification in `DeviceListViewModel`. Task 3.1 (new XAML view) is the largest surface area but is a standard WPF CRUD form.

---

## 8. Dependencies Satisfied in Order — PASS

The dependency chain is correct: 1.1 → 1.2 → 2.1, and 1.1 → 3.1 → 3.2 → 4.1. Phase 2 correctly depends on Phase 1's API. Phase 3 correctly depends on both the storage layer and the iteration logic being in place. Phase 4 correctly comes last since it touches views created in Phase 3.

---

## 9. Vague or Ambiguous Tasks — FAIL

**Task 3.1** has two unresolved design decisions:
- *"A ListBox or DataGrid"* — these have very different editing semantics. A DataGrid supports inline editing natively; a ListBox requires custom item templates. This choice affects the entire control's architecture.
- *"Add button → inline row or small dialog"* — inline editing vs. dialog is a UX pattern that changes the XAML structure significantly.

**Task 3.2** has three ambiguities:
- *"Replace or augment the existing single username/password fields"* — two developers would build different UIs from this. Does the quick-login area stay or go?
- *"possibly ToolBarView.xaml"* — the file scope is uncertain.
- *"Login button behavior: if the entered credential is new, add it to the store; if it matches existing, select it as active"* — "matches" is undefined. The existing `Account.Equals()` compares by `Name` only (see `AccountManager.cs:33`). So entering the same username with a different password would "match" and not update the stored password. Is that the intended behavior?

**Required change:** Resolve these design decisions in the plan text. Specifically:
1. Choose DataGrid or ListBox for 3.1 and state why.
2. Choose inline-add or dialog-add for 3.1.
3. Decide whether 3.2 keeps, replaces, or hides the quick-login fields, and document the interaction between quick-login and the credential list.
4. Define what "matches" means for credential deduplication — by name only, or by name+password.

**Doer:** fixed in commit 26d735a — Task 3.1: DataGrid with inline editing (CanUserAddRows), no dialog. Task 3.2: keep quick-login fields, "Manage Credentials" opens child window, deduplication on username (case-insensitive) with password update prompt.

---

## 10. Hidden Dependencies — NOTE

Two minor hidden dependencies worth documenting (not blocking):

1. **Task 3.1** says "publish Refresh event so devices re-authenticate" — this depends on the Prism `EventAggregator` and the specific event type used in `AuthView.xaml.cs:btLogin_Click()`. The plan should name the event class to avoid guesswork.

2. **Account struct equality** — `Account.Equals` compares by `Name` only. If `CredentialStore` uses a `List<Account>`, operations like `Remove(Account)` or `Contains(Account)` would match on username alone. This is fine if by design, but Task 1.1 should explicitly note that the store uses index-based operations (which it does — `Remove(int index)`, `Update(int index, Account)`) to avoid this trap.

---

## 11. Risk Register — FAIL

The plan has no risk register. Individual tasks have "Blocker" notes, but these are narrow per-task concerns, not a consolidated view of project-level risks.

**Required change:** Add a "## Risks" section after the Summary table with at least these entries:

| # | Risk | Likelihood | Impact | Mitigation |
|---|------|-----------|--------|------------|
| R1 | DPAPI `ProtectedData` unavailable or blocked by group policy on target machines | Low | High — credentials cannot be saved | Fall back to `System.Security.Cryptography.Aes` with a machine-derived key, or detect and warn user |
| R2 | Auth failures indistinguishable from network errors in ONVIF SOAP faults | Medium | High — iteration logic is unreliable | Spike in Phase 1 (see check 4 above) |
| R3 | Credential iteration adds unacceptable latency (N credentials * timeout per device) | Medium | Medium — poor UX on connect | Set per-credential timeout to 2-3s; show progress indicator |
| R4 | Migration of existing `account.def.xml` loses data if new format write succeeds but old file deletion is deferred | Low | Medium — user loses saved credential | Write new file first, then delete old; keep backup |
| R5 | `PasswordBox` ↔ `TextBox` toggle in `TogglePasswordBox` loses cursor position or selection state | Low | Low — minor UX glitch | Accept as known limitation or sync `SelectionStart` |

The plan author should review and adjust these, but the section must exist.

**Doer:** fixed in commit 26d735a — Added Risk Register section with 5 risks (DPAPI portability, error discrimination, iteration latency, migration data loss, toggle UX) with mitigations.

---

## 12. Alignment with Requirements — PASS

The plan covers REQ-2 (multiple credential pairs) in Phases 1-3 and REQ-1 (password visibility toggle) in Phase 4, in the correct dependency order specified by `requirements.md`. REQ-3 and REQ-4 are out of scope for this branch, which is correct.

Key requirements mapping:
- "User can add, edit, and delete multiple username/password pairs" → Tasks 3.1, 3.2
- "Credentials persist across application restarts" → Task 1.1
- "On camera connect, each stored credential pair is attempted in order" → Task 2.1
- "Credentials are not stored in plaintext" → Task 1.1 (DPAPI)
- "Eye icon visible next to every password input field" → Task 4.1

The plan solves the right problem. The DPAPI approach satisfies the "encrypted local store" constraint. The iteration-then-anonymous-fallback strategy matches the "failed pairs are skipped silently" acceptance criterion.

---

## Summary

**Passed (8/12):** Done criteria, cohesion/coupling, early abstractions, DRY reuse, phase structure, session size, dependency order, requirements alignment.

**Failed (3/12):**
1. **Check 4 — Riskiest assumption:** Credential iteration error handling is hand-waved. Add an error-discrimination spike to Phase 1.
2. **Check 9 — Vague tasks:** Tasks 3.1 and 3.2 have unresolved design decisions (DataGrid vs. ListBox, inline vs. dialog, quick-login interaction, credential matching semantics). Resolve these in the plan.
3. **Check 11 — Risk register:** Missing entirely. Add a consolidated risk section.

**Noted (1/12):** Check 10 — minor hidden dependencies around event types and Account equality semantics. Not blocking but worth documenting inline.

The doer should annotate each relevant section with `**Doer:** fixed in commit <sha> — <what changed>` before requesting re-review.
