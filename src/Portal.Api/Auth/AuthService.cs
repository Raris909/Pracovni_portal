using Microsoft.AspNetCore.Identity;
using Portal.Contracts.Auth;

namespace Portal.Api.Auth;

public enum LoginStatus
{
    Success,
    InvalidCredentials
}

public enum UpdateAccountStatus
{
    Success,
    UserNotFound
}

public enum ChangePasswordStatus
{
    Success,
    UserNotFound,
    InvalidCurrentPassword
}

public enum DeactivateAccountStatus
{
    Success,
    UserNotFound,
    InvalidCurrentPassword
}

public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher<UserResponse> passwordHasher,
    TokenService tokenService)
{
    public async Task<(AuthResponse? Auth, RegisterUserStatus Status)> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var user = new UserResponse(
            Guid.NewGuid(),
            request.Email.Trim().ToLowerInvariant(),
            request.FirstName.Trim(),
            request.LastName.Trim(),
            request.Role);

        var passwordHash = passwordHasher.HashPassword(user, request.Password);

        var status = await userRepository.CreateUserAsync(user, passwordHash, cancellationToken);
        if (status != RegisterUserStatus.Success)
            return (null, status);

        var auth = tokenService.Create(user);
        return (auth, RegisterUserStatus.Success);
    }

    public async Task<(AuthResponse? Auth, LoginStatus Status)> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var (user, passwordHash) = await userRepository.FindByEmailAsync(request.Email, cancellationToken);
        if (user is null || passwordHash is null)
            return (null, LoginStatus.InvalidCredentials);

        var verificationResult = passwordHasher.VerifyHashedPassword(user, passwordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
            return (null, LoginStatus.InvalidCredentials);

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var newHash = passwordHasher.HashPassword(user, request.Password);
            await userRepository.UpdatePasswordHashAsync(user.Id, newHash, cancellationToken);
        }

        var auth = tokenService.Create(user);
        return (auth, LoginStatus.Success);
    }

    public Task<UserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.FindByIdAsync(userId, cancellationToken);

    public async Task<(UserResponse? User, UpdateAccountStatus Status)> UpdateAccountAsync(
        Guid userId,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var updated = await userRepository.UpdateUserAsync(
            userId,
            request.FirstName.Trim(),
            request.LastName.Trim(),
            phone,
            cancellationToken);

        return updated is null
            ? (null, UpdateAccountStatus.UserNotFound)
            : (updated, UpdateAccountStatus.Success);
    }

    public async Task<ChangePasswordStatus> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var (user, passwordHash) = await userRepository.FindWithPasswordHashByIdAsync(userId, cancellationToken);
        if (user is null || passwordHash is null)
            return ChangePasswordStatus.UserNotFound;

        var verificationResult = passwordHasher.VerifyHashedPassword(user, passwordHash, request.CurrentPassword);
        if (verificationResult == PasswordVerificationResult.Failed)
            return ChangePasswordStatus.InvalidCurrentPassword;

        var newHash = passwordHasher.HashPassword(user, request.NewPassword);
        await userRepository.UpdatePasswordHashAsync(user.Id, newHash, cancellationToken);
        return ChangePasswordStatus.Success;
    }

    public async Task<DeactivateAccountStatus> DeactivateAccountAsync(
        Guid userId,
        DeactivateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var (user, passwordHash) = await userRepository.FindWithPasswordHashByIdAsync(userId, cancellationToken);
        if (user is null || passwordHash is null)
            return DeactivateAccountStatus.UserNotFound;

        var verificationResult = passwordHasher.VerifyHashedPassword(user, passwordHash, request.CurrentPassword);
        if (verificationResult == PasswordVerificationResult.Failed)
            return DeactivateAccountStatus.InvalidCurrentPassword;

        var deactivated = await userRepository.DeactivateUserAsync(user.Id, cancellationToken);
        return deactivated ? DeactivateAccountStatus.Success : DeactivateAccountStatus.UserNotFound;
    }
}
