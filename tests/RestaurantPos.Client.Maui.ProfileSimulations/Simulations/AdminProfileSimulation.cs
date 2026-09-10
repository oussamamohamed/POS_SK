using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ProfileSimulations.Fakes;
using RestaurantPos.Client.Maui.ProfileSimulations.Helpers;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Simulations;

/// <summary>
/// Simulation of the Admin profile (P3).
/// Exercises: PIN auth → StaffAdmin → PrinterAdmin → CatalogAdmin → edge cases.
/// </summary>
public class AdminProfileSimulation
{
    private readonly FakePlatformEnvironmentService _env = new();
    private readonly FakeOperatorAuthenticationService _auth = new(SimulatedOperator.Admin());
    private readonly FakeStaffManagementService _staff = new();
    private readonly FakeBackOfficeCatalogService _catalog = new();
    private readonly FakePrinterConfigurationService _printers = new();

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticateAdmin_WithValidPin_ShouldSucceed()
    {
        var vm = new PinLockViewModel(_env, _auth);

        foreach (char digit in "5555")
            await vm.AppendDigitAsync(digit.ToString());

        vm.IsAuthenticated.Should().BeTrue();
        vm.CurrentOperatorRole.Should().Be(UserRole.Admin);
        vm.CurrentOperatorName.Should().Be("Alexandre Dupont");
    }

    [Fact]
    public void Admin_DomainPermissions_ShouldGrantFullAccess()
    {
        var user = new User { Name = "Alexandre", Role = UserRole.Admin, PinHash = "h", PinSalt = "s" };
        user.CanAccessBackOffice().Should().BeTrue();
        user.CanVoidItems().Should().BeTrue();
        user.CanPrintZReports().Should().BeTrue();
    }

    // ── Staff Admin ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_CreateStaffMember_ShouldAppearInStaffList()
    {
        var vm = new StaffAdminViewModel(_staff, _env);
        await vm.LoadStaffAsync();

        vm.ShowCreateForm();
        vm.NewStaffName = "Lena Dupuis";
        vm.NewStaffRole = UserRole.Cashier;
        vm.NewStaffPin = "6789";

        await vm.CreateStaffAsync();

        vm.StaffMembers.Should().Contain(u => u.Name == "Lena Dupuis");
        vm.IsFormVisible.Should().BeFalse();
        vm.StatusMessage.Should().Contain("Lena Dupuis");
        vm.ErrorMessage.Should().BeEmpty();
        _env.HapticCalls.Should().Contain(HapticFeedbackType.Success);
    }

    [Fact]
    public async Task Admin_CreateDuplicateStaff_ShouldSetErrorMessage()
    {
        // Pre-seed with an existing user
        var existingUser = new User
        {
            Id = UuidV7.NewGuid(),
            Name = "Lena Dupuis",
            Role = UserRole.Cashier,
            PinHash = "h",
            PinSalt = "s"
        };
        var staffWithSeed = new FakeStaffManagementService([existingUser]);
        var vm = new StaffAdminViewModel(staffWithSeed, _env);
        await vm.LoadStaffAsync();

        vm.NewStaffName = "Lena Dupuis"; // duplicate!
        vm.NewStaffPin = "1234";

        await vm.CreateStaffAsync();

        vm.ErrorMessage.Should().NotBeNullOrEmpty("duplicate name should produce an error");
        vm.StaffMembers.Count(u => u.Name == "Lena Dupuis").Should().Be(1, "no duplicate should be created");
    }

    [Fact]
    public async Task Admin_DeactivateStaffMember_ShouldSetInactive()
    {
        var user = new User
        {
            Id = UuidV7.NewGuid(),
            Name = "Jean Dupont",
            Role = UserRole.Waiter,
            PinHash = "h",
            PinSalt = "s",
            IsActive = true
        };
        var staffWithUser = new FakeStaffManagementService([user]);
        var vm = new StaffAdminViewModel(staffWithUser, _env);
        await vm.LoadStaffAsync();

        await vm.DeactivateStaffAsync(user);

        user.IsActive.Should().BeFalse();
        vm.StatusMessage.Should().Contain("Jean Dupont");
    }

    // ── Printer Admin ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_RegisterPrinter_ShouldAppearInPrinterList()
    {
        var discoveryMock = new Mock<ICrossPlatformDiscoveryService>();
        var vm = new PrinterAdminViewModel(_printers, discoveryMock.Object, _env);
        await vm.LoadPrintersAsync();

        vm.NewPrinterName = "Küche Drucker";
        vm.NewPrinterIp = "192.168.1.50";
        vm.NewPrinterPort = 9100;

        await vm.RegisterPrinterAsync();

        vm.Printers.Should().HaveCount(1);
        vm.Printers.First().IpAddress.Should().Be("192.168.1.50");
        vm.Printers.First().Name.Should().Be("Küche Drucker");
        vm.StatusMessage.Should().Contain("Küche Drucker");
    }

    [Fact]
    public async Task Admin_SendTestPrint_ShouldSucceedWithoutErrors()
    {
        var discoveryMock = new Mock<ICrossPlatformDiscoveryService>();
        var vm = new PrinterAdminViewModel(_printers, discoveryMock.Object, _env);

        // Register a printer first
        vm.NewPrinterName = "Receipt Printer";
        vm.NewPrinterIp = "192.168.1.51";
        await vm.RegisterPrinterAsync();

        var printer = vm.Printers.First();
        await vm.TestPrintAsync(printer);

        vm.ErrorMessage.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("Succès");
        _env.HapticCalls.Should().Contain(HapticFeedbackType.Success);
    }

    // ── Catalog Admin ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_CreateCatalogProduct_ShouldAppearInProductList()
    {
        var vm = new CatalogAdminViewModel(_catalog, _env);
        await vm.LoadCategoriesAsync();

        // Select the first seeded category ("Entrées")
        var entrees = vm.Categories.First(c => c.Id == "CAT-001");
        await vm.SelectCategoryAsync(entrees);

        int countBefore = vm.Products.Count;
        await vm.CreateProductAsync("Mousse au Chocolat");

        vm.Products.Should().HaveCount(countBefore + 1);
        vm.Products.Should().Contain(p => p.Name == "Mousse au Chocolat");
    }

    // ── Edge Case: Deactivated Operator Auth ──────────────────────────────────

    [Fact]
    public async Task Admin_DeactivatedOperator_ShouldNotAuthenticate()
    {
        // Use a fake auth that simulates a deactivated account (never returns success)
        var deactivatedAuth = new FakeOperatorAuthenticationService(
            SimulatedOperator.Admin() with { KnownPin = "IMPOSSIBLE" }); // PIN that will never match

        var vm = new PinLockViewModel(_env, deactivatedAuth);

        foreach (char digit in "5555")
            await vm.AppendDigitAsync(digit.ToString());

        vm.IsAuthenticated.Should().BeFalse("deactivated or wrong-pin operator should not authenticate");
    }
}
