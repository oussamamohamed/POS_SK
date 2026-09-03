using System;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IJwtTokenGeneratorService
{
    string GenerateToken(Guid operatorId, string operatorName, UserRole role);
}
