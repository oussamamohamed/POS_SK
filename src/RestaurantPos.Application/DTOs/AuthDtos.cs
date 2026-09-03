namespace RestaurantPos.Application.DTOs;

public record PinLoginRequest(string Pin);

public record SupervisorOverrideRequest(string SupervisorPin, string Action);
