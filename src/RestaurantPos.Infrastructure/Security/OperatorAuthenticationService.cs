using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
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
            return new OperatorAuthenticationResult(false, null, null, null, Texts.T("errors.pin_invalid"));
        }

        var activeUsers = await _dbContext.Users
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var user in activeUsers)
        {
            string computedHash = HashPin(rawPin, user.PinSalt);
            bool matched = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(user.PinHash));

            if (!matched)
            {
                // Check legacy single-round SHA-256 for transparent backward compatibility
                byte[] legacyCombined = Encoding.UTF8.GetBytes(user.PinSalt + rawPin);
                string legacyHash = Convert.ToHexString(SHA256.HashData(legacyCombined)).ToLowerInvariant();
                if (CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(legacyHash),
                        Encoding.UTF8.GetBytes(user.PinHash)))
                {
                    matched = true;
                    user.PinHash = computedHash;
                    _dbContext.Users.Update(user);
                    await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            if (matched)
            {
                return new OperatorAuthenticationResult(
                    IsSuccess: true,
                    OperatorId: user.Id,
                    OperatorName: user.Name,
                    Role: user.Role,
                    ErrorMessage: null);
            }
        }

        return new OperatorAuthenticationResult(false, null, null, null, Texts.T("errors.pin_or_credentials_invalid"));
    }

    public string HashPin(string rawPin, string salt)
    {
        byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(rawPin),
            saltBytes,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string GenerateSalt()
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(saltBytes).ToLowerInvariant();
    }
}

