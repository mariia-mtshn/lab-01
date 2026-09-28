using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

public sealed class IncidentEndpointTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetList_ReturnsSeededIncidents()
    {
        var incidents = await _client.GetFromJsonAsync<List<IncidentListItemResponse>>(
            "/api/incidents");

        Assert.NotNull(incidents);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(incidents, incident => incident.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task GetDetails_ForUnknownId_ReturnsProblemDetails404()
    {
        using var response = await _client.GetAsync($"/api/incidents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetDetails_DoesNotExposeInternalOwnerFields()
    {
        using var response = await _client.GetAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(document.RootElement.TryGetProperty("ownerUserId", out _));
        Assert.False(document.RootElement.TryGetProperty("email", out _));
        Assert.Equal("Аліса Коваль", document.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task ClientScript_DoesNotUseDangerousInnerHtmlSink()
    {
        var script = await _client.GetStringAsync("/app.js");

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSeveritySummary_ReturnsSeededGroupsInCriticalityOrder()
    {
        using var response = await _client.GetAsync("/api/incidents/severity-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var summary = await response.Content.ReadFromJsonAsync<List<IncidentSeveritySummaryResponse>>();

        Assert.NotNull(summary);
        Assert.Equal(new[] { "High", "Medium", "Low" }, summary.Select(item => item.Severity));
        Assert.All(summary, item => Assert.Equal(1, item.Count));
    }

    [Fact]
    public async Task GetSeveritySummary_ReturnsOnlySeverityAndCountFields()
    {
        using var response = await _client.GetAsync("/api/incidents/severity-summary");
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var names = element.EnumerateObject().Select(property => property.Name);
            Assert.Equal(new[] { "severity", "count" }, names);
        }
    }

    [Fact]
    public async Task GetSeveritySummary_WithStatus_FiltersGroups()
    {
        var summary = await _client.GetFromJsonAsync<List<IncidentSeveritySummaryResponse>>(
            "/api/incidents/severity-summary?status=Triaged");

        Assert.NotNull(summary);
        var item = Assert.Single(summary);
        Assert.Equal("Medium", item.Severity);
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public async Task GetSeveritySummary_ForStatusWithoutIncidents_ReturnsEmptyArray()
    {
        using var response = await _client.GetAsync(
            "/api/incidents/severity-summary?status=Resolved");
        var summary = await response.Content.ReadFromJsonAsync<List<IncidentSeveritySummaryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(summary);
        Assert.Empty(summary);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("1")]
    [InlineData("New,Triaged")]
    public async Task GetSeveritySummary_WithInvalidStatus_Returns400(string status)
    {
        using var response = await _client.GetAsync(
            $"/api/incidents/severity-summary?status={Uri.EscapeDataString(status)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("1")]
    [InlineData("New,Triaged")]
    public async Task GetList_WithInvalidStatus_Returns400(string status)
    {
        using var response = await _client.GetAsync(
            $"/api/incidents?status={Uri.EscapeDataString(status)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetails404_NotHtmlFallback()
    {
        using var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}