using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Yanban.Application.Common;
using Yanban.Domain.Entities;
using Yanban.Infrastructure.Security;

namespace Yanban.UnitTests;

public class PasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesBcryptHash_NotPlaintext()
    {
        var hash = _hasher.Hash("correct horse battery staple");
        hash.ShouldNotBe("correct horse battery staple");
        hash.ShouldStartWith("$2");
    }

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var hash = _hasher.Hash("s3cret-password");
        _hasher.Verify("s3cret-password", hash).ShouldBeTrue();
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hash = _hasher.Hash("s3cret-password");
        _hasher.Verify("wrong-password", hash).ShouldBeFalse();
    }
}

public class JwtTokenServiceTests
{
    private static JwtTokenService CreateService() =>
        new(Options.Create(new JwtOptions
        {
            Secret = "unit-test-signing-key-at-least-32-bytes-long-xx",
            Issuer = "yanban-test",
            Audience = "yanban-test",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30
        }));

    [Fact]
    public void CreateAccessToken_EmitsIdentityClaims_AndNoRoleClaim()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "a@example.com",
            DisplayName = "Alice",
            PasswordHash = "x",
            TokenVersion = 3
        };

        var token = CreateService().CreateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        jwt.Claims.ShouldContain(c => c.Type == "sub" && c.Value == user.Id.ToString());
        jwt.Claims.ShouldContain(c => c.Type == "tv" && c.Value == "3");
        jwt.Claims.ShouldContain(c => c.Type == "jti");
        jwt.Claims.ShouldContain(c => c.Type == "iat");
        jwt.Claims.ShouldNotContain(c => c.Type == "role");
    }

    [Fact]
    public void CreateAccessToken_ExpiresInAboutFifteenMinutes()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "a@example.com",
            DisplayName = "A",
            PasswordHash = "x"
        };

        var token = CreateService().CreateAccessToken(user);
        var minutes = (token.ExpiresAt - DateTimeOffset.UtcNow).TotalMinutes;
        minutes.ShouldBeInRange(14, 15.1);
    }
}

public class AppExceptionTests
{
    [Theory]
    [InlineData(typeof(ValidationAppException), 400)]
    [InlineData(typeof(UnauthorizedAppException), 401)]
    [InlineData(typeof(ForbiddenAppException), 403)]
    [InlineData(typeof(NotFoundAppException), 404)]
    [InlineData(typeof(ConflictAppException), 409)]
    public void StatusCode_MatchesExceptionType(Type exceptionType, int expected)
    {
        var ex = (AppException)Activator.CreateInstance(exceptionType, "message")!;
        ex.StatusCode.ShouldBe(expected);
    }
}
