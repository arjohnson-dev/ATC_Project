// General database types, such as CommandType and ParameterDirection.
using System.Data;

// Oracle-specific connections, commands, parameters, and data types.
using Oracle.ManagedDataAccess.Client;

// Oracle return-value types, such as OracleDecimal and OracleRefCursor.
using Oracle.ManagedDataAccess.Types;

namespace Atc.Api;

// A repository handles communication with the database.
//
// "sealed" prevents inheritance.
//
// The constructor parameter IConfiguration configuration uses primary
// constructor syntax. ASP.NET Core can supply configuration through
// dependency injection.
public sealed class OrderRepository(IConfiguration configuration)
{
    // Shared FROM/JOIN clause used by multiple queries.
    //
    // o, p, and d are SQL aliases for the three tables.
    //
    // These are INNER JOINs: an order only appears if its matching
    // product and department also exist.
    private const string From =
        " FROM ProductionOrders o " +
        "JOIN Products p ON p.ProductId=o.ProductId " +
        "JOIN Departments d ON d.DepartmentId=o.DepartmentId ";

    // Shared SELECT clause.
    //
    // Triple quotes define a raw multiline string.
    //
    // The order of these columns matters because ReadOrders reads
    // them by their zero-based positions.
    //
    // :asOf is a bound parameter supplied when the command is created.
    //
    // The final two columns calculate:
    //   10: quantity remaining
    //   11: whether the order is overdue, represented as 1 or 0
    private const string Columns = """
        SELECT o.OrderId, o.ProductId, p.PartNumber, p.Description,
        o.DepartmentId, d.Name, o.QuantityOrdered, o.QuantityCompleted, o.DueDate, o.Status,
        o.QuantityOrdered-o.QuantityCompleted,
        CASE WHEN o.DueDate < :asOf AND o.QuantityCompleted < o.QuantityOrdered
             AND o.Status NOT IN ('Completed','Cancelled') THEN 1 ELSE 0 END
        """;

    // Opens an Oracle connection.
    //
    // Task<OracleConnection> means this asynchronous method eventually
    // returns an OracleConnection.
    //
    // CancellationToken lets the caller request cancellation.
    private async Task<OracleConnection> Open(CancellationToken ct)
    {
        // Reads the connection string named "Oracle" from configuration.
        var connection = new OracleConnection(
            configuration.GetConnectionString("Oracle"));

        try
        {
            // await allows the operation to finish asynchronously.
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            // If opening fails, clean up the connection before
            // passing the original exception back to the caller.
            await connection.DisposeAsync();
            throw;
        }
    }

    // Creates a configured Oracle command.
    //
    // Each parameter is a tuple containing:
    //   Name  -> the SQL parameter name
    //   Type  -> its Oracle database type
    //   Value -> its value, which can be null
    //
    // The parameter collection is optional and defaults to null.
    private static OracleCommand Command(
        OracleConnection connection,
        string sql,
        IEnumerable<(string Name, OracleDbType Type, object? Value)>?
            parameters = null)
    {
        var command = connection.CreateCommand();

        // Match parameters by name rather than their collection order.
        command.BindByName = true;

        // This can contain SQL text or, when configured later,
        // the name of a stored procedure.
        command.CommandText = sql;

        // Sets the command timeout to 30 seconds.
        command.CommandTimeout = 30;

        if (parameters is not null)
        {
            // Tuple deconstruction gives each tuple element a variable.
            foreach (var (name, type, value) in parameters)
            {
                // Bound parameters keep input values separate from SQL.
                //
                // DBNull.Value represents SQL NULL.
                // C# null and database NULL are different representations.
                command.Parameters.Add(name, type).Value =
                    value ?? DBNull.Value;
            }
        }

        return command;
    }

    // Gets today's date according to the Oracle database server.
    private static async Task<DateOnly> Today(
        OracleConnection connection,
        CancellationToken ct)
    {
        // SYSDATE is the database server's current date/time.
        // TRUNC removes its time component.
        // dual is Oracle's special one-row table.
        //
        // "using" disposes the command when this scope ends.
        using var command = Command(
            connection,
            "SELECT TRUNC(SYSDATE) FROM dual");

        // ExecuteScalarAsync returns the first column of the first row.
        //
        // The cast converts the result to DateTime.
        // '!' suppresses a nullable warning; it does not check for null.
        return DateOnly.FromDateTime(
            (DateTime)(await command.ExecuteScalarAsync(ct))!);
    }

