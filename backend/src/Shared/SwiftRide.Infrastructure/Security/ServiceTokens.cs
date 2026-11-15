using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace SwiftRide.Infrastructure;

public sealed class ServiceTokens(IConfiguration config)
{
    private string? token;
    private DateTimeOffset expires;

    public Task<string> Get(CancellationToken ct)
    {
        if (token is not null && expires > DateTimeOffset.UtcNow.AddSeconds(30))
            return Task.FromResult(token);
        ct.ThrowIfCancellationRequested();
        var now = DateTime.UtcNow;
        var issuer = config["Auth:Issuer"] ?? "swiftride-local";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Auth:SigningKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer,
            "swiftride-api",
            [
                new Claim("sub", "swiftride-internal-service"),
                new Claim("role", "service"),
                new Claim("preferred_username", "swiftride-service"),
            ],
            now,
            now.AddMinutes(15),
            credentials
        );
        token = new JwtSecurityTokenHandler().WriteToken(jwt);
        expires = new DateTimeOffset(now.AddMinutes(15));
        return Task.FromResult(token);
    }
}
