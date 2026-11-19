using Portal.Contracts.Auth;

namespace Portal.Api.Auth;

public enum RegisterUserStatus
{
    Success,
    DuplicateEmail
}

public interface IUserRepository
{
    Task<RegisterUserStatus> CreateUserAsync(
        UserResponse user,
        string passwordHash,
        CancellationToken cancellationToken);

    Task<(UserResponse? User, string? PasswordHash)> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken);

    Task<UserResponse?> FindByIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<(UserResponse? User, string? PasswordHash)> FindWithPasswordHashByIdAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<UserResponse?> UpdateUserAsync(
        Guid userId,
        string firstName,
        string lastName,
        string? phone,
        CancellationToken cancellationToken);

    Task UpdatePasswordHashAsync(
        Guid userId,
        string newPasswordHash,
        CancellationToken cancellationToken);

    Task<bool> DeactivateUserAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
