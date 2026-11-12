using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Portal.Contracts.CvReviews;

namespace Portal.Api.Ai;

public sealed class GeminiCvAnalysisClient : ICvAnalysisClient
{
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

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
        var documentPart = await CreateDocumentPartAsync(documentStream, mimeType, cancellationToken);
        var prompt = """
            Analyze the provided candidate CV.
            Return strictly a JSON object matching this structure:
            {
              "score": 0-100,
              "result": {
                "summary": "string",
                "strengths": ["string"],
                "improvements": ["string"],
                "extractedSkills": ["string"],
                "sectionFeedback": [
                  {
                    "sectionName": "string",
                    "score": 0-100,
                    "feedback": "string"
                  }
                ]
              }
            }
            """;

        var responseJson = await CallGeminiAsync(prompt, documentPart, cancellationToken);
        var parsed = JsonSerializer.Deserialize<CandidateResponseWrapper>(CleanJson(responseJson), JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize candidate CV analysis response.");

        var normalizedResult = new CandidateCvReviewResultPayload(
            Summary: parsed.Result?.Summary ?? string.Empty,
            Strengths: parsed.Result?.Strengths ?? [],
            Improvements: parsed.Result?.Improvements ?? [],
            ExtractedSkills: parsed.Result?.ExtractedSkills ?? [],
            SectionFeedback: parsed.Result?.SectionFeedback);

        var score = Math.Clamp(parsed.Score, (short)0, (short)100);
        var metadata = new AiMetadata(_options.Provider, _options.Model, _options.PromptVersion);

        return new AiCandidateCvAnalysisOutput(score, normalizedResult, metadata);
    }

    public async Task<AiApplicationCvAnalysisOutput> AnalyzeApplicationCvAsync(
        Stream documentStream,
        string mimeType,
        JobRequirementsSnapshot jobRequirements,
        CancellationToken cancellationToken = default)
    {
        var documentPart = await CreateDocumentPartAsync(documentStream, mimeType, cancellationToken);
        var reqJson = JsonSerializer.Serialize(jobRequirements, JsonOptions);
        var prompt = $$"""
            Analyze the provided candidate CV against the following job requirements snapshot:
            {{reqJson}}
            Return strictly a JSON object matching this structure:
            {
              "score": 0-100,
              "result": {
                "summary": "string",
                "matchingSkills": ["string"],
                "missingRequirements": ["string"],
                "strengths": ["string"],
                "risksOrGaps": ["string"],
                "suggestedInterviewQuestions": ["string"],
                "overallRecommendation": "string"
              }
            }
            """;

        var responseJson = await CallGeminiAsync(prompt, documentPart, cancellationToken);
        var parsed = JsonSerializer.Deserialize<ApplicationResponseWrapper>(CleanJson(responseJson), JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize application CV analysis response.");

        var normalizedResult = new ApplicationCvReviewResultPayload(
            Summary: parsed.Result?.Summary ?? string.Empty,
            MatchingSkills: parsed.Result?.MatchingSkills ?? [],
            MissingRequirements: parsed.Result?.MissingRequirements ?? [],
            Strengths: parsed.Result?.Strengths ?? [],
            RisksOrGaps: parsed.Result?.RisksOrGaps ?? [],
            SuggestedInterviewQuestions: parsed.Result?.SuggestedInterviewQuestions ?? [],
            OverallRecommendation: parsed.Result?.OverallRecommendation);

        var score = Math.Clamp(parsed.Score, (short)0, (short)100);
        var metadata = new AiMetadata(_options.Provider, _options.Model, _options.PromptVersion);

        return new AiApplicationCvAnalysisOutput(score, normalizedResult, metadata);
    }

    private async Task<string> CallGeminiAsync(
        string prompt,
        object documentPart,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        documentPart
                    }
                }
            },
            generationConfig = new { response_mime_type = "application/json" }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent?key={_options.ApiKey}";
        using var response = await _httpClient.PostAsJsonAsync(url, requestBody, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseContent);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0 ||
            !candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0 ||
            !parts[0].TryGetProperty("text", out var textElement))
        {
            throw new InvalidOperationException("Gemini response did not contain valid text output.");
        }

        return textElement.GetString() ?? throw new InvalidOperationException("Gemini returned empty text.");
    }

    private static async Task<object> CreateDocumentPartAsync(
        Stream stream,
        string mimeType,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        if (string.Equals(mimeType, DocxMimeType, StringComparison.OrdinalIgnoreCase))
        {
            var extractedText = ExtractTextFromDocx(bytes);
            return new { text = $"Candidate CV Document Text:\n{extractedText}" };
        }

        var base64Document = Convert.ToBase64String(bytes);
        return new { inline_data = new { mime_type = mimeType, data = base64Document } };
    }

    private static string ExtractTextFromDocx(byte[] docxBytes)
    {
        using var stream = new MemoryStream(docxBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var documentEntry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("Invalid DOCX file: word/document.xml entry is missing.");

        using var entryStream = documentEntry.Open();
        var xdoc = XDocument.Load(entryStream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var sb = new StringBuilder();
        foreach (var paragraph in xdoc.Descendants(w + "p"))
        {
            var paragraphText = string.Concat(paragraph.Descendants(w + "t").Select(t => t.Value));
            if (!string.IsNullOrWhiteSpace(paragraphText))
            {
                sb.AppendLine(paragraphText.Trim());
            }
        }

        return sb.ToString().Trim();
    }

    private static string CleanJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3];
            }
        }

        return trimmed.Trim();
    }

    private sealed record CandidateResponseWrapper(short Score, CandidateCvReviewResultPayload? Result);

    private sealed record ApplicationResponseWrapper(short Score, ApplicationCvReviewResultPayload? Result);
}
