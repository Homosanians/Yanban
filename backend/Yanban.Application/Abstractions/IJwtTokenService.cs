using Yanban.Application.Auth;
using Yanban.Domain.Entities;

namespace Yanban.Application.Abstractions;

public interface IJwtTokenService
{
    AccessToken CreateAccessToken(User user);
}
