using System.ComponentModel.DataAnnotations;
namespace Atc.Api;

public static class OrderStatuses
{
    public const string Pattern = "^(Planned|Released|In Progress|Completed|Cancelled)$";
}
public sealed class OrderFilter : IValidatableObject
{
    [Range(1, long.MaxValue)] public long? DepartmentId { get; set; }
    [Range(1, long.MaxValue)] public long? ProductId { get; set; }
    [RegularExpression(OrderStatuses.Pattern)] public string? Status { get; set; }
    public DateOnly? DueFrom { get; set; }
    public DateOnly? DueTo { get; set; }
    [StringLength(100)] public string? Search { get; set; }
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 200)] public int PageSize { get; set; } = 50;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (DueFrom > DueTo) yield return new("DueFrom must be on or before DueTo.", [nameof(DueFrom)]);
    }
}
public sealed class CreateOrderRequest : IValidatableObject
{
    [Range(1, long.MaxValue)] public long ProductId { get; set; }
    [Range(1, long.MaxValue)] public long DepartmentId { get; set; }
    [Range(1, 9999999999L)] public long QuantityOrdered { get; set; }
    [Range(0, 9999999999L)] public long QuantityCompleted { get; set; }
    [Required] public DateOnly? DueDate { get; set; }
    [Required, RegularExpression(OrderStatuses.Pattern)] public string Status { get; set; } = "Planned";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (QuantityCompleted > QuantityOrdered)
            yield return new("QuantityCompleted cannot exceed QuantityOrdered.", [nameof(QuantityCompleted)]);
        if (Status == "Completed" && QuantityCompleted != QuantityOrdered)
            yield return new("Completed orders require the full quantity completed.", [nameof(Status)]);
    }
}
public sealed class UpdateOrderRequest : IValidatableObject
{
    [Range(0, 9999999999L)] public long? QuantityCompleted { get; set; }
    [RegularExpression(OrderStatuses.Pattern)] public string? Status { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (QuantityCompleted is null && Status is null) yield return new("Supply QuantityCompleted, Status, or both.");
        if (Status is not null && string.IsNullOrWhiteSpace(Status)) yield return new("Status cannot be empty.", [nameof(Status)]);
    }
}
public sealed record OrderDto(long OrderId, long ProductId, string PartNumber, string Description,
    long DepartmentId, string DepartmentName, long QuantityOrdered, long QuantityCompleted,
    DateOnly DueDate, string Status, long QuantityRemaining, bool IsOverdue);
public sealed record DepartmentDto(long DepartmentId, string Name);
public sealed record ProductDto(long ProductId, string PartNumber, string Description);
public sealed record OrderTotals(long OrderCount, long QuantityOrdered, long QuantityCompleted, long QuantityRemaining);
public sealed record OrderPage(IReadOnlyList<OrderDto> Items, long TotalCount, int Page, int PageSize);
public sealed record ExceptionReport(DateOnly AsOfDate, OrderTotals Totals, IReadOnlyList<OrderDto> Items, int Page, int PageSize);