    // Builds a WHERE clause and its associated parameter values.
    //
    // Returns both values together as a named tuple.
    private static (
        string Sql,
        List<(string Name, OracleDbType Type, object? Value)> Parameters)
        Where(OrderFilter filter, DateOnly asOf)
    {
        // Always true, making it convenient to append "AND ..." clauses.
        var sql = " WHERE 1=1 ";

        // asOf is needed by the shared SELECT clause's overdue calculation.
        var parameters = new List<(string, OracleDbType, object?)>
        {
            (
                "asOf",
                OracleDbType.Date,
                asOf.ToDateTime(TimeOnly.MinValue)
            )
        };

        // A local function, usable only inside Where.
        //
        // Adds a SQL condition and its matching parameter together.
        void Add(
            string clause,
            string name,
            OracleDbType type,
            object value)
        {
            sql += clause;
            parameters.Add((name, type, value));
        }

        // "is { } department" checks for a non-null value and
        // stores that value in the variable department.
        if (filter.DepartmentId is { } department)
            Add(
                " AND o.DepartmentId=:department",
                "department",
                OracleDbType.Int64,
                department);

        if (filter.ProductId is { } product)
            Add(
                " AND o.ProductId=:product",
                "product",
                OracleDbType.Int64,
                product);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            Add(
                " AND o.Status=:status",
                "status",
                OracleDbType.Varchar2,
                filter.Status);

        if (filter.DueFrom is { } start)
            Add(
                " AND o.DueDate>=:dueFrom",
                "dueFrom",
                OracleDbType.Date,
                start.ToDateTime(TimeOnly.MinValue));

        if (filter.DueTo is { } end)
        {
            // Oracle DATE includes a time component.
            //
            // Adding 86399/86400 days adds 23 hours, 59 minutes,
            // and 59 seconds, including the entire ending date
            // at Oracle DATE's second precision.
            Add(
                " AND o.DueDate <= :dueTo + (86399/86400)",
                "dueTo",
                OracleDbType.Date,
                end.ToDateTime(TimeOnly.MinValue));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Normalize the search text.
            //
            // Escape LIKE's special characters so user-entered
            // % and _ are treated literally.
            var search = filter.Search
                .Trim()
                .ToUpperInvariant()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            // UPPER makes the compared database text uppercase too.
            //
            // Surrounding the parameter value with % means
            // "contains this text."
            //
            // In the C# string, \\ becomes one literal backslash.
            Add(
                " AND (UPPER(p.PartNumber) LIKE :search ESCAPE '\\' " +
                "OR UPPER(p.Description) LIKE :search ESCAPE '\\')",
                "search",
                OracleDbType.Varchar2,
                "%" + search + "%");
        }

        return (sql, parameters);
    }

    // Executes a query and converts each result row into an OrderDto.
    private static async Task<List<OrderDto>> ReadOrders(
        OracleCommand command,
        CancellationToken ct)
    {
        var result = new List<OrderDto>();

        // A reader provides access to the returned rows.
        using var reader = await command.ExecuteReaderAsync(ct);

        // Move through the result one row at a time.
        while (await reader.ReadAsync(ct))
        {
            // Column positions start at zero and must match the SELECT.
            //
            // new(...) is inferred to mean new OrderDto(...).
            result.Add(new(
                reader.GetInt64(0),  // OrderId
                reader.GetInt64(1),  // ProductId
                reader.GetString(2), // PartNumber
                reader.GetString(3), // Description
                reader.GetInt64(4),  // DepartmentId
                reader.GetString(5), // DepartmentName
                reader.GetInt64(6),  // QuantityOrdered
                reader.GetInt64(7),  // QuantityCompleted
                DateOnly.FromDateTime(reader.GetDateTime(8)), // DueDate
                reader.GetString(9), // Status
                reader.GetInt64(10), // QuantityRemaining
                reader.GetInt32(11) == 1, // IsOverdue

                // Some queries may supply a 13th column: DaysOverdue.
                // If it is absent, use zero.
                reader.FieldCount > 12 ? reader.GetInt32(12) : 0));
        }

        return result;
    }

