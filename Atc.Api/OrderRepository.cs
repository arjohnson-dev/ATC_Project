using System.Data;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace Atc.Api;

public sealed class OrderRepository(IConfiguration configuration)
{
    private const string From = " FROM ProductionOrders o JOIN Products p ON p.ProductId=o.ProductId JOIN Departments d ON d.DepartmentId=o.DepartmentId ";
    private const string Columns = """
        SELECT o.OrderId, o.ProductId, p.PartNumber, p.Description,
        o.DepartmentId, d.Name, o.QuantityOrdered, o.QuantityCompleted, o.DueDate, o.Status,
        o.QuantityOrdered-o.QuantityCompleted,
        CASE WHEN o.DueDate < :asOf AND o.QuantityCompleted < o.QuantityOrdered
             AND o.Status NOT IN ('Completed','Cancelled') THEN 1 ELSE 0 END
        """;

    private async Task<OracleConnection> Open(CancellationToken ct)
    {
        var connection = new OracleConnection(configuration.GetConnectionString("Oracle"));
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static OracleCommand Command(OracleConnection connection, string sql,
        IEnumerable<(string Name, OracleDbType Type, object? Value)>? parameters = null)
    {
        var command = connection.CreateCommand();
        command.BindByName = true;
        command.CommandText = sql;
        command.CommandTimeout = 30;
        if (parameters is not null)
            foreach (var (name, type, value) in parameters)
                command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
        return command;
    }

    private static async Task<DateOnly> Today(OracleConnection connection, CancellationToken ct)
    {
        using var command = Command(connection, "SELECT TRUNC(SYSDATE) FROM dual");
        return DateOnly.FromDateTime((DateTime)(await command.ExecuteScalarAsync(ct))!);
    }

    private static (string Sql, List<(string Name, OracleDbType Type, object? Value)> Parameters)
        Where(OrderFilter filter, DateOnly asOf, bool exceptions)
    {
        var sql = " WHERE 1=1 ";
        var parameters = new List<(string, OracleDbType, object?)>
            { ("asOf", OracleDbType.Date, asOf.ToDateTime(TimeOnly.MinValue)) };
        void Add(string clause, string name, OracleDbType type, object value)
        {
            sql += clause;
            parameters.Add((name, type, value));
        }
        if (filter.DepartmentId is { } department) Add(" AND o.DepartmentId=:department", "department", OracleDbType.Int64, department);
        if (filter.ProductId is { } product) Add(" AND o.ProductId=:product", "product", OracleDbType.Int64, product);
        if (!string.IsNullOrWhiteSpace(filter.Status)) Add(" AND o.Status=:status", "status", OracleDbType.Varchar2, filter.Status);
        if (filter.DueFrom is { } start) Add(" AND o.DueDate>=:dueFrom", "dueFrom", OracleDbType.Date, start.ToDateTime(TimeOnly.MinValue));
        if (filter.DueTo is { } end)
        {
            // Inclusive calendar date, including Oracle DATE values with a time component.
            Add(" AND o.DueDate <= :dueTo + (86399/86400)", "dueTo", OracleDbType.Date, end.ToDateTime(TimeOnly.MinValue));
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpperInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            Add(" AND (UPPER(p.PartNumber) LIKE :search ESCAPE '\\' OR UPPER(p.Description) LIKE :search ESCAPE '\\')",
                "search", OracleDbType.Varchar2, "%" + search + "%");
        }
        if (exceptions) sql += " AND o.DueDate < :asOf AND o.QuantityCompleted < o.QuantityOrdered AND o.Status NOT IN ('Completed','Cancelled')";
        return (sql, parameters);
    }

    private static async Task<List<OrderDto>> ReadOrders(OracleCommand command, CancellationToken ct)
    {
        var result = new List<OrderDto>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt64(4), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7),
                DateOnly.FromDateTime(reader.GetDateTime(8)), reader.GetString(9), reader.GetInt64(10), reader.GetInt32(11) == 1));
        return result;
    }

    private async Task<(OrderTotals Totals, List<OrderDto> Items, DateOnly AsOf)> Query(
        OrderFilter filter, bool exceptions, DateOnly? asOf, CancellationToken ct)
    {
        using var connection = await Open(ct);
        // Totals and paginated detail rows share one consistent Oracle snapshot.
        using (var snapshot = Command(connection, "SET TRANSACTION READ ONLY")) await snapshot.ExecuteNonQueryAsync(ct);
        var date = asOf ?? await Today(connection, ct);
        var (where, parameters) = Where(filter, date, exceptions);
        OrderTotals totals;
        using (var command = Command(connection,
            "SELECT COUNT(*), NVL(SUM(o.QuantityOrdered),0), NVL(SUM(o.QuantityCompleted),0), NVL(SUM(o.QuantityOrdered-o.QuantityCompleted),0)" + From + where,
            parameters.Where(p => p.Name != "asOf" || exceptions)))
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            totals = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        }
        parameters.Add(("offset", OracleDbType.Int64, (long)(filter.Page - 1) * filter.PageSize));
        parameters.Add(("pageSize", OracleDbType.Int32, filter.PageSize));
        using var rows = Command(connection, Columns + From + where +
            " ORDER BY o.DueDate, o.OrderId OFFSET :offset ROWS FETCH NEXT :pageSize ROWS ONLY", parameters);
        return (totals, await ReadOrders(rows, ct), date);
    }

    public async Task<OrderPage> List(OrderFilter filter, CancellationToken ct)
    {
        var result = await Query(filter, false, null, ct);
        return new(result.Items, result.Totals.OrderCount, filter.Page, filter.PageSize);
    }

    public async Task<ExceptionReport> Exceptions(OrderFilter filter, DateOnly? asOf, CancellationToken ct)
    {
        var result = await Query(filter, true, asOf, ct);
        return new(result.AsOf, result.Totals, result.Items, filter.Page, filter.PageSize);
    }

    private static async Task<OrderDto?> Get(OracleConnection connection, long id, CancellationToken ct)
    {
        using var command = Command(connection, Columns + From + " WHERE o.OrderId=:id",
            [("asOf", OracleDbType.Date, (await Today(connection, ct)).ToDateTime(TimeOnly.MinValue)), ("id", OracleDbType.Int64, id)]);
        return (await ReadOrders(command, ct)).SingleOrDefault();
    }

    public async Task<OrderDto?> Get(long id, CancellationToken ct)
    {
        using var connection = await Open(ct);
        return await Get(connection, id, ct);
    }

    public async Task<OrderDto> Create(CreateOrderRequest request, CancellationToken ct)
    {
        using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection, """
            INSERT INTO ProductionOrders (ProductId,DepartmentId,QuantityOrdered,QuantityCompleted,DueDate,Status)
            VALUES (:product,:department,:ordered,:completed,:due,:status) RETURNING OrderId INTO :id
            """, [("product", OracleDbType.Int64, request.ProductId), ("department", OracleDbType.Int64, request.DepartmentId),
            ("ordered", OracleDbType.Int64, request.QuantityOrdered), ("completed", OracleDbType.Int64, request.QuantityCompleted),
            ("due", OracleDbType.Date, request.DueDate!.Value.ToDateTime(TimeOnly.MinValue)), ("status", OracleDbType.Varchar2, request.Status)]);
        var id = command.Parameters.Add("id", OracleDbType.Int64, ParameterDirection.Output);
        await command.ExecuteNonQueryAsync(ct);
        var order = (await Get(connection, ((OracleDecimal)id.Value).ToInt64(), ct))!;
        await transaction.CommitAsync(ct);
        return order;
    }

    public async Task<OrderDto?> Update(long id, UpdateOrderRequest request, CancellationToken ct)
    {
        using var connection = await Open(ct);
        using var transaction = connection.BeginTransaction();
        // One atomic statement preserves an omitted value and lets Oracle validate the result.
        using var command = Command(connection, """
            UPDATE ProductionOrders SET QuantityCompleted=NVL(:completed,QuantityCompleted),
            Status=NVL(:status,Status) WHERE OrderId=:id
            """, [("completed", OracleDbType.Int64, request.QuantityCompleted),
            ("status", OracleDbType.Varchar2, request.Status), ("id", OracleDbType.Int64, id)]);
        if (await command.ExecuteNonQueryAsync(ct) == 0) return null;
        var order = await Get(connection, id, ct);
        await transaction.CommitAsync(ct);
        return order;
    }

    public async Task<List<DepartmentDto>> Departments(CancellationToken ct)
    {
        using var connection = await Open(ct);
        using var command = Command(connection, "SELECT DepartmentId,Name FROM Departments ORDER BY Name");
        using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<DepartmentDto>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    public async Task<List<ProductDto>> Products(CancellationToken ct)
    {
        using var connection = await Open(ct);
        using var command = Command(connection, "SELECT ProductId,PartNumber,Description FROM Products ORDER BY PartNumber");
        using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<ProductDto>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }
}
