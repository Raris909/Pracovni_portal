using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Portal.Api.Auth;
using Portal.Contracts.Auth;

namespace Portal.Api.Tests;

public sealed class AccountManagementTests
{
    private static readonly JwtOptions TestJwtOptions = new()
    {
        Issuer = "portal-test",
        Audience = "portal-test",
        SigningKey = new string('k', 64),
        LifetimeMinutes = 30
    };

    [Fact]
    public async Task UpdateAccountAsync_TrimsFieldsAndNormalizesBlankPhoneToNull()
    {
        var repo = new InMemoryUserRepository();
        var service = CreateService(repo);
        var user = await SeedUserAsync(repo, "jana@example.com", "OldPassword123!", UserRoles.Company);

        var (updated, status) = await service.UpdateAccountAsync(
            user.Id,
            new UpdateAccountRequest
            {
                FirstName = "  Jana  ",
                LastName = "  Novotná  ",
                Phone = "   "
            },
            CancellationToken.None);

        Assert.Equal(UpdateAccountStatus.Success, status);
        Assert.NotNull(updated);
        Assert.Equal("Jana", updated!.FirstName);
        Assert.Equal("Novotná", updated.LastName);
        Assert.Null(updated.Phone);
    }

    [Fact]
    public async Task ChangePasswordAsync_UpdatesHashAndAllowsLoginWithNewPassword()
    {
        var repo = new InMemoryUserRepository();
        var service = CreateService(repo);
        var user = await SeedUserAsync(repo, "petr@example.com", "OldPassword123!", UserRoles.Candidate);

        var status = await service.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest
            {
                CurrentPassword = "OldPassword123!",
                NewPassword = "NewPassword456!"
            },
            CancellationToken.None);

        Assert.Equal(ChangePasswordStatus.Success, status);

        var (_, oldLoginStatus) = await service.LoginAsync(
            new LoginRequest { Email = "petr@example.com", Password = "OldPassword123!" },
            CancellationToken.None);
        var (_, newLoginStatus) = await service.LoginAsync(
            new LoginRequest { Email = "petr@example.com", Password = "NewPassword456!" },
            CancellationToken.None);

