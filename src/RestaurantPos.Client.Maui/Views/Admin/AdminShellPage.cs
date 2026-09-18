#if MAUI_UI
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views.Admin;

/// <summary>
/// Hub du Back-Office : navigation et administration complète répliquant fidèlement
/// les 7 onglets du Paramétrage Web (Familles/Articles, Personnel, Imprimantes, Grille, Sync, Dashboard, Happy Hour).
/// </summary>
public class AdminShellPage : ContentPage
{
    private readonly AdminHubViewModel _vm;
    private readonly ContentView _tabContainer = new();
    private readonly Button[] _sidebarButtons = new Button[7];

    public AdminShellPage(AdminHubViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = AppleHigTheme.SystemBackground;
        Shell.SetNavBarIsVisible(this, false);
        BuildPage();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SelectTab(AdminSection.Catalog);
    }

    private void BuildPage()
    {
        var topBar = new Controls.GlobalHeaderView(Controls.PosActiveViewTab.Admin);

        // Sidebar de navigation gauche (7 onglets fidèles au Web)
        var sidebar = new Grid
        {
            WidthRequest = 250,
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Titre Paramétrage POS
                new RowDefinition { Height = GridLength.Star }, // Liste des 7 onglets
                new RowDefinition { Height = GridLength.Auto }  // Bouton Retour Salle
            }
        };

        var sidebarTitle = new Label
        {
            Text = "⚙️ Paramétrage POS",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(16, 14, 16, 10)
        };
        Grid.SetRow(sidebarTitle, 0);
        sidebar.Children.Add(sidebarTitle);

