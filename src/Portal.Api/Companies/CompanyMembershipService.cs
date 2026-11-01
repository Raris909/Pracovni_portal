using Portal.Contracts.Companies;

namespace Portal.Api.Companies;

public sealed class CompanyMembershipService(ICompanyRepository repository)
{
    public Task<IReadOnlyList<CompanyMembershipResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        repository.ListUserCompaniesAsync(userId, cancellationToken);

    public Task<CompanyMembershipResponse?> FindAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        repository.FindUserCompanyAsync(userId, companyId, cancellationToken);

    public Task<(CompanyMembershipResponse? Membership, CreateCompanyStatus Status)> CreateCompanyAsync(
        Guid userId, CreateCompanyRequest request, CancellationToken cancellationToken) =>
        repository.CreateCompanyAsync(userId, request, cancellationToken);

    public async Task<IReadOnlyList<CompanyMemberResponse>?> GetMembersAsync(
        Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        if (await repository.FindUserCompanyAsync(userId, companyId, cancellationToken) is null)
            return null;

        return await repository.GetMembersAsync(companyId, cancellationToken);
    }

    public Task<(CompanyMemberResponse? Member, MemberManagementStatus Status)> AddMemberAsync(
        Guid companyId, Guid currentUserId, AddCompanyMemberRequest request, CancellationToken cancellationToken) =>
        repository.AddMemberAsync(companyId, currentUserId, request, cancellationToken);

    public Task<(CompanyMemberResponse? Member, MemberManagementStatus Status)> UpdateMemberRoleAsync(
        Guid companyId, Guid currentUserId, Guid memberId, UpdateCompanyMemberRequest request, CancellationToken cancellationToken) =>
        repository.UpdateMemberRoleAsync(companyId, currentUserId, memberId, request.Role, cancellationToken);

    public Task<MemberManagementStatus> RemoveMemberAsync(
        Guid companyId, Guid currentUserId, Guid memberId, CancellationToken cancellationToken) =>
        repository.RemoveMemberAsync(companyId, currentUserId, memberId, cancellationToken);
}
