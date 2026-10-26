namespace Portal.Contracts.Applications;

public sealed record ApplicationProfileSnapshot(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Headline,
    string? Summary,
    string? City,
    string? CountryCode,
    IReadOnlyList<string> Skills,
    IReadOnlyList<ApplicationExperienceSnapshot> Experiences,
    IReadOnlyList<ApplicationEducationSnapshot> Education);

public sealed record ApplicationExperienceSnapshot(
    string Employer,
    string Position,
    string? Description,
    DateOnly StartDate,
    DateOnly? EndDate);

public sealed record ApplicationEducationSnapshot(
    string School,
    string? FieldOfStudy,
    string? Degree,
    DateOnly StartDate,
    DateOnly? EndDate);
