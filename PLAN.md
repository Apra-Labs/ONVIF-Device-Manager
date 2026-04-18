# Sprint 8 — Media2 Typed Generation

## Goal

Replace the LINQ-to-XML Media2 hack with properly generated WCF types from the ONVIF Media2 WSDL
via `dotnet-svcutil`. Remove `Media2XmlParser`, raw `IMedia2`, and 8 per-operation if/else forks.
Introduce `routeMedia` Strategy helper and typed Media2 proxy.

Branch: `feat/media2-typed` from `development`

---

## Phase 1 — Generate typed Media2 proxy

**1.1** Create `feat/media2-typed` branch from `development`:
```
git -C C:\akhil\git\ONVIF-Device-Manager fetch origin
git -C C:\akhil\git\ONVIF-Device-Manager checkout -b feat/media2-typed origin/development
```

**1.2** Create project directory structure:
```
onvif/odm.onvif.gen/
  wsdl/
  odm.onvif.gen.csproj
```
`odm.onvif.gen.csproj` — SDK-style, targeting `net48`, referencing `System.ServiceModel`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>odm.onvif.gen</AssemblyName>
    <RootNamespace>onvif.services</RootNamespace>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.ServiceModel.Http" Version="6.*" />
    <PackageReference Include="System.ServiceModel.Duplex" Version="6.*" />
    <PackageReference Include="System.ServiceModel.Security" Version="6.*" />
  </ItemGroup>
</Project>
```

**1.3** Copy source files from `C:\akhil\alca\rock-onvif\onvif_gen\src\`:
- `wsdl\media2.wsdl` → `onvif/odm.onvif.gen/wsdl/media2.wsdl`
- `wsdl\media.wsdl` → `onvif/odm.onvif.gen/wsdl/media.wsdl`
- `wsdl\onvif.wsdl` → `onvif/odm.onvif.gen/wsdl/onvif.wsdl`
- `wsdl\catalog.xml` → `onvif/odm.onvif.gen/wsdl/catalog.xml`
- `schema\b-2.xsd` → `onvif/odm.onvif.gen/wsdl/b-2.xsd`
- `schema\bf-2.xsd` → `onvif/odm.onvif.gen/wsdl/bf-2.xsd`
- `schema\r-2.xsd` → `onvif/odm.onvif.gen/wsdl/r-2.xsd`
- `schema\t-1.xsd` → `onvif/odm.onvif.gen/wsdl/t-1.xsd`

**1.4** Install `dotnet-svcutil` if not present and run to generate `OnvifMedia2Gen.cs`:
```
cd C:\akhil\git\ONVIF-Device-Manager\onvif\odm.onvif.gen
dotnet tool install --global dotnet-svcutil   # if not installed
dotnet-svcutil ^
  -n "*,onvif.services" ^
  -n "http://www.onvif.org/ver10/schema,onvif.services" ^
  -n "http://www.onvif.org/ver20/media/wsdl,onvif.services" ^
  --serializer XmlSerializer ^
  -o OnvifMedia2Gen.cs ^
  wsdl/media2.wsdl
