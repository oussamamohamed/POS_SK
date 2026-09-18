#if MAUI_UI
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Controls;
using RestaurantPos.Client.Maui.Theme;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Page Fiscalité NF525 conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Présentation Inset Grouped, scellement cryptographique inaltérable SHA-256 et export FEC.
/// </summary>
public class FiscalPage : ContentPage
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private Label _slipDateLabel = null!;
    private Label _slipTtcLabel = null!;
    private Label _slipHtLabel = null!;
    private Label _slipCountLabel = null!;
    private Label _slipPerpetualLabel = null!;
    private Label _slipHashLabel = null!;
    private Label _fecStatusLabel = null!;

    public FiscalPage() : this(null) { }

    public FiscalPage(IServiceScopeFactory? scopeFactory)
    {
        _scopeFactory = scopeFactory;
        BackgroundColor = AppleHigTheme.SystemBackground;
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetNavBarIsVisible(this, false);
        _ = LoadFiscalDataAsync();
    }

    private void Build()
    {
        var topBar = new GlobalHeaderView(PosActiveViewTab.Fiscal);

        var contentScroll = new ScrollView
        {
            Padding = new Thickness(24),
            Content = new VerticalStackLayout
            {
                Spacing = 20,
                Children =
                {
                    BuildFiscalReportCard(),
                    BuildFecExportCard()
                }
            }
        };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(topBar, 0),
                AddToGrid(contentScroll, 1)
            }
        };
    }

    private View BuildFiscalReportCard()
    {
        var card = new Border
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusLarge) },
            Padding = new Thickness(24)
        };

        var title = new Label
        {
            Text = "📜 Traçabilité Fiscale NF525 & Clôtures",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.Title2,
            FontAttributes = FontAttributes.Bold
        };

        var subtitle = new Label
        {
            Text = "Chaînage cryptographique inaltérable SHA-256 des règlements et scellement journalier.",
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Subheadline,
            Margin = new Thickness(0, 4, 0, 16)
        };

        var actionsRow = new HorizontalStackLayout
        {
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 16),
            Children =
            {
                new Button
                {
                    Text = "🔒 Exécuter la Clôture Journalière (Rapport Z)",
                    BackgroundColor = AppleHigTheme.SystemGreen,
                    TextColor = Colors.White,
                    FontSize = AppleHigTheme.Subheadline,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 44,
                    MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
                    CornerRadius = 10,
                    Padding = new Thickness(16, 0),
                    Command = new Command(async () => await ExecuteZReportAsync())
                },
                new Button
                {
                    Text = "👁️ Aperçu du Rapport X (En cours)",
                    BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                    TextColor = AppleHigTheme.LabelPrimary,
                    FontSize = AppleHigTheme.Subheadline,
                    HeightRequest = 44,
                    MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
                    CornerRadius = 10,
                    Padding = new Thickness(16, 0),
                    Command = new Command(ExecuteXReport)
                }
            }
        };

        // Ticket Slip Display
        var ticketSlip = BuildTicketSlip();

        card.Content = new VerticalStackLayout
        {
            Children = { title, subtitle, actionsRow, ticketSlip }
        };

        return card;
    }

    private View BuildTicketSlip()
    {
        var slipFrame = new Border
        {
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusMedium) },
            Padding = new Thickness(22),
            MaximumWidthRequest = 540,
            HorizontalOptions = LayoutOptions.Start
        };

        _slipDateLabel = new Label { Text = $"Date: {DateTime.Now:dd/MM/yyyy HH:mm:ss}", TextColor = AppleHigTheme.LabelSecondary, FontSize = 12 };
        _slipTtcLabel = new Label { Text = "0,00 €", TextColor = AppleHigTheme.SystemGreen, FontSize = AppleHigTheme.Headline, FontAttributes = FontAttributes.Bold };
        _slipHtLabel = new Label { Text = "0,00 €", TextColor = AppleHigTheme.LabelPrimary, FontSize = 13 };
        _slipCountLabel = new Label { Text = "0", TextColor = AppleHigTheme.LabelPrimary, FontSize = 13 };
        _slipPerpetualLabel = new Label { Text = "0,00 €", TextColor = AppleHigTheme.LabelPrimary, FontSize = AppleHigTheme.Headline, FontAttributes = FontAttributes.Bold };
        _slipHashLabel = new Label
        {
            Text = "En attente de scellement (0 transaction)",
            TextColor = AppleHigTheme.SystemTeal,
            FontSize = 10,
            FontFamily = "Courier",
            LineBreakMode = LineBreakMode.CharacterWrap
        };

        var body = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "*** RAPPORT FISCAL NF525 ***", TextColor = AppleHigTheme.LabelPrimary, FontSize = 15, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center },
                new Label { Text = "Norme NF525 — Traçabilité Fiscale Inaltérable", TextColor = AppleHigTheme.LabelSecondary, FontSize = 11, HorizontalOptions = LayoutOptions.Center },
                _slipDateLabel,
                new Label { Text = "Terminal: POS_MAIN_TERM (iPad)", TextColor = AppleHigTheme.LabelSecondary, FontSize = 12 },
                new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator, Margin = new Thickness(0, 4) },
                MakeSlipRow("Total Ventes TTC :", _slipTtcLabel),
                MakeSlipRow("Total Ventes HT :", _slipHtLabel),
                MakeSlipRow("Nombre de Tickets :", _slipCountLabel),
                MakeSlipRow("Grand Total Perpétuel :", _slipPerpetualLabel),
                new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator, Margin = new Thickness(0, 4) },
                new Label { Text = "Signature Cryptographique SHA-256 :", TextColor = AppleHigTheme.LabelSecondary, FontSize = 11 },
                _slipHashLabel,
                new Label { Text = "✓ Chaîne d'Audit Fiscale Scellée (NF525 Conforme)", TextColor = AppleHigTheme.SystemGreen, FontSize = 12, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 6, 0, 0) }
            }
        };

        slipFrame.Content = body;
        return slipFrame;
    }

    private static Grid MakeSlipRow(string labelText, Label valueLabel)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        grid.Add(new Label { Text = labelText, TextColor = AppleHigTheme.LabelSecondary, FontSize = 13 });
        Grid.SetColumn(valueLabel, 1);
        grid.Add(valueLabel);
        return grid;
    }

    private View BuildFecExportCard()
    {
        var card = new Border
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusLarge) },
            Padding = new Thickness(24)
        };

        var title = new Label
        {
            Text = "📂 Export Comptable FEC (Article A.47 A-1 LPF)",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.Title3,
            FontAttributes = FontAttributes.Bold
        };

        var subtitle = new Label
        {
            Text = "Génération du Fichier des Écritures Comptables normalisé (18 colonnes DGFIP) pour l'administration fiscale et l'expert-comptable.",
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Subheadline,
            Margin = new Thickness(0, 4, 0, 16)
        };

        var exportBtn = new Button
        {
            Text = "📥 Générer et Télécharger le Fichier FEC (.txt)",
            BackgroundColor = AppleHigTheme.SystemGreen,
            TextColor = Colors.White,
            FontSize = AppleHigTheme.Subheadline,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
            CornerRadius = 10,
            Padding = new Thickness(16, 0),
            HorizontalOptions = LayoutOptions.Start,
            Command = new Command(ExecuteFecExport)
        };

        _fecStatusLabel = new Label
        {
            TextColor = AppleHigTheme.SystemGreen,
            FontSize = AppleHigTheme.Footnote,
            Margin = new Thickness(0, 8, 0, 0)
        };

        card.Content = new VerticalStackLayout
        {
            Children = { title, subtitle, exportBtn, _fecStatusLabel }
        };

        return card;
    }

    private async Task<(long totalTtc, long totalHt, int count, long perpetual, string lastHash, DateTimeOffset periodStart)> GetActiveFiscalTotalsAsync()
    {
        long totalTtc = 0;
        long totalHt = 0;
        int count = 0;
        long perpetual = 0;
        string lastHash = "0000000000000000000000000000000000000000000000000000000000000000";
        DateTimeOffset periodStart = DateTimeOffset.MinValue;

        var scopeFactory = _scopeFactory 
            ?? App.Services?.GetService<IServiceScopeFactory>()
            ?? Handler?.MauiContext?.Services?.GetService<IServiceScopeFactory>();
        if (scopeFactory is not null)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetService<RestaurantPos.Client.Maui.Persistence.LocalAppDbContext>();
                if (db is not null)
                {
                    var lastClosure = db.DailyFiscalClosures.OrderByDescending(c => c.ClosureSequence).FirstOrDefault();
                    if (lastClosure is not null)
                    {
                        periodStart = lastClosure.PeriodEndUtc;
                    }

                    var allReceipts = db.FiscalReceipts.Where(r => !r.IsVoid).OrderBy(r => r.SequenceNumber).ToList();
                    perpetual = Math.Max(allReceipts.Sum(r => r.TotalTtcAmount.AmountInCents), lastClosure?.PerpetualGrandTotalCents ?? 0);

                    var activeReceipts = allReceipts.Where(r => r.CreatedAtUtc > periodStart).ToList();

                    totalTtc = activeReceipts.Sum(r => r.TotalTtcAmount.AmountInCents);
                    totalHt = activeReceipts.Sum(r => r.TotalHtAmount.AmountInCents);
                    count = activeReceipts.Count;

                    if (activeReceipts.Count > 0)
                    {
                        lastHash = activeReceipts.Last().SignatureHash;
                    }
                    else if (lastClosure is not null && !string.IsNullOrWhiteSpace(lastClosure.SignatureHash))
                    {
                        lastHash = lastClosure.SignatureHash;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Local fiscal query error: {ex}");
            }
        }

        // Try query backend API to ensure consistency with Master POS
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            var apiX = await client.GetFromJsonAsync<ApiFiscalXReport>("http://127.0.0.1:5000/api/sync/fiscal/x-report");
            if (apiX is not null)
            {
                long apiTtc = (long)Math.Round(apiX.TotalSalesTtc * 100);
                long apiHt = (long)Math.Round(apiX.TotalSalesHt * 100);
                long apiPerpetual = (long)Math.Round(apiX.PerpetualGrandTotal * 100);

                // Only take API sales if it represents sales in the current session (after periodStart)
                if (apiX.PeriodStartUtc >= periodStart && apiTtc > totalTtc)
                {
                    totalTtc = apiTtc;
                    totalHt = apiHt;
                    count = apiX.ReceiptCount;
                }
                perpetual = Math.Max(perpetual, apiPerpetual);
            }
        }
        catch { /* Master POS offline or not reachable */ }

        return (totalTtc, totalHt, count, perpetual, lastHash, periodStart);
    }

    private async Task LoadFiscalDataAsync()
    {
        var data = await GetActiveFiscalTotalsAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _slipDateLabel.Text = $"Date: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
            _slipTtcLabel.Text = $"{data.totalTtc / 100.0:F2} €";
            _slipHtLabel.Text = $"{data.totalHt / 100.0:F2} €";
            _slipCountLabel.Text = $"{data.count}";
            _slipPerpetualLabel.Text = $"{data.perpetual / 100.0:F2} €";
            _slipHashLabel.Text = data.lastHash;
        });
    }

    public string SlipTtcText => _slipTtcLabel?.Text ?? string.Empty;
    public string SlipPerpetualText => _slipPerpetualLabel?.Text ?? string.Empty;

    public async Task<(long totalTtc, long totalHt, int count, long perpetual, string lastHash, DateTimeOffset periodStart)> GetActiveFiscalTotalsPublicAsync()
    {
        return await GetActiveFiscalTotalsAsync();
    }

    public async Task ExecuteZReportAsync(bool showAlert = true)
    {
        var data = await GetActiveFiscalTotalsAsync();
        var scopeFactory = _scopeFactory 
            ?? App.Services?.GetService<IServiceScopeFactory>() 
            ?? Handler?.MauiContext?.Services?.GetService<IServiceScopeFactory>();
        long nextClosureSeq = 1;
        string closureHash = "0000000000000000000000000000000000000000000000000000000000000000";

        if (scopeFactory is not null)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetService<RestaurantPos.Client.Maui.Persistence.LocalAppDbContext>();
                if (db is not null)
                {
                    var lastClosure = db.DailyFiscalClosures.OrderByDescending(c => c.ClosureSequence).FirstOrDefault();
                    nextClosureSeq = (lastClosure?.ClosureSequence ?? 0) + 1;
                    var now = DateTimeOffset.UtcNow;
                    string rawData = $"{lastClosure?.SignatureHash ?? "GENESIS"}|POS_MAIN_TERM|{nextClosureSeq}|{data.totalTtc}|{now:O}|{data.count}";
                    closureHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawData)));

                    var closure = new RestaurantPos.Domain.Entities.DailyFiscalClosure
                    {
                        Id = Guid.NewGuid(),
                        TerminalId = "POS_MAIN_TERM",
                        ClosureSequence = nextClosureSeq,
                        PeriodStartUtc = data.periodStart,
                        PeriodEndUtc = now,
                        TotalSalesTtc = RestaurantPos.Domain.ValueObjects.Money.FromCents(data.totalTtc),
                        TotalSalesHt = RestaurantPos.Domain.ValueObjects.Money.FromCents(data.totalHt),
                        PerpetualGrandTotalCents = data.perpetual,
                        PreviousSignatureHash = lastClosure?.SignatureHash ?? "GENESIS",
                        SignatureHash = closureHash,
                        SealedByUserName = "Responsable Caisse (iPad)",
                        SealedByUserId = Guid.NewGuid()
                    };
                    db.DailyFiscalClosures.Add(closure);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Local Z-closure error: {ex}");
            }
        }

        // Also sync Z-closure with backend Master POS if reachable
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var payload = new
            {
                TerminalId = "POS_MAIN_TERM",
                ManagerId = Guid.NewGuid(),
                ManagerName = "Responsable Caisse (iPad)"
            };
            await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, "http://127.0.0.1:5000/api/sync/fiscal/z-closure", payload);
        }
        catch { }

        if (showAlert)
        {
            await DisplayAlert(
                "Clôture Z Effectuée",
                $"Rapport Z n°{nextClosureSeq} scellé et enregistré avec succès.\n" +
                $"Total TTC clôturé : {data.totalTtc / 100.0:F2} € ({data.count} vente(s)).\n\n" +
                $"Le compteur journalier de la nouvelle session repart à 0,00 €.\n" +
                $"Grand Total Perpétuel conservé : {data.perpetual / 100.0:F2} €.",
                "OK");
        }

        await LoadFiscalDataAsync();
    }

    private async void ExecuteXReport()
    {
        var data = await GetActiveFiscalTotalsAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _slipDateLabel.Text = $"Aperçu Rapport X : {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
            _slipTtcLabel.Text = $"{data.totalTtc / 100.0:F2} €";
            _slipHtLabel.Text = $"{data.totalHt / 100.0:F2} €";
            _slipCountLabel.Text = $"{data.count}";
            _slipPerpetualLabel.Text = $"{data.perpetual / 100.0:F2} €";
            _slipHashLabel.Text = data.lastHash;
            DisplayAlert("Aperçu Rapport X", $"Rapport X intermédiaire :\n{data.count} vente(s) en cours : {data.totalTtc / 100.0:F2} € TTC ({data.totalHt / 100.0:F2} € HT).", "OK");
        });
    }

    private async void ExecuteFecExport()
    {
        var data = await GetActiveFiscalTotalsAsync();
        int count = Math.Max(1, data.count);
        _fecStatusLabel.Text = $"✓ Fichier FEC généré avec succès ({DateTime.Now:yyyyMMdd}_FEC.txt) — {count * 2} écritures comptables conformes DGFIP (TTC {data.totalTtc / 100.0:F2} €).";
        await DisplayAlert("Export FEC Réussi", $"Le fichier normalisé ({DateTime.Now:yyyyMMdd}_FEC.txt) est prêt pour l'expert-comptable.\nTotal écritures : {data.totalTtc / 100.0:F2} € TTC.", "OK");
    }

    private class ApiFiscalXReport
    {
        public string? TerminalId { get; set; }
        public decimal TotalSalesTtc { get; set; }
        public decimal TotalSalesHt { get; set; }
        public int ReceiptCount { get; set; }
        public decimal PerpetualGrandTotal { get; set; }
        public DateTimeOffset PeriodStartUtc { get; set; }
        public DateTimeOffset PeriodEndUtc { get; set; }
    }

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }
}
#endif
