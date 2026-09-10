using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public record OperatorAuthenticationResult(
    bool IsSuccess,
    Guid? OperatorId,
    string? OperatorName,
    UserRole? Role,
    string? ErrorMessage);

public interface IOperatorAuthenticationService
{
    ValueTask<OperatorAuthenticationResult> AuthenticatePinAsync(string rawPin, CancellationToken cancellationToken = default);
    string HashPin(string rawPin, string salt);
    string GenerateSalt();
}
