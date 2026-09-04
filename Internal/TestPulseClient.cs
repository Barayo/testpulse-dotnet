using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TestPulse.Internal;

public sealed record UnmatchedTest(
    [property: JsonPropertyName("caseKey")] string CaseKey,
    [property: JsonPropertyName("verdict")] string Verdict);

public sealed record RunInfo(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("key")] string? Key);

public sealed record ImportResult(
    [property: JsonPropertyName("run")] RunInfo? Run,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("matched")] int Matched,
    [property: JsonPropertyName("unmatched")] List<UnmatchedTest>? Unmatched);

public sealed record NewAttachment(
    [property: JsonPropertyName("caseKey")] string? CaseKey,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("data")] string Data);

public sealed record ImportRequest(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("report")] string Report,
    [property: JsonPropertyName("attachments")] List<NewAttachment>? Attachments);

public sealed record TestCaseInfo(
    [property: JsonPropertyName("key")] string? Key);

public sealed class ImportOutcome
{
    public required int StatusCode { get; init; }
    public RunInfo? Run { get; init; }
    public ImportResult? Result { get; init; }
    public string? ErrorBody { get; init; }
}

public sealed class TestPulseClient : IDisposable
{
    private readonly HttpClient _http;

    public TestPulseClient(string baseUrl, string token)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<ImportOutcome> SubmitImportAsync(string projectKey, ImportRequest request)
    {
        var response = await _http.PostAsJsonAsync($"/api/v1/projects/{projectKey}/imports", request);
        var statusCode = (int)response.StatusCode;

        if (statusCode == 201)
        {
            var run = await response.Content.ReadFromJsonAsync<RunInfo>();
            return new ImportOutcome { StatusCode = statusCode, Run = run };
        }
        if (statusCode == 207)
        {
            var result = await response.Content.ReadFromJsonAsync<ImportResult>();
            return new ImportOutcome { StatusCode = statusCode, Result = result };
        }

        var body = await response.Content.ReadAsStringAsync();
        return new ImportOutcome { StatusCode = statusCode, ErrorBody = body };
    }

    public async Task<List<string>> ListCaseKeysAsync(string projectKey)
    {
        var response = await _http.GetAsync($"/api/v1/projects/{projectKey}/cases");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"testpulse: failed to list cases: status {(int)response.StatusCode}: {body}");
        }

        var cases = await response.Content.ReadFromJsonAsync<List<TestCaseInfo>>() ?? new List<TestCaseInfo>();
        var keys = new List<string>();
        foreach (var c in cases)
        {
            if (c.Key is not null)
            {
                keys.Add(c.Key);
            }
        }
        return keys;
    }

    public void Dispose() => _http.Dispose();
}
