using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.ProfileSimulations.Helpers;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// Deterministic fake for <see cref="IOperatorAuthenticationService"/>.
/// Validates a pre-configured <see cref="SimulatedOperator"/> PIN.
/// Tracks consecutive failures and locks out after 5 wrong attempts.
/// </summary>
public sealed class FakeOperatorAuthenticationService : IOperatorAuthenticationService
{
    private readonly SimulatedOperator _operator;
    private int _failedAttempts;

    public FakeOperatorAuthenticationService(SimulatedOperator op) => _operator = op;

    public ValueTask<OperatorAuthenticationResult> AuthenticatePinAsync(
        string rawPin,
        CancellationToken cancellationToken = default)
    {
        if (_failedAttempts >= 5)
        {
            return ValueTask.FromResult(new OperatorAuthenticationResult(
                IsSuccess: false,
                OperatorId: null,
                OperatorName: null,
                Role: null,
                ErrorMessage: "Compte verrouillé suite à trop de tentatives incorrectes."));
        }

        if (rawPin == _operator.KnownPin)
        {
            _failedAttempts = 0; // Reset on success
            return ValueTask.FromResult(new OperatorAuthenticationResult(
                IsSuccess: true,
                OperatorId: _operator.OperatorId,
                OperatorName: _operator.Name,
                Role: _operator.Role,
                ErrorMessage: null));
        }

        _failedAttempts++;
        return ValueTask.FromResult(new OperatorAuthenticationResult(
            IsSuccess: false,
            OperatorId: null,
            OperatorName: null,
            Role: null,
            ErrorMessage: "Code PIN invalide."));
    }

    public string HashPin(string rawPin, string salt) => "FAKE_HASH";
    public string GenerateSalt() => "FAKE_SALT";
}
