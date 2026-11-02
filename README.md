# ATC Production Order Tracker

VB.NET Windows Forms frontend, C# ASP.NET Core API, and the existing local Oracle
AI Database. All desktop data access goes through HTTP/JSON; the frontend contains
no database driver, SQL, or database credentials. The SQL Server suggestions in
the original example are implemented with Oracle to match this project's database.

## Start the application

```powershell
./scripts/Start-Tracker.ps1
```

This builds the desktop app, reuses the local API if available, or starts it in
the background, then opens the tracker. Requires the .NET 10 SDK and local Oracle
service. The background API stays running when the desktop window closes.

Alternatively run these in separate terminals:

```powershell
dotnet run --project Atc.Api --launch-profile http
dotnet run --project Atc.Desktop
```

Open `ATC.slnx` in a Visual Studio version supporting .NET 10 with the .NET desktop
development workload to work with both projects.

### Desktop screens

1. **Orders:** department, status, inclusive optional due dates, and part/description
   search; 50-row paging; New order; Open selected; Exceptions report. Double-click
   a row to edit. Apply filters to load results; Reset clears them.
2. **Order editor:** choose product and department, enter quantities and due date,
   and save. Existing orders allow progress/status changes. Mark complete fills
   completed quantity and chooses Completed; Save order commits it. The history
   grid displays initial status and subsequent changes through the API.
3. **Exceptions report:** choose a reporting date and optional filters, generate
   the report, review four summary cards and overdue work by department, then
   open an order or export CSV. Saving from the report regenerates it immediately.
   Completed work disappears from the exception detail.

Requests run asynchronously, controls prevent duplicate submissions, and closing
a window cancels its outstanding requests. The editor checks quantities locally;
the API and Oracle also validate them. Connection errors explain how to retry.
If a save times out or loses its response, refresh the order before retrying.

CSV exports contain **all** rows from the displayed report snapshot, its filters,
reporting date, generation timestamp, summary, department totals, and detail.
The report requests `allRows=true` once so paging or intervening updates cannot
cause an export assembled from inconsistent pages. Text fields escape quotes,
commas, and newlines, and neutralize spreadsheet formula prefixes. Output is UTF-8
with a BOM. Changing report filters disables export until regeneration.
The full snapshot is loaded in memory, appropriate for this small demo; very large
production reports would need a server-side streaming export.

Configure the desktop API address in `Atc.Desktop/appsettings.json` (copied to the
output directory), or override it with `ATC_API_URL`. Default: `http://localhost:5080/`.

### Five-minute demonstration

1. Open Exceptions report, select November 5, 2026, and Generate report.
2. Identify an overdue order and open it from the detail grid.
3. Choose Mark complete, then Save order. The report refreshes and removes it.
4. Open the same order from Orders and show its status history.
5. Show `database/04_exceptions_report.sql`, API validation and transaction code,
   and export a CSV containing the report metadata and totals.

Demo updates persist in the fictional database. Automated tests instead create
and remove their own order; they leave the original seed rows intact.

## API

ASP.NET Core Web API in C#, targeting .NET 10, connected to the existing local
Oracle `FREEPDB1` database as `ATC_APP`. Uses Oracle.ManagedDataAccess.Core,
parameterized SQL, controller validation, and the database's status-history trigger.
No database rebuild or migration is needed.

## Run locally

The Oracle connection is already configured in .NET user secrets on this machine.
From this directory:

```powershell
dotnet run --project Atc.Api --launch-profile http
```

Base URL: `http://localhost:5080`. OpenAPI JSON (Development only):
`http://localhost:5080/openapi/v1.json`.

Sample requests are in `Atc.Api/Atc.Api.http` for Visual Studio/VS Code HTTP clients.

For a different checkout/user, run:

```powershell
./scripts/Configure-LocalDatabase.ps1
```

This reads the ignored `database/connection.local.txt` and stores the connection
outside the repository in .NET user secrets. Alternatively configure the
`ConnectionStrings__Oracle` environment variable. The application fails at startup
with a configuration message if the connection string is missing. User secrets
load in the Development environment, as configured in the launch profile.

This is a local development API with no authentication. The supplied launch
profile binds to localhost. Authentication and deployment configuration are not
included.

## Endpoints

| Method | URL | Response |
| --- | --- | --- |
| GET | `/api/orders` | `{ items, totalCount, page, pageSize }` |
| GET | `/api/orders/{id}` | One order, or 404 |
| POST | `/api/orders` | Created order, 201, and a Location header |
| PUT | `/api/orders/{id}` | Updated order, or 404 |
| GET | `/api/departments` | Array of `{ departmentId, name }` |
| GET | `/api/products` | Array of `{ productId, partNumber, description }` |
| GET | `/api/reports/order-exceptions` | `{ asOfDate, totals, departments, items, generatedAt, allRows, page, pageSize }` |
| GET | `/api/orders/{id}/history` | Status history, ordered by change time and history ID |

