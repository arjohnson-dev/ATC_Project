# ATC Windows executables

Self-contained, single-file application builds for Windows x64. The .NET runtime
is included; no .NET SDK or separate runtime installation is required. Keep each
EXE with its configuration files. The Oracle database and seeded schema/report
procedure must still be available to the backend.

## Start the backend first

Open PowerShell in the Backend folder and run:

```powershell
./Start-Backend.ps1
```

The API listens at http://localhost:5080. Keep the console open; Ctrl+C stops it.
Stop a development API already using port 5080 before starting this copy. This
EXE runs the API as a console process; it is not registered as a Windows Service.

Configure the Oracle connection through `ConnectionStrings__Oracle`, or copy
`appsettings.Production.example.json` to `appsettings.Production.json` and set the
actual password and database address. The example file is not used automatically.
If published with `-ConfigureLocalDatabase`, the working Production configuration
has already been created from the local credential file. It contains a password;
exclude it when sharing these builds. Development user secrets are not included.

The helper explicitly selects Production. Double-clicking `Atc.Api.exe` also works
with the Production config when no environment override is set. It reads its
configuration beside the EXE, independent of the current working directory.

## Start the frontend

Double-click `Frontend/Atc.Desktop.exe`. Its `appsettings.json` defaults to
http://localhost:5080/. Change `ApiBaseUrl` there, or set `ATC_API_URL`, when using
another API address. The frontend needs no Oracle credentials or Oracle client.

The backend remains local-only by default. This demo has no authentication;
configure access controls and HTTPS before deploying it for shared network use.

## Rebuild from source

From the source project directory:

```powershell
./scripts/Publish-Windows.ps1
# To also configure this machine's published backend using its local credentials:
./scripts/Publish-Windows.ps1 -ConfigureLocalDatabase
```

Close published EXEs before replacing their files. Publishing does not copy the
database itself. See the source README for schema setup, usage, and tests.