    // Retrieves totals for ALL matching orders plus ONE page of details.
    //
    // Returns a tuple containing totals, detail rows, and the reference date.
    private async Task<(
        OrderTotals Totals,
        List<OrderDto> Items,
        DateOnly AsOf)> Query(
            OrderFilter filter,
            CancellationToken ct)
    {
        using var connection = await Open(ct);

        // A read-only transaction gives subsequent queries the same
        // consistent database snapshot.
        //
        // This prevents another user's changes from making the totals
        // and detail queries disagree during this operation.
        using (var snapshot = Command(
            connection,
            "SET TRANSACTION READ ONLY"))
        {
            await snapshot.ExecuteNonQueryAsync(ct);
        }

        var date = await Today(connection, ct);

        // Deconstruct the tuple returned by Where.
        var (where, parameters) = Where(filter, date);

        OrderTotals totals;

        // Aggregate query:
        // COUNT(*) -> number of matching orders
        // SUM(...) -> combined quantities
        // NVL(...,0) -> return zero when a SUM is NULL
        //
        // This query has no pagination, so totals cover all matches.
        //
        // Exclude asOf because this SQL does not reference it.
        using (var command = Command(
            connection,
            "SELECT COUNT(*), " +
            "NVL(SUM(o.QuantityOrdered),0), " +
            "NVL(SUM(o.QuantityCompleted),0), " +
            "NVL(SUM(o.QuantityOrdered-o.QuantityCompleted),0)" +
            From + where,
            parameters.Where(p => p.Name != "asOf")))
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);

