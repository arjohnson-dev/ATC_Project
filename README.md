# ATC Production Order Tracker

An internal application for tracking manufacturing orders and generating a
repeatable exceptions report from fictional data. Employees can enter work,
record progress, identify overdue orders, and export report results to CSV.

The project has three layers:

| Layer | Project / directory | Responsibility |
| --- | --- | --- |
| Frontend | `Atc.Desktop` — VB.NET Windows Forms | Order entry, progress updates, filters, report display, and CSV export |
| Backend | `Atc.Api` — C# ASP.NET Core Web API | HTTP/JSON endpoints, validation, transactions, and Oracle access |
| Database | `database` — Oracle AI Database 26ai SQL scripts | Related records, integrity constraints, status history, and report calculations |

Requests flow from the desktop app to the API, then to Oracle; results return as
JSON for the desktop to display. All frontend database access goes through the
API. The desktop contains no database driver, SQL, or database credentials.

## 1. Using the frontend — VB.NET Windows Forms

### Start the application

Requires Windows, the .NET 10 SDK, and the local Oracle service. From the project
directory:

```powershell
cd C:\Repos\Oracle\ATC_Project
./scripts/Start-Tracker.ps1
```

This builds the desktop app, reuses the local API if available, or starts it in
the background, then opens the tracker. Requires the .NET 10 SDK and local Oracle
service. The background API stays running when the desktop window closes.

Alternatively, start the API in one terminal:

```powershell
dotnet run --project Atc.Api --launch-profile http
```

Then start the frontend in another terminal:

```powershell
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

The report's four summary cards show open orders, overdue orders, open units
remaining, and overdue units. The department grid groups overdue work, while the
detail grid shows each exception's order number, part, department, due date,
remaining quantity, days overdue, and status.

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

### Frontend code guide

| File | Purpose |
| --- | --- |
| `OrdersForm.vb` | Loads order choices and paginated orders; opens the editor and report |
| `OrderEditorForm.vb` | Creates orders, saves progress/status, and displays history |
| `ReportForm.vb` | Generates a report snapshot, shows totals/detail, and opens exception orders |
| `OrderFilters.vb` | Builds department, status, due-date, and search filters |
| `ApiClient.vb` | Sends asynchronous HTTP requests and translates API/connection errors |
| `CsvReport.vb` | Exports the displayed report snapshot and its metadata |
| `AsyncForm.vb` / `Ui.vb` | Shared request handling and visual controls |

### Five-minute demonstration

1. Open Exceptions report, select November 5, 2026, and Generate report.
2. Identify an overdue order and open it from the detail grid.
3. Choose Mark complete, then Save order. The report refreshes and removes it.
4. Open the same order from Orders and show its status history.
5. Show `database/04_exceptions_report.sql`, API validation and transaction code,
   and export a CSV containing the report metadata and totals.

Demo updates persist in the fictional database. Automated tests instead create
and remove their own order; they leave the original seed rows intact.

## 2. C# backend — ASP.NET Core Web API

ASP.NET Core Web API in C#, targeting .NET 10, connected to the existing local
Oracle `FREEPDB1` database as `ATC_APP`. Uses Oracle.ManagedDataAccess.Core,
parameterized SQL, controller validation, and the database's status-history trigger.
The local schema and report procedure are already installed. For another database,
apply the SQL scripts described in layer 3 before running the API.

### Run and configure the API

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

### Backend code guide and request flow

| File | Purpose |
| --- | --- |
| `Program.cs` | Registers controllers, the repository, OpenAPI, and error handling |
| `OrdersController.cs` | Routes order, history, catalog, and report requests |
| `Models.cs` | Defines request/response models and input validation rules |
| `OrderRepository.cs` | Executes parameterized Oracle commands, manages transactions, and calls the report procedure |
| `Atc.Api.http` | Example HTTP requests for exercising the API directly |

For example, saving a completed order sends a PUT request from the VB editor.
The controller validates the request and calls the repository. The repository
updates the order inside an Oracle transaction; the database trigger adds the
status-history entry in that same transaction. After commit, the API returns the
updated order as JSON. The report screen then requests a fresh report, so the
completed order disappears from the exceptions.

### Endpoints

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

#### Filtering and paging

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

#### Create an order

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

#### Update progress/status

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

#### Exception report response

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

### Validation and errors

400: invalid body/query, unsupported status, invalid quantities, or missing
product/department reference. 404: order not found. 409: database concurrency
conflict requiring reload/retry. Unexpected errors return generic 500 Problem
Details without database credentials or SQL details; server logs retain diagnostics.

### Build and verification

```powershell
dotnet build ATC.slnx
# In another PowerShell 7 terminal, while the API is running:
./tests/Smoke-Test.ps1
./tests/Desktop-Smoke-Test.ps1
dotnet list ATC.slnx package --vulnerable --include-transitive
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

