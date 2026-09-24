using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// In-memory fake for <see cref="IStaffManagementService"/>.
/// Throws <see cref="InvalidOperationException"/> on duplicate staff names (edge case).
/// </summary>
public sealed class FakeStaffManagementService : IStaffManagementService
{
    private readonly List<User> _store;

    public FakeStaffManagementService(IEnumerable<User>? seed = null)
        => _store = seed?.ToList() ?? [];

    public Task<IReadOnlyList<User>> GetAllStaffAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        IReadOnlyList<User> result = includeInactive
            ? _store.AsReadOnly()
            : _store.Where(u => u.IsActive).ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<User?> GetStaffByIdAsync(Guid staffId, CancellationToken ct = default)
        => Task.FromResult(_store.FirstOrDefault(u => u.Id == staffId));

    public Task<User> CreateStaffMemberAsync(string name, UserRole role, string pin, CancellationToken ct = default)
    {
        if (_store.Any(u => u.Name == name))
            throw new InvalidOperationException($"Un opérateur nommé '{name}' existe déjà.");

        var user = new User
        {
            Id = UuidV7.NewGuid(),
            Name = name,
            Role = role,
            PinHash = "FAKE_HASH",
            PinSalt = "FAKE_SALT",
            IsActive = true
        };
        _store.Add(user);
        return Task.FromResult(user);
    }

    public Task<User> UpdateStaffMemberAsync(Guid staffId, string name, UserRole role, bool isActive, CancellationToken ct = default)
    {
        var user = _store.First(u => u.Id == staffId);
        user.Name = name;
        user.Role = role;
        user.IsActive = isActive;
        return Task.FromResult(user);
    }

    public Task<bool> ResetStaffPinAsync(Guid staffId, string newPin, CancellationToken ct = default)
    {
        var user = _store.FirstOrDefault(u => u.Id == staffId);
        if (user is null) return Task.FromResult(false);
        user.PinHash = "FAKE_HASH_RESET";
        return Task.FromResult(true);
    }

    public Task<bool> DeactivateStaffMemberAsync(Guid staffId, CancellationToken ct = default)
    {
        var user = _store.FirstOrDefault(u => u.Id == staffId);
        if (user is null) return Task.FromResult(false);
        user.IsActive = false;
        return Task.FromResult(true);
    }
}