            // The last two optional OrderTotals constructor arguments
            // are omitted, so their defaults are used.
            totals = new(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3));
        }

        // Example: page 3 with 50 rows per page skips the first 100 rows.
        //
        // Cast to long before multiplication.
        parameters.Add((
            "offset",
            OracleDbType.Int64,
            (long)(filter.Page - 1) * filter.PageSize));

        parameters.Add((
            "pageSize",
            OracleDbType.Int32,
            filter.PageSize));

        // Sort by due date, then unique order ID for stable pagination.
        //
        // OFFSET skips earlier rows.
        // FETCH NEXT limits how many rows are returned.
        using var rows = Command(
            connection,
            Columns + From + where +
            " ORDER BY o.DueDate, o.OrderId " +
            "OFFSET :offset ROWS FETCH NEXT :pageSize ROWS ONLY",
            parameters);

        return (totals, await ReadOrders(rows, ct), date);
    }

    // Public method used to retrieve a page of orders.
    public async Task<OrderPage> List(
        OrderFilter filter,
        CancellationToken ct)
    {
        var result = await Query(filter, ct);

        // TotalCount describes all matches, while Items contains
        // only the requested page.
        return new(
            result.Items,
            result.Totals.OrderCount,
            filter.Page,
            filter.PageSize);
    }

    // Builds an exception report by calling an Oracle stored procedure.
    //
    // asOf: optional reference date for the report.
    // allRows: whether to return every matching detail row.
    public async Task<ExceptionReport> Exceptions(
        OrderFilter filter,
        DateOnly? asOf,
        bool allRows,
        CancellationToken ct)
    {
        using var connection = await Open(ct);

        // Keep the report's database reads on one consistent snapshot.
        using (var snapshot = Command(
            connection,
            "SET TRANSACTION READ ONLY"))
        {
            await snapshot.ExecuteNonQueryAsync(ct);
        }

        // ?? uses the right-hand value only when asOf is null.
        var date = asOf ?? await Today(connection, ct);

        // Prepare an optional "contains" search pattern.
        // The stored procedure must apply the matching LIKE/ESCAPE logic.
        var search = string.IsNullOrWhiteSpace(filter.Search)
            ? null
            : "%" + filter.Search.Trim().ToUpperInvariant()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_") + "%";

        // Supply the stored procedure's input parameters.
        //
        // ?. calls ToDateTime only if the nullable date has a value.
        //
        // condition ? first : second is a conditional expression.
        using var command = Command(
            connection,
            "usp_GetOrderExceptions",
            [
                ("p_as_of", OracleDbType.Date,
                    date.ToDateTime(TimeOnly.MinValue)),
                ("p_department", OracleDbType.Int64,
                    filter.DepartmentId),
                ("p_product", OracleDbType.Int64,
                    filter.ProductId),
                ("p_status", OracleDbType.Varchar2,
                    filter.Status),
                ("p_due_from", OracleDbType.Date,
                    filter.DueFrom?.ToDateTime(TimeOnly.MinValue)),
                ("p_due_to", OracleDbType.Date,
                    filter.DueTo?.ToDateTime(TimeOnly.MinValue)),
                ("p_search", OracleDbType.Varchar2, search),

                // When allRows is true, start at the first row.
                ("p_offset", OracleDbType.Int64,
                    allRows ? 0L : (long)(filter.Page - 1) * filter.PageSize),

                // Passing NULL here relies on the stored procedure
                // interpreting it as "no page-size limit."
                ("p_page_size", OracleDbType.Int32,
                    allRows ? null : filter.PageSize)
            ]);

        // Interpret CommandText as a stored procedure name.
        command.CommandType = CommandType.StoredProcedure;

        // REF CURSOR output parameters return result sets.
        //
        // This procedure is expected to return three:
        // totals, department summaries, and order details.
        var totalsCursor = command.Parameters.Add(
            "p_totals",
            OracleDbType.RefCursor,
            ParameterDirection.Output);

        var departmentsCursor = command.Parameters.Add(
            "p_departments",
            OracleDbType.RefCursor,
            ParameterDirection.Output);

        var detailsCursor = command.Parameters.Add(
            "p_details",
            OracleDbType.RefCursor,
            ParameterDirection.Output);

        // Execute the procedure; read its output cursors afterward.
        await command.ExecuteNonQueryAsync(ct);

        OrderTotals totals;

        using (var cursor = (OracleRefCursor)totalsCursor.Value)
        using (var reader = cursor.GetDataReader())
        {
            await reader.ReadAsync(ct);

            // The procedure must return these six columns in this order.
            totals = new(
                reader.GetInt64(0), // OrderCount
                reader.GetInt64(1), // QuantityOrdered
                reader.GetInt64(2), // QuantityCompleted
                reader.GetInt64(3), // QuantityRemaining
                reader.GetInt64(4), // OpenOrderCount
                reader.GetInt64(5)); // OpenQuantityRemaining
        }

        var departments = new List<DepartmentExceptionDto>();

        using (var cursor = (OracleRefCursor)departmentsCursor.Value)
        using (var reader = cursor.GetDataReader())
        {
            while (await reader.ReadAsync(ct))
            {
                departments.Add(new(
                    reader.GetInt64(0),  // DepartmentId
                    reader.GetString(1), // DepartmentName
                    reader.GetInt64(2),  // OrderCount
                    reader.GetInt64(3))); // QuantityRemaining
            }
        }

        var items = new List<OrderDto>();

        using (var cursor = (OracleRefCursor)detailsCursor.Value)
        using (var reader = cursor.GetDataReader())
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(new(
                    reader.GetInt64(0),  // OrderId
                    reader.GetInt64(1),  // ProductId
                    reader.GetString(2), // PartNumber
                    reader.GetString(3), // Description
                    reader.GetInt64(4),  // DepartmentId
                    reader.GetString(5), // DepartmentName
                    reader.GetInt64(6),  // QuantityOrdered
                    reader.GetInt64(7),  // QuantityCompleted
                    DateOnly.FromDateTime(reader.GetDateTime(8)),
                    reader.GetString(9), // Status
                    reader.GetInt64(10), // QuantityRemaining

                    // Assumes every row returned by the stored procedure
                    // is overdue. Column 11 is not read here.
                    true,

                    reader.GetInt32(12))); // DaysOverdue
            }
        }

        // Package everything into the report DTO.
        //
        // For allRows, report one page containing every returned item.
        // GeneratedAt uses the API host's current UTC time.
        return new(
            date,
            totals,
            items,
            allRows ? 1 : filter.Page,
            allRows ? items.Count : filter.PageSize,
            departments,
            DateTimeOffset.UtcNow,
            allRows);
    }

    // Returns an order's status history.
    //
    // null means the order does not exist.
    // An empty list means it exists but has no history rows.
    public async Task<List<StatusHistoryDto>?> History(
        long id,
        CancellationToken ct)
    {
        using var connection = await Open(ct);

        if (await Get(connection, id, ct) is null)
            return null;

        using var command = Command(
            connection,
            "SELECT HistoryId,PreviousStatus,NewStatus,ChangedAt " +
            "FROM OrderStatusHistory WHERE OrderId=:id " +
            "ORDER BY ChangedAt,HistoryId",
            [("id", OracleDbType.Int64, id)]);

        using var reader = await command.ExecuteReaderAsync(ct);

        var result = new List<StatusHistoryDto>();

        while (await reader.ReadAsync(ct))
        {
            // Read an Oracle TIMESTAMP WITH TIME ZONE value.
            var timestamp = reader.GetOracleTimeStampTZ(3);

            // Preserve its clock time and explicit UTC offset.
            //
            // Unspecified prevents DateTime from being treated as
            // already local or UTC when applying the supplied offset.
            var changedAt = new DateTimeOffset(
                DateTime.SpecifyKind(
                    timestamp.Value,
                    DateTimeKind.Unspecified),
                timestamp.GetTimeZoneOffset());

            result.Add(new(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                changedAt));
        }

        return result;
    }

    // Internal Get overload that reuses an existing connection.
    //
    // Create and Update use it to read back an order within
    // the same transaction as their write.
    private static async Task<OrderDto?> Get(
        OracleConnection connection,
        long id,
        CancellationToken ct)
    {
        using var command = Command(
            connection,
            Columns + From + " WHERE o.OrderId=:id",
            [
                ("asOf", OracleDbType.Date,
                    (await Today(connection, ct))
                        .ToDateTime(TimeOnly.MinValue)),
                ("id", OracleDbType.Int64, id)
            ]);

        // Returns the one matching order, or null if there is none.
        // Throws if more than one matching row is returned.
        return (await ReadOrders(command, ct)).SingleOrDefault();
    }

    // Public Get overload that manages its own connection.
    public async Task<OrderDto?> Get(long id, CancellationToken ct)
    {
        using var connection = await Open(ct);
        return await Get(connection, id, ct);
    }

    // Inserts a new production order and returns the saved order.
    public async Task<OrderDto> Create(
        CreateOrderRequest request,
        CancellationToken ct)
    {
        using var connection = await Open(ct);

        // Start a transaction so the write can be committed together.
        // Disposing an uncommitted transaction rolls it back.
        using var transaction = connection.BeginTransaction();

        // RETURNING captures the inserted order's ID without a
        // separate query to discover which ID was generated.
        //
        // This code expects request validation to have run already.
        // DueDate!.Value will fail if DueDate is actually null.
        using var command = Command(
            connection,
            """
            INSERT INTO ProductionOrders (ProductId,DepartmentId,QuantityOrdered,QuantityCompleted,DueDate,Status)
            VALUES (:product,:department,:ordered,:completed,:due,:status) RETURNING OrderId INTO :id
            """,
            [
                ("product", OracleDbType.Int64, request.ProductId),
                ("department", OracleDbType.Int64, request.DepartmentId),
                ("ordered", OracleDbType.Int64, request.QuantityOrdered),
                ("completed", OracleDbType.Int64, request.QuantityCompleted),
                ("due", OracleDbType.Date,
                    request.DueDate!.Value.ToDateTime(TimeOnly.MinValue)),
                ("status", OracleDbType.Varchar2, request.Status)
            ]);

        // Output parameter receives the generated OrderId.
        var id = command.Parameters.Add(
            "id",
            OracleDbType.Int64,
            ParameterDirection.Output);

        await command.ExecuteNonQueryAsync(ct);

        // Convert Oracle's numeric output to a C# long,
        // then retrieve the full order with its joined fields.
        //
        // '!' expresses the assumption that the inserted order is found.
        var order = (await Get(
            connection,
            ((OracleDecimal)id.Value).ToInt64(),
            ct))!;

        // Make the insert permanent.
        await transaction.CommitAsync(ct);

        return order;
    }

    // Updates the supplied fields on an existing order.
    // Returns null if no order matches the ID.
    public async Task<OrderDto?> Update(
        long id,
        UpdateOrderRequest request,
        CancellationToken ct)
    {
        using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction();

        // NVL(newValue, existingValue) preserves the existing field
        // when its input parameter is NULL.
        //
        // Both fields change in one atomic UPDATE statement.
        //
        // Business rules such as completed <= ordered must be
        // enforced by database constraints/triggers or other code;
        // this statement alone does not implement those checks.
        using var command = Command(
            connection,
            """
            UPDATE ProductionOrders SET QuantityCompleted=NVL(:completed,QuantityCompleted),
            Status=NVL(:status,Status) WHERE OrderId=:id
            """,
            [
                ("completed", OracleDbType.Int64,
                    request.QuantityCompleted),
                ("status", OracleDbType.Varchar2, request.Status),
                ("id", OracleDbType.Int64, id)
            ]);

        // ExecuteNonQueryAsync returns the number of affected rows
        // for this UPDATE.
        if (await command.ExecuteNonQueryAsync(ct) == 0)
            return null;

        // Read the updated values before committing.
        var order = await Get(connection, id, ct);

        await transaction.CommitAsync(ct);

        return order;
    }

    // Returns departments, typically for a frontend dropdown.
    public async Task<List<DepartmentDto>> Departments(
        CancellationToken ct)
    {
        using var connection = await Open(ct);

        using var command = Command(
            connection,
            "SELECT DepartmentId,Name FROM Departments ORDER BY Name");

        using var reader = await command.ExecuteReaderAsync(ct);

        var result = new List<DepartmentDto>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new(
                reader.GetInt64(0),
                reader.GetString(1)));
        }

        return result;
    }

    // Returns products, typically for a frontend dropdown.
    public async Task<List<ProductDto>> Products(CancellationToken ct)
    {
        using var connection = await Open(ct);

        using var command = Command(
            connection,
            "SELECT ProductId,PartNumber,Description " +
            "FROM Products ORDER BY PartNumber");

        using var reader = await command.ExecuteReaderAsync(ct);

        var result = new List<ProductDto>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return result;
    }
}