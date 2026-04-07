# ODM Credentials UI — Plan Review

**Reviewer:** odm-rev
**Date:** 2026-04-07 12:00:00+00:00
**Verdict:** APPROVED

> See the recent git history of this file to understand the context of this review.

---

## 1. Clear "Done" Criteria — PASS

Every task has a "Done:" line with testable, unambiguous conditions. The new tasks since last review maintain this standard: Task 1.0 (spike) has "Exception types documented above; Task 2.1 updated to reference this finding" — clear and verifiable. Task 2.2 (credential cache) has "cache is consulted first and iteration is skipped ... cache entry is evicted and re-iterated correctly" — two specific behavioral tests.

Task 1.V's done criteria ("no regressions in existing code paths") remains the weakest, but as noted in the prior review, this is acceptable given the project has no test suite. No change needed.

---

## 2. Cohesion and Coupling — PASS

The architecture remains well-decomposed. The addition of Task 2.2 (per-device credential cache) fits cleanly within Phase 2 since it is a direct optimization of the iteration logic in Task 2.1, modifying the same file (`DeviceListViewModel.cs`). It doesn't introduce cross-phase coupling — the cache is internal to the connection flow and invisible to the UI or storage layers.

---

## 3. Key Abstractions in Earliest Tasks — PASS

`CredentialStore` (Task 1.1) and `AccountManager` multi-credential API (Task 1.2) remain the foundational abstractions, placed first. The credential cache in Task 2.2 is not a shared abstraction — it's an internal optimization within `DeviceListViewModel` — so its later placement is correct.

---

## 4. Riskiest Assumption Validated Early — PASS (previously FAIL)

**Prior finding:** The plan hand-waved error discrimination between auth failures and network errors. A spike was required.

**Resolution:** Task 1.0 now exists as a dedicated spike that thoroughly documents the finding: auth failures surface as generic `FaultException` while network errors surface as `CommunicationException` subtypes, and the existing `SessionProcess` catches all exceptions generically with no discrimination. The spike correctly concludes that iteration must treat all failures as "try next credential" and explicitly states the implication for Task 2.1. Task 2.1's blocker note now cross-references Task 1.0's finding.

This is a well-executed resolution. The spike answered the question, documented the answer, and the downstream task was updated to reflect the constrained design space. The "try all, fallback to anonymous" strategy is the only viable approach given the WCF/SOAP fault architecture.

---

## 5. Later Tasks Reuse Early Abstractions (DRY) — PASS

Same as prior review. Task 2.2 adds a new internal data structure (`_credentialCache` dictionary) but correctly reuses the `Account` struct from `AccountManager` and the `TrySessionWithCredentials` helper from Task 2.1. No redundant abstractions introduced.

---

## 6. Phase Structure (2-3 Tasks + Verify) — PASS

Phase counts have shifted since the prior review:
- Phase 1: 1 spike + 2 work tasks + verify (3 tasks total, but the spike is read-only and produces no code)
- Phase 2: 2 work tasks + verify (improved from 1 task — see below)
- Phase 3: 2 work tasks + verify
- Phase 4: 1 work task + verify

Phase 2 now has two work tasks (2.1 iteration + 2.2 cache), which is better than before. The prior review accepted the single-task phase; now it conforms to the 2-3 task guideline.

---

## 7. Each Task Completable in One Session — PASS

Task 2.2 (credential cache) is well-scoped: one dictionary field, populate on success, check before iteration, evict on failure. This is additive to a single file and straightforward. All other tasks remain session-sized as previously reviewed.

---

## 8. Dependencies Satisfied in Order — PASS

The dependency chain remains correct. Task 2.2 correctly follows Task 2.1 (it extends the `TrySessionWithCredentials` helper that 2.1 creates). The full chain: 1.0 → 1.1 → 1.2 → 2.1 → 2.2 for the storage-to-connection path, and 1.1 → 3.1 → 3.2 → 4.1 for the storage-to-UI path.

**NOTE:** Task 1.0 is missing from the Summary table. This is a minor documentation gap — the spike is documented in the Phase 1 section, so there's no risk of it being skipped, but the table should be complete. Not blocking.

---

## 9. Vague or Ambiguous Tasks — PASS (previously FAIL)

**Prior finding:** Four specific ambiguities in Tasks 3.1 and 3.2 — DataGrid vs. ListBox, inline vs. dialog, quick-login interaction, and credential deduplication semantics.

**Resolution:** All four have been resolved in the plan text:

1. **DataGrid chosen** (Task 3.1, line 104): "DataGrid chosen over ListBox because it provides inline editing natively without custom item templates." Clear choice with stated rationale.

2. **Inline add via CanUserAddRows** (Task 3.1, line 105): "Inline DataGrid row via `DataGrid.CanUserAddRows = true` — no separate dialog." Unambiguous.

