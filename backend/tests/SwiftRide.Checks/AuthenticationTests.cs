using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SwiftRide.Infrastructure;
using Xunit;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task OidcAuthenticatesRidersAndReservesLocalTokensForServices()
    {
        const string issuer = "https://issuer.example";
        var builder = WebApplication.CreateBuilder();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.Configuration["Auth:SigningKey"] = secret;
        builder.Configuration["Auth:Authority"] = issuer;
        builder.Configuration["OTEL_SDK_DISABLED"] = "true";
        builder.AddPlatform("test");
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test" };
        builder.Services.Configure<JwtBearerOptions>(
            "oidc",
            options =>
            {
                options.Configuration = new OpenIdConnectConfiguration { Issuer = issuer };
                options.Configuration.SigningKeys.Add(key);
            }
        );
        await using var app = builder.Build();

        async Task<bool> Authorized(string token, string? policyName = null)
        {
            using var scope = app.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Headers.Authorization = "Bearer " + token;
            var policies = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
            var policy = policyName is null
                ? await policies.GetDefaultPolicyAsync()
                : await policies.GetPolicyAsync(policyName);
            Assert.NotNull(policy);
            var evaluator = scope.ServiceProvider.GetRequiredService<IPolicyEvaluator>();
            var identity = await evaluator.AuthenticateAsync(policy, context);
            return (await evaluator.AuthorizeAsync(policy, identity, context, null)).Succeeded;
        }

        string Issue(string role, string audience, bool local = false)
        {
            var credentials = local
                ? new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    SecurityAlgorithms.HmacSha256
                )
                : new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
            var token = new JwtSecurityToken(
                local ? "swiftride-local" : issuer,
                audience,
                [new Claim("sub", "test-subject"), new Claim("role", role)],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: credentials
            );
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        var rider = Issue("rider", "swiftride-api");
        Assert.True(await Authorized(rider));
        Assert.True(await Authorized(rider, "rider"));
        Assert.False(await Authorized(rider, "driver"));
        Assert.False(await Authorized(Issue("rider", "other-api")));
        Assert.False(await Authorized(Issue("service", "swiftride-api")));
        Assert.False(await Authorized(Issue("rider", "swiftride-api", local: true)));
        var machine = await app.Services.GetRequiredService<ServiceTokens>().Get(default);
        Assert.True(await Authorized(machine, "service"));
        Assert.True(await Authorized(machine));
    }
}
