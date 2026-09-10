// Contract: IStaffManagementService
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

/// <summary>
/// Service contract for managing staff members, roles, and salted PIN credentials.
/// </summary>
public interface IStaffManagementService
{
    Task<IReadOnlyList<User>> GetAllStaffAsync(bool includeInactive = false, CancellationToken ct = default);
    Task<User?> GetStaffByIdAsync(Guid staffId, CancellationToken ct = default);
    Task<User> CreateStaffMemberAsync(string fullName, UserRole role, string pin, CancellationToken ct = default);
    Task<User> UpdateStaffMemberAsync(Guid staffId, string fullName, UserRole role, bool isActive, CancellationToken ct = default);
    Task<bool> ResetStaffPinAsync(Guid staffId, string newPin, CancellationToken ct = default);
    Task<bool> DeactivateStaffMemberAsync(Guid staffId, CancellationToken ct = default);
}