```
Internet access required (WSDL stubs import live ONVIF.org schemas).

If svcutil reports type conflicts with existing `onvif.services` types, re-run with:
```
--reference C:\akhil\git\ONVIF-Device-Manager\onvif\onvif.services\onvif.services.csproj
```
This tells svcutil to reuse types already in `onvif.services` instead of regenerating them.

Verify the generated `OnvifMedia2Gen.cs` contains:
- `interface IMedia2` with typed return types (NOT `System.ServiceModel.Channels.Message`)
- `class Media2Client : System.ServiceModel.ClientBase<IMedia2>`
- `class MediaProfile` with `Configurations: ConfigurationSet`
- `class ConfigurationSet` with `VideoEncoder: VideoEncoder2Configuration`
- `class VideoEncoder2Configuration` with `Encoding` as `string`

If svcutil generates duplicate type definitions that conflict with `onvif.types.cs` /
`Reference.cs` (e.g. `Profile`, `VideoEncoderConfiguration`), remove the duplicates from
`OnvifMedia2Gen.cs`. Keep only types that are new: the `IMedia2` interface, client proxy,
`MediaProfile`, `ConfigurationSet`, `VideoEncoder2Configuration`, and any Media2-specific types.

**1.5** Add `odm.onvif.gen.csproj` to `odm.sln`:
Use `dotnet sln C:\akhil\git\ONVIF-Device-Manager\odm.sln add C:\akhil\git\ONVIF-Device-Manager\onvif\odm.onvif.gen\odm.onvif.gen.csproj`

**V1 — VERIFY 1**
- Build: `msbuild C:\akhil\git\ONVIF-Device-Manager\odm.sln /p:Configuration=Release /p:Platform=x64 /v:minimal`
- Must succeed with 0 errors.
- Confirm `OnvifMedia2Gen.cs` is committed.
- Push: `git -C C:\akhil\git\ONVIF-Device-Manager push origin feat/media2-typed`
- **STOP** — report to PM.

---

## Phase 2 — Integrate typed IMedia2 into NvtSession.fs

**2.1** Add project reference from `onvif.session.fsproj` to `odm.onvif.gen.csproj`:
In `onvif/onvif.session/onvif.session.fsproj`, add:
```xml
<ProjectReference Include="..\odm.onvif.gen\odm.onvif.gen.csproj" />
```
(Remove any existing `ProjectReference` to the old IMedia2 if one exists.)

**2.2** Implement `routeMedia` Strategy helper in `NvtSession.fs`.
Find where `GetMedia2Client` is defined (around line 1218) and add `routeMedia` nearby:
```fsharp
/// Routes a Media operation: try Media2 first (with HTTP fallback), then fall back to Media1.
let routeMedia
        (media2Work: IMedia2     -> Async<'T>)
        (media1Work: IMediaAsync -> Async<'T>) : Async<'T> = async {
    let! m2 = GetMedia2Client()
    if m2 |> NotNull then
        try  return! withMedia2HttpFallback m2 media2Work
        with _ ->
            let! m1 = GetMediaClient()
            if m1 |> NotNull then return! withMedia1HttpFallback m1 media1Work
            else return raise (System.InvalidOperationException("No Media service available"))
    else
        let! m1 = GetMediaClient()
        if m1 |> NotNull then return! withMedia1HttpFallback m1 media1Work
        else return raise (System.InvalidOperationException("No Media service available"))
}
```
Replace `IMedia2` in the type annotation with the fully-qualified type from `onvif.services`
if needed (e.g., `onvif.services.IMedia2`).

**2.3** Add type mapping helpers (only if needed — check if svcutil reused existing types):
```fsharp
/// Map a generated MediaProfile to the existing Profile type used by INvtSession.
let private mapMedia2Profile (p: onvif.services.MediaProfile) : Profile =
    Profile(token = p.token, name = p.Name, ...)
    // Fill in all Profile fields from ConfigurationSet p.Configurations

/// Map VideoEncoder2Configuration (Encoding: string) to VideoEncoderConfiguration (VideoEncoding enum).
let private mapVideoEncoder2Config (c: onvif.services.VideoEncoder2Configuration) : VideoEncoderConfiguration =
    let encoding =
        match c.Encoding with
        | "H265" | "H.265" | "HEVC" -> VideoEncoding.h265
        | "H264" | "H.264"          -> VideoEncoding.H264
        | "MPEG4"                   -> VideoEncoding.MPEG4
        | _                         -> VideoEncoding.JPEG
    VideoEncoderConfiguration(encoding = encoding, ...)
```
Only add mappers that are actually needed. If svcutil reused `Profile` and
`VideoEncoderConfiguration` directly, no mapping needed.

**2.4** Replace the 8 per-operation if/else forks with `routeMedia` calls.
For each operation in `NvtSession.fs` that currently has:
```fsharp
let! media2 = GetMedia2Client()
if media2 |> NotNull then
    // Media2 path
    ...
else
    // Media1 path
    ...
```
Replace with:
```fsharp
return! routeMedia
    (fun m2 -> async { ... (* typed Media2 call *) })
    (fun m1 -> ... (* Media1 call as before *))
```
Operations to migrate:
- `GetProfiles`
- `GetStreamUri`
- `GetVideoEncoderConfigurations`
- `GetVideoEncoderConfigurationOptions`
- `SetVideoEncoderConfiguration`
- `GetAudioEncoderConfigurations`
- `SetAudioEncoderConfiguration`
- Any other operations that had the if/else fork in the feat/media2-support branch.

**2.5** Remove raw-XML approach code from `onvif/onvif.services/onvif.services.cs`:
- Delete the `interface IMedia2` block (the raw Message version — NOT the generated one).
- Delete all `Media2GetXxxRequest` and `Media2GetXxxResponse` classes.
- Delete `class Media2EncoderOptions`.
- Delete `static class Media2XmlParser` (~250 lines of LINQ-to-XML parsing).
- Remove any `using` statements that are now unused.

**V2 — VERIFY 2**
- Build Release x64. Must succeed with 0 errors.
- Run offline unit tests:
  `dotnet build C:\akhil\git\ONVIF-Device-Manager\odm\odm.tests\odm.tests.csproj -v quiet`
  `vstest.console.exe ... --TestCaseFilter:"TestCategory!=Integration"`
  All previously-passing tests (54) must still pass.
- Push: `git -C C:\akhil\git\ONVIF-Device-Manager push origin feat/media2-typed`
- **STOP** — report to PM.

---

## Phase 3 — Tests and docs

**3.1** Update test projects:
- **Delete** `odm/odm.tests/Media2XmlParserTests.cs` — tests the removed parser.
- **Review** `odm/odm.tests/Media2IntegrationTests.cs`:
  - Remove any tests that tested internal parsing behaviour of `Media2XmlParser`.
  - Keep behavioral integration tests (those that test `INvtSession` operations against a real camera).
  - Update any references to removed types (`Media2EncoderOptions`, raw `IMedia2`).

**3.2** Rewrite `docs/features/media2-routing.md`:
- Describe the typed approach: generated `IMedia2`, `routeMedia` helper, fallback chain.
- Explain how to add a new Media2 operation in future (call the typed method — no parser needed).
- Remove references to `Media2XmlParser` and raw Message approach.

**3.3** Update `docs/architecture.md`:
- Add a "Media2 Service Detection" subsection under the Transport Layer section.
- Cover: memoized `GetMedia2Client`, null-return on non-Media2 cameras, routeMedia helper,
  zero overhead for Media1-only cameras.

**3.4** Update `docs/features/media2-testing.md`:
- Remove references to `Media2XmlParser`.
- Update test file locations if any changed.
- Keep integration test instructions (ODM_TEST_HOST etc.) intact.

**V3 — VERIFY 3**
- Build Release x64. Must succeed.
- Run offline unit tests. All must pass.
- Confirm `Media2XmlParserTests.cs` is deleted.
- Push: `git -C C:\akhil\git\ONVIF-Device-Manager push origin feat/media2-typed`
- **STOP** — report to PM.
