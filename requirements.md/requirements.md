# Requirements — ODM

## Base Branch
`development`

---

## REQ-1 — Password Visibility Toggle
**Depends on:** REQ-2 (implement after)

### Goal
Users have no way to view the password they've entered. Add an eye icon next to each password field so users can toggle plaintext visibility.

### Scope
- Add show/hide eye icon button to each password field in the credentials UI
- Toggle input type between `password` and `text` on click

### Out of Scope
- Any changes to how passwords are stored or validated

### Acceptance Criteria
- [ ] Eye icon is visible next to every password input field
- [ ] Clicking the icon reveals the password in plaintext; clicking again hides it
- [ ] Works for all credential entries (new and existing)

---

## REQ-2 — Multiple Credential Pairs

### Goal
Support multiple username/password pairs. Each pair must be saved securely and loaded on system startup. On connect, all pairs are tried in sequence until one works.

### Scope
- UI to add, edit, and remove multiple username/password credential entries
- Secure persistent storage (loaded automatically on startup)
- Connection logic: iterate through all stored pairs per camera until authentication succeeds

### Out of Scope
- Per-camera credential assignment — credentials are tried globally against every camera

### Constraints
- Credentials must be stored securely (e.g. Windows Credential Manager or encrypted local store) — plaintext storage is not acceptable

### Acceptance Criteria
- [ ] User can add, edit, and delete multiple username/password pairs
- [ ] Credentials persist across application restarts
- [ ] On camera connect, each stored credential pair is attempted in order until one succeeds
- [ ] Failed pairs are skipped silently; success proceeds as normal
- [ ] Credentials are not stored in plaintext

---

## REQ-3 — Release Executable Signing

### Goal
The build pipeline must support signing the release executable with the Apra Labs certificate (already acquired via Azure Artifact Signing Service). Signing must only run on release builds, not regular CI builds.

### Scope
- Add a signing step to the GitHub Actions workflow, gated to release triggers only
- Integrate with the existing Azure Artifact Signing Service certificate
- Regular CI builds (push, PR) must remain unsigned and unaffected

### Out of Scope
- Certificate acquisition — already done
- Signing of non-release artifacts (installers, packages) unless already part of the release build

### Constraints
- Azure Artifact Signing Service credentials must be stored as GitHub Actions secrets
- Must not add signing overhead to regular CI build times

### Acceptance Criteria
- [ ] Release builds produce a signed executable
- [ ] Signature is verifiable with the Apra Labs certificate
- [ ] Regular CI builds (non-release) are not signed and complete without signing-related steps
- [ ] Signing failure causes the release workflow to fail (not silently skip)

---

## REQ-4 — Investigate Video Playback Failure in GitHub-Built Executables

### Goal
Field reports indicate executables built via GitHub Actions never play video, while executables built locally on the developer machine work correctly. Root cause must be identified and fixed.

### Scope
- Diff the GitHub Actions build environment against the local build environment
- Identify missing dependencies, codecs, runtime libraries, or build flags causing the regression
- Fix the GitHub Actions workflow so CI-built executables play video correctly

### Out of Scope
- Feature changes to the video playback component itself

### Constraints
- Fix must not break local builds

### Acceptance Criteria
- [ ] Root cause of the playback failure in CI-built executables is documented
- [ ] CI-built executable plays video correctly on a clean test machine
- [ ] No regression in local builds

---

## Implementation Order

| Order | REQ | Dependency |
|-------|-----|------------|
| 1 | REQ-2 — Multiple credential pairs | none |
| 2 | REQ-1 — Password visibility toggle | after REQ-2 |
| 3 | REQ-3 — Release signing | none |
| 4 | REQ-4 — Video playback investigation | none |

REQ-3 and REQ-4 are independent and can be sprinted in parallel with REQ-1/REQ-2 if capacity allows.
