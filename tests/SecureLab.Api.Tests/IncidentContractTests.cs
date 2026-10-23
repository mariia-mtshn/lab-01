using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class IncidentContractTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly Guid UsbId  = Guid.Parse("20000000-0000-0000-0000-000000000005");
    private static readonly Guid BrienId = Guid.Parse("20000000-0000-0000-0000-000000000004");

    private static object Body(string title, string severity = "Low",
        string description = "Штучний запис для автоматичного тесту.") => new
    {
        title, description, severity,
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task CleanupAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(i => i.Title == title).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task T02_InvalidSeverity7_Returns400ProblemJson()
    {
        var title = $"T02-{Guid.NewGuid():N}";
        try
        {
            using var r = await _client.PostAsJsonAsync("/api/incidents", Body(title, "7"));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
            var json = await r.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("severity", out _));
            Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { await CleanupAsync(title); }
    }

    [Fact]
    public async Task T03_DuplicateActiveTitle_Returns409()
    {
        var title = $"T03-{Guid.NewGuid():N}";
        try
        {
            using var first = await _client.PostAsJsonAsync("/api/incidents", Body(title));
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            using var second = await _client.PostAsJsonAsync("/api/incidents", Body(title));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        }
        finally { await CleanupAsync(title); }
    }

    [Fact]
    public async Task T09_HighSeverity_Description39Fails_40Passes()
    {
        var t39 = $"T09a-{Guid.NewGuid():N}";
        var t40 = $"T09b-{Guid.NewGuid():N}";
        try
        {
            using var bad = await _client.PostAsJsonAsync("/api/incidents",
                Body(t39, "High", new string('x', 39)));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            using var doc = JsonDocument.Parse(await bad.Content.ReadAsStringAsync());
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("description", out _));

            using var ok = await _client.PostAsJsonAsync("/api/incidents",
                Body(t40, "High", new string('x', 40)));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }
        finally { await CleanupAsync(t39); await CleanupAsync(t40); }
    }

    [Fact]
    public async Task A02_Overposting_ServerManagedFieldsAreIgnored()
    {
        var title = $"A02-{Guid.NewGuid():N}";
        var fakeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        try
        {
            var body = new
            {
                title, description = "Спроба підмінити серверні поля.", severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                id = fakeId, ownerUserId = Guid.NewGuid(), status = "Closed",
                createdAtUtc = "2000-01-01T00:00:00Z"
            };
            using var r = await _client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            using var created = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            Assert.NotEqual(fakeId, created.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("New", created.RootElement.GetProperty("status").GetString());

            using var get = await _client.GetAsync(r.Headers.Location);
            using var detail = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
            Assert.Equal("Аліса Коваль", detail.RootElement.GetProperty("ownerDisplayName").GetString());
        }
        finally { await CleanupAsync(title); }
    }

    [Fact]
    public async Task S02_ControlInput_ReturnsEmptySet_AndNormalSearchStillWorks()
    {
        var evil = Uri.EscapeDataString("zz-no-match' OR TRUE -- ");
        var empty = await _client.GetFromJsonAsync<List<IncidentSearchResponse>>(
            "/api/incidents/search?q=" + evil);
        Assert.NotNull(empty);
        Assert.Empty(empty);

        var usb = await _client.GetFromJsonAsync<List<IncidentSearchResponse>>(
            "/api/incidents/search?q=USB");
        Assert.NotNull(usb);
        Assert.Equal(UsbId, Assert.Single(usb).Id);

        var brien = await _client.GetFromJsonAsync<List<IncidentSearchResponse>>(
            "/api/incidents/search?q=" + Uri.EscapeDataString("O'Brien"));
        Assert.NotNull(brien);
        Assert.Equal(BrienId, Assert.Single(brien).Id);
        Assert.DoesNotContain(brien, x => x.Id == UsbId);
    }

    [Fact]
    public async Task T05_UnknownSortBy_Returns400WithSortByKey()
    {
        using var r = await _client.GetAsync("/api/incidents/search?sortBy=price");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("sortBy", out _));
    }
}