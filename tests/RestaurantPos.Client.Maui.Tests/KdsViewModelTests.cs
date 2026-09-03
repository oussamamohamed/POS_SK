using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class KdsViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();

    [Fact]
    public async Task BumpTicketAsyncMovesTicketFromPendingToInPrepAndThenToReady()
    {
        // Arrange
        var vm = new KdsViewModel(_envMock.Object);

        var ticketDto = new KitchenTicketDto(
            TicketId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            TableNumber: "T12",
            ServerName: "Alexandre",
            CoversCount: 4,
            StationId: "STATION-HOT",
            Status: TicketStatus.Pending,
            DispatchedAtUtc: DateTimeOffset.UtcNow,
            Items: new List<KitchenTicketItemDto>()
        );

        vm.AddIncomingTicket(ticketDto);
        vm.PendingTickets.Should().HaveCount(1);

        var ticketItem = vm.PendingTickets.First();

        // Act 1: Bump from Pending -> InPrep
        await vm.BumpTicketAsync(ticketItem);

        // Assert 1
        vm.PendingTickets.Should().BeEmpty();
        vm.InPrepTickets.Should().HaveCount(1);
        ticketItem.Ticket.Status.Should().Be(TicketStatus.InPreparation);

        // Act 2: Bump from InPrep -> Ready
        await vm.BumpTicketAsync(ticketItem);

        // Assert 2
        vm.InPrepTickets.Should().BeEmpty();
        vm.ReadyTickets.Should().HaveCount(1);
        ticketItem.Ticket.Status.Should().Be(TicketStatus.Ready);
    }

    [Fact]
    public async Task RecallTicketAsyncRestoresTicketBackToPending()
    {
        // Arrange
        var vm = new KdsViewModel(_envMock.Object);
        var ticketDto = new KitchenTicketDto(
            TicketId: Guid.NewGuid(),
            OrderId: Guid.NewGuid(),
            TableNumber: "T04",
            ServerName: "Sophie",
            CoversCount: 2,
            StationId: "STATION-HOT",
            Status: TicketStatus.Pending,
            DispatchedAtUtc: DateTimeOffset.UtcNow,
            Items: new List<KitchenTicketItemDto>()
        );

        vm.AddIncomingTicket(ticketDto);
        var ticketItem = vm.PendingTickets.First();
        await vm.BumpTicketAsync(ticketItem); // Now InPrep

        // Act: Recall
        await vm.RecallTicketAsync(ticketItem);

        // Assert
        vm.InPrepTickets.Should().BeEmpty();
        vm.PendingTickets.Should().HaveCount(1);
        ticketItem.Ticket.Status.Should().Be(TicketStatus.Pending);
    }
}
