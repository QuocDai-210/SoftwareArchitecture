using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace SwiftRide.Trips.Infrastructure;

public sealed class LocalAuthentication(TripDb db, IConfiguration config)
{
    public async Task<IResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (
            request is null
            || string.IsNullOrWhiteSpace(request.Username)
            || request.Username.Length > 100
            || string.IsNullOrEmpty(request.Password)
            || request.Password.Length > 256
        )
            return Results.Problem(
                statusCode: 401,
                detail: "Tên đăng nhập hoặc mật khẩu không đúng."
            );
        var username = request.Username.Trim().ToLowerInvariant();
        var account = await db.LocalAccounts.SingleOrDefaultAsync(x => x.Username == username, ct);
        if (
            account is null
            || account.Role is not ("rider" or "driver")
            || !VerifyPassword(request.Password, account.PasswordHash)
        )
            return Results.Problem(
                statusCode: 401,
                detail: "Tên đăng nhập hoặc mật khẩu không đúng."
            );

        var issuer = config["Auth:Issuer"] ?? "swiftride-local";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Auth:SigningKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer,
            "swiftride-api",
            [
                new Claim("sub", account.Id),
                new Claim("role", account.Role),
                new Claim("preferred_username", account.Username),
            ],
            now,
            now.AddHours(8),
            credentials
        );
        return Results.Ok(
            new
            {
                accessToken = new JwtSecurityTokenHandler().WriteToken(token),
                expiresAt = new DateTimeOffset(now.AddHours(8)).ToUnixTimeSeconds(),
                user = new
                {
                    id = account.Id,
                    name = account.Username,
                    role = account.Role,
                },
            }
        );
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string encoded)
    {
        var parts = encoded.Split('.');
        if (parts.Length != 3 || parts[0] != "pbkdf2-sha256")
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                210_000,
                HashAlgorithmName.SHA256,
                expected.Length
            );
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record LoginRequest(string Username, string Password);
