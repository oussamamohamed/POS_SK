using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class ReservationServiceTests
{
    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);

    private static (ReservationService Service, AppDbContext Db, SettableClock Clock) Build()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("ResDb_" + Guid.NewGuid().ToString("N")).Options);
        db.DiningTables.Add(new DiningTable { TableNumber = "T1", Capacity = 4 });
        db.DiningTables.Add(new DiningTable { TableNumber = "T2", Capacity = 2 });
        db.SaveChanges();
        var clock = new SettableClock(T0);
        return (new ReservationService(db, new TableManagementService(db), clock), db, clock);
    }

    private static ReservationInput Input(DateTimeOffset? at = null, string? table = "T1", int covers = 2, string? phone = null, bool waitlist = false, string name = "Martin") =>
        new(name, covers, at ?? T0.AddHours(1), 90, table, null, phone, waitlist);

    private static async Task ShouldFail(Task task, ReservationError error) =>
        (await task.Invoking(t => t).Should().ThrowAsync<ReservationException>()).Which.Error.Should().Be(error);

    [Theory]
    [InlineData("", 2, 90, null, ReservationError.InvalidName)]
    [InlineData("Martin", 0, 90, null, ReservationError.InvalidCovers)]
    [InlineData("Martin", 31, 90, null, ReservationError.InvalidCovers)]
    [InlineData("Martin", 2, 10, null, ReservationError.InvalidDuration)]
    [InlineData("Martin", 2, 90, "abc", ReservationError.InvalidPhone)]
    public async Task Create_rejects_invalid_input(string name, int covers, int duration, string? phone, ReservationError expected)
    {
        var (service, _, _) = Build();
        await ShouldFail(service.CreateAsync(new ReservationInput(name, covers, T0.AddHours(1), duration, "T1", null, phone, false), true), expected);
    }

    [Fact]
    public async Task Create_rejects_unknown_table_and_too_small_table()
    {
        var (service, _, _) = Build();
        await ShouldFail(service.CreateAsync(Input(table: "T9"), true), ReservationError.UnknownTable);
        await ShouldFail(service.CreateAsync(Input(table: "T2", covers: 3), true), ReservationError.TableCapacity);
    }

    [Fact]
    public async Task Same_table_overlap_is_refused_but_back_to_back_and_waitlist_are_fine()
    {
        var (service, _, _) = Build();
        await service.CreateAsync(Input(at: T0.AddHours(1)), true);            // 19:00-20:30

        await ShouldFail(service.CreateAsync(Input(at: T0.AddHours(2)), true), ReservationError.TableConflict);   // 20:00
        await service.CreateAsync(Input(at: T0.AddHours(1).AddMinutes(90)), true);                                // 20:30 pile
        await service.CreateAsync(Input(at: T0.AddHours(1), table: null, waitlist: true), true);                  // liste d'attente
    }

    [Fact]
    public async Task Phone_is_hidden_unless_manager_and_late_flag_follows_the_clock()
    {
        var (service, _, clock) = Build();
        var created = await service.CreateAsync(Input(phone: "+33 6 12 34 56 78"), includePhone: false);
        created.Phone.Should().BeNull();
        created.HasPhone.Should().BeTrue();
        (await service.ListAsync(T0, T0.AddDays(1), includePhone: true)).Single().Phone.Should().Be("+33 6 12 34 56 78");

        created.IsLate.Should().BeFalse();
        clock.Now = T0.AddHours(1).AddMinutes(10);
        (await service.ListAsync(T0, T0.AddDays(1), true)).Single().IsLate.Should().BeFalse();
        clock.Now = T0.AddHours(1).AddMinutes(20);
        (await service.ListAsync(T0, T0.AddDays(1), true)).Single().IsLate.Should().BeTrue();
    }

    [Fact]
    public async Task Update_by_a_non_manager_keeps_the_hidden_phone()
    {
        var (service, db, _) = Build();
        var created = await service.CreateAsync(Input(phone: "0612345678"), includePhone: true);

        await service.UpdateAsync(created.Id, Input(covers: 3, phone: null), includePhone: false);
        (await db.Reservations.SingleAsync()).Phone.Should().Be("0612345678");

        await service.UpdateAsync(created.Id, Input(covers: 3, phone: null), includePhone: true);
        (await db.Reservations.SingleAsync()).Phone.Should().BeNull();
    }

    [Fact]
    public async Task Seat_opens_the_table_once_and_refuses_an_occupied_table()
    {
        var (service, db, _) = Build();
        var first = await service.CreateAsync(Input(), true);
        var second = await service.CreateAsync(Input(at: T0.AddHours(5)), true);

        var seated = await service.SeatAsync(first.Id, null, Guid.NewGuid(), "Sophie", true);

        seated.Status.Should().Be((int)ReservationStatus.Seated);
        (await db.DiningTables.FindAsync("T1"))!.ActiveOrderId.Should().NotBeNull();
        await ShouldFail(service.SeatAsync(first.Id, null, Guid.NewGuid(), "Sophie", true), ReservationError.InvalidState);
        await ShouldFail(service.SeatAsync(second.Id, "T1", Guid.NewGuid(), "Sophie", true), ReservationError.TableOccupied);
    }

    [Fact]
    public async Task Waitlist_entry_needs_a_table_to_be_seated()
    {
        var (service, _, _) = Build();
        var waiting = await service.CreateAsync(Input(table: null, waitlist: true), true);
        await ShouldFail(service.SeatAsync(waiting.Id, null, Guid.NewGuid(), "Sophie", true), ReservationError.TableRequired);
        (await service.SeatAsync(waiting.Id, "T1", Guid.NewGuid(), "Sophie", true)).TableNumber.Should().Be("T1");
    }

    [Fact]
    public async Task Status_transitions_are_limited()
    {
        var (service, _, _) = Build();
        var r = await service.CreateAsync(Input(), true);
        await ShouldFail(service.SetStatusAsync(r.Id, ReservationStatus.Done, true), ReservationError.InvalidState);
        (await service.SetStatusAsync(r.Id, ReservationStatus.NoShow, true)).Status.Should().Be((int)ReservationStatus.NoShow);
        await ShouldFail(service.SetStatusAsync(r.Id, ReservationStatus.Cancelled, true), ReservationError.InvalidState);
        await ShouldFail(service.UpdateAsync(r.Id, Input(), true), ReservationError.InvalidState);
        await ShouldFail(service.SetStatusAsync(Guid.NewGuid(), ReservationStatus.Cancelled, true), ReservationError.NotFound);
    }

    [Fact]
    public async Task Purge_erases_old_phones_then_old_rows()
    {
        var (service, db, clock) = Build();
        var recent = await service.CreateAsync(Input(at: T0.AddHours(1), phone: "0612345678"), true);
        var old = await service.CreateAsync(Input(at: T0.AddHours(1), table: "T2", phone: "0698765432"), true);

        clock.Now = T0.AddDays(1);
        (await service.PurgeAsync()).Should().Be(0);

        clock.Now = T0.AddDays(3);
        (await service.PurgeAsync()).Should().Be(2);
        db.ChangeTracker.Clear();
        (await db.Reservations.ToListAsync()).Should().OnlyContain(r => r.Phone == null);

        clock.Now = T0.AddDays(95);
        (await service.PurgeAsync()).Should().Be(2);
        (await db.Reservations.CountAsync()).Should().Be(0);
        recent.Id.Should().NotBe(old.Id);
    }
}
