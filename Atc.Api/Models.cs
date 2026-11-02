// Provides validation attributes such as [Required], [Range],
// and the IValidatableObject interface.
using System.ComponentModel.DataAnnotations;

// Groups these types under the Atc.Api namespace.
namespace Atc.Api;

// Holds a shared validation pattern for order statuses.
//
// static: use this class directly without creating an instance.
public static class OrderStatuses
{
    // const: this string is a compile-time constant.
    //
    // This regular expression accepts exactly one listed status.
    // ^ means start of text; $ marks its end.
    // | separates alternatives.
    //
    // Matching is case-sensitive: "Planned" passes; "planned" fails.
    public const string Pattern =
        "^(Planned|Released|In Progress|Completed|Cancelled)$";
}

// Describes the filters the frontend can send when listing orders.
//
// sealed: another class cannot inherit from this class.
// IValidatableObject: provides custom validation through Validate().
public sealed class OrderFilter : IValidatableObject
{
    // Optional department filter.
    //
    // long is a 64-bit integer.
    // ? allows null, meaning no department was specified.
    // get/set lets code read and change the property.
    //
    // When supplied, the ID must be positive.
    [Range(1, long.MaxValue)]
    public long? DepartmentId { get; set; }

    // Optional product filter; must be positive when supplied.
    [Range(1, long.MaxValue)]
    public long? ProductId { get; set; }

    // Optional status filter.
    // Nonempty values must match the shared status pattern.
    //
    // RegularExpression alone does not make a property required.
    [RegularExpression(OrderStatuses.Pattern)]
    public string? Status { get; set; }

    // Optional first due date to include.
    // DateOnly represents a date without a time of day.
    public DateOnly? DueFrom { get; set; }

    // Optional last due date to include.
    // The repository determines how these dates filter the query.
    public DateOnly? DueTo { get; set; }

    // Optional search text, limited to 100 characters.
    [StringLength(100)]
    public string? Search { get; set; }

    // Requested results page.
    // Defaults to page 1 if no value is assigned.
    [Range(1, 1000000)]
    public int Page { get; set; } = 1;

    // Number of results per page.
    // Defaults to 50, with a maximum of 200.
    [Range(1, 200)]
    public int PageSize { get; set; } = 50;

    // Checks rules involving multiple properties.
    //
    // IEnumerable<ValidationResult> means this method produces
    // a sequence of validation errors.
    //
    // context provides validation context; this method does not use it.
    public IEnumerable<ValidationResult> Validate(
        ValidationContext context)
    {
        // If both dates are supplied, the start cannot follow the end.
        // If either is null, this comparison is false.
        //
        // yield return produces an error.
        // new(...) is inferred to mean new ValidationResult(...).
        //
        // nameof(DueFrom) produces "DueFrom".
        // [nameof(DueFrom)] is a collection identifying the property
        // associated with the error.
        if (DueFrom > DueTo)
            yield return new(
                "DueFrom must be on or before DueTo.",
                [nameof(DueFrom)]);
    }
}

// Describes the data accepted when creating an order.
//
// These properties hold input data; they do not insert database rows.
public sealed class CreateOrderRequest : IValidatableObject
{
    // Must contain a positive product ID.
    //
    // Because this is not nullable, its initial value is 0.
    // The range check rejects that default if it remains unchanged.
    //
    // This does not verify that the product exists in the database.
    [Range(1, long.MaxValue)]
    public long ProductId { get; set; }

    // Must contain a positive department ID.
    // Database logic must verify that the department exists.
    [Range(1, long.MaxValue)]
    public long DepartmentId { get; set; }

    // Number of units requested: at least 1.
    //
    // The L suffix makes 9999999999 a long integer literal.
    [Range(1, 9999999999L)]
    public long QuantityOrdered { get; set; }

    // Number of units already finished.
    // Zero is allowed and is also the property's initial value.
    [Range(0, 9999999999L)]
    public long QuantityCompleted { get; set; }

    // Nullable so a missing date can be represented as null.
    // [Required] then rejects that missing date during validation.
    [Required]
    public DateOnly? DueDate { get; set; }

    // Required status that must match the allowed values.
    //
    // If no value is assigned, it starts as "Planned".
    // Required rejects null, empty, and whitespace-only strings.
    [Required, RegularExpression(OrderStatuses.Pattern)]
    public string Status { get; set; } = "Planned";

