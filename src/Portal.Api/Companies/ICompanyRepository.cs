using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

public enum CreateCompanyStatus
{
    Success,
    DuplicateRegistrationNumber,
    UserNotFound
}

public enum MemberManagementStatus
{
    Success,
    CompanyNotFound,
    Forbidden,
    UserNotFound,
    AlreadyMemberOrOwnerConflict,
    MemberNotFound,
    CannotModifyOwner
}

public interface ICompanyRepository
{
    Task<IReadOnlyList<CompanyMembershipResponse>> ListUserCompaniesAsync(Guid userId, CancellationToken cancellationToken);
    Task<CompanyMembershipResponse?> FindUserCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken);
    Task<(CompanyMembershipResponse? Membership, CreateCompanyStatus Status)> CreateCompanyAsync(Guid userId, CreateCompanyRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CompanyMemberResponse>> GetMembersAsync(Guid companyId, CancellationToken cancellationToken);
    Task<(CompanyMemberResponse? Member, MemberManagementStatus Status)> AddMemberAsync(Guid companyId, Guid currentUserId, AddCompanyMemberRequest request, CancellationToken cancellationToken);
    Task<(CompanyMemberResponse? Member, MemberManagementStatus Status)> UpdateMemberRoleAsync(Guid companyId, Guid currentUserId, Guid memberId, string newRole, CancellationToken cancellationToken);
    Task<MemberManagementStatus> RemoveMemberAsync(Guid companyId, Guid currentUserId, Guid memberId, CancellationToken cancellationToken);
}
