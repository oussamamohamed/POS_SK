using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Security;

public class OperatorAuthenticationService : IOperatorAuthenticationService
{
    private readonly AppDbContext _dbContext;

    public OperatorAuthenticationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<OperatorAuthenticationResult> AuthenticatePinAsync(string rawPin, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPin) || rawPin.Length < 4)
        {
            return new OperatorAuthenticationResult(false, null, null, null, "Code PIN invalide");
        }

        var activeUsers = await _dbContext.Users
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var user in activeUsers)
        {
            string computedHash = HashPin(rawPin, user.PinSalt);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedHash),
                    Encoding.UTF8.GetBytes(user.PinHash)))
            {
                return new OperatorAuthenticationResult(
                    IsSuccess: true,
                    OperatorId: user.Id,
                    OperatorName: user.Name,
                    Role: user.Role,
                    ErrorMessage: null);
            }
        }

        return new OperatorAuthenticationResult(false, null, null, null, "Code PIN ou identifiants incorrects");
    }

    public string HashPin(string rawPin, string salt)
    {
        byte[] combined = Encoding.UTF8.GetBytes(salt + rawPin);
        byte[] hash = SHA256.HashData(combined);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string GenerateSalt()
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(saltBytes).ToLowerInvariant();
    }
}
