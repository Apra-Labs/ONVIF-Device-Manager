# ODM Sprint 6 — Milesight Camera Compatibility Fixes

## Branch
`feat/media2-support` (adds commits to open PR #23 — base `development`). No new branch.

## Issues Addressed
- **#25** — `NvtSession.CreateSession(Uri[])` SOAP probe fails against cameras that require TLS 1.2 + `Expect: 100-Continue` disabled + relaxed cert validation.
- **#26** — `UpgradeScheme` rewrites `http://camera:80/onvif/Media` to `https://camera:443/...` when the device URI is HTTPS, even on cameras that serve the media service only over HTTP (e.g. Milesight at 192.168.1.190). Test harness also hardcodes `:443`.

## Risk Register

| Risk | Mitigation |
|------|-----------|
| Fix A behavior change (port remapping) breaks existing cameras that served media on `http://host:8080` with an HTTPS device URI | Covered by updated unit test; manual integration test against Milesight + regression smoke on Hikvision HTTPS camera |
| HTTP fallback retry masks legitimate auth/WCF errors | Fallback only triggers on `WebException` with `ConnectFailure` or `SocketException` with `ConnectionRefused`/`ConnectionReset`; other exceptions propagate unchanged |
| Global `ServicePointManager` mutation in `CreateSession(Uri[])` impacts other callers in the process | Matches the setting already applied by `HttpsIntegrationTests.ClassInitialize`; production entry point to `CreateSession(Uri[])` is application startup where no other WCF traffic runs |
| `UpgradeScheme` currently tested via `FixUrlHttpsTests` — tests must be updated without losing coverage | Keep existing `HttpUrl_WhenSessionIsHttps` + `HttpsUrl_Unchanged` + `HttpUrl_WhenSessionIsHttp_Unchanged` passing; rewrite `NonStandardHttpPort` test to assert new port-mapping contract |
| Memoized Media1/Media2 clients use upgraded URLs; HTTP fallback must construct a second non-memoized client | New helper creates an on-demand channel at the original HTTP xAddr and aborts it after the retry completes |
| `GetMedia1HttpXAddr` depends on `GetCapabilities()` — if device call itself fails, fallback cannot proceed | Fallback helper returns `null` when upstream calls fail; caller then propagates original connection-refused exception unchanged |
| `UpgradeScheme` (public static) and `UpgradeSchemeIfNeeded` (private closure) could drift | Phase 2 rewrites `UpgradeSchemeIfNeeded` to delegate to the static member — single source of truth |

---

## Phase 1 — Harness parity: CreateSession(Uri[]) + Media2 test setup (Issue #25)

### Task 1.1 — Apply ServicePointManager settings before `raceEndpoints`
- **Files:** `onvif/onvif.session/NvtSession.fs:514-569` (`CreateSession(uris:Uri[])`)
- **What:** Immediately after the `uris.Length = 0` guard (currently line 569) and before `let! httpResult = raceEndpoints uris` (line 572), set:
  - `ServicePointManager.SecurityProtocol <- SecurityProtocolType.Tls12`
  - `ServicePointManager.Expect100Continue <- false`
  - `ServicePointManager.ServerCertificateValidationCallback <- fun _ _ _ _ -> true`

  The global `Expect100Continue = false` flag propagates to any new `ServicePoint` created by probe channels; the per-ServicePoint override already set at line 660 (for `CreateSession(deviceUri)`) continues to work unchanged.
- **Done:**
  - `dotnet build odm/odm.tests/odm.tests.csproj -v quiet` → 0 errors
  - `SchemeUpgradeTests.SoapProbe_OnSilentPort_TriggersHttpsFallback` still passes
  - Against Milesight `192.168.1.190` from a clean process: invoking `CreateSession(new[] { new Uri("http://192.168.1.190:80/onvif/device_service") })` returns a session (no `failwith "SOAP probe failed for all URIs"`). Proves TLS/Expect settings reach the SOAP probe.
- **Tier:** cheap

### Task 1.2 — Media2IntegrationTests ClassInitialize + `ODM_TEST_HTTP_PORT` env var
- **Files:** `odm/odm.tests/Media2IntegrationTests.cs:32-41` (`CreateSession`), add `ClassInitialize` (no existing one)
- **What:**
  - Add `[ClassInitialize]` that reads `.env` (mirror of `HttpsIntegrationTests.cs:23-43`) and applies the same `ServicePointManager` trio.
  - Rewrite `CreateSession()` body so the URI honours `ODM_TEST_HTTP_PORT` (default `"80"`):
    ```csharp
    var port = Environment.GetEnvironmentVariable("ODM_TEST_HTTP_PORT") ?? "80";
    var uri = new Uri(string.Format("http://{0}:{1}/onvif/device_service", TestHost, port));
    ```
- **Done:**
  - Build passes; `TestCategory!=Integration` run unchanged (0 new failures).
  - With `ODM_TEST_HOST=192.168.1.190` (no `ODM_TEST_HTTP_PORT` set), all 7 `Media2IntegrationTests` reach `GetProfiles()` and fail only on the Milesight-specific HTTPS→HTTP fallback bug (addressed Phase 3), not on `CreateSession`.
- **Tier:** cheap

### VERIFY 1
- [ ] `dotnet build odm/odm.tests/odm.tests.csproj -v quiet` → Build succeeded, 0 errors
- [ ] `vstest.console.exe odm.tests.dll --TestCaseFilter:"TestCategory!=Integration"` → all pass (baseline preserved)
- [ ] Integration smoke against Milesight: Media2 tests progress past `CreateSession()` (no "SOAP probe failed for all URIs")

---

## Phase 2 — UpgradeScheme honours device port + test-harness hardcode (Issue #26 Fix A + Fix C)

### Task 2.1 — UpgradeScheme maps HTTP→HTTPS via deviceUri port; make closure delegate
- **Files:**
  - `onvif/onvif.session/NvtSession.fs:478-485` (static `UpgradeScheme`)
  - `onvif/onvif.session/NvtSession.fs:771-779` (private `UpgradeSchemeIfNeeded` closure in `CreateSession(deviceUri)`)
- **What:**
  1. In `UpgradeScheme` (478-485) replace `if b.Port = 80 then b.Port <- 443` with
     ```fsharp
     let httpsPort = if deviceUri.IsDefaultPort then 443 else deviceUri.Port
     b.Port <- httpsPort
     ```
  2. Rewrite `UpgradeSchemeIfNeeded` (771-779) to delegate to the static: `let UpgradeSchemeIfNeeded (url: Uri) = NvtSessionFactory.UpgradeScheme deviceUri url` — single source of truth, no drift.
- **Done:** Closure body is a one-liner delegating to the static; build passes.
- **Tier:** cheap

### Task 2.2 — Update FixUrlHttpsTests to reflect new port-mapping contract
- **Files:** `odm/odm.tests/FixUrlHttpsTests.cs:44-55, 77-91`
- **What:**
  - Keep `UpgradeScheme_HttpUrl_Port80_WhenSessionIsHttps_ReturnsHttpsPort443` passing (default port 443 case still maps 80→443).
  - Rename/rewrite `UpgradeScheme_NonStandardHttpPort_MapsToSameNonStandardHttpsPort` → `UpgradeScheme_HttpUrl_MapsToDeviceHttpsPort` asserting: input `http://host:8080/...` with device `https://host/...` maps to `https://host:443/...` (port from device, not from the URL).
  - Add `UpgradeScheme_HttpUrl_WithNonDefaultHttpsDevicePort_MapsToDevicePort`: device `https://host:8443/...`, input `http://host:80/...` → result port 8443.
- **Done:** All 5 tests in `FixUrlHttpsTests` pass after changes.
- **Tier:** cheap

### Task 2.3 — Remove hardcoded `:443` from HttpsIntegrationTests (Fix C)
- **Files:** `odm/odm.tests/HttpsIntegrationTests.cs:64-66`
- **What:** Replace the hardcoded
  ```csharp
  new Uri(string.Format("https://{0}:443/onvif/device_service", host))
  ```
  with a build from `_host` + `_httpsPort`:
  ```csharp
  new Uri(string.Format("https://{0}:{1}/onvif/device_service", _host, _httpsPort))
  ```
  (The ClassInitialize code above already populates `_httpsPort` from `ODM_TEST_HTTPS_PORT` default 443.)
- **Done:** Test still passes against Milesight at port 443; would also pass if `ODM_TEST_HTTPS_PORT=8443`.
- **Tier:** cheap

### VERIFY 2
- [ ] `dotnet build odm/odm.tests/odm.tests.csproj -v quiet` → 0 errors
- [ ] `vstest.console.exe ... --TestCaseFilter:"TestCategory!=Integration"` → all pass including updated FixUrlHttpsTests
- [ ] No lingering `"https://.*:443"` string literals in `odm/odm.tests/HttpsIntegrationTests.cs` (grep check)

---

## Phase 3 — HTTP fallback for media service calls (Issue #26 Fix B) — part 1: plumbing

### Task 3.1 — Connection-refused detector + original-HTTP-xAddr memoization
- **Files:** `onvif/onvif.session/NvtSession.fs` — add new helpers inside `CreateSession(deviceUri)` immediately after `GetMedia2Client` ends (current line 1054) and before `MediaGetVideoSources` (current line 1056)
- **What:**
  1. Expose detector as `static member IsConnectionRefused (err: exn) : bool` on `NvtSessionFactory` (so unit tests can call it). Returns true when unwrapping any level of `AggregateException` / `InnerException` reveals:
     - `WebException` with `Status ∈ {ConnectFailure; SecureChannelFailure}`
     - `SocketException` with `SocketErrorCode ∈ {ConnectionRefused; ConnectionReset; HostUnreachable; TimedOut}`
     - `CommunicationException` whose inner matches
  2. Add memoized `GetMedia2HttpXAddr : unit -> Async<Uri>` — reads `GetServices()`, finds the Media2 service entry, returns `new Uri(service.XAddr)` without passing it through `FixUrl`. Returns `null` on absent service / absent services array.
  3. Add memoized `GetMedia1HttpXAddr : unit -> Async<Uri>` — reads `GetCapabilities()`, returns `new Uri(caps.media.xAddr)` without `FixUrl`. Returns `null` when media caps are null.
- **Done:**
  - Build passes.
  - New test class `odm/odm.tests/ConnectionRefusedDetectorTests.cs` covers:
    - WebException(ConnectFailure) → true
    - SocketException(ConnectionRefused) wrapped in AggregateException → true
    - CommunicationException wrapping SocketException → true
    - FaultException → false
    - new Exception("random") → false
- **Tier:** standard

### Task 3.2 — Fallback client factories (non-memoized, per retry)
- **Files:** `onvif/onvif.session/NvtSession.fs` — add near task 3.1 helpers
- **What:** Add two helpers:
  ```fsharp
  let createMedia2ClientAt (url: Uri) : Async<IMedia2> = async {
      do! Async.SwitchToThreadPool()
      let useTls = url.Scheme = Uri.UriSchemeHttps
      let! factory = getMedia2Factory(useTls)
      let proxy = factory.CreateChannel(new EndpointAddress(url))
      do! SetupUserNameToken(proxy :?> IClientChannel)
      return proxy
  }
  let createMediaClientAt (url: Uri) : Async<IMediaAsync> = async {
      do! Async.SwitchToThreadPool()
      let useTls = url.Scheme = Uri.UriSchemeHttps
      let! factory = getMediaFactory(useTls)
      let proxy = factory.CreateChannel(new EndpointAddress(url))
      do! SetupUserNameToken(proxy :?> IClientChannel)
      return (new MediaAsync(proxy) :> IMediaAsync)
  }
  ```
- **Done:** Build passes; helpers unused until Phase 4 wires them in.
- **Tier:** cheap

### VERIFY 3
- [ ] `dotnet build onvif/onvif.session/onvif.session.fsproj` → 0 errors
- [ ] `dotnet build odm/odm.tests/odm.tests.csproj` → 0 errors
- [ ] `vstest.console.exe ... --TestCaseFilter:"TestCategory!=Integration"` → baseline green + new `ConnectionRefusedDetectorTests` pass
- [ ] No behavior change on integration tests (plumbing only, not yet wired)

---

## Phase 4 — HTTP fallback wiring (Issue #26 Fix B) — part 2: per-operation retry

### Task 4.1 — Media2 HTTP-fallback combinator + wire into Media2 calls
- **Files:** `onvif/onvif.session/NvtSession.fs`
  - Combinator: add after Task 3.2 helpers (after `createMedia2ClientAt`)
  - Call sites: `GetProfiles` (1600), `GetStreamUri` (1630), `GetSnapshotUri` (1650), `GetVideoSourceConfigurations` (1748), `GetVideoEncoderConfigurations` (1765), `GetCompatibleVideoEncoderConfigurations` (1881), `SetVideoEncoderConfiguration` (1959), `GetVideoEncoderConfigurationOptions` (1998)
- **What:**
  1. Add combinator
     ```fsharp
     // Calls `work media2`. On connection-refused, rebuilds a Media2 client at the
     // original (non-upgraded) HTTP xAddr and retries once. Any other exception —
     // including the retry's exception — propagates unchanged.
     let withMedia2HttpFallback (media2: IMedia2) (work: IMedia2 -> Async<'T>) : Async<'T> = async {
         try
             return! work media2
         with err when NvtSessionFactory.IsConnectionRefused err ->
             let! httpXAddr = GetMedia2HttpXAddr()
             if httpXAddr |> IsNull then return raise err
             else
                 let! fallback = createMedia2ClientAt httpXAddr
                 return! work fallback
     }
     ```
  2. At each call site that currently reads `return! getXViaMedia2 media2 ...`, rewrite as `return! withMedia2HttpFallback media2 (fun m -> getXViaMedia2 m ...)`. The outer `try ... with err -> Media1 fallback` remains unchanged.
- **Done:**
  - Build passes; each of the 8 methods uses `withMedia2HttpFallback`.
  - No method retries on `FaultException` (guarded by `IsConnectionRefused`).
- **Tier:** standard

### Task 4.2 — Media1 HTTP-fallback combinator + wire into Media1 branches
- **Files:** `onvif/onvif.session/NvtSession.fs` — same 8 member implementations as 4.1, Media1 branches
- **What:**
  1. Add combinator
     ```fsharp
     let withMedia1HttpFallback (media1: IMediaAsync) (work: IMediaAsync -> Async<'T>) : Async<'T> = async {
         try
             return! work media1
         with err when NvtSessionFactory.IsConnectionRefused err ->
             let! httpXAddr = GetMedia1HttpXAddr()
             if httpXAddr |> IsNull then return raise err
             else
                 let! fallback = createMediaClientAt httpXAddr
                 return! work fallback
     }
     ```
  2. Wrap each `let! med = GetMediaClient()` + direct-call pair in the Media1 branches with `withMedia1HttpFallback med (fun m -> m.GetProfiles())`, etc. Preserve all existing `FaultException ActionNotSupported` handling — only the transport-layer retry is new.
- **Done:**
  - Build passes.
  - Against Milesight `192.168.1.190` via HTTPS session (device HTTPS, media HTTP):
    - `HttpsIntegrationTests.GetProfiles_ReturnsAtLeastOneProfile` passes
    - `HttpsIntegrationTests.GetStreamUri_ReturnsValidUri` passes
- **Tier:** standard

### Task 4.3 — Unit tests: combinator behavior
- **Files:** `odm/odm.tests/MediaHttpFallbackTests.cs` (new)
- **What:**
  - Fake IMedia2/IMediaAsync that throws a chosen exception.
  - Test a: primary throws `WebException(ConnectFailure)` → fallback client is invoked and returns success.
  - Test b: primary throws `FaultException` (not connection-refused) → no fallback invoked; exception propagates.
  - Test c: fallback xAddr is null → exception propagates.
- **Done:** All three tests pass; total offline test count increases.
- **Tier:** standard

### VERIFY 4
- [ ] `dotnet build odm/odm.tests/odm.tests.csproj` → 0 errors
- [ ] `vstest.console.exe ... --TestCaseFilter:"TestCategory!=Integration"` → all pass (baseline + new MediaHttpFallbackTests)
- [ ] Against Milesight `192.168.1.190` via HTTPS session:
  - `HttpsIntegrationTests.GetProfiles_ReturnsAtLeastOneProfile` passes
  - `HttpsIntegrationTests.GetStreamUri_ReturnsValidUri` passes
- [ ] Against Milesight `192.168.1.190` via HTTP session (Media2IntegrationTests): 7/7 pass

---

## Phase 5 — Final verification

### Task 5.1 — Release x64 build
- **Files:** none (build only)
- **What:** Run
  ```
  & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' \
      'C:\akhil\git\ONVIF-Device-Manager\odm.sln' /p:Configuration=Release /p:Platform=x64 /v:minimal
  ```
- **Done:** Exit code 0, no new errors vs. current baseline.
- **Tier:** cheap

### Task 5.2 — Full integration test pass against Milesight
- **Files:** none (test run)
- **What:** With `ODM_TEST_HOST=192.168.1.190`, `ODM_TEST_HTTPS_PORT=443`, `ODM_TEST_HTTP_PORT=80`, `ODM_TEST_USER`/`PASS` set, run
  ```
  vstest.console.exe odm.tests.dll --TestCaseFilter:"TestCategory=Integration"
  ```
- **Done:** 13/13 tests pass. Acceptance criteria section of `requirements.md` is satisfied.
- **Tier:** cheap

### VERIFY 5
- [ ] Release x64 build: PASSED, 0 new errors
- [ ] 13/13 integration tests against 192.168.1.190 PASS
- [ ] All offline unit tests PASS
- [ ] PR #23 comment thread updated noting Sprint 6 commits rebased on top

---

## Out of Scope (per `requirements.md`)
- Issues #20, #19, #14 — separate backlog items
- Media2 audio / PTZ / analytics / metadata operations
- Any change to Media2 feature logic shipped in PR #23
