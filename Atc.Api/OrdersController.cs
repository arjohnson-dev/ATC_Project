// Provides ASP.NET Core controller features, routing attributes,
// and HTTP response helpers such as Ok() and NotFound().
using Microsoft.AspNetCore.Mvc;

namespace Atc.Api;

// Enables API-specific behavior, including automatic HTTP 400 responses
// when model binding or model validation fails.
//
// Complex request objects are normally inferred to come from the body,
// unless another source is specified.
[ApiController]

// Base URL for every action in this controller.
[Route("api/orders")]

// Handles HTTP requests involving orders.
//
// The primary constructor receives OrderRepository through
// dependency injection. It must be registered in the app's services.
//
// ControllerBase provides API helpers without view-rendering features.
// sealed prevents another class from inheriting from this controller.
public sealed class OrdersController(OrderRepository repository)
    : ControllerBase
{
    // GET /api/orders
    //
    // Example:
    // GET /api/orders?departmentId=2&status=Planned&page=1&pageSize=50
    [HttpGet]
    public Task<OrderPage> List(
        // Builds an OrderFilter from URL query-string values.
        [FromQuery] OrderFilter filter,

        // ASP.NET Core supplies a token tied to request cancellation.
        // Passing it onward lets database operations respond to cancellation.
        CancellationToken ct)

        // => is an expression-bodied method: return this expression.
        //
        // The task is returned directly; this method needs no async/await.
        // ASP.NET Core normally serializes the resulting OrderPage as JSON
        // with an HTTP 200 response.
        => repository.List(filter, ct);

    // GET /api/orders/123
    //
    // {id:long:min(1)} captures an ID from the URL.
    // The route matches only values that fit a long and are at least 1.
    [HttpGet("{id:long:min(1)}")]
    public async Task<ActionResult<OrderDto>> Get(
        long id,
        CancellationToken ct)

        // Wait for the repository to find the order.
        //
        // "is { } order" checks that the result is not null and
        // stores the non-null result in a variable named order.
        //
        // condition ? first : second chooses between two results:
        //   Found     -> HTTP 200 with the order as JSON.
        //   Not found -> HTTP 404.
        //
        // ActionResult<OrderDto> allows either an OrderDto response
        // or an HTTP result such as NotFound().
        => await repository.Get(id, ct) is { } order
            ? Ok(order)
            : NotFound();

    // POST /api/orders
    //
    // Creates an order using JSON supplied in the request body.
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(
        // Under [ApiController], this request DTO is normally inferred
        // to come from the body, equivalent here to [FromBody].
        //
        // Its validation rules run before the action executes.
        CreateOrderRequest request,
        CancellationToken ct)
    {
        // Ask the repository to insert the order and return its saved data.
        var order = await repository.Create(request, ct);

        // Returns HTTP 201 Created.
        //
        // nameof(Get) identifies this controller's Get action.
        // new { id = ... } creates an anonymous object with the route ID.
        //
        // The response contains:
        //   Body: the newly created order.
        //   Location header: the URL for retrieving that order.
        //
        // This generates a link to Get; it does not call Get.
        return CreatedAtAction(
            nameof(Get),
            new { id = order.OrderId },
            order);
    }

    // GET /api/orders/123/history
    //
    // Retrieves status changes for the specified order.
    [HttpGet("{id:long:min(1)}/history")]
    public async Task<ActionResult<List<StatusHistoryDto>>> History(
        long id,
        CancellationToken ct)

        // The repository returns:
        //   null       -> order does not exist -> HTTP 404.
        //   a list     -> order exists         -> HTTP 200.
        //
        // An empty list still produces HTTP 200.
        => await repository.History(id, ct) is { } history
            ? Ok(history)
            : NotFound();

    // PUT /api/orders/123
    //
    // Updates the specified order.
    //
    // Although this endpoint uses PUT, the repository implements
    // partial-update behavior: omitted fields retain their current values.
    [HttpPut("{id:long:min(1)}")]
    public async Task<ActionResult<OrderDto>> Update(
        // Taken from the URL route.
        long id,

        // Normally read from the JSON body and validated automatically.
        UpdateOrderRequest request,
        CancellationToken ct)

        // Returns the updated order with HTTP 200,
        // or HTTP 404 if the order does not exist.
        => await repository.Update(id, request, ct) is { } order
            ? Ok(order)
            : NotFound();
}

// A second controller for lookup data and reporting endpoints.
[ApiController]

// Base URL for this controller.
// Each action adds its own path after /api.
[Route("api")]
public sealed class CatalogController(OrderRepository repository)
    : ControllerBase
{
    // GET /api/departments
    //
    // Returns department data, such as options for a dropdown.
    [HttpGet("departments")]
    public Task<List<DepartmentDto>> Departments(CancellationToken ct)

        // Normally returns HTTP 200 with a JSON array.
        => repository.Departments(ct);

    // GET /api/products
    //
    // Returns product data, such as options for a dropdown.
    [HttpGet("products")]
    public Task<List<ProductDto>> Products(CancellationToken ct)
        => repository.Products(ct);

    // GET /api/reports/order-exceptions
    //
    // Example:
    // GET /api/reports/order-exceptions?departmentId=2&asOfDate=2026-11-02&allRows=true
    [HttpGet("reports/order-exceptions")]
    public Task<ExceptionReport> Exceptions(
        // Reads filter and pagination properties from the query string.
        [FromQuery] OrderFilter filter,

        // Optional reference date.
        // If omitted, null is passed to the repository, which uses
        // the database server's current date.
        [FromQuery] DateOnly? asOfDate,

        // Supplied by ASP.NET Core, not from the query string.
        CancellationToken ct,

        // Optional query-string flag; defaults to false.
        //
        // false -> request one page of details.
        // true  -> request all matching detail rows.
        [FromQuery] bool allRows = false)

        // The repository runs the report queries and builds the result.
        // ASP.NET Core normally returns that result as JSON with HTTP 200.
        => repository.Exceptions(filter, asOfDate, allRows, ct);
}