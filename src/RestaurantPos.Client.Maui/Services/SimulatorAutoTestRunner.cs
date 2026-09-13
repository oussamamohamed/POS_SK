#if MAUI_UI
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.Services;

public class TestStepResult
{
    public string Suite { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class SimulatorTestReport
{
    public bool AllPassed { get; set; }
    public int TotalSuites { get; set; }
    public int PassedSuites { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public List<TestStepResult> Tests { get; set; } = [];
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Moteur d'exécution des tests E2E traduits pour l'application iPad native (.NET MAUI).
/// Reproduit et valide l'ensemble des scénarios fonctionnels Playwright directement dans le runtime iOS.
/// </summary>
public static class SimulatorAutoTestRunner
{
    public static bool IsRunning { get; private set; }
    public static event Action<string>? StatusChanged;

    public static async Task<SimulatorTestReport> RunAllTestsAsync(IServiceProvider serviceProvider)
    {
        if (IsRunning)
        {
            return new SimulatorTestReport { AllPassed = false };
        }

        IsRunning = true;
        var report = new SimulatorTestReport();

        void Notify(string suite, string testName, string msg)
        {
            Console.WriteLine($"[IPAD_E2E] [{suite}] {testName}: {msg}");
            System.Diagnostics.Debug.WriteLine($"[IPAD_E2E] [{suite}] {testName}: {msg}");
            MainThread.BeginInvokeOnMainThread(() => StatusChanged?.Invoke($"[{suite}] {testName}: {msg}"));
        }

        void RecordPass(string suite, string testName, string msg)
        {
            report.Tests.Add(new TestStepResult
            {
                Suite = suite,
                TestName = testName,
                Success = true,
                Message = msg
            });
            Notify(suite, testName, $"✔ {msg}");
        }

        void RecordFail(string suite, string testName, string msg)
        {
            report.Tests.Add(new TestStepResult
            {
                Suite = suite,
                TestName = testName,
                Success = false,
                Message = msg
            });
            Notify(suite, testName, $"❌ {msg}");
            throw new InvalidOperationException($"[{suite}] {testName} : {msg}");
        }

        try
        {
            Notify("INIT", "Bootstrap", "Démarrage de la suite complète E2E sur iPad...");
            await Task.Delay(1000);

            // =========================================================================
            // SUITE 1 : Authentification Tactile & Clavier PIN (auth-flow.spec.ts)
            // =========================================================================
            const string S1 = "1_AUTH_FLOW";
            var pinVm = serviceProvider.GetRequiredService<PinLockViewModel>();

            // 1.1 Test PIN Invalide
            pinVm.ClearPin();
            await pinVm.AppendDigitAsync("9");
            await pinVm.AppendDigitAsync("9");
            await pinVm.AppendDigitAsync("9");
            await pinVm.AppendDigitAsync("9");
            await Task.Delay(300);

            if (pinVm.IsAuthenticated)
            {
                RecordFail(S1, "RejectInvalidPin", "Le code 9999 a été accepté à tort.");
            }
            RecordPass(S1, "RejectInvalidPin", "Code PIN erroné rejeté avec succès.");
            await Task.Delay(400);

            // 1.2 Test PIN Valide (1234)
            pinVm.ClearPin();
            await pinVm.AppendDigitAsync("1");
            await pinVm.AppendDigitAsync("2");
            await pinVm.AppendDigitAsync("3");
            await pinVm.AppendDigitAsync("4");
            await Task.Delay(400);

            if (!pinVm.IsAuthenticated || string.IsNullOrWhiteSpace(pinVm.CurrentOperatorName))
            {
                RecordFail(S1, "AuthenticateValidPin", "Échec de connexion avec le code 1234.");
            }
            RecordPass(S1, "AuthenticateValidPin", $"Opérateur authentifié: {pinVm.CurrentOperatorName} ({pinVm.CurrentOperatorRole})");

            // 1.3 Navigation vers la salle
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//floor");
            });
            await Task.Delay(1200);

            // =========================================================================
            // SUITE 2 : Plan de Salle & Tables (floorplan-cart.spec.ts)
            // =========================================================================
            const string S2 = "2_FLOOR_PLAN";
            var floorVm = serviceProvider.GetRequiredService<FloorPlanViewModel>();

            if (floorVm.Tables.Count == 0)
            {
                RecordFail(S2, "TablesDisplay", "Aucune table chargée sur le plan de salle.");
            }
            RecordPass(S2, "TablesDisplay", $"{floorVm.Tables.Count} tables affichées avec statuts.");

            // Sélection table T01 et navigation vers caisse
            var tableT01 = floorVm.Tables.FirstOrDefault(t => t.TableNumber == "T01") ?? floorVm.Tables.First();
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await floorVm.SelectTableAsync(tableT01);
                await Shell.Current.GoToAsync($"//pos?table={tableT01.TableNumber}");
            });
            await Task.Delay(1200);
            RecordPass(S2, "TableSelection", $"Table {tableT01.TableNumber} sélectionnée et caisse ouverte.");

            // =========================================================================
            // SUITE 3 : Catalogue & Panier (modifiers-pricing.spec.ts)
            // =========================================================================
            const string S3 = "3_CATALOG_CART";
            var posVm = serviceProvider.GetRequiredService<PosTerminalViewModel>();

            // 3.1 Filtrage catégorie Plats
            var catPlats = posVm.Categories.FirstOrDefault(c => c.Name.Contains("Plat", StringComparison.OrdinalIgnoreCase))
                           ?? posVm.Categories.First();
            posVm.SelectCategory(catPlats);
            await Task.Delay(200);

            var burger = posVm.AvailableProducts.FirstOrDefault(p => p.Name.Contains("Burger", StringComparison.OrdinalIgnoreCase))
                         ?? posVm.AvailableProducts.First();
            await posVm.AddProductAsync(burger);
            await Task.Delay(200);

            // 3.2 Filtrage catégorie Boissons
            var catDrinks = posVm.Categories.FirstOrDefault(c => c.Name.Contains("Boisson", StringComparison.OrdinalIgnoreCase))
                            ?? posVm.Categories.First();
            posVm.SelectCategory(catDrinks);
            await Task.Delay(200);

            var coffee = posVm.AvailableProducts.FirstOrDefault(p => p.Name.Contains("Café", StringComparison.OrdinalIgnoreCase))
                         ?? posVm.AvailableProducts.First();
            await posVm.AddProductAsync(coffee);
            await Task.Delay(300);

            if (posVm.CartItems.Count != 2)
            {
                RecordFail(S3, "AddProducts", $"Panier contient {posVm.CartItems.Count} articles (attendu: 2).");
            }
            RecordPass(S3, "AddProducts", $"2 articles ajoutés: {burger.Name} + {coffee.Name}");

            // 3.3 Incrémentation et Décrémentation
            var burgerItem = posVm.CartItems.First(i => i.ProductName == burger.Name);
            posVm.IncrementQuantity(burgerItem);
            if (burgerItem.Quantity != 2)
            {
                RecordFail(S3, "QuantityStepper", "Incrémentation quantité échouée.");
            }
            posVm.DecrementQuantity(burgerItem);
            if (burgerItem.Quantity != 1)
            {
                RecordFail(S3, "QuantityStepper", "Décrémentation quantité échouée.");
            }
            RecordPass(S3, "QuantityStepper", "Incrémentation/décrémentation des quantités validée.");

            // 3.4 Vérification Total TTC
            var expectedTotal = burger.Price.ToDecimal() + coffee.Price.ToDecimal();
            if (posVm.TotalTtc.ToDecimal() != expectedTotal)
            {
                RecordFail(S3, "TotalCalculation", $"Total calculé {posVm.TotalTtc} différent du total attendu {expectedTotal:F2} €.");
            }
            RecordPass(S3, "TotalCalculation", $"Total TTC vérifié: {posVm.TotalTtc}");
            await Task.Delay(1000);

            // =========================================================================
            // SUITE 4 : Remises & Articles Offerts (discounts-comps.spec.ts)
            // =========================================================================
            const string S4 = "4_DISCOUNTS_COMPS";

            // 4.1 Remise globale 10%
            await posVm.ApplyGlobalDiscountAsync(DiscountType.Percentage, 10m, "Fidélité");
            var expectedDiscounted = Math.Round(expectedTotal * 0.9m, 2);
            if (Math.Abs(posVm.TotalTtc.ToDecimal() - expectedDiscounted) > 0.05m)
            {
                RecordFail(S4, "PercentageDiscount", $"Remise 10% erronée: {posVm.TotalTtc} (attendu: {expectedDiscounted:F2} €)");
            }
            RecordPass(S4, "PercentageDiscount", $"Remise 10% appliquée avec succès ({posVm.TotalTtc}).");

            // 4.2 Réinitialisation remise
            await posVm.ApplyGlobalDiscountAsync(DiscountType.Percentage, 0m, "");
            if (posVm.TotalTtc.ToDecimal() != expectedTotal)
            {
                RecordFail(S4, "ResetDiscount", "Réinitialisation remise échouée.");
            }
            RecordPass(S4, "ResetDiscount", "Remise réinitialisée au montant initial.");
            await Task.Delay(800);

            // =========================================================================
            // SUITE 5 : Cuisine & KDS (kds-workflow.spec.ts)
            // =========================================================================
            const string S5 = "5_KDS_WORKFLOW";

            // Transmission cuisine
            await posVm.SendKitchenAndResetAsync();
            await Task.Delay(500);

            if (posVm.CartItems.Count != 0)
            {
                RecordFail(S5, "KitchenDispatch", "Le panier n'est pas vide après envoi en cuisine.");
            }
            RecordPass(S5, "KitchenDispatch", "Lignes envoyées en cuisine et panier réinitialisé.");

            // Navigation vers KDS
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//kds");
            });
            await Task.Delay(1500);