3. **Quick-login fields kept** (Task 3.2, line 118): "Keep the existing username/password quick-login fields in AuthView. Add a 'Manage Credentials' button that opens CredentialManagerView as a child window." The interaction model is clear — quick-login for single use, child window for management.

4. **Deduplication rule defined** (Task 3.2, lines 119-120): "Match on username using case-insensitive comparison. If username matches, prompt to update the stored password — never create a duplicate username entry." This is explicit and addresses the `Account.Equals` concern from the prior review.

Two developers would now build the same UI from these specifications.

---

## 10. Hidden Dependencies — NOTE

The two items from the prior review remain as minor notes:

1. **Event class naming** — Task 3.1 still says "publish Refresh event so devices re-authenticate" without naming the specific Prism event class. The implementer will need to inspect `AuthView.xaml.cs:btLogin_Click()` to find the correct event type. This is a small lookup, not a design ambiguity, so it remains a NOTE rather than a FAIL.

2. **Account equality semantics** — Task 1.1 specifies index-based operations (`Remove(int index)`, `Update(int index, Account)`), which sidesteps the `Account.Equals` by-name-only trap. Task 3.2 now explicitly defines deduplication as case-insensitive username comparison with password update prompt, so the equality semantics are clear at every layer. This concern is effectively resolved.

**New note:** Task 2.2's cache key is described as "device URI / host" — the slash suggests either could work, but the implementer should pick one. URI is more specific (handles multiple cameras on the same host with different ports), so URI is the better default. Minor — the implementer can make this call.

---

## 11. Risk Register — PASS (previously FAIL)

**Prior finding:** No risk register existed.

**Resolution:** A Risk Register section now exists with 6 risks (R1-R6). Reviewing each:

- **R1 (DPAPI portability):** The doer reframed this from "DPAPI blocked by group policy" to "credentials are machine/user-bound" — this is actually a more likely real-world concern and a better risk description. Mitigation (document the limitation) is pragmatic. PASS.
- **R2 (Error discrimination):** Correctly marked as resolved by Task 1.0 spike. PASS.
- **R3 (Iteration latency):** Mitigation says "preserve existing timeout values; add cancellation support if already present." This is weaker than the original suggestion of "2-3s per-credential timeout" but more honest — the plan doesn't want to introduce arbitrary timeout constants into an existing flow. Task 2.2's credential cache also mitigates this for repeat connections. Acceptable.
- **R4 (Migration data loss):** Mitigation is correct — write-then-delete with verification. PASS.
- **R5 (Toggle UX):** Reframed from cursor-position loss to "plaintext visible while typing" — this is a more realistic concern. Accepted as standard behavior. PASS.
- **R6 (Credential cache invalidation):** New risk added for the new Task 2.2. Mitigation (evict on failure, re-iterate) is correct and matches the task description. Good addition.

The register covers the key project-level risks with reasonable mitigations. It is no longer just per-task "Blocker" notes.

---

## 12. Alignment with Requirements — PASS

The plan continues to map correctly to requirements:

| Requirement | Plan Coverage |
|-------------|--------------|
| REQ-2: Add, edit, delete multiple credential pairs | Tasks 3.1, 3.2 |
| REQ-2: Secure persistent storage | Task 1.1 (DPAPI) |
| REQ-2: Iterate credentials on connect | Tasks 2.1, 2.2 |
| REQ-2: Failed pairs skipped silently | Task 2.1 (try-all strategy from spike) |
| REQ-2: Not stored in plaintext | Task 1.1 (DPAPI encryption) |
| REQ-1: Eye icon on every password field | Task 4.1 |
| REQ-3, REQ-4 | Out of scope (correct) |

Task 2.2 (credential cache) is not explicitly required but is a reasonable UX optimization that prevents re-iterating N credentials on every refresh for known devices. It doesn't add scope creep — it's a small additive task within the connection phase.

---

## Summary

**All 12 checks pass.** The three prior FAIL findings have been resolved:

1. **Check 4 (was FAIL, now PASS):** Task 1.0 spike thoroughly investigated auth-failure exception types, documented that discrimination is not possible, and Task 2.1 was updated to use the "try all, fallback to anonymous" strategy.

2. **Check 9 (was FAIL, now PASS):** All four UI ambiguities resolved — DataGrid with inline editing, CanUserAddRows for new entries, quick-login fields retained alongside a child window for credential management, and case-insensitive username deduplication with password update prompt.

3. **Check 11 (was FAIL, now PASS):** Risk register added with 6 risks covering DPAPI portability, error discrimination, iteration latency, migration safety, toggle UX, and cache invalidation. Mitigations are pragmatic.

**Minor notes (not blocking):**
- Task 1.0 is missing from the Summary table
- Task 3.1 should name the Prism event class for the refresh trigger
- Task 2.2 cache key should be device URI (not host) for multi-port scenarios

The plan is ready for implementation.
