using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using Portal.Api.Auth;
using Portal.Contracts.Auth;

namespace Portal.Api.Tests;

public sealed class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPasswordHasher<UserResponse>> _passwordHasherMock = new();
    private readonly TokenService _tokenService;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('s', 64),
            LifetimeMinutes = 30
        });

        _tokenService = new TokenService(jwtOptions, TimeProvider.System);
        _authService = new AuthService(_userRepositoryMock.Object, _passwordHasherMock.Object, _tokenService);
    }

    [Fact]
    public async Task RegisterAsync_NormalizesEmail_AndHashesPassword()
    {
        var request = new RegisterRequest
        {
            Email = "  Test.User@Example.COM  ",
            FirstName = "  Karel  ",
            LastName = "  Maly  ",
            Password = "MyPassword123!",
            Role = UserRoles.Company
        };

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.Is<UserResponse>(u => u.Email == "test.user@example.com"), request.Password))
            .Returns("secure_hash");

        _userRepositoryMock
            .Setup(r => r.CreateUserAsync(
                It.Is<UserResponse>(u => u.Email == "test.user@example.com" && u.FirstName == "Karel" && u.LastName == "Maly"),
                "secure_hash",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RegisterUserStatus.Success);

        var (auth, status) = await _authService.RegisterAsync(request, CancellationToken.None);

        Assert.Equal(RegisterUserStatus.Success, status);
        Assert.NotNull(auth);
        Assert.NotEmpty(auth.AccessToken);
        Assert.Equal("test.user@example.com", auth.User.Email);
    }

    [Fact]
    public async Task LoginAsync_RehashesPassword_WhenRehashIsNeeded()
    {
        var request = new LoginRequest { Email = "user@test.cz", Password = "PassWord123!" };
        var user = new UserResponse(Guid.NewGuid(), "user@test.cz", "Jan", "Novak", UserRoles.Candidate);

        _userRepositoryMock
            .Setup(r => r.FindByEmailAsync(request.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user, "old_hash"));

        _passwordHasherMock
            .Setup(h => h.VerifyHashedPassword(user, "old_hash", request.Password))
            .Returns(PasswordVerificationResult.SuccessRehashNeeded);

        _passwordHasherMock
            .Setup(h => h.HashPassword(user, request.Password))
            .Returns("new_upgraded_hash");

        var (auth, status) = await _authService.LoginAsync(request, CancellationToken.None);

        Assert.Equal(LoginStatus.Success, status);
        Assert.NotNull(auth);

        _userRepositoryMock.Verify(
            r => r.UpdatePasswordHashAsync(user.Id, "new_upgraded_hash", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_DoesNotRehash_WhenVerificationIsDirectSuccess()
    {
        var request = new LoginRequest { Email = "user@test.cz", Password = "PassWord123!" };
        var user = new UserResponse(Guid.NewGuid(), "user@test.cz", "Jan", "Novak", UserRoles.Candidate);

        _userRepositoryMock
            .Setup(r => r.FindByEmailAsync(request.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user, "current_hash"));

        _passwordHasherMock
            .Setup(h => h.VerifyHashedPassword(user, "current_hash", request.Password))
            .Returns(PasswordVerificationResult.Success);

        var (auth, status) = await _authService.LoginAsync(request, CancellationToken.None);

        Assert.Equal(LoginStatus.Success, status);
        Assert.NotNull(auth);

        _userRepositoryMock.Verify(
            r => r.UpdatePasswordHashAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
