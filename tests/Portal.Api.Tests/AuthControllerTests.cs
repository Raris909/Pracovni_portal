using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Portal.Api.Auth;
using Portal.Contracts.Auth;

namespace Portal.Api.Tests;

public sealed class AuthControllerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPasswordHasher<UserResponse>> _passwordHasherMock = new();
    private readonly TokenService _tokenService;
    private readonly AuthService _authService;
    private readonly Guid _userId = Guid.NewGuid();

    public AuthControllerTests()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('x', 64),
            LifetimeMinutes = 30
        });

        _tokenService = new TokenService(jwtOptions, TimeProvider.System);
        _authService = new AuthService(_userRepositoryMock.Object, _passwordHasherMock.Object, _tokenService);
    }

    private AuthController CreateController(bool authenticated = false)
    {
        var controller = new AuthController(_authService);
        if (authenticated)
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", _userId.ToString()),
                new Claim("role", UserRoles.Candidate)
            ], "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }
        else
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        return controller;
    }

    [Fact]
    public async Task Register_ReturnsCreated_WhenRegistrationSucceeds()
    {
        var request = new RegisterRequest
        {
            Email = "novy@example.com",
            FirstName = "Jan",
            LastName = "Novak",
            Password = "SecurePassword123!",
            Role = UserRoles.Candidate
        };

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<UserResponse>(), request.Password))
            .Returns("hashed_pwd");

        _userRepositoryMock
            .Setup(r => r.CreateUserAsync(It.IsAny<UserResponse>(), "hashed_pwd", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RegisterUserStatus.Success);

        var controller = CreateController();

        var result = await controller.Register(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        var auth = Assert.IsType<AuthResponse>(objectResult.Value);
        Assert.Equal("novy@example.com", auth.User.Email);
    }

    [Fact]
    public async Task Register_ReturnsConflict_WhenEmailAlreadyExists()
    {
        var request = new RegisterRequest
        {
            Email = "duplicitni@example.com",
            FirstName = "Jan",
            LastName = "Novak",
            Password = "SecurePassword123!",
            Role = UserRoles.Candidate
        };

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<UserResponse>(), request.Password))
            .Returns("hashed_pwd");

        _userRepositoryMock
            .Setup(r => r.CreateUserAsync(It.IsAny<UserResponse>(), "hashed_pwd", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RegisterUserStatus.DuplicateEmail);

        var controller = CreateController();

        var result = await controller.Register(request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, problemResult.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsOk_WhenCredentialsAreValid()
    {
        var request = new LoginRequest { Email = "user@example.com", Password = "ValidPassword123!" };
        var user = new UserResponse(_userId, "user@example.com", "Jan", "Novak", UserRoles.Company);

        _userRepositoryMock
            .Setup(r => r.FindByEmailAsync(request.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((user, "hashed_pwd"));

        _passwordHasherMock
            .Setup(h => h.VerifyHashedPassword(user, "hashed_pwd", request.Password))
            .Returns(PasswordVerificationResult.Success);

        var controller = CreateController();

        var result = await controller.Login(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var auth = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.Equal(user.Email, auth.User.Email);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenCredentialsAreInvalid()
    {
        var request = new LoginRequest { Email = "user@example.com", Password = "WrongPassword" };

        _userRepositoryMock
            .Setup(r => r.FindByEmailAsync(request.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((UserResponse?)null, (string?)null));

        var controller = CreateController();

        var result = await controller.Login(request, CancellationToken.None);

        var problemResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problemResult.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsCurrentUser_WhenAuthenticatedAndFound()
    {
        var user = new UserResponse(_userId, "me@example.com", "Petr", "Svoboda", UserRoles.Candidate);

        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var controller = CreateController(authenticated: true);

        var result = await controller.Me(CancellationToken.None);

        Assert.Equal(user, result.Value);
    }

    [Fact]
    public async Task Me_ReturnsUnauthorized_WhenUserNotFoundInDatabase()
    {
        _userRepositoryMock
            .Setup(r => r.FindByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserResponse?)null);

        var controller = CreateController(authenticated: true);

        var result = await controller.Me(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }
}
