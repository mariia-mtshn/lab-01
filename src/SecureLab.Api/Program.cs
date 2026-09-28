using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Endpoints;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "Client"
});

builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions =
        ActivityTrackingOptions.TraceId |
        ActivityTrackingOptions.SpanId |
        ActivityTrackingOptions.ParentId;
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("SecureLab")
    ?? throw new InvalidOperationException(
        "Connection string 'SecureLab' не налаштовано.");

builder.Services.AddDbContext<SecureLabDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IncidentQueries>();

var app = builder.Build();

var resetRequested = args.Contains("--reset-database", StringComparer.Ordinal);

await DatabaseBootstrap.InitializeAsync(
    app.Services,
    app.Configuration,
    app.Environment,
    resetRequested);

if (resetRequested)
{
    return;
}

var requestLogger = app.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("SecureLab.Request");

app.Use(async (context, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString()
        ?? context.TraceIdentifier;

    using (requestLogger.BeginScope(
        new Dictionary<string, object>
        {
            ["TraceId"] = traceId
        }))
    {
        await next(context);
    }
});

app.UseExceptionHandler();
app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");

    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet(
    "/health",
    async (
        SecureLabDbContext dbContext,
        CancellationToken cancellationToken) =>
        await dbContext.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready" })
            : Results.Problem(
                title: "База даних недоступна",
                statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithName("GetHealth")
    .Produces(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapIncidentEndpoints();

app.Map(
        "/api/{**path}",
        () => Results.Problem(
            title: "Ресурс не знайдено",
            detail: "Такого API-маршруту не існує.",
            statusCode: StatusCodes.Status404NotFound))
    .ExcludeFromDescription();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;