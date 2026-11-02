using Microsoft.AspNetCore.Mvc;
namespace Atc.Api;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderRepository repository) : ControllerBase
{
    [HttpGet]
    public Task<OrderPage> List([FromQuery] OrderFilter filter, CancellationToken ct) => repository.List(filter, ct);
    [HttpGet("{id:long:min(1)}")]
    public async Task<ActionResult<OrderDto>> Get(long id, CancellationToken ct)
        => await repository.Get(id, ct) is { } order ? Ok(order) : NotFound();
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await repository.Create(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.OrderId }, order);
    }
    [HttpGet("{id:long:min(1)}/history")]
    public async Task<ActionResult<List<StatusHistoryDto>>> History(long id, CancellationToken ct)
        => await repository.History(id, ct) is { } history ? Ok(history) : NotFound();
    [HttpPut("{id:long:min(1)}")]
    public async Task<ActionResult<OrderDto>> Update(long id, UpdateOrderRequest request, CancellationToken ct)
        => await repository.Update(id, request, ct) is { } order ? Ok(order) : NotFound();
}
[ApiController]
[Route("api")]
public sealed class CatalogController(OrderRepository repository) : ControllerBase
{
    [HttpGet("departments")]
    public Task<List<DepartmentDto>> Departments(CancellationToken ct) => repository.Departments(ct);
    [HttpGet("products")]
    public Task<List<ProductDto>> Products(CancellationToken ct) => repository.Products(ct);
    [HttpGet("reports/order-exceptions")]
    public Task<ExceptionReport> Exceptions([FromQuery] OrderFilter filter,
        [FromQuery] DateOnly? asOfDate, CancellationToken ct, [FromQuery] bool allRows = false)
        => repository.Exceptions(filter, asOfDate, allRows, ct);
}
