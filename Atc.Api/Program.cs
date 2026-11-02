using Atc.Api;
using Microsoft.AspNetCore.Diagnostics;
using Oracle.ManagedDataAccess.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<OrderRepository>();
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Oracle")))
    throw new InvalidOperationException("Configure ConnectionStrings:Oracle using dotnet user-secrets or ConnectionStrings__Oracle.");
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = error switch
    {
        OracleException { Number: 2291 } => (400, "The product or department does not exist."),
        OracleException { Number: 2290 or 1400 or 1438 } => (400, "The order violates a database validation rule."),
        OracleException { Number: 8177 or 60 } => (409, "The order was changed concurrently. Reload and retry."),
        _ => (500, "An unexpected error occurred.")
    };
    await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
}));
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapControllers();
app.Run();
