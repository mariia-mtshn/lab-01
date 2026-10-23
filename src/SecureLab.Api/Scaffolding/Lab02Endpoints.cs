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
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
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
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);