Order responses include product/department IDs and display fields, ordered and
completed quantities, due date, status, quantity remaining, and `isOverdue`.

### Filtering and paging

Both order listing and the exception report accept these query parameters:

- `departmentId`, `productId`: positive IDs.
- `status`: `Planned`, `Released`, `In Progress`, `Completed`, or `Cancelled`
  (case sensitive).
- `dueFrom`, `dueTo`: inclusive calendar dates in `YYYY-MM-DD` format.
- `search`: case-insensitive literal substring of product part number or description,
  at most 100 characters. `%` and `_` are treated as literal characters.
- `page`: 1-based, default 1, maximum 1,000,000.
- `pageSize`: default 50, range 1–200.

Results sort by due date then order ID. Counts and report totals cover all filtered
rows, regardless of page size, including when the requested page is empty. Totals
and rows are queried within one read-only Oracle transaction for consistency.

```text
GET /api/orders?departmentId=1&status=In%20Progress&dueTo=2026-11-30&pageSize=25
```

### Create

```json
{
  "productId": 1,
  "departmentId": 1,
  "quantityOrdered": 100,
  "dueDate": "2026-11-15"
}
```

Required: productId, departmentId, quantityOrdered, dueDate. Status defaults to
`Planned`; quantityCompleted defaults to 0. Both may also be explicitly provided.
IDs are generated by Oracle; use the returned orderId for subsequent requests.

### Update progress/status

```json
{
  "quantityCompleted": 25,
  "status": "In Progress"
}
```

PUT accepts quantityCompleted, status, or both. An omitted/null value preserves
the saved value. Empty updates and unsupported JSON fields return 400.
Other order fields cannot be changed through this endpoint. Quantities are whole
numbers up to 9,999,999,999. Completed quantity cannot exceed ordered quantity;
status Completed requires the full quantity completed. Status transitions among
the five allowed values are not otherwise restricted. Simultaneous writes to the
same field use the last successful write; this API has no version/ETag check.

The existing database trigger records initial status and actual status changes.
Progress-only changes do not add history rows. Writes and their history commit
atomically; invalid writes roll back.

### Exception report

An exception is an order whose due date is **before** the as-of date, with quantity
remaining, excluding Completed and Cancelled orders. An order due on the as-of
date is not overdue. This is the initial report definition and can be changed to
match your business rules.

The report accepts optional `asOfDate=YYYY-MM-DD`. Default is the Oracle server's
current calendar date (`TRUNC(SYSDATE)`), matching the seed data's date convention.
The regular order endpoints use that same current date for `isOverdue`.
An explicit report asOfDate evaluates the **current saved order state** against
that date; it does not reconstruct historical state from status history.

`totals` contains `orderCount`, `quantityOrdered`, `quantityCompleted`, and
`quantityRemaining` for overdue orders, plus `openOrderCount` and
`openQuantityRemaining` for all open work matching the filters. Open means units
remaining and a status other than Completed or Cancelled. `departments` groups
overdue counts and remaining quantities. All totals respect every supplied filter.
`items` contains the exception orders, including SQL-calculated `daysOverdue`.
`allRows=true` returns all matching detail rows in one snapshot; otherwise the
standard page/pageSize parameters apply. `generatedAt` is the API's generation
timestamp. Calculations live in Oracle procedure `usp_GetOrderExceptions`, which
queries `vw_OrderReportBase` and returns three REF CURSORs in a read-only transaction.

### Errors

400: invalid body/query, unsupported status, invalid quantities, or missing
product/department reference. 404: order not found. 409: database concurrency
conflict requiring reload/retry. Unexpected errors return generic 500 Problem
Details without database credentials or SQL details; server logs retain diagnostics.

## Verification

```powershell
dotnet build Atc.Api
# In another PowerShell 7 terminal, while the API is running:
./tests/Smoke-Test.ps1
./tests/Desktop-Smoke-Test.ps1
dotnet list Atc.Api package --vulnerable --include-transitive
```

The API integration script checks all seven original endpoints against the local Oracle
database, verifies report totals with independent SQL, and checks history tracking.
It requires SQL*Plus on PATH and the local credential file. It creates one test
order and removes that order and its history in a finally block; existing orders
are not modified. Oracle identity values advance during testing.

The desktop integration script opens the actual VB forms and uses the real HTTP
client to create an overdue order, validate input, generate/export the report,
complete the order, verify its removal and history, test filters and empty results,
and confirm a friendly connection error. It saves screenshots and CSV under ignored
`test-results/`. Requires PowerShell 7, SQL*Plus and the local credential file;
its finally block removes its test order and history. The desktop app must be
built and the API running before the test starts.

The direct System.Security.Cryptography.Pkcs dependency overrides an older
vulnerable transitive dependency from the Oracle driver.

See `database/README.md` for schema and mock-data scripts.
