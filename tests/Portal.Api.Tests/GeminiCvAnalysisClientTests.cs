using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portal.Api.Ai;
using Portal.Contracts.CvReviews;
using Xunit;

namespace Portal.Api.Tests;

public sealed class GeminiCvAnalysisClientTests
{
    [Fact]
    public async Task AnalyzeCandidateCvAsync_WithPdf_SendsInlineDataAndParsesResponse()
    {
        string? capturedBody = null;
        var fakeResponse = new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = JsonSerializer.Serialize(new
                                {
                                    score = 88,
                                    result = new
                                    {
                                        summary = "Strong backend profile",
                                        strengths = new[] { "C#", "PostgreSQL" },
                                        improvements = new[] { "Add metrics" },
                                        extractedSkills = new[] { "C#", ".NET", "SQL" }
                                    }
                                })
                            }
                        }
                    }
                }
            }
        };

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(fakeResponse), Encoding.UTF8, "application/json")
            };
        });

        var options = Options.Create(new GeminiOptions
        {
            ApiKey = "test-key",
            Model = "gemini-3.8-flash",
            Provider = "Gemini",
            PromptVersion = "v1"
        });

        var client = new GeminiCvAnalysisClient(new HttpClient(handler), options);
        using var stream = new MemoryStream("%PDF-1.7 dummy"u8.ToArray());

        var output = await client.AnalyzeCandidateCvAsync(stream, "application/pdf");

        Assert.Equal(88, output.Score);
        Assert.Equal("Strong backend profile", output.Result.Summary);
        Assert.Contains("C#", output.Result.ExtractedSkills);
        Assert.Equal("Gemini", output.Metadata.Provider);
        Assert.NotNull(capturedBody);
        Assert.Contains("application/pdf", capturedBody);
    }

    [Fact]
    public async Task AnalyzeApplicationCvAsync_WithDocx_ExtractsXmlTextAndParsesResponse()
    {
        string? capturedBody = null;
        var fakeResponse = new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = JsonSerializer.Serialize(new
                                {
                                    score = 92,
                                    result = new
                                    {
                                        summary = "Great match for .NET role",
                                        matchingSkills = new[] { "C#", "Docker" },
                                        missingRequirements = Array.Empty<string>(),
                                        strengths = new[] { "Relevant experience" },
                                        risksOrGaps = Array.Empty<string>(),
                                        suggestedInterviewQuestions = new[] { "Explain async/await" },
                                        overallRecommendation = "Invite to interview"
                                    }
                                })
                            }
                        }
                    }
                }
            }
        };

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(fakeResponse), Encoding.UTF8, "application/json")
            };
        });

        var options = Options.Create(new GeminiOptions
        {
            ApiKey = "test-key",
            Model = "gemini-3.8-flash"
        });

        var client = new GeminiCvAnalysisClient(new HttpClient(handler), options);
        using var docxStream = CreateMinimalDocxStream("Senior .NET Engineer with PostgreSQL experience");
        var snapshot = new JobRequirementsSnapshot(
            "Backend Developer",
            "Build APIs",
            "C#, PostgreSQL",
            "Prague",
            "hybrid",
            "full_time");

        var output = await client.AnalyzeApplicationCvAsync(
            docxStream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            snapshot);

        Assert.Equal(92, output.Score);
        Assert.Equal("Great match for .NET role", output.Result.Summary);
        Assert.Equal("Invite to interview", output.Result.OverallRecommendation);
        Assert.NotNull(capturedBody);
        Assert.Contains("Senior .NET Engineer with PostgreSQL experience", capturedBody);
    }

    private static MemoryStream CreateMinimalDocxStream(string text)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>{text}</w:t></w:r></w:p>
                  </w:body>
                </w:document>
                """);
        }

        ms.Position = 0;
        return ms;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
