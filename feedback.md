# Sprint 8 Media2 Typed Generation — Code Review

**Reviewer:** odm-rev
**Date:** 2026-04-18 18:45:00+05:30
**Phase reviewed:** Phase 1 (V1 checkpoint)
**Verdict:** APPROVED

> See git history of this file for prior review context.

---

## Generated Proxy — OnvifMedia2Gen.cs

PASS — File exists at `onvif/odm.onvif.gen/OnvifMedia2Gen.cs` (17,814 lines). Generated via `dotnet-svcutil 2.1.0` against the ONVIF Media2 WSDL.

**Typed interface:** The `Media2` interface contains fully typed return values (`Task<GetProfilesResponse>`, `Task<Capabilities2>`, etc.). Zero occurrences of `System.ServiceModel.Channels.Message` — confirms this is not a raw-Message proxy.

**Required types present:**
- `MediaProfile` (line 15388) with `Configurations: ConfigurationSet` — PASS
- `ConfigurationSet` (line 15204) with `VideoEncoder: VideoEncoder2Configuration` — PASS
- `VideoEncoder2Configuration` (line 14180) with `Encoding: string` — PASS
- `Media2Client : ClientBase<Media2>` (line 17262) — PASS

NOTE — The interface is named `Media2` (not `IMedia2`). This is standard svcutil naming. Phase 2 should use `onvif.services.Media2` as the type in `routeMedia` signatures rather than `IMedia2` as written in PLAN.md.

---

## Project Configuration — odm.onvif.gen.csproj

PASS — SDK-style project targeting `net48`. Uses framework references (`System.ServiceModel`, `System.Runtime.Serialization`) instead of NuGet `System.ServiceModel.Http 6.*`. This is the correct approach for net48 — the NuGet packages are for .NET Core/.NET 5+. The doer fixed this in commit `a6873fb`.

Namespace is `onvif.services` as specified.

---

## Solution Integration

PASS — `odm.onvif.gen` is listed in `odm.sln` with GUID `{A5CB567D-818E-4A1E-987A-DB8159EC23A1}`.

---

## WSDL and Schema Files

PASS — 10 files committed under `onvif/odm.onvif.gen/wsdl/`:
- `media2.wsdl`, `media.wsdl` (stub WSDLs pointing to ONVIF.org)
- `catalog.xml` (OASIS XML catalog for local schema resolution)
- `b-2.xsd`, `bf-2.xsd`, `r-2.xsd`, `t-1.xsd` (ONVIF base schemas)
- `bw-2.wsdl`, `rw-2.wsdl` (WS-BaseNotification WSDLs)
- `.gitkeep`

NOTE — The WSDL files are stubs that import from `http://www.onvif.org/`. The actual generation required internet access. The local schemas (`t-1.xsd`, `b-2.xsd`, etc.) are committed for reference but were not sufficient for offline generation. This is acceptable — regeneration is a rare operation.

---

## Build Verification

PARTIAL — `odm.onvif.gen.csproj` builds successfully in isolation via `dotnet build` with 0 errors, 0 warnings, producing `odm.onvif.gen.dll`. The full solution build via MSBuild.exe could not be executed in this review session (tool permission issue). The doer's V1 progress notes state "Release x64 build: 0 errors. odm.onvif.gen.dll built." — accepted with caveat.

---

## Commit History

Two clean commits on the branch:
1. `840d7fb` — Phase 1 generation (tasks 1.1–1.5)
2. `a6873fb` — Fix: switch from NuGet System.ServiceModel.Http to built-in framework reference

The fix commit shows good judgment — NuGet `System.ServiceModel.Http 6.*` targets .NET Core and is incompatible with net48.

---

## Summary

All Phase 1 "done" criteria are met. The generated proxy contains fully typed Media2 operations with no raw Message fallback. Types (`MediaProfile`, `ConfigurationSet`, `VideoEncoder2Configuration`) match ONVIF Media2 WSDL structure. The project is correctly configured for net48 and integrated into the solution.

One note for Phase 2: PLAN.md references `IMedia2` but the generated interface is named `Media2`. The doer should use `onvif.services.Media2` in `routeMedia` type signatures.

Build verification is partial (isolated project only, not full solution) — recommend confirming the full MSBuild Release x64 build at V2 checkpoint.
