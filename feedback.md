# ODM Sprint 2 — Plan Review

**Reviewer:** odm-rev
**Date:** 2026-04-07 00:00:00+00:00
**Verdict:** APPROVED

---

## 1. Done Criteria

Every task has concrete, testable done criteria. Task 1.1 specifies the exact equality semantic ("two accounts with same name but different password are NOT equal"). Task 1.2 specifies observable behavior ("multiple credentials with same username coexist"). Task 2.1 lists four discrete UI elements that must be present. Task 3.1 specifies three login scenarios with expected outcomes. Task 4.1 specifies binding propagation behavior. **PASS.**

---

## 2. Cohesion and Coupling

Phase 1 is internally cohesive — both tasks address the same root cause (name-only equality) and its consequences. Phase 2 (UX redesign) and Phase 3 (login gating) are independent concerns. Phase 4 is verification-only.

**NOTE:** Task 1.2 modifies `AuthView.btLogin_Click` (removing the "Update the stored password?" prompt), and Task 3.1 also modifies `btLogin_Click` (adding "Save this credential?" prompt and three-way gating logic). Both tasks edit the same method for related but distinct reasons. This is acceptable because they execute sequentially and the removals in 1.2 simplify the method before 3.1 restructures it — but an implementer should be aware that Task 3.1 effectively rebuilds the method that Task 1.2 partially gutted. **PASS with NOTE.**

---

## 3. Key Abstractions First

The foundational fix — `Account.Equals` comparing both Name and Password — is correctly placed in Task 1.1, the very first work item. Everything downstream (dedup logic in 1.2, login gating in 3.1) depends on this semantic change. **PASS.**

---

## 4. Riskiest Assumption Validated Early

The root cause hypothesis — that `Account.Equals` name-only comparison is the single root cause of both BUG-1 and BUG-2 — is the riskiest assumption in the plan. Task 1.1 validates it immediately, and Phase 1.V verifies before proceeding. The plan identifies the specific callers (line numbers in AccountManager.cs) and explains the causal chain from `Equals` → `CurrentAccount` setter → missing event → no device refresh. This is credible and well-evidenced. **PASS.**

---

## 5. DRY and Reuse

The corrected `Account.Equals` from Task 1.1 is reused by all downstream equality checks. Task 1.2's dedup logic and Task 3.1's store-aware gating both rely on the fixed equality semantic rather than reimplementing comparison. **PASS.**

---

## 6. Phase Structure (2–3 tasks + verify)

Phase 1 has 2 work tasks + verify. Phases 2, 3, and 4 each have 1 work task + verify. The single-task phases are appropriate given the scope of each fix — there is no artificial splitting or combining. **PASS.**

---

## 7. Session Completability

All tasks are marked "cheap" or "standard" tier. File counts are small (1–2 files per task). No task requires cross-cutting refactoring or multi-project coordination. Each is completable in one session. **PASS.**

---

## 8. Dependency Order

Phase 1 (equality fix) → Phase 2 (UX redesign, independent) → Phase 3 (login gating, depends on working credential store from Phase 1) → Phase 4 (verification). Dependencies are satisfied in order. Phase 2 could technically run in parallel with Phase 3, but sequential execution is simpler and the plan doesn't claim parallelism. **PASS.**

---

## 9. Ambiguity Check

Task 3.1 offers two implementation approaches: `DelegateCommand` with `canExecute` delegate vs. a simple check at the top of `btLogin_Click` with manual disable. The plan recommends `DelegateCommand` as "cleanest" but leaves the door open. This is minor — the recommended approach is clear, the alternative is a fallback. No task is so vague that two developers would produce incompatible results. **PASS.**

---

## 10. Hidden Dependencies

The `btLogin_Click` overlap between Task 1.2 and Task 3.1 (discussed in §2) is the only notable cross-task dependency. It is partially implicit — the plan does not explicitly state "Task 3.1 assumes the prompt removal from 1.2 is already done." However, since they are in sequential phases and 1.V verifies before Phase 3 begins, this is manageable. No other hidden dependencies detected. **PASS with NOTE.**

---

## 11. Risk Register

The risk register identifies three risks with mitigations:

1. **Account.Equals change impact** — mitigated by grepping all callers. Concrete and verifiable.
2. **DataGrid + TogglePasswordBox binding timing** — mitigated with `UpdateSourceTrigger` and a fallback (`RowEditEnding`).
3. **Empty-field login UX confusion** — mitigated with visual indication.

**NOTE:** One risk is missing from the register: **BUG-2 root cause confidence.** The plan assumes `Account.Equals` is the *sole* root cause of BUG-2. The requirements describe a broader investigation scope ("trace the call path from camera connect through `TrySessionWithCredentials()` → `CredentialStore.GetAllCredentials()`"). If there is a second break in the chain beyond `Account.Equals` — e.g., `CredentialStore.GetAllCredentials()` not returning entries, or `TrySessionWithCredentials` not iterating the store — it would not be caught until Phase 1.V testing. The root cause analysis is credible and includes specific line numbers, but this should be acknowledged as a risk.

**Suggested addition to risk register:**

| 4 | BUG-2 has a second root cause beyond Account.Equals (e.g., CredentialStore not returning entries, TrySessionWithCredentials not iterating store) | Phase 1.V must test end-to-end: add credential in CredentialManagerView → close → connect to camera → verify credential is tried. If iteration still fails, trace DeviceListViewModel.TrySessionWithCredentials path. |

**PASS with NOTE.**

---

## 12. Requirements Alignment

| Item | Requirements Intent | Plan Coverage | Verdict |
|------|-------------------|---------------|---------|
| BUG-1 | Dedup key must be (username, password) pair | Task 1.2 changes dedup to match on both fields | **PASS** |
| BUG-2 | Stored credentials must be tried during camera connect | Task 1.1 fixes Account.Equals so CurrentAccount setter fires events; root cause analysis is specific and credible | **PASS** |
| UX-1 | 5 specific UI changes (× button, Delete key, Add button, remove Remove button, CanUserAddRows=false) | Task 2.1 addresses all 5 with specific XAML and code-behind changes | **PASS** |
| UX-2 | 4 behaviors (enable when store non-empty, quick-try with save prompt, empty-field store iteration, both-empty block) | Task 3.1 addresses all 4 scenarios explicitly | **PASS** |
| UX-3 | Replace inline show/hide with TogglePasswordBox | Plan correctly identifies Sprint 1 already did this; Task 4.1 is verification + binding fix if needed | **PASS** |

The plan solves the right problems. The UX-3 verification-only approach is honest — the plan doesn't invent work where none exists. **PASS.**

---

## Summary

**All 12 checklist items pass.** The plan is well-structured, correctly identifies `Account.Equals` as the shared root cause of BUG-1 and BUG-2, sequences the riskiest fix first, and maps cleanly to all five requirements items.

**Two notes to carry forward (non-blocking):**
1. **btLogin_Click overlap:** Tasks 1.2 and 3.1 both modify `AuthView.btLogin_Click`. The implementer should treat Task 3.1 as a rebuild of the method, not a patch on top of 1.2's changes. The plan should make the dependency explicit.
2. **BUG-2 root cause confidence:** Add Risk #4 to the register — if `Account.Equals` is not the sole root cause, Phase 1.V testing must trace `TrySessionWithCredentials` end-to-end before proceeding to Phase 2.

**No changes required. Plan is approved for implementation.**
