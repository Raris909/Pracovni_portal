using System.Text.Json;
using Microsoft.Extensions.Options;
using Portal.Contracts.CvReviews;

namespace Portal.Api.Ai;

public sealed class GeminiCvAnalysisClient : ICvAnalysisClient
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GeminiCvAnalysisClient(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<AiCandidateCvAnalysisOutput> AnalyzeCandidateCvAsync(
        Stream documentStream,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var base64Document = await GetBase64Async(documentStream, cancellationToken);
        var prompt = "Analyze the provided CV. Return a JSON object with 'Score' (integer 0-100) and 'Result' object containing 'Summary', 'Strengths', 'Improvements', 'ExtractedSkills' (arrays of strings) and optionally 'SectionFeedback' (array of objects with 'SectionName', 'Score', 'Feedback').";

        var responseJson = await CallGeminiAsync(prompt, base64Document, mimeType, cancellationToken);
        var parsed = JsonSerializer.Deserialize<CandidateResponseWrapper>(responseJson, JsonOptions);

        var metadata = new AiMetadata("Gemini", _options.Model, "v1");
        return new AiCandidateCvAnalysisOutput(parsed!.Score, parsed.Result, metadata);
    }

    public async Task<AiApplicationCvAnalysisOutput> AnalyzeApplicationCvAsync(
        Stream documentStream,
        string mimeType,
        JobRequirementsSnapshot jobRequirements,
        CancellationToken cancellationToken = default)
    {
        var base64Document = await GetBase64Async(documentStream, cancellationToken);
        var reqJson = JsonSerializer.Serialize(jobRequirements, JsonOptions);
        var prompt = $"Analyze the provided CV against these job requirements: {reqJson}. Return a JSON object with 'Score' (integer 0-100) and 'Result' object containing 'Summary', 'MatchingSkills', 'MissingRequirements', 'Strengths', 'RisksOrGaps', 'SuggestedInterviewQuestions', and 'OverallRecommendation'.";

        var responseJson = await CallGeminiAsync(prompt, base64Document, mimeType, cancellationToken);
        var parsed = JsonSerializer.Deserialize<ApplicationResponseWrapper>(responseJson, JsonOptions);

        var metadata = new AiMetadata("Gemini", _options.Model, "v1");
        return new AiApplicationCvAnalysisOutput(parsed!.Score, parsed.Result, metadata);
    }

    private async Task<string> CallGeminiAsync(string prompt, string base64Document, string mimeType, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new { inline_data = new { mime_type = mimeType, data = base64Document } }
                    }
                }
            },
            generationConfig = new { response_mime_type = "application/json" }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent?key={_options.ApiKey}";
        var response = await _httpClient.PostAsJsonAsync(url, requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseContent);

        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()!;
    }

    private static async Task<string> GetBase64Async(Stream stream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return Convert.ToBase64String(memoryStream.ToArray());
    }

    private sealed record CandidateResponseWrapper(short Score, CandidateCvReviewResultPayload Result);

    private sealed record ApplicationResponseWrapper(short Score, ApplicationCvReviewResultPayload Result);
}
