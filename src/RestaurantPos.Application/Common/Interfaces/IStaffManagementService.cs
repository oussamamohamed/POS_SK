using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IStaffManagementService
{
    Task<IReadOnlyList<User>> GetAllStaffAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<User?> GetStaffByIdAsync(Guid staffId, CancellationToken ct = default);
    Task<User> CreateStaffMemberAsync(string name, UserRole role, string pin, CancellationToken ct = default);
    Task<User> UpdateStaffMemberAsync(Guid staffId, string name, UserRole role, bool isActive, CancellationToken ct = default);
    Task<bool> ResetStaffPinAsync(Guid staffId, string newPin, CancellationToken ct = default);
    Task<bool> DeactivateStaffMemberAsync(Guid staffId, CancellationToken ct = default);
}
