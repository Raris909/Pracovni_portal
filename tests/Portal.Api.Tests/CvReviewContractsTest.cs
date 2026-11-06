using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Portal.Contracts.CvReviews;
using Xunit;

namespace Portal.Api.Tests;

public sealed class CvReviewContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static IList<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("queued", true)]
    [InlineData("processing", true)]
    [InlineData("succeeded", true)]
    [InlineData("failed", true)]
    [InlineData("pending", false)]
    [InlineData("completed", false)]
    [InlineData("", false)]
    public void CvReviewStatuses_MatchesDatabaseCheckConstraint(string status, bool expected)
    {
        var isValid = CvReviewStatuses.All.Contains(status);
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public void CandidateCvReviewResultPayload_ValidModel_PassesValidation()
    {
        var payload = new CandidateCvReviewResultPayload(
            Summary: "Strong backend developer profile with clear .NET experience.",
            Strengths: ["C# and ASP.NET Core", "PostgreSQL schema design"],
            Improvements: ["Add quantifiable metrics to project descriptions"],
            ExtractedSkills: ["C#", ".NET", "PostgreSQL", "Docker"],
            SectionFeedback:
            [
                new CvSectionFeedback("Experience", 85, "Well-structured chronological history.")
            ]);

        var errors = ValidateModel(payload);

        Assert.Empty(errors);
    }

    [Fact]
    public void CandidateCvReviewResultPayload_EmptySummary_FailsValidation()
    {
        var payload = new CandidateCvReviewResultPayload(
            Summary: "",
            Strengths: ["Good layout"],
            Improvements: [],
            ExtractedSkills: ["C#"]);

        var errors = ValidateModel(payload);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CandidateCvReviewResultPayload.Summary)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void CvSectionFeedback_ScoreOutOfRange_FailsValidation(int invalidScore)
    {
        var section = new CvSectionFeedback(
            SectionName: "Education",
            Score: invalidScore,
            Feedback: "Needs more detail.");

        var errors = ValidateModel(section);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CvSectionFeedback.Score)));
    }

    [Fact]
    public void JobRequirementsSnapshot_ValidModel_PassesValidation()
    {
        var snapshot = new JobRequirementsSnapshot(
            Title: "Medior .NET Developer",
            Description: "Building REST APIs and microservices.",
            Requirements: "3+ years C#, PostgreSQL, Docker",
            Location: "Prague",
            WorkMode: "hybrid",
            EmploymentType: "full_time");

        var errors = ValidateModel(snapshot);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("", "Desc", "Req", "hybrid", "full_time", nameof(JobRequirementsSnapshot.Title))]
    [InlineData("Title", "", "Req", "hybrid", "full_time", nameof(JobRequirementsSnapshot.Description))]
    [InlineData("Title", "Desc", "", "hybrid", "full_time", nameof(JobRequirementsSnapshot.Requirements))]
    [InlineData("Title", "Desc", "Req", "", "full_time", nameof(JobRequirementsSnapshot.WorkMode))]
    [InlineData("Title", "Desc", "Req", "hybrid", "", nameof(JobRequirementsSnapshot.EmploymentType))]
    public void JobRequirementsSnapshot_MissingRequiredFields_FailsValidation(
        string title,
        string description,
        string requirements,
        string workMode,
        string employmentType,
        string expectedInvalidMember)
    {
        var snapshot = new JobRequirementsSnapshot(
            Title: title,
            Description: description,
            Requirements: requirements,
            Location: null,
            WorkMode: workMode,
            EmploymentType: employmentType);

        var errors = ValidateModel(snapshot);

        Assert.Contains(errors, e => e.MemberNames.Contains(expectedInvalidMember));
    }

    [Fact]
    public void CandidateCvReviewResponse_ScoreOutOfBounds_FailsValidation()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new CandidateCvReviewResponse(
            Id: Guid.NewGuid(),
            CandidateId: Guid.NewGuid(),
            DocumentId: Guid.NewGuid(),
            DocumentKind: "cv",
            Status: CvReviewStatuses.Succeeded,
            Provider: "openai",
            Model: "gpt-4o-mini",
            PromptVersion: "v1.0",
            Score: 150, // Invalid: DB constraint is BETWEEN 0 AND 100
            Result: null,
            ErrorMessage: null,
            CreatedAt: now,
            CompletedAt: now,
            UpdatedAt: now);

        var errors = ValidateModel(response);

        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(CandidateCvReviewResponse.Score)));
    }

    [Fact]
    public void ApplicationCvReviewResultPayload_JsonRoundTrip_PreservesAllFields()
    {
        var original = new ApplicationCvReviewResultPayload(
            Summary: "Good match for the backend role.",
            MatchingSkills: ["C#", "PostgreSQL", "Docker"],
            MissingRequirements: ["Kubernetes"],
            Strengths: ["Solid API architecture background"],
            RisksOrGaps: ["No production cloud experience listed"],
            SuggestedInterviewQuestions: ["Can you describe how you handle database migrations in Docker?"],
            OverallRecommendation: "Proceed to technical interview");

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ApplicationCvReviewResultPayload>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Summary, deserialized.Summary);
        Assert.Equal(original.MatchingSkills, deserialized.MatchingSkills);
        Assert.Equal(original.MissingRequirements, deserialized.MissingRequirements);
        Assert.Equal(original.Strengths, deserialized.Strengths);
        Assert.Equal(original.RisksOrGaps, deserialized.RisksOrGaps);
        Assert.Equal(original.SuggestedInterviewQuestions, deserialized.SuggestedInterviewQuestions);
        Assert.Equal(original.OverallRecommendation, deserialized.OverallRecommendation);
    }

    [Fact]
    public void CreateRequests_DefaultDocumentId_IsNull()
    {
        var candidateRequest = new CreateCandidateCvReviewRequest();
        var applicationRequest = new CreateApplicationCvReviewRequest();

        Assert.Null(candidateRequest.DocumentId);
        Assert.Null(applicationRequest.DocumentId);
    }
}