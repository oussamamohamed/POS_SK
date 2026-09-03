using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Infrastructure.Security;

public class JwtTokenGeneratorService : IJwtTokenGeneratorService
{
    private readonly IConfiguration _configuration;

    public JwtTokenGeneratorService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(Guid operatorId, string operatorName, UserRole role)
    {
        var secret = _configuration["Jwt:Secret"] ?? "SuperSecretKeyForRestaurantPosSystemThatIsAtLeast32BytesLong!";
        var issuer = _configuration["Jwt:Issuer"] ?? "RestaurantPos.Api";
        var audience = _configuration["Jwt:Audience"] ?? "RestaurantPos.Client";
        
        // Phase 2: Token valid for 12 hours (typical shift length)
        var expirationHours = int.Parse(_configuration["Jwt:ExpirationHours"] ?? "12", System.Globalization.CultureInfo.InvariantCulture);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, operatorId.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, operatorName),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expirationHours),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
