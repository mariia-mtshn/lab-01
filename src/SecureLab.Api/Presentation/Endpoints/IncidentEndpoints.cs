using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    private static readonly HashSet<string> AllowedStatusNames =
        new(Enum.GetNames<IncidentStatus>(), StringComparer.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .WithTags("Incidents");

        group.MapGet("/", GetListAsync)
            .WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetDetailsAsync)
            .WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/severity-summary", GetSeveritySummaryAsync)
            .WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static bool TryParseStatus(string? status, out IncidentStatus? parsedStatus)
    {
        parsedStatus = null;
        if (status is null)
        {
            return true;
        }

        if (!AllowedStatusNames.Contains(status))
        {
            return false;
        }

        parsedStatus = Enum.Parse<IncidentStatus>(status, ignoreCase: true);
        return true;
    }

    private static IResult StatusValidationProblem() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
        });

    private static async Task<IResult> GetListAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus))
        {
            return StatusValidationProblem();
        }

        return Results.Ok(await queries.GetListAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetSeveritySummaryAsync(
        string? status,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        if (!TryParseStatus(status, out var parsedStatus))
        {
            return StatusValidationProblem();
        }

        return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken));
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid id,
        IncidentQueries queries,
        CancellationToken cancellationToken)
    {
        var incident = await queries.GetDetailsAsync(id, cancellationToken);
        return incident is null
            ? Results.Problem(
                title: "Інцидент не знайдено",
                detail: $"Інцидент '{id}' не існує.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(incident);
    }
}