using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RestaurantPos.Api.Tests;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Check if an explicit test role is requested via header
        if (Request.Headers.TryGetValue("X-Test-Role", out var roleHeader) && !string.IsNullOrWhiteSpace(roleHeader))
        {
            var role = roleHeader.ToString();
            var operatorId = Request.Headers.TryGetValue("X-Test-Operator-Id", out var idHeader) && !string.IsNullOrWhiteSpace(idHeader)
                ? idHeader.ToString()
                : "01a067d9-b861-7259-8471-73a4ce48d469";

            var operatorName = Request.Headers.TryGetValue("X-Test-Operator-Name", out var nameHeader) && !string.IsNullOrWhiteSpace(nameHeader)
                ? nameHeader.ToString()
                : $"Test Operator ({role})";

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, operatorId),
                new Claim(ClaimTypes.Name, operatorName),
                new Claim(ClaimTypes.Role, role),
                new Claim("sub", operatorId)
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // 2. Check if a Bearer token was supplied in the standard Authorization header
        if (Request.Headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader))
        {
            var authVal = authHeader.ToString();
            if (authVal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authVal.Substring("Bearer ".Length).Trim();
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    if (handler.CanReadToken(token))
                    {
                        var jwt = handler.ReadJwtToken(token);
                        var identity = new ClaimsIdentity(jwt.Claims, SchemeName);
                        var principal = new ClaimsPrincipal(identity);
                        var ticket = new AuthenticationTicket(principal, SchemeName);
                        return Task.FromResult(AuthenticateResult.Success(ticket));
                    }
                }
                catch
                {
                    // Fall through to NoResult on invalid token format
                }
            }
        }

        // No test credentials provided; let normal authorization failure happen if endpoint requires auth
        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