    // Additional business rules comparing multiple properties.
    public IEnumerable<ValidationResult> Validate(
        ValidationContext context)
    {
        // Cannot finish more units than were requested.
        if (QuantityCompleted > QuantityOrdered)
            yield return new(
                "QuantityCompleted cannot exceed QuantityOrdered.",
                [nameof(QuantityCompleted)]);

        // && means both conditions must be true.
        // != means "not equal".
        //
        // If the order is marked Completed, every unit must be finished.
        //
        // This rule does not require fully produced orders to have
        // Completed status; it checks only the direction shown here.
        if (Status == "Completed" &&
            QuantityCompleted != QuantityOrdered)
            yield return new(
                "Completed orders require the full quantity completed.",
                [nameof(Status)]);
    }
}

// Describes the fields that can be changed on an existing order.
public sealed class UpdateOrderRequest : IValidatableObject
{
    // Optional new completed quantity.
    //
    // null means no quantity was supplied.
    // 0 means explicitly set the quantity to zero.
    [Range(0, 9999999999L)]
    public long? QuantityCompleted { get; set; }

    // Optional new status.
    [RegularExpression(OrderStatuses.Pattern)]
    public string? Status { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext context)
    {
        // Reject a request that supplies neither field.
        //
        // No property collection is provided, so this is an error
        // associated with the request as a whole.
        if (QuantityCompleted is null && Status is null)
            yield return new(
                "Supply QuantityCompleted, Status, or both.");

        // A supplied status cannot be empty or only whitespace.
        //
        // null remains allowed when only updating the quantity.
        if (Status is not null && string.IsNullOrWhiteSpace(Status))
            yield return new(
                "Status cannot be empty.",
                [nameof(Status)]);

        // This object does not know the existing QuantityOrdered.
        // Checks against that quantity must happen elsewhere,
        // such as database constraints or application logic.
    }
}

// The remaining types are response data containers.
//
// DTO means Data Transfer Object.
//
// record: generates properties, a constructor, value-based equality,
// and support for copying with a "with" expression.
//
// These positional record-class properties are init-only:
// they can be assigned during initialization.
//
// sealed: prevents other records from inheriting from these types.

// One order, including related product and department information.
//
// QuantityRemaining, IsOverdue, and DaysOverdue are supplied by
// the repository; this record does not calculate them.
public sealed record OrderDto(
    long OrderId,
    long ProductId,
    string PartNumber,
    string Description,
    long DepartmentId,
    string DepartmentName,
    long QuantityOrdered,
    long QuantityCompleted,
    DateOnly DueDate,
    string Status,
    long QuantityRemaining,
    bool IsOverdue,

    // Optional constructor argument; defaults to zero when omitted.
    int DaysOverdue = 0);

// A department, suitable for a dropdown or lookup list.
public sealed record DepartmentDto(
    long DepartmentId,
    string Name);

// A product, suitable for a dropdown or lookup list.
public sealed record ProductDto(
    long ProductId,
    string PartNumber,
    string Description);

// Summary counts and quantities.
//
// The query supplying these values determines which orders are included
// and what qualifies as "open".
public sealed record OrderTotals(
    long OrderCount,
    long QuantityOrdered,
    long QuantityCompleted,
    long QuantityRemaining,

    // Default to zero when these constructor arguments are omitted.
    long OpenOrderCount = 0,
    long OpenQuantityRemaining = 0);

// One page of order results.
public sealed record OrderPage(
    // Read-only list interface: consumers cannot add/remove items
    // through this interface.
    //
    // It does not guarantee the underlying collection is immutable.
    IReadOnlyList<OrderDto> Items,

    // Total matches across all pages.
    long TotalCount,

    // Current page number.
    int Page,

    // Requested number of items per page.
    int PageSize);

// A department's summary within an exception report.
public sealed record DepartmentExceptionDto(
    long DepartmentId,
    string DepartmentName,
    long OrderCount,
    long QuantityRemaining);

// Combines the data needed to display or export an exception report.
public sealed record ExceptionReport(
    // Reference date used to evaluate overdue orders.
    DateOnly AsOfDate,

    // Overall summary values.
    OrderTotals Totals,

    // Individual order rows.
    IReadOnlyList<OrderDto> Items,

    // Pagination information.
    int Page,
    int PageSize,

    // Summary values grouped by department.
    IReadOnlyList<DepartmentExceptionDto> Departments,

    // When the report was generated, including a UTC offset.
    DateTimeOffset GeneratedAt,

    // Indicates whether all matching detail rows were requested.
    bool AllRows);

// One entry in an order's status-change history.
public sealed record StatusHistoryDto(
    long HistoryId,

    // Nullable because an entry may have no previous status,
    // such as an initial status assignment.
    string? PreviousStatus,

    string NewStatus,

    // Date/time of the change, including a UTC offset.
    DateTimeOffset ChangedAt);