        Assert.Equal(LoginStatus.InvalidCredentials, oldLoginStatus);
        Assert.Equal(LoginStatus.Success, newLoginStatus);
    }

    [Fact]
    public async Task ChangePasswordAsync_ReturnsInvalidCurrentPassword_WhenCurrentPasswordDoesNotMatch()
    {
        var repo = new InMemoryUserRepository();
        var service = CreateService(repo);
        var user = await SeedUserAsync(repo, "petr@example.com", "OldPassword123!", UserRoles.Candidate);

        var status = await service.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest
            {
                CurrentPassword = "WrongPassword1!",
                NewPassword = "NewPassword456!"
            },
            CancellationToken.None);

        Assert.Equal(ChangePasswordStatus.InvalidCurrentPassword, status);
    }

    [Fact]
    public async Task DeactivateAccountAsync_BlocksFutureLookupsAndLogin()
    {
        var repo = new InMemoryUserRepository();
        var service = CreateService(repo);
        var user = await SeedUserAsync(repo, "eva@example.com", "SecretPass123!", UserRoles.Company);

        var status = await service.DeactivateAccountAsync(
            user.Id,
            new DeactivateAccountRequest { CurrentPassword = "SecretPass123!" },
            CancellationToken.None);

        Assert.Equal(DeactivateAccountStatus.Success, status);
        Assert.Null(await service.GetCurrentUserAsync(user.Id, CancellationToken.None));

        var (_, loginStatus) = await service.LoginAsync(
            new LoginRequest { Email = "eva@example.com", Password = "SecretPass123!" },
            CancellationToken.None);
        Assert.Equal(LoginStatus.InvalidCredentials, loginStatus);
    }

    [Fact]
    public async Task Controller_ChangePassword_ReturnsBadRequest_WhenCurrentPasswordIsInvalid()
    {
        var repo = new InMemoryUserRepository();
        var service = CreateService(repo);
        var user = await SeedUserAsync(repo, "hr@example.com", "SecretPass123!", UserRoles.Company);
        var controller = CreateController(service, user.Id, user.Role);

        var result = await controller.ChangePassword(
            new ChangePasswordRequest
            {
                CurrentPassword = "WrongPassword1!",
                NewPassword = "NewSecret456!"
            },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task Controller_Update_ReturnsOkWithUpdatedUser()
    {
        var repo = new InMemoryUserRepository();
        var service = CreateService(repo);
        var user = await SeedUserAsync(repo, "hr@example.com", "SecretPass123!", UserRoles.Company);
        var controller = CreateController(service, user.Id, user.Role);

        var actionResult = await controller.Update(
            new UpdateAccountRequest
            {
                FirstName = "Karel",
                LastName = "Dvořák",
                Phone = "+420777123456"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var payload = Assert.IsType<UserResponse>(ok.Value);
        Assert.Equal("Karel", payload.FirstName);
        Assert.Equal("Dvořák", payload.LastName);
        Assert.Equal("+420777123456", payload.Phone);
    }

    [Fact]
    public async Task Controller_Get_ReturnsUnauthorized_WhenUserIsDeactivated()
    {
        var repo = new InMemoryUserRepository();
        var service = CreateService(repo);
        var user = await SeedUserAsync(repo, "hr@example.com", "SecretPass123!", UserRoles.Company);
        await service.DeactivateAccountAsync(
            user.Id,
            new DeactivateAccountRequest { CurrentPassword = "SecretPass123!" },
            CancellationToken.None);
        var controller = CreateController(service, user.Id, user.Role);

        var actionResult = await controller.Get(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(actionResult.Result);
    }

    [Fact]
    public void RequestValidation_EnforcesNameAndPasswordPolicy()
    {
        var accountErrors = Validate(new UpdateAccountRequest
        {
            FirstName = "   ",
            LastName = "Novák"
        });
        Assert.Contains(accountErrors, e => e.MemberNames.Contains(nameof(UpdateAccountRequest.FirstName)));

        var samePasswordErrors = Validate(new ChangePasswordRequest
        {
            CurrentPassword = "SamePassword1!",
            NewPassword = "SamePassword1!"
        });
        Assert.Contains(samePasswordErrors, e => e.MemberNames.Contains(nameof(ChangePasswordRequest.NewPassword)));

        var weakPasswordErrors = Validate(new ChangePasswordRequest
        {
            CurrentPassword = "OldPassword123!",
            NewPassword = "weakpassword"
        });
        Assert.True(weakPasswordErrors.Count >= 3);
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }

    private static AuthService CreateService(InMemoryUserRepository repo) =>
        new(repo, new PasswordHasher<UserResponse>(), new TokenService(Options.Create(TestJwtOptions), TimeProvider.System));

    private static async Task<UserResponse> SeedUserAsync(
        InMemoryUserRepository repo, string email, string password, string role)
    {
        var user = new UserResponse(Guid.NewGuid(), email, "Jan", "Novák", role);
        var hash = new PasswordHasher<UserResponse>().HashPassword(user, password);
        await repo.CreateUserAsync(user, hash, CancellationToken.None);
        return user;
    }

    private static AccountController CreateController(AuthService service, Guid userId, string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("role", role)
        ], "TestAuth"));

        return new AccountController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            },
            ProblemDetailsFactory = new TestProblemDetailsFactory()
        };
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly Dictionary<Guid, (UserResponse User, string PasswordHash, bool IsActive)> _users = [];

        public Task<RegisterUserStatus> CreateUserAsync(
            UserResponse user, string passwordHash, CancellationToken cancellationToken)
        {
            if (_users.Values.Any(x => x.User.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(RegisterUserStatus.DuplicateEmail);

            _users[user.Id] = (user, passwordHash, true);
            return Task.FromResult(RegisterUserStatus.Success);
        }

        public Task<(UserResponse? User, string? PasswordHash)> FindByEmailAsync(
            string email, CancellationToken cancellationToken)
        {
            var match = _users.Values.FirstOrDefault(x =>
                x.IsActive && x.User.Email.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(match.User is null ? (null, null) : ((UserResponse?)match.User, (string?)match.PasswordHash));
        }

        public Task<UserResponse?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_users.TryGetValue(userId, out var entry) && entry.IsActive ? entry.User : null);

        public Task<(UserResponse? User, string? PasswordHash)> FindWithPasswordHashByIdAsync(
            Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_users.TryGetValue(userId, out var entry) && entry.IsActive
                ? ((UserResponse?)entry.User, (string?)entry.PasswordHash)
                : (null, null));

        public Task<UserResponse?> UpdateUserAsync(
            Guid userId, string firstName, string lastName, string? phone, CancellationToken cancellationToken)
        {
            if (!_users.TryGetValue(userId, out var entry) || !entry.IsActive)
                return Task.FromResult<UserResponse?>(null);

            var updated = entry.User with { FirstName = firstName, LastName = lastName, Phone = phone };
            _users[userId] = (updated, entry.PasswordHash, true);
            return Task.FromResult<UserResponse?>(updated);
        }

        public Task UpdatePasswordHashAsync(Guid userId, string newPasswordHash, CancellationToken cancellationToken)
        {
            if (_users.TryGetValue(userId, out var entry) && entry.IsActive)
                _users[userId] = (entry.User, newPasswordHash, true);

            return Task.CompletedTask;
        }

        public Task<bool> DeactivateUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (!_users.TryGetValue(userId, out var entry) || !entry.IsActive)
                return Task.FromResult(false);

            _users[userId] = (entry.User, entry.PasswordHash, false);
            return Task.FromResult(true);
        }
    }

    private sealed class TestProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext, ModelStateDictionary modelStateDictionary, int? statusCode = null,
            string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };
    }
}
