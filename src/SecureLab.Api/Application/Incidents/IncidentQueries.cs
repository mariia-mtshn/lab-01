using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(
    SecureLabDbContext dbContext,
    ILogger<IncidentQueries> logger)
{
    private static readonly IncidentSeverity[] SeverityOrder =
    [
        IncidentSeverity.Critical,
        IncidentSeverity.High,
        IncidentSeverity.Medium,
        IncidentSeverity.Low
    ];

    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Loading incidents with status filter {Status}",
            status);

        var query = dbContext.Incidents.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<IncidentDetailsResponse?> GetDetailsAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Loading incident {IncidentId} | TraceId: {TraceId}",
            id,
            Activity.Current?.TraceId.ToString() ?? "none");

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Incidents.AsNoTracking();

        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        var groups = await query
            .GroupBy(incident => incident.Severity)
            .Select(item => new
            {
                Severity = item.Key,
                Count = item.Count()
            })
            .ToListAsync(cancellationToken);

        var countsBySeverity = groups.ToDictionary(
            item => item.Severity,
            item => item.Count);

        var summary = SeverityOrder
            .Where(countsBySeverity.ContainsKey)
            .Select(severity => new IncidentSeveritySummaryResponse(
                severity.ToString(),
                countsBySeverity[severity]))
            .ToList();

        logger.LogInformation(
            "Severity summary built: {GroupCount} groups, status filter {Status}",
            summary.Count,
            status);

        return summary;
    }
}