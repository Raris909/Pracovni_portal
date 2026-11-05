using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

public interface ICandidateProfileRepository
{
    Task<CandidateProfileResponse?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    Task<CandidateProfileResponse?> UpdateProfileAsync(Guid userId, UpdateCandidateProfileRequest request, CancellationToken cancellationToken);

    Task<CandidateExperienceResponse> AddExperienceAsync(Guid userId, CandidateExperienceRequest request, CancellationToken cancellationToken);

    Task<CandidateExperienceResponse?> UpdateExperienceAsync(Guid userId, Guid experienceId, CandidateExperienceRequest request, CancellationToken cancellationToken);

    Task<bool> RemoveExperienceAsync(Guid userId, Guid experienceId, CancellationToken cancellationToken);

    Task<CandidateEducationResponse> AddEducationAsync(Guid userId, CandidateEducationRequest request, CancellationToken cancellationToken);

    Task<CandidateEducationResponse?> UpdateEducationAsync(Guid userId, Guid educationId, CandidateEducationRequest request, CancellationToken cancellationToken);

    Task<bool> RemoveEducationAsync(Guid userId, Guid educationId, CancellationToken cancellationToken);
}
