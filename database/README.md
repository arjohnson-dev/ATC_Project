# Local production database

Created in the existing Oracle AI Database 26ai instance, inside pluggable database
`FREEPDB1`, under the dedicated schema `ATC_APP`.

## Connect with SQL Developer or another Oracle client

- Host: `localhost`
- Port: `1521`
- Connection type: Basic
- Service name: `FREEPDB1` (select Service name, not SID)
- Username: `ATC_APP`
- Password: see `connection.local.txt` in this directory.

The generated local password file is excluded from Git. Treat it as a credential.
For SQL*Plus, run `sqlplus ATC_APP@localhost:1521/FREEPDB1` and enter the password
when prompted.

## Contents

| Table | Seed rows |
| --- | ---: |
| Departments | 8 |
| Products | 40 |
| ProductionOrders | 200 |
| OrderStatusHistory | 480 |

There are 40 orders in each status: Planned, Released, In Progress, Completed,
and Cancelled. Due dates span 20 days before to 40 days after the seed run.
History includes the initial Planned status (with a null PreviousStatus) and
subsequent changes. Seed history dates are synthetic dates before the seed run.

Primary keys are generated automatically. Foreign keys link orders to products
and departments, and history to orders. Constraints validate quantities and
statuses. Completed orders must have their full quantity completed. A trigger
records initial status and every subsequent status change automatically.

## SQL scripts

These scripts have already been applied to your local database.

- `01_schema.sql`: creates the four tables, indexes, and history trigger.
- `02_mock_data.sql`: populates an empty schema in one transaction; refuses to
  seed if any table already contains rows.
- `03_verify.sql`: shows table counts, order totals by status, and sample orders.

To rebuild elsewhere, use a new empty Oracle schema with CREATE SESSION,
CREATE TABLE, CREATE TRIGGER privileges and a tablespace quota, then run scripts
01, 02, and 03 in order. Script 01 is intentionally not destructive or rerunnable
over existing tables.

## Example query

```sql
SELECT o.OrderId, p.PartNumber, d.Name AS Department,
       o.QuantityOrdered, o.QuantityCompleted, o.DueDate, o.Status
FROM ProductionOrders o
JOIN Products p ON p.ProductId = o.ProductId
JOIN Departments d ON d.DepartmentId = o.DepartmentId
ORDER BY o.DueDate, o.OrderId;
```

Oracle stores these unquoted table and column identifiers in uppercase; normal
unquoted queries may use any capitalization.