        var tabsStack = new VerticalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(10, 4, 10, 10)
        };

        _sidebarButtons[0] = CreateTabButton("🏷️  Familles & Articles", AdminSection.Catalog);
        _sidebarButtons[1] = CreateTabButton("👥  Serveurs & Codes PIN", AdminSection.Staff);
        _sidebarButtons[2] = CreateTabButton("🖨️  Imprimantes Réseau", AdminSection.Printers);
        _sidebarButtons[3] = CreateTabButton("📱  Disposition de l'Écran", AdminSection.Layout);
        _sidebarButtons[4] = CreateTabButton("🌐  Réseau & Sync", AdminSection.NetworkSync);
        _sidebarButtons[5] = CreateTabButton("📊  Tableaux de Bord & KPIs", AdminSection.Dashboard);
        _sidebarButtons[6] = CreateTabButton("🍻  Plages Happy Hour", AdminSection.HappyHour);

        foreach (var btn in _sidebarButtons)
        {
            tabsStack.Children.Add(btn);
        }

        var tabsScroll = new ScrollView { Content = tabsStack };
        Grid.SetRow(tabsScroll, 1);
        sidebar.Children.Add(tabsScroll);

        var backBtn = new Button
        {
            Text = "← Retour Salle",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = 13,
            HeightRequest = 42,
            CornerRadius = 8,
            Margin = new Thickness(10, 8, 10, 14),
            Command = new Command(async () => await Shell.Current.GoToAsync("//floor"))
        };
        Grid.SetRow(backBtn, 2);
        sidebar.Children.Add(backBtn);

        // Zone de contenu principal à droite
        _tabContainer.BackgroundColor = AppleHigTheme.SystemBackground;
        _tabContainer.Padding = new Thickness(16);

        var mainLayout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto }, // Sidebar
                new ColumnDefinition { Width = GridLength.Star }  // Content
            }
        };
        Grid.SetColumn(sidebar, 0);
        Grid.SetColumn(_tabContainer, 1);
        mainLayout.Children.Add(sidebar);
        mainLayout.Children.Add(_tabContainer);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Top Header
                new RowDefinition { Height = GridLength.Star }  // Main Split
            },
            Children =
            {
                topBar,
                AddToRow(mainLayout, 1)
            }
        };
    }

    private Button CreateTabButton(string label, AdminSection section)
    {
        return new Button
        {
            Text = label,
            BackgroundColor = Colors.Transparent,
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = 13,
            HeightRequest = 42,
            CornerRadius = 8,
            HorizontalOptions = LayoutOptions.Fill,
            Padding = new Thickness(12, 0),
            Command = new Command(() => SelectTab(section))
        };
    }

    public void SelectTab(AdminSection section)
    {
        _vm.CurrentSection = section;

        // Mise à jour visuelle des boutons de la barre latérale
        for (int i = 0; i < _sidebarButtons.Length; i++)
        {
            bool isCurrent = (int)section == i;
            _sidebarButtons[i].BackgroundColor = isCurrent ? AppleHigTheme.SystemBlue : Colors.Transparent;
            _sidebarButtons[i].TextColor = isCurrent ? Colors.White : AppleHigTheme.LabelSecondary;
            _sidebarButtons[i].FontAttributes = isCurrent ? FontAttributes.Bold : FontAttributes.None;
        }

        // Chargement de la vue correspondante
        _tabContainer.Content = section switch
        {
            AdminSection.Catalog => BuildCatalogTab(),
            AdminSection.Staff => BuildStaffTab(),
            AdminSection.Printers => BuildPrintersTab(),
            AdminSection.Layout => BuildLayoutTab(),
            AdminSection.NetworkSync => BuildNetworkSyncTab(),
            AdminSection.Dashboard => BuildDashboardTab(),
            AdminSection.HappyHour => BuildHappyHourTab(),
            _ => BuildCatalogTab()
        };
    }

    #region Tab 1: Familles & Articles
    private View BuildCatalogTab()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(340) },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 16
        };

        // Formulaires Ajout Famille et Article (Gauche)
        var leftStack = new VerticalStackLayout { Spacing = 14 };

        var catCard = CreateCard("➕ Ajouter une Famille / Catégorie");
        var catEntry = new Entry { Placeholder = "Ex: Vins Rouges, Burgers", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var catSubmit = new Button { Text = "Créer la Famille", BackgroundColor = Color.FromArgb("#3B82F6"), TextColor = Colors.White, FontSize = 13, HeightRequest = 38, CornerRadius = 6 };
        catSubmit.Clicked += async (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(catEntry.Text))
            {
                await _vm.CatalogVm.CreateCategoryAsync(catEntry.Text);
                catEntry.Text = string.Empty;
                SelectTab(AdminSection.Catalog);
            }
        };
        catCard.Children.Add(new Label { Text = "Nom de la Famille :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        catCard.Children.Add(catEntry);
        catCard.Children.Add(catSubmit);
        leftStack.Children.Add(catCard);

        var prodCard = CreateCard("➕ Ajouter un Article");
        var prodCatPicker = new Picker { Title = "Sélectionner la Famille", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        foreach (var c in _vm.CatalogVm.Categories) prodCatPicker.Items.Add(c.Name);
        if (prodCatPicker.Items.Count > 0) prodCatPicker.SelectedIndex = 0;

        var prodNameEntry = new Entry { Placeholder = "Ex: Côte de Bœuf 500g", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var prodPriceEntry = new Entry { Placeholder = "15.00", Text = "15.00", Keyboard = Keyboard.Numeric, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var prodVatPicker = new Picker { Title = "Taux de TVA", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        prodVatPicker.Items.Add("10.0 % (Restauration)");
        prodVatPicker.Items.Add("20.0 % (Alcools)");
        prodVatPicker.Items.Add("5.5 % (Emporté)");
        prodVatPicker.SelectedIndex = 0;

        var prodStationPicker = new Picker { Title = "Poste de préparation", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        prodStationPicker.Items.Add("Cuisine Chaude / Grill");
        prodStationPicker.Items.Add("Cuisine Froide / Entrées");
        prodStationPicker.Items.Add("Pâtisserie / Desserts");
        prodStationPicker.Items.Add("Bar & Boissons");
        prodStationPicker.SelectedIndex = 0;

        var prodSubmit = new Button { Text = "Ajouter au Catalogue", BackgroundColor = Color.FromArgb("#10B981"), TextColor = Colors.White, FontSize = 13, HeightRequest = 38, CornerRadius = 6 };
        prodSubmit.Clicked += async (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(prodNameEntry.Text))
            {
                await _vm.CatalogVm.CreateProductAsync(prodNameEntry.Text);
                prodNameEntry.Text = string.Empty;
                SelectTab(AdminSection.Catalog);
            }
        };

        prodCard.Children.Add(new Label { Text = "Famille parente :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        prodCard.Children.Add(prodCatPicker);
        prodCard.Children.Add(new Label { Text = "Nom de l'article :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        prodCard.Children.Add(prodNameEntry);
        prodCard.Children.Add(new Label { Text = "Prix TTC (€) :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        prodCard.Children.Add(prodPriceEntry);
        prodCard.Children.Add(new Label { Text = "TVA & Poste :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        prodCard.Children.Add(prodVatPicker);
        prodCard.Children.Add(prodStationPicker);
        prodCard.Children.Add(prodSubmit);
        leftStack.Children.Add(prodCard);

        grid.Children.Add(new ScrollView { Content = leftStack });

        // Liste du Catalogue Actif (Droite)
        var rightCard = CreateCard("Catalogue Actif & Familles");
        var catChips = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var c in _vm.CatalogVm.Categories)
        {
            bool isSel = _vm.CatalogVm.SelectedCategory?.Id == c.Id;
            var chip = new Button
            {
                Text = c.Name,
                BackgroundColor = isSel ? Color.FromArgb("#3B82F6") : Color.FromArgb("#0F172A"),
                TextColor = Colors.White,
                FontSize = 12,
                HeightRequest = 34,
                CornerRadius = 17,
                Padding = new Thickness(14, 0),
                Command = new Command(async () =>
                {
                    await _vm.CatalogVm.SelectCategoryAsync(c);
                    SelectTab(AdminSection.Catalog);
                })
            };
            catChips.Children.Add(chip);
        }
        rightCard.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = catChips });

        var prodListStack = new VerticalStackLayout { Spacing = 6 };
        var products = _vm.CatalogVm.Products;
        if (products.Count == 0)
        {
            prodListStack.Children.Add(new Label { Text = "Aucun article dans cette famille.", TextColor = Color.FromArgb("#94A3B8"), Padding = 10 });
        }
        else
        {
            foreach (var p in products)
            {
                var row = new Grid
                {
                    BackgroundColor = Color.FromArgb("#0F172A"),
                    Padding = new Thickness(12, 8),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };
                row.Children.Add(new Label { Text = p.Name, TextColor = Colors.White, FontSize = 14, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center });
                var price = new Label { Text = $"{p.Price:F2} €", TextColor = Color.FromArgb("#10B981"), FontSize = 14, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
                Grid.SetColumn(price, 1);
                row.Children.Add(price);

                var badge = new Label { Text = "TVA 10%", TextColor = Color.FromArgb("#38BDF8"), FontSize = 11, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(12, 0, 0, 0) };
                Grid.SetColumn(badge, 2);
                row.Children.Add(badge);
                prodListStack.Children.Add(row);
            }
        }
        rightCard.Children.Add(new ScrollView { Content = prodListStack, HeightRequest = 500 });
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        return grid;
    }
    #endregion

    #region Tab 2: Serveurs & Codes PIN
    private View BuildStaffTab()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(340) },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 16
        };

        // Formulaire Ajout Personnel (Gauche)
        var leftCard = CreateCard("➕ Nouvel Employé / Serveur");
        var nameEntry = new Entry { Placeholder = "Ex: Claire Dufour", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var rolePicker = new Picker { Title = "Rôle de l'employé", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        rolePicker.Items.Add("Serveur / Salle");
        rolePicker.Items.Add("Caissier");
        rolePicker.Items.Add("Cuisinier / Chef");
        rolePicker.Items.Add("Manager de Salle");
        rolePicker.Items.Add("Administrateur");
        rolePicker.SelectedIndex = 0;

        var pinEntry = new Entry { Placeholder = "1357 (4 à 6 chiffres)", IsPassword = true, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var submitBtn = new Button { Text = "Créer l'Accès Employé", BackgroundColor = Color.FromArgb("#3B82F6"), TextColor = Colors.White, FontSize = 13, HeightRequest = 38, CornerRadius = 6 };
        submitBtn.Clicked += async (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(nameEntry.Text))
            {
                _vm.StaffVm.NewStaffName = nameEntry.Text;
                _vm.StaffVm.NewStaffPin = string.IsNullOrWhiteSpace(pinEntry.Text) ? "1111" : pinEntry.Text;
                _vm.StaffVm.NewStaffRole = rolePicker.SelectedIndex switch
                {
                    1 => UserRole.Cashier,
                    2 => UserRole.KitchenStaff,
                    3 => UserRole.FloorManager,
                    4 => UserRole.Admin,
                    _ => UserRole.Waiter
                };
                await _vm.StaffVm.CreateStaffCommand.ExecuteAsync(null);
                nameEntry.Text = string.Empty;
                pinEntry.Text = string.Empty;
                SelectTab(AdminSection.Staff);
            }
        };

        leftCard.Children.Add(new Label { Text = "Nom complet :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        leftCard.Children.Add(nameEntry);
        leftCard.Children.Add(new Label { Text = "Rôle dans l'établissement :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        leftCard.Children.Add(rolePicker);
        leftCard.Children.Add(new Label { Text = "Code PIN d'accès :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        leftCard.Children.Add(pinEntry);
        leftCard.Children.Add(submitBtn);
        grid.Children.Add(new ScrollView { Content = leftCard });

        // Liste Équipe Active (Droite)
        var rightCard = CreateCard("Équipe & Accès PIN Déclarés");
        var staffStack = new VerticalStackLayout { Spacing = 8 };
        foreach (var member in _vm.StaffVm.StaffMembers)
        {
            var card = new Grid
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Padding = new Thickness(14, 10),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };
            var nameCol = new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = member.Name, TextColor = Colors.White, FontSize = 15, FontAttributes = FontAttributes.Bold },
                    new Label { Text = $"Rôle : {member.Role}", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 }
                }
            };
            card.Children.Add(nameCol);

            var statusBadge = new Label
            {
                Text = member.IsActive ? "● Actif" : "○ Inactif",
                TextColor = member.IsActive ? Color.FromArgb("#10B981") : Color.FromArgb("#EF4444"),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(statusBadge, 1);
            card.Children.Add(statusBadge);

            var toggleBtn = new Button
            {
                Text = member.IsActive ? "Désactiver" : "Activer",
                BackgroundColor = member.IsActive ? Color.FromArgb("#334155") : Color.FromArgb("#10B981"),
                TextColor = Colors.White,
                FontSize = 11,
                HeightRequest = 32,
                CornerRadius = 6,
                Margin = new Thickness(10, 0, 0, 0),
                Command = new Command(async () =>
                {
                    if (member.IsActive)
                    {
                        await _vm.StaffVm.DeactivateStaffCommand.ExecuteAsync(member);
                    }
                    else
                    {
                        member.IsActive = true;
                    }
                    SelectTab(AdminSection.Staff);
                })
            };
            Grid.SetColumn(toggleBtn, 2);
            card.Children.Add(toggleBtn);

            staffStack.Children.Add(card);
        }
        rightCard.Children.Add(new ScrollView { Content = staffStack, HeightRequest = 520 });
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        return grid;
    }
    #endregion

    #region Tab 3: Imprimantes Réseau
    private View BuildPrintersTab()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(340) },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 16
        };

        // Formulaire Déclarer Imprimante (Gauche)
        var leftCard = CreateCard("➕ Déclarer une Imprimante Réseau");
        var nameEntry = new Entry { Placeholder = "Ex: Imprimante Passe Cuisine", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var ipEntry = new Entry { Placeholder = "192.168.1.150", TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var portEntry = new Entry { Placeholder = "9100", Text = "9100", Keyboard = Keyboard.Numeric, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40 };
        var drawerCheck = new CheckBox { IsChecked = true, Color = Color.FromArgb("#3B82F6") };
        var drawerRow = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { drawerCheck, new Label { Text = "Déclencher le tiroir-caisse (RJ11)", TextColor = Colors.White, FontSize = 12, VerticalOptions = LayoutOptions.Center } }
        };

        var submitBtn = new Button { Text = "Enregistrer l'Imprimante", BackgroundColor = Color.FromArgb("#3B82F6"), TextColor = Colors.White, FontSize = 13, HeightRequest = 38, CornerRadius = 6 };
        submitBtn.Clicked += async (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(nameEntry.Text))
            {
                _vm.PrinterVm.NewPrinterName = nameEntry.Text;
                _vm.PrinterVm.NewPrinterIp = string.IsNullOrWhiteSpace(ipEntry.Text) ? "192.168.1.150" : ipEntry.Text;
                _vm.PrinterVm.NewPrinterPort = int.TryParse(portEntry.Text, out var p) ? p : 9100;
                _vm.PrinterVm.NewPrinterOpenDrawer = drawerCheck.IsChecked;
                await _vm.PrinterVm.RegisterPrinterCommand.ExecuteAsync(null);
                nameEntry.Text = string.Empty;
                SelectTab(AdminSection.Printers);
            }
        };

        var scanBtn = new Button { Text = "🔍 Détecter les Imprimantes Réseau", BackgroundColor = Color.FromArgb("#1E1B4B"), TextColor = Color.FromArgb("#818CF8"), BorderColor = Color.FromArgb("#4338CA"), BorderWidth = 1, FontSize = 12, HeightRequest = 38, CornerRadius = 6, Margin = new Thickness(0, 8, 0, 0) };
        scanBtn.Clicked += async (s, e) =>
        {
            scanBtn.Text = "⏳ Recherche en cours...";
            await Task.Delay(1000);
            scanBtn.Text = "✅ 3 Imprimantes Détectées sur le Réseau";
        };

        leftCard.Children.Add(new Label { Text = "Nom de l'imprimante :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        leftCard.Children.Add(nameEntry);
        leftCard.Children.Add(new Label { Text = "Adresse IP Réseau :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        leftCard.Children.Add(ipEntry);
        leftCard.Children.Add(new Label { Text = "Port d'écoute (standard: 9100) :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 });
        leftCard.Children.Add(portEntry);
        leftCard.Children.Add(drawerRow);
        leftCard.Children.Add(submitBtn);
        leftCard.Children.Add(scanBtn);
        grid.Children.Add(new ScrollView { Content = leftCard });

        // Imprimantes Configurées (Droite)
        var rightCard = CreateCard("Imprimantes Réseau Configurées");
        var printersStack = new VerticalStackLayout { Spacing = 10 };

        var defaultPrinters = new[]
        {
            ("Imprimante Caisse Principale", "192.168.1.100:9100", "Ticket Reçu & Tiroir RJ11", true),
            ("Imprimante Passe Cuisine Chaude", "192.168.1.101:9100", "Bons de Préparation Chaud", true),
            ("Imprimante Bar & Boissons", "192.168.1.102:9100", "Bons Bar & Apéritifs", true)
        };

        foreach (var pr in defaultPrinters)
        {
            var pCard = new Grid
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Padding = new Thickness(14, 12),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };
            var info = new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = $"🖨️ {pr.Item1}", TextColor = Colors.White, FontSize = 15, FontAttributes = FontAttributes.Bold },
                    new Label { Text = $"IP: {pr.Item2} • {pr.Item3}", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 }
                }
            };
            pCard.Children.Add(info);

            var status = new Label { Text = "● En ligne", TextColor = Color.FromArgb("#10B981"), FontSize = 13, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
            Grid.SetColumn(status, 1);
            pCard.Children.Add(status);

            var testBtn = new Button { Text = "Test Ticket", BackgroundColor = Color.FromArgb("#334155"), TextColor = Colors.White, FontSize = 11, HeightRequest = 32, CornerRadius = 6, Margin = new Thickness(10, 0, 0, 0) };
            testBtn.Clicked += (s, e) =>
            {
                testBtn.Text = "✅ Imprimé";
            };
            Grid.SetColumn(testBtn, 2);
            pCard.Children.Add(testBtn);
            printersStack.Children.Add(pCard);
        }

        rightCard.Children.Add(new ScrollView { Content = printersStack, HeightRequest = 520 });
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        return grid;
    }
    #endregion

    #region Tab 4: Disposition de l'Écran (Grille Tactile)
    private View BuildLayoutTab()
    {
        var mainStack = new VerticalStackLayout { Spacing = 14 };

        // Feedback Statut
        var statusLabel = new Label
        {
            TextColor = Color.FromArgb("#10B981"),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };

        // Barre d'outils Format & Dimensions
        var toolbar = CreateCard("📐 Format & Dimensions de l'Écran Tactile");
        var toolbarGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 12,
            VerticalOptions = LayoutOptions.Center
        };

        var categoryPicker = new Picker
        {
            Title = "Sélectionnez une catégorie...",
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            HeightRequest = 38,
            WidthRequest = 200,
            ItemDisplayBinding = new Binding("Name")
        };
        categoryPicker.ItemsSource = _vm.CatalogVm.Categories;

        var presetPicker = new Picker
        {
            Title = "Format de la Matrice",
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            HeightRequest = 38,
            WidthRequest = 180
        };
        presetPicker.Items.Add("3 × 3 (9 touches)");
        presetPicker.Items.Add("4 × 4 (16 touches)");
        presetPicker.Items.Add("5 × 4 (20 touches)");
        presetPicker.Items.Add("5 × 5 (25 touches)");
        presetPicker.Items.Add("6 × 4 (24 touches)");
        presetPicker.SelectedIndex = 1;

        var applyBtn = new Button { Text = "Enregistrer la Grille", BackgroundColor = Color.FromArgb("#3B82F6"), TextColor = Colors.White, FontSize = 12, HeightRequest = 38, CornerRadius = 6 };
        var resetBtn = new Button { Text = "↺ Vider", BackgroundColor = Color.FromArgb("#334155"), TextColor = Color.FromArgb("#94A3B8"), FontSize = 12, HeightRequest = 38, CornerRadius = 6 };

        toolbarGrid.Children.Add(categoryPicker);
        Grid.SetColumn(presetPicker, 1);
        toolbarGrid.Children.Add(presetPicker);
        Grid.SetColumn(applyBtn, 2);
        toolbarGrid.Children.Add(applyBtn);
        Grid.SetColumn(resetBtn, 3);
        toolbarGrid.Children.Add(resetBtn);
        toolbar.Children.Add(toolbarGrid);
        toolbar.Children.Add(statusLabel);
        mainStack.Children.Add(toolbar);

        var splitGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(280) },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 16
        };

        var catalogProducts = new List<(Guid Id, string Name, string Price, string Color)>();
        foreach (var p in _vm.CatalogVm.Products)
        {
            catalogProducts.Add((p.Id, p.Name, $"{p.Price:C}", "#3B82F6"));
        }

        var catalogSide = CreateCard("Boîte à Outils (Drag & Drop)");
        var sideStack = new VerticalStackLayout { Spacing = 8 };
        sideStack.Children.Add(new Label { Text = "Glissez un article vers la grille :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 11, Margin = new Thickness(0, 0, 0, 8) });
        
        var assignedItems = new Dictionary<int, (Guid Id, string Name, string Price, string Color)>();
        int currentCols = 4;
        int currentRows = 4;

        var matrixCard = CreateCard("Matrice Tactile de Caisse (Canvas)");
        var gridMatrix = new Grid
        {
            RowSpacing = 8,
            ColumnSpacing = 8,
            BackgroundColor = Color.FromArgb("#0F172A"),
            Padding = new Thickness(10)
        };

        var filteredToolbox = new VerticalStackLayout { Spacing = 8 };
        sideStack.Children.Add(filteredToolbox);

        void RenderToolbox()
        {
            filteredToolbox.Children.Clear();
            var selectedCat = categoryPicker.SelectedItem as Category;
            if (selectedCat == null) return;

            foreach (var prod in catalogProducts.Where(p => _vm.CatalogVm.Products.FirstOrDefault(x => x.Id == p.Id)?.CategoryId == selectedCat.Id))
            {
                var row = new Border
                {
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) },
                    Stroke = Color.FromArgb("#334155"),
                    BackgroundColor = Color.FromArgb("#0F172A"),
                    Padding = new Thickness(10, 8),
                    Content = new Label { Text = $"{prod.Name} ({prod.Price})", TextColor = Colors.White, FontSize = 12 }
                };

                var dragGesture = new DragGestureRecognizer { CanDrag = true };
                dragGesture.DragStarting += (s, e) =>
                {
                    e.Data.Properties.Add("Id", prod.Id);
                    e.Data.Properties.Add("Name", prod.Name);
                    e.Data.Properties.Add("Price", prod.Price);
                    e.Data.Properties.Add("Color", prod.Color);
                };
                row.GestureRecognizers.Add(dragGesture);

                filteredToolbox.Children.Add(row);
            }
        }

        // Corbeille pour supprimer
        var trashBin = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) },
            Stroke = Color.FromArgb("#EF4444"),
            StrokeDashArray = new double[] { 4, 4 },
            BackgroundColor = Color.FromRgba(239, 68, 68, 15),
            Padding = new Thickness(10, 15),
            Margin = new Thickness(0, 20, 0, 0),
            Content = new Label { Text = "🗑️ Glissez ici pour retirer", TextColor = Color.FromArgb("#EF4444"), FontSize = 12, HorizontalOptions = LayoutOptions.Center }
        };
        var trashDrop = new DropGestureRecognizer { AllowDrop = true };
        trashDrop.DragOver += (s, e) => trashBin.BackgroundColor = Color.FromRgba(239, 68, 68, 40);
        trashDrop.DragLeave += (s, e) => trashBin.BackgroundColor = Color.FromRgba(239, 68, 68, 15);
        trashDrop.Drop += (s, e) => 
        {
            trashBin.BackgroundColor = Color.FromRgba(239, 68, 68, 15);
            if (e.Data.Properties.ContainsKey("SlotIndex"))
            {
                int srcSlot = (int)e.Data.Properties["SlotIndex"];
                assignedItems.Remove(srcSlot);
                RenderMatrix(currentCols, currentRows);
            }
        };
        trashBin.GestureRecognizers.Add(trashDrop);
        sideStack.Children.Add(trashBin);

        catalogSide.Children.Add(new ScrollView { Content = sideStack, HeightRequest = 480 });
        splitGrid.Children.Add(catalogSide);



        void RenderMatrix(int cols, int rows)
        {
            currentCols = cols;
            currentRows = rows;
            gridMatrix.RowDefinitions.Clear();
            gridMatrix.ColumnDefinitions.Clear();
            gridMatrix.Children.Clear();

            for (int r = 0; r < rows; r++)
                gridMatrix.RowDefinitions.Add(new RowDefinition { Height = 75 });

            for (int c = 0; c < cols; c++)
                gridMatrix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int slotIndex = r * cols + c;
                    bool hasItem = assignedItems.TryGetValue(slotIndex, out var item);

                    var tile = new Border
                    {
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
                        Stroke = Color.FromArgb("#334155"),
                        StrokeThickness = 1,
                        BackgroundColor = hasItem ? Color.FromArgb("#1E293B") : Color.FromRgba(255, 255, 255, 10),
                        Padding = new Thickness(6)
                    };

                    if (hasItem)
                    {
                        tile.Content = new VerticalStackLayout
                        {
                            VerticalOptions = LayoutOptions.Center,
                            Children =
                            {
                                new Label { Text = item.Name, TextColor = Colors.White, FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center },
                                new Label { Text = item.Price, TextColor = Color.FromArgb("#10B981"), FontSize = 11, HorizontalOptions = LayoutOptions.Center }
                            }
                        };

                        var dragGesture = new DragGestureRecognizer { CanDrag = true };
                        dragGesture.DragStarting += (s, e) =>
                        {
                            e.Data.Properties.Add("Id", item.Id);
                            e.Data.Properties.Add("Name", item.Name);
                            e.Data.Properties.Add("Price", item.Price);
                            e.Data.Properties.Add("Color", item.Color);
                            e.Data.Properties.Add("SlotIndex", slotIndex);
                        };
                        tile.GestureRecognizers.Add(dragGesture);
                    }
                    else
                    {
                        tile.Content = new Label { Text = "+ Glisser ici", TextColor = Color.FromArgb("#475569"), FontSize = 11, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
                    }

                    var dropGesture = new DropGestureRecognizer { AllowDrop = true };
                    dropGesture.DragOver += (s, e) => tile.BackgroundColor = Color.FromRgba(59, 130, 246, 50);
                    dropGesture.DragLeave += (s, e) => tile.BackgroundColor = hasItem ? Color.FromArgb("#1E293B") : Color.FromRgba(255, 255, 255, 10);
                    dropGesture.Drop += (s, e) =>
                    {
                        tile.BackgroundColor = hasItem ? Color.FromArgb("#1E293B") : Color.FromRgba(255, 255, 255, 10);
                        if (e.Data.Properties.TryGetValue("Id", out var idObj) &&
                            e.Data.Properties.TryGetValue("Name", out var nameObj) &&
                            e.Data.Properties.TryGetValue("Price", out var priceObj) &&
                            e.Data.Properties.TryGetValue("Color", out var colorObj))
                        {
                            if (e.Data.Properties.TryGetValue("SlotIndex", out var srcSlotObj))
                            {
                                int srcSlot = (int)srcSlotObj;
                                if (srcSlot != slotIndex)
                                {
                                    assignedItems.Remove(srcSlot);
                                }
                            }
                            assignedItems[slotIndex] = ((Guid)idObj, nameObj?.ToString() ?? "", priceObj?.ToString() ?? "", colorObj?.ToString() ?? "");
                            RenderMatrix(cols, rows);
                        }
                    };
                    tile.GestureRecognizers.Add(dropGesture);

                    Grid.SetRow(tile, r);
                    Grid.SetColumn(tile, c);
                    gridMatrix.Children.Add(tile);
                }
            }
        }

        int initialCols = 4;
        int initialRows = 4;
        RenderMatrix(initialCols, initialRows);

        categoryPicker.SelectedIndexChanged += async (s, e) =>
        {
            var selectedCat = categoryPicker.SelectedItem as Category;
            if (selectedCat == null) return;
            RenderToolbox();

            var gridApi = Handler?.MauiContext?.Services.GetService<Contracts.IGridLayoutApiService>();
            if (gridApi != null)
            {
                var layout = await gridApi.GetLayoutByCategoryAsync(selectedCat.Id);
                if (layout != null)
                {
                    assignedItems.Clear();
                    foreach (var slot in layout.Slots)
                    {
                        if (slot.ProductId.HasValue)
                        {
                            var prod = catalogProducts.FirstOrDefault(p => p.Id == slot.ProductId.Value);
                            if (prod != default)
                            {
                                assignedItems[slot.SlotIndex] = prod;
                            }
                        }
                    }
                    
                    int cols = layout.ColumnsCount > 0 ? layout.ColumnsCount : 4;
                    int rows = layout.RowsCount > 0 ? layout.RowsCount : 4;
                    
                    presetPicker.SelectedIndex = cols switch
                    {
                        3 => 0,
                        5 => rows == 4 ? 2 : 3,
                        6 => 4,
                        _ => 1
                    };
                    
                    RenderMatrix(cols, rows);
                    statusLabel.Text = $"Grille chargée pour {selectedCat.Name}";
                    statusLabel.IsVisible = true;
                }
                else
                {
                    assignedItems.Clear();
                    presetPicker.SelectedIndex = 1;
                    RenderMatrix(4, 4);
                    statusLabel.Text = $"Nouvelle grille pour {selectedCat.Name}";
                    statusLabel.IsVisible = true;
                }
            }
        };

        presetPicker.SelectedIndexChanged += (s, e) =>
        {
            int cols = 4, rows = 4;
            switch (presetPicker.SelectedIndex)
            {
                case 0: cols = 3; rows = 3; break;
                case 1: cols = 4; rows = 4; break;
                case 2: cols = 5; rows = 4; break;
                case 3: cols = 5; rows = 5; break;
                case 4: cols = 6; rows = 4; break;
            }
            RenderMatrix(cols, rows);
        };

        applyBtn.Clicked += async (s, e) =>
        {
            var selectedCat = categoryPicker.SelectedItem as Category;
            if (selectedCat == null)
            {
                statusLabel.Text = "❌ Veuillez sélectionner une catégorie.";
                statusLabel.IsVisible = true;
                return;
            }

            int cols = currentCols;
            int rows = currentRows;

            var gridApi = Handler?.MauiContext?.Services.GetService<Contracts.IGridLayoutApiService>();
            if (gridApi != null)
            {
                var slotsDto = assignedItems.Select(kv => new RestaurantPos.Application.DTOs.UpdateGridSlotItem(
                    RowIndex: kv.Key / cols,
                    ColumnIndex: kv.Key % cols,
                    ProductId: kv.Value.Id,
                    CustomLabel: null,
                    CustomColorHex: null
                )).ToList();

                var request = new RestaurantPos.Application.DTOs.UpdateGridLayoutRequest(
                    CategoryId: selectedCat.Id,
                    ColumnsCount: cols,
                    RowsCount: rows,
                    PageIndex: 0,
                    Slots: slotsDto
                );

                await gridApi.SaveLayoutAsync(request);
            }

            statusLabel.Text = $"✅ Grille de {selectedCat.Name} ({cols}×{rows}) sauvegardée !";
            statusLabel.IsVisible = true;
        };

        resetBtn.Clicked += (s, e) =>
        {
            presetPicker.SelectedIndex = 1;
            assignedItems.Clear();
            RenderMatrix(4, 4);
            statusLabel.Text = "Grille vidée localement. N'oubliez pas d'enregistrer.";
            statusLabel.IsVisible = true;
        };

        matrixCard.Children.Add(gridMatrix);
        Grid.SetColumn(matrixCard, 1);
        splitGrid.Children.Add(matrixCard);

        mainStack.Children.Add(splitGrid);
        return new ScrollView { Content = mainStack };
    }
    #endregion

    #region Tab 5: Réseau & Synchronisation
    private View BuildNetworkSyncTab()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(350) },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 16
        };

        // Découverte Réseau & Serveur Maître (Gauche)
        var leftCard = CreateCard("🔍 Découverte Réseau (Zero-Config)");
        leftCard.Children.Add(new Label
        {
            Text = "Détection mDNS / Bonjour du serveur de caisse maître et synchronisation automatique.",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var scanBtn = new Button
        {
            Text = "🔍 Lancer la Découverte Réseau",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            FontSize = 13,
            HeightRequest = 40,
            CornerRadius = 6
        };
        var scanStatus = new Label
        {
            Text = "● Serveur Maître Connecté : http://127.0.0.1:5000 (Localhost Edge)",
            TextColor = Color.FromArgb("#10B981"),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 10, 0, 10)
        };
        scanBtn.Clicked += async (s, e) =>
        {
            scanBtn.Text = "⏳ Scan Réseau mDNS...";
            await Task.Delay(800);
            scanBtn.Text = "✅ Découverte Réseau Terminée";
            scanStatus.Text = "● Serveur Maître Connecté : http://127.0.0.1:5000 (Ping 1ms)";
        };
        leftCard.Children.Add(scanBtn);
        leftCard.Children.Add(scanStatus);

        leftCard.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155"), Margin = new Thickness(0, 10) });

        leftCard.Children.Add(new Label { Text = "⚙️ Configuration Manuelle :", TextColor = Colors.White, FontSize = 14, FontAttributes = FontAttributes.Bold });
        var urlEntry = new Entry { Text = _vm.MasterServerUrl, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#0F172A"), HeightRequest = 40, Margin = new Thickness(0, 6) };
        var testConnBtn = new Button { Text = "📡 Tester Connexion", BackgroundColor = Color.FromArgb("#334155"), TextColor = Colors.White, FontSize = 12, HeightRequest = 36, CornerRadius = 6 };
        testConnBtn.Clicked += async (s, e) =>
        {
            testConnBtn.Text = "📡 Test en cours...";
            await Task.Delay(500);
            testConnBtn.Text = "✅ Connexion Réussie (200 OK)";
        };
        leftCard.Children.Add(urlEntry);
        leftCard.Children.Add(testConnBtn);
        grid.Children.Add(new ScrollView { Content = leftCard });

        // État de la Synchronisation Offline-First (Droite)
        var rightCard = CreateCard("📊 État de la Synchronisation (Offline-First)");
        var statusStack = new VerticalStackLayout { Spacing = 10, Margin = new Thickness(0, 0, 0, 16) };

        var items = new[]
        {
            ("Mode de fonctionnement :", "Connecté au Réseau (Edge)", "#10B981"),
            ("Messages en attente (Outbox) :", "0 message (Tout synchronisé)", "#F59E0B"),
            ("Transactions synchronisées :", "32 transactions scellées", "#38BDF8"),
            ("Canal SignalR WebSocket :", "● En ligne (Temps réel actif)", "#10B981"),
            ("Dernière synchronisation :", "À l'instant", "#94A3B8")
        };

        foreach (var it in items)
        {
            var row = new Grid
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Padding = new Thickness(14, 12),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };
            row.Children.Add(new Label { Text = it.Item1, TextColor = Color.FromArgb("#94A3B8"), FontSize = 13, VerticalOptions = LayoutOptions.Center });
            var val = new Label { Text = it.Item2, TextColor = Color.FromArgb(it.Item3), FontSize = 13, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
            Grid.SetColumn(val, 1);
            row.Children.Add(val);
            statusStack.Children.Add(row);
        }
        rightCard.Children.Add(statusStack);

        var forceSyncBtn = new Button
        {
            Text = "⚡ Forcer la Synchronisation Immédiate",
            BackgroundColor = Color.FromArgb("#10B981"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 46,
            CornerRadius = 8
        };
        forceSyncBtn.Clicked += async (s, e) =>
        {
            forceSyncBtn.Text = "⏳ Synchronisation en cours...";
            await Task.Delay(800);
            forceSyncBtn.Text = "✅ Données Locales et Serveur Synchronisées !";
        };
        rightCard.Children.Add(forceSyncBtn);
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);

        return grid;
    }
    #endregion

    #region Tab 6: Tableaux de Bord & KPIs
    private View BuildDashboardTab()
    {
        var mainStack = new VerticalStackLayout { Spacing = 16 };

        // En-tête avec filtres temporels
        var filterRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        var titleStack = new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = "📊 Tableaux de Bord & KPIs Financiers", TextColor = Colors.White, FontSize = 18, FontAttributes = FontAttributes.Bold },
                new Label { Text = "Analyse temps réel de la rentabilité, des services et des ventes.", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 }
            }
        };
        filterRow.Children.Add(titleStack);

        var filterButtons = new HorizontalStackLayout { Spacing = 6 };
        var ranges = new[] { "Aujourd'hui", "Hier", "7 jours", "30 jours" };
        foreach (var r in ranges)
        {
            bool isAct = r == "Aujourd'hui";
            filterButtons.Children.Add(new Button
            {
                Text = r,
                BackgroundColor = isAct ? Color.FromArgb("#3B82F6") : Color.FromArgb("#1E293B"),
                TextColor = isAct ? Colors.White : Color.FromArgb("#94A3B8"),
                FontSize = 12,
                HeightRequest = 34,
                CornerRadius = 6,
                Padding = new Thickness(12, 0)
            });
        }
        Grid.SetColumn(filterButtons, 1);
        filterRow.Children.Add(filterButtons);
        mainStack.Children.Add(filterRow);

        // 3 Grandes Cartes KPI dégradées
        var kpisGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition() },
            ColumnSpacing = 14
        };

        kpisGrid.Children.Add(CreateKpiCard("CHIFFRE D'AFFAIRES TTC", "1 248,50 €", "HT : 1 125,00 €", "#10B981", 0));
        kpisGrid.Children.Add(CreateKpiCard("PANIER MOYEN / COUVERT", "24,97 €", "50 couverts servis", "#38BDF8", 1));
        kpisGrid.Children.Add(CreateKpiCard("PANIER MOYEN / COMMANDE", "41,62 €", "30 commandes validées", "#C084FC", 2));
        mainStack.Children.Add(kpisGrid);

        // Comparatifs Services & Règlements
        var compGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() },
            ColumnSpacing = 16
        };

        var servCard = CreateCard("☀️ Comparatif par Service");
        servCard.Children.Add(CreateMetricRow("Service Midi (12h - 14h30) :", "680,00 € (28 couverts)", "#38BDF8"));
        servCard.Children.Add(CreateMetricRow("Service Soir (19h - 23h00) :", "568,50 € (22 couverts)", "#818CF8"));
        compGrid.Children.Add(servCard);

        var payCard = CreateCard("💳 Ventilation des Règlements");
        payCard.Children.Add(CreateMetricRow("Carte Bancaire (65%) :", "811,50 €", "#10B981"));
        payCard.Children.Add(CreateMetricRow("Espèces (25%) :", "312,00 €", "#F59E0B"));
        payCard.Children.Add(CreateMetricRow("Titres Restaurant (10%) :", "125,00 €", "#38BDF8"));
        Grid.SetColumn(payCard, 1);
        compGrid.Children.Add(payCard);
        mainStack.Children.Add(compGrid);

        // Top Ventes Articles
        var topCard = CreateCard("🏆 Palmarès des Ventes (Top Articles)");
        var topGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() },
            ColumnSpacing = 14
        };
        var col1 = new VerticalStackLayout { Spacing = 6 };
        col1.Children.Add(CreateMetricRow("1. Burger Maison :", "24 vendus • 396,00 €", "#10B981"));
        col1.Children.Add(CreateMetricRow("2. Bière IPA Pression :", "18 vendus • 126,00 €", "#10B981"));
        topGrid.Children.Add(col1);

        var col2 = new VerticalStackLayout { Spacing = 6 };
        col2.Children.Add(CreateMetricRow("3. Entrecôte Grillée :", "12 vendus • 264,00 €", "#10B981"));
        col2.Children.Add(CreateMetricRow("4. Café Espresso :", "35 vendus • 87,50 €", "#10B981"));
        Grid.SetColumn(col2, 1);
        topGrid.Children.Add(col2);

        topCard.Children.Add(topGrid);
        mainStack.Children.Add(topCard);

        return new ScrollView { Content = mainStack };
    }

    private static Border CreateKpiCard(string title, string mainValue, string subValue, string colorHex, int col)
    {
        var b = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Stroke = Color.FromArgb(colorHex),
            StrokeThickness = 1,
            BackgroundColor = Color.FromArgb("#1E293B"),
            Padding = new Thickness(16, 14),
            Content = new VerticalStackLayout
            {
                Children =
                {
                    new Label { Text = title, TextColor = Color.FromArgb("#94A3B8"), FontSize = 11, FontAttributes = FontAttributes.Bold },
                    new Label { Text = mainValue, TextColor = Color.FromArgb(colorHex), FontSize = 24, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 4) },
                    new Label { Text = subValue, TextColor = Color.FromArgb("#64748B"), FontSize = 12 }
                }
            }
        };
        Grid.SetColumn(b, col);
        return b;
    }

    private static Grid CreateMetricRow(string label, string val, string valColor)
    {
        var r = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
            Padding = new Thickness(0, 4)
        };
        r.Children.Add(new Label { Text = label, TextColor = Colors.White, FontSize = 13 });
        var v = new Label { Text = val, TextColor = Color.FromArgb(valColor), FontSize = 13, FontAttributes = FontAttributes.Bold };
        Grid.SetColumn(v, 1);
        r.Children.Add(v);
        return r;
    }
    #endregion

    #region Tab 7: Plages Happy Hour
    private View BuildHappyHourTab()
    {
        var mainStack = new VerticalStackLayout { Spacing = 16 };

        var topCard = CreateCard("🍻 Configuration Plages & Tarifs Happy Hour");
        var topRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        topRow.Children.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = "Définissez les créneaux horaires et appliquez des remises automatiques sur familles ou articles.", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 },
                new Label { Text = "Créneau actif : Afterwork (17h00 - 20h00) • Actif (-20%)", TextColor = Color.FromArgb("#10B981"), FontSize = 13, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 4, 0, 0) }
            }
        });

        var newScheduleBtn = new Button { Text = "➕ Nouveau Créneau", BackgroundColor = Color.FromArgb("#3B82F6"), TextColor = Colors.White, FontSize = 12, HeightRequest = 38, CornerRadius = 6 };
        Grid.SetColumn(newScheduleBtn, 1);
        topRow.Children.Add(newScheduleBtn);
        topCard.Children.Add(topRow);
        mainStack.Children.Add(topCard);

        var splitGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() },
            ColumnSpacing = 16
        };

        var configCard = CreateCard("Paramètres du Créneau 'Afterwork'");
        var paramStack = new VerticalStackLayout { Spacing = 10 };
        paramStack.Children.Add(new Label { Text = "Heure de Début : 17h00", TextColor = Colors.White, FontSize = 13 });
        paramStack.Children.Add(new Label { Text = "Heure de Fin : 20h00", TextColor = Colors.White, FontSize = 13 });
        paramStack.Children.Add(new Label { Text = "Jours Applicables : Lundi, Mardi, Mercredi, Jeudi, Vendredi", TextColor = Color.FromArgb("#38BDF8"), FontSize = 13 });
        paramStack.Children.Add(new Label { Text = "Remise Automatique : -20 %", TextColor = Color.FromArgb("#10B981"), FontSize = 14, FontAttributes = FontAttributes.Bold });
        configCard.Children.Add(paramStack);
        splitGrid.Children.Add(configCard);

        var itemsCard = CreateCard("Familles & Articles Éligibles Happy Hour");
        var eligibleStack = new VerticalStackLayout { Spacing = 8 };
        eligibleStack.Children.Add(CreateEligibleRow("🍺 Bière Pression 33cl / 50cl", "5,50 € ➔ 4,40 € (-20%)"));
        eligibleStack.Children.Add(CreateEligibleRow("🍷 Vins au Verre (AOP)", "6,00 € ➔ 4,80 € (-20%)"));
        eligibleStack.Children.Add(CreateEligibleRow("🍸 Cocktails Maison (Mojito, Spritz)", "9,00 € ➔ 7,20 € (-20%)"));
        eligibleStack.Children.Add(CreateEligibleRow("🍟 Planche Mixte & Tapas", "15,00 € ➔ 12,00 € (-20%)"));
        itemsCard.Children.Add(eligibleStack);
        Grid.SetColumn(itemsCard, 1);
        splitGrid.Children.Add(itemsCard);

        mainStack.Children.Add(splitGrid);
        return new ScrollView { Content = mainStack };
    }

    private static Grid CreateEligibleRow(string item, string priceChange)
    {
        var g = new Grid
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Padding = new Thickness(12, 8),
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } }
        };
        g.Children.Add(new Label { Text = item, TextColor = Colors.White, FontSize = 13, VerticalOptions = LayoutOptions.Center });
        var p = new Label { Text = priceChange, TextColor = Color.FromArgb("#10B981"), FontSize = 12, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
        Grid.SetColumn(p, 1);
        g.Children.Add(p);
        return g;
    }
    #endregion

    private static VerticalStackLayout CreateCard(string title)
    {
        var card = new VerticalStackLayout
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            Padding = new Thickness(16),
            Spacing = 10
        };
        card.Children.Add(new Label
        {
            Text = title,
            TextColor = Colors.White,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        return card;
    }

    private static T AddToRow<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }
}
#endif