## 3. Database — Oracle AI Database 26ai

This project uses the locally installed Oracle database, with service name
`FREEPDB1` and application schema `ATC_APP`. The C# API accesses it using Oracle's
managed ADO.NET provider. SQL Server, LocalDB, and `Microsoft.Data.SqlClient` are
not dependencies of this implementation.

### Connect and inspect the data

Use SQL Developer or another Oracle client with these settings:

| Setting | Value |
| --- | --- |
| Host | `localhost` |
| Port | `1521` |
| Service name | `FREEPDB1` — select Service name, not SID |
| Username | `ATC_APP` |
| Password | See the ignored local file `database/connection.local.txt` |

For SQL*Plus, run the following and enter the password when prompted:

```powershell
sqlplus ATC_APP@localhost:1521/FREEPDB1
```

The password stays out of the README and source control. The API reads its
connection string from .NET user secrets in Development, or from the environment;
the frontend only receives the API address.

### Tables, relationships, and mock data

| Table | Key fields and purpose | Initial seed rows |
| --- | --- | ---: |
| `Departments` | `DepartmentId`, unique `Name`; department choices | 8 |
| `Products` | `ProductId`, unique `PartNumber`, `Description`; product choices | 40 |
| `ProductionOrders` | `OrderId`, `ProductId`, `DepartmentId`, ordered/completed quantities, due date, status | 200 |
| `OrderStatusHistory` | `HistoryId`, `OrderId`, previous/new status, change timestamp | 480 |

Each order references one product and one department through foreign keys. Each
history row references an order. Primary keys are generated by Oracle identity
columns. The initial data includes 40 orders in each of the five statuses, with
due dates before and after the seed date. Counts change as users create or update
orders; the figures above describe the original seed.

Constraints require a positive ordered quantity, a nonnegative completed quantity
that does not exceed the ordered quantity, and one of the five allowed statuses.
Completed orders must have their full quantity completed. Foreign keys prevent
orders from referencing nonexistent products or departments.

### Automatic history and transactions

`trg_order_status_history` runs after an order is inserted or its status is
updated. It records the initial status with a null previous status and records
each actual status change. A progress-only update does not create another history
row when the status stays the same.

The trigger executes within the API's order transaction. The order update and
history entry commit together; if the transaction fails, both roll back. This
keeps the audit trail consistent without requiring the frontend to write history.

### Report SQL and calculation rules

`vw_OrderReportBase` joins orders, products, and departments and calculates
remaining quantity. `usp_GetOrderExceptions` applies filters, aggregates summary
and department totals, and returns detail rows through three Oracle REF CURSORs.
The API calls it inside a read-only transaction so all three result sets share
one consistent database snapshot.

| Calculation | Rule |
| --- | --- |
| Remaining units | Ordered quantity minus completed quantity |
| Open order | Remaining units greater than zero, excluding Completed and Cancelled |
| Overdue order / exception | An open order with a due date before the selected reporting date |
| Days overdue | Reporting date minus due date, using calendar dates |
| Department summary | Overdue order count and remaining units grouped by department |

Orders due on the reporting date are not overdue. The selected date evaluates
the order's current saved state; it does not reconstruct a historical snapshot.
All report totals respect the supplied filters. The VB app displays the API's
SQL-calculated values and exports that same snapshot.

### SQL scripts and setup order

The following scripts have already been applied locally:

| Script | Purpose |
| --- | --- |
| `database/01_schema.sql` | Creates tables, foreign keys, validation constraints, indexes, and the history trigger |
| `database/02_mock_data.sql` | Seeds fictional departments, products, orders, and status history in one transaction |
| `database/03_verify.sql` | Shows row counts, order totals by status, and sample joined orders |
| `database/04_exceptions_report.sql` | Creates/replaces the report view and stored procedure and checks compilation |

For another installation, create an empty Oracle schema with `CREATE SESSION`,
`CREATE TABLE`, `CREATE TRIGGER`, `CREATE VIEW`, and `CREATE PROCEDURE` privileges
and a tablespace quota. Connect as that schema and run scripts 01 through 04 in
order. Script 01 expects an empty schema; script 02 refuses to seed tables that
already contain data. Do not rerun them over the populated local database.

To inspect the existing database without changing data, run this from SQL*Plus
started in the project directory:

```sql
@database/03_verify.sql
```

See [database/README.md](database/README.md) for additional database setup details
and an example joined query.
