using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            
            var pattern = "%" + EscapeLike(q ?? "") + "%";
            var found = db.Incidents.AsNoTracking()
                .Where(i => EF.Functions.ILike(i.Title, pattern, "\\")
                        || EF.Functions.ILike(i.Description, pattern, "\\"));

            
            IQueryable<Incident>? ordered = sortBy switch
            {
                null or "" or "createdAtUtc" => found
                    .OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id),
                "severity" => found
                    .OrderBy(i => i.Severity == IncidentSeverity.Critical ? 0
                                : i.Severity == IncidentSeverity.High ? 1
                                : i.Severity == IncidentSeverity.Medium ? 2 : 3)
                    .ThenBy(i => i.Id),
                "status" => found
                    .OrderBy(i => i.Status == IncidentStatus.New ? 0
                                : i.Status == IncidentStatus.Triaged ? 1
                                : i.Status == IncidentStatus.InProgress ? 2
                                : i.Status == IncidentStatus.Resolved ? 3 : 4)
                    .ThenBy(i => i.Id),
                _ => null
            };
            if (ordered is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Allowed values: createdAtUtc, severity, status."]
                });

            var rows = await ordered.Take(50).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new IncidentSearchResponse(
                r.Id, r.Title, r.Description, r.Severity.ToString(), r.Status.ToString(), r.CreatedAtUtc)));
        });
        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;                       // час фіксуємо один раз
            var errors = new Dictionary<string, string[]>();

            
            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Title is required."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Title must not exceed 160 characters."];

            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Description is required."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Description must not exceed 4000 characters."];

            
            var severityIsValid =
                Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!severityIsValid)
                errors["severity"] = ["Allowed values: Low, Medium, High, Critical."];

            
            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["occurredAtUtc is required."];
            else if (request.OccurredAtUtc.Value > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["occurredAtUtc must not be in the future."];

            
            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            
            if (severityIsValid
                && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && !errors.ContainsKey("description") && description.Length < 40)
                errors["description"] = ["For High/Critical severity description must have at least 40 characters."];

            
            if (!errors.ContainsKey("description") && !errors.ContainsKey("title")
                && string.Equals(description, title, StringComparison.Ordinal))
                errors["description"] = ["Description must differ from title."];

           
            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            
            var activeStatuses = new[] { IncidentStatus.New, IncidentStatus.Triaged,
                                        IncidentStatus.InProgress, IncidentStatus.Resolved };
            var duplicate = await db.Incidents
                .AnyAsync(i => i.Title == title && activeStatuses.Contains(i.Status), ct);
            if (duplicate)
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Інцидент уже існує",
                    detail: "Активний інцидент з таким заголовком уже створено.");

            
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            
            return Results.Created($"/api/incidents/{incident.Id}",
                new CreatedIncidentResponse(incident.Id, incident.Title, incident.Severity.ToString(),
                    incident.Status.ToString(), incident.OccurredAtUtc, incident.CreatedAtUtc));
        });
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);

public sealed record IncidentSearchResponse(
    Guid Id, string Title, string Description, string Severity, string Status,
    DateTimeOffset CreatedAtUtc);