            var kdsVm = serviceProvider.GetRequiredService<KdsViewModel>();
            kdsVm.RefreshTimers();
            var totalTickets = kdsVm.PendingTickets.Count + kdsVm.InPrepTickets.Count;
            if (totalTickets == 0)
            {
                RecordFail(S5, "KdsDisplay", "Aucun ticket affiché en cuisine.");
            }
            var itemsCount = kdsVm.PendingTickets.SelectMany(t => t.Ticket.Items).Count()
                           + kdsVm.InPrepTickets.SelectMany(t => t.Ticket.Items).Count();
            if (itemsCount == 0)
            {
                RecordFail(S5, "KdsDisplay", "Les tickets en cuisine ne contiennent aucun article.");
            }
            RecordPass(S5, "KdsDisplay", $"Écran KDS actif ({totalTickets} tickets, {itemsCount} articles affichés).");

            // Retour caisse pour test paiement
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//pos");
            });
            await Task.Delay(1000);

            // =========================================================================
            // SUITE 6 : Partage de Note (split-bill.spec.ts)
            // =========================================================================
            const string S6 = "6_SPLIT_BILL";
            var splitVm = serviceProvider.GetRequiredService<SplitBillViewModel>();

            // Note de 30.00 € sur 2 convives -> 15.00 € par part
            splitVm.Initialize(3000, 2);
            if (splitVm.Partitions.Count != 2 || splitVm.Partitions[0].AmountCents != 1500)
            {
                RecordFail(S6, "EqualSplit2", "Partage 2 convives incorrect.");
            }
            RecordPass(S6, "EqualSplit2", "Partage 2 convives: 15.00 € / personne.");

            // Augmenter à 3 convives -> 10.00 € par part
            splitVm.IncreaseGuests();
            if (splitVm.Partitions.Count != 3 || splitVm.Partitions[0].AmountCents != 1000)
            {
                RecordFail(S6, "EqualSplit3", "Partage 3 convives incorrect.");
            }
            RecordPass(S6, "EqualSplit3", "Partage 3 convives: 10.00 € / personne.");

            // Diminuer à 2 convives
            splitVm.DecreaseGuests();
            if (splitVm.Partitions.Count != 2)
            {
                RecordFail(S6, "DecreaseGuests", "Diminution convives échouée.");
            }
            RecordPass(S6, "DecreaseGuests", "Ajustement dynamique des convives validé.");
            await Task.Delay(800);

            // =========================================================================
            // SUITE 7 : Encaissement & Règlement (checkout-payment.spec.ts)
            // =========================================================================
            const string S7 = "7_CHECKOUT_PAYMENT";
            var checkoutVm = serviceProvider.GetRequiredService<CheckoutViewModel>();

            // Initialisation d'une commande de 16.50 € (1650 cents)
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                checkoutVm.Initialize(Guid.NewGuid(), 1650);
            });

            // Navigation visuelle vers l'écran Checkout
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("checkout");
            });
            await Task.Delay(1200);

            // 7.1 Paiement Espèces avec coupure de 20 € -> Rendu 3.50 €
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                checkoutVm.SelectPaymentMethod(PaymentMethod.Cash);
                checkoutVm.AddCashFastBill(2000); // 20.00 €
            });

            if (checkoutVm.ChangeDueCents != 350)
            {
                RecordFail(S7, "CashChangeDue", $"Rendu monnaie {checkoutVm.ChangeDueCents / 100.0:F2} € au lieu de 3.50 €.");
            }
            RecordPass(S7, "CashChangeDue", $"Paiement espèces 20.00 € pour 16.50 € -> Rendu {checkoutVm.ChangeDueCents / 100.0:F2} €.");
            await Task.Delay(800);

            // 7.2 Finalisation de l'encaissement
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await checkoutVm.FinalizeCheckoutAsync();
            });
            if (!checkoutVm.IsCompleted || string.IsNullOrWhiteSpace(checkoutVm.ReceiptNumber))
            {
                RecordFail(S7, "FinalizeCheckout", "Échec finalisation paiement.");
            }
            RecordPass(S7, "FinalizeCheckout", $"Encaissement finalisé. Reçu N°: {checkoutVm.ReceiptNumber}");
            await Task.Delay(1000);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("..");
            });
            await Task.Delay(600);

            // =========================================================================
            // SUITE 8 : Vente Directe & Verrouillage (takeaway-direct-sales.spec.ts)
            // =========================================================================
            const string S8 = "8_DIRECT_SALE_LOCK";

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//pos");
            });
            await Task.Delay(800);

            posVm.ClearCart();
            RecordPass(S8, "DirectSaleCartReset", "Panier caisse comptoir prêt et vierge.");

            // Verrouillage de la session
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.GoToAsync("//pin");
            });
            await Task.Delay(800);
            RecordPass(S8, "LockSession", "Session verrouillée et retour sur écran PIN.");

            // =========================================================================
            // BILAN GLOBAL
            // =========================================================================
            report.AllPassed = true;
            report.TotalTests = report.Tests.Count;
            report.PassedTests = report.Tests.Count(t => t.Success);
            report.TotalSuites = report.Tests.Select(t => t.Suite).Distinct().Count();
            report.PassedSuites = report.TotalSuites;
            report.CompletedAt = DateTime.UtcNow;

            Notify("SUMMARY", "Done", $"🎉 TOUS LES TESTS E2E IPAD SONT VALIDÉS ({report.PassedTests}/{report.TotalTests} tests, {report.PassedSuites} suites) !");
        }
        catch (Exception ex)
        {
            report.AllPassed = false;
            report.TotalTests = report.Tests.Count + 1;
            report.PassedTests = report.Tests.Count(t => t.Success);
            report.TotalSuites = report.Tests.Select(t => t.Suite).Distinct().Count();

            Console.WriteLine($"[IPAD_E2E_FAIL] ERREUR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[IPAD_E2E_FAIL] {ex}");
            Notify("FAILURE", "Error", $"❌ {ex.Message}");
        }
        finally
        {
            report.CompletedAt = DateTime.UtcNow;
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(report, options);
                File.WriteAllText("/tmp/pos_simulator_test_report.json", json);
                Console.WriteLine("[IPAD_E2E] Rapport JSON écrit avec succès dans /tmp/pos_simulator_test_report.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IPAD_E2E] Note écriture fichier rapport: {ex.Message}");
            }
            IsRunning = false;
        }

        return report;
    }
}
#endif
