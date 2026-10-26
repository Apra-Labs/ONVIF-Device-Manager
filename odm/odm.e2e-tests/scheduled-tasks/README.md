# Scheduled Tasks for E2E Suite

These XML exports register the two Windows Scheduled Tasks the e2e
suite needs on a Windows test host. They run elevated (ODM requires
elevation for WS-Discovery).

## Tasks

| Task | Action | Purpose |
|---|---|---|
| `LaunchODM` | `C:\ak\ODM-Test\build\odm.exe` | Launches ODM elevated; optional helper task |
| `RunE2ETest` | `C:\odm-e2e-results\run-e2e.bat` | Runs the full e2e suite elevated; writes logs under `C:\odm-e2e-results\reports\` |

## Import on a new machine

Run in an **Administrator** prompt. The `/ru` override replaces the
machine-specific SID captured in the XML with the current user.

```cmd
schtasks /create /xml "odm\odm.e2e-tests\scheduled-tasks\LaunchODM.xml"  /tn "\LaunchODM"   /ru "%USERDOMAIN%\%USERNAME%" /f
schtasks /create /xml "odm\odm.e2e-tests\scheduled-tasks\RunE2ETest.xml" /tn "\RunE2ETest"  /ru "%USERDOMAIN%\%USERNAME%" /f
```

After importing, confirm:

```cmd
schtasks /query /tn \RunE2ETest /fo list /v
```

## Trigger

```cmd
schtasks /run /tn \RunE2ETest
```

See `docs/e2e-testing.md` for the full runbook.
