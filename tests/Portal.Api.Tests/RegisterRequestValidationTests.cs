using System.ComponentModel.DataAnnotations;
using Portal.Contracts.Auth;

namespace Portal.Api.Tests;

public sealed class RegisterRequestValidationTests
{
    private static RegisterRequest ValidRequest() => new()
    {
        Email = "jan.novak@example.cz",
        Password = "Valid-password-123",
        ConfirmPassword = "Valid-password-123",
        FirstName = "Jan",
        LastName = "Novak",
        Role = UserRoles.Candidate
    };

    private static IList<ValidationResult> Validate(RegisterRequest request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequest_PassesValidation()
    {
        var request = ValidRequest();
        var results = Validate(request);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("bez-zavinace.cz")]
    [InlineData("@chybi-user.cz")]
    [InlineData("user@localhost")]
    [InlineData("user@domena")]
    [InlineData("user@domena.c")]
    [InlineData("user domena@test.cz")]
    [InlineData("")]
    public void InvalidEmail_FailsValidation(string email)
    {
        var request = ValidRequest();
        request.Email = email;

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Email)));
    }

    [Theory]
    [InlineData("valid.user@firma.cz")]
    [InlineData("test+alias@sub.domain.com")]
    [InlineData("name.surname@mail.org")]
    public void ValidEmail_PassesValidation(string email)
    {
        var request = ValidRequest();
        request.Email = email;

        var results = Validate(request);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Email)));
    }

    [Fact]
    public void Password_TooShort_FailsValidation()
    {
        var request = ValidRequest();
        request.Password = "Short1!";
        request.ConfirmPassword = "Short1!";

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Password)));
    }

    [Fact]
    public void Password_MissingUppercase_FailsValidation()
    {
        var request = ValidRequest();
        request.Password = "valid-password-123";
        request.ConfirmPassword = "valid-password-123";

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Password))
            && r.ErrorMessage!.Contains("velké písmeno"));
    }

    [Fact]
    public void Password_MissingLowercase_FailsValidation()
    {
        var request = ValidRequest();
        request.Password = "VALID-PASSWORD-123";
        request.ConfirmPassword = "VALID-PASSWORD-123";

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Password))
            && r.ErrorMessage!.Contains("malé písmeno"));
    }

    [Fact]
    public void Password_MissingDigit_FailsValidation()
    {
        var request = ValidRequest();
        request.Password = "Valid-password-xyz";
        request.ConfirmPassword = "Valid-password-xyz";

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Password))
            && r.ErrorMessage!.Contains("číslici"));
    }

    [Fact]
    public void Password_MissingSpecialChar_FailsValidation()
    {
        var request = ValidRequest();
        request.Password = "ValidPassword123";
        request.ConfirmPassword = "ValidPassword123";

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.Password))
            && r.ErrorMessage!.Contains("speciální znak"));
    }

    [Fact]
    public void Passwords_DoNotMatch_FailsValidation()
    {
        var request = ValidRequest();
        request.ConfirmPassword = "Different-password-123";

        var results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RegisterRequest.ConfirmPassword)));
    }
}
