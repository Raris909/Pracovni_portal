using Microsoft.AspNetCore.Identity;
using Portal.Contracts.Auth;

namespace Portal.Api.Auth;

public enum LoginStatus
{
    Success,
    InvalidCredentials
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
}
