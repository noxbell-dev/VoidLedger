using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VoidLedger.Models;
using VoidLedger.Services;

namespace VoidLedger
{
    public partial class MainWindow : Window
    {
        private readonly WfmService _svc = new();
        private readonly RelicService _relicSvc = new();
        private Dictionary<RelicTier, List<RelicData>> _data = new();
        private RelicTier _activeTier = RelicTier.Lith;
        private string _activeSort = "name";
        private string _searchFilter = string.Empty;
        private bool _filterOwned = false;
        private bool _filterUnvaulted = false;
        private readonly HashSet<string> _selectedRelicUrls = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ownedRelicUrls   = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxSelectedRelics = 4;
        private bool _squadActive = false;
        private CancellationTokenSource? _cts;
        private const double PriceRequestsPerSecond = 2.0;
        private DateTime _pulledAt;

        private static readonly Dictionary<RelicTier, Color> TierColors = new()
        {
            [RelicTier.Lith] = (Color)ColorConverter.ConvertFromString("#B87333"),
            [RelicTier.Meso] = (Color)ColorConverter.ConvertFromString("#8E9EAB"),
            [RelicTier.Neo]  = (Color)ColorConverter.ConvertFromString("#D4AF37"),
            [RelicTier.Axi]  = (Color)ColorConverter.ConvertFromString("#A78BFA"),
        };

        private static readonly Dictionary<string, Color> RarityColors = new()
        {
            ["common"]    = (Color)ColorConverter.ConvertFromString("#7EB3D4"),
            ["uncommon"]  = (Color)ColorConverter.ConvertFromString("#89C89E"),
            ["rare"]      = (Color)ColorConverter.ConvertFromString("#E8C553"),
            ["legendary"] = (Color)ColorConverter.ConvertFromString("#CC5577"),
        };

        public MainWindow()
        {
            InitializeComponent();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            VersionLabel.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
            Loaded += MainWindow_Loaded;
        }

        //  FULLSCREEN FIX
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                var wa = SystemParameters.WorkArea;
                MaxWidth  = wa.Width;
                MaxHeight = wa.Height;
            }
            else
            {
                MaxWidth  = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;
            }
        }

        //  AUTO-LOAD CACHE ON STARTUP
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var cached = await CacheService.LoadAsync();
            if (cached is null) return;

            _data = cached.Value.Data;
            _ownedRelicUrls.Clear();
            foreach (var url in cached.Value.OwnedRelics)
                _ownedRelicUrls.Add(url);

            _pulledAt = cached.Value.PulledAt;
            StatusLabel.Text = $"Loaded from cache  ·  pulled {FormatPulledAt(_pulledAt)}";
            LoadButton.Content = "REFRESH";
            ShowDataUI();
            RenderList();
        }

        //  LOAD / REFRESH
        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            // Confirm dialog when refreshing (button already shows "REFRESH" after first load)
            if (LoadButton.Content?.ToString() == "REFRESH")
            {
                var dlg = new ConfirmDialog(
                    "Refresh Relic Data",
                    "Are you sure you want to pull relic data again?",
                    this);
                if (dlg.ShowDialog() != true)
                    return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            LoadButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            StatusLabel.Text = "";
            RelicListPanel.Children.Clear();

            try
            {
                SetProgress(2, "Fetching item list…");
                var allItems = await _svc.GetAllItemsAsync();
                var slugLookup = RelicService.BuildSlugLookup(allItems);

                var vaultedMap = allItems
                    .Where(i => !string.IsNullOrEmpty(i.Slug))
                    .ToDictionary(i => i.Slug!, i => i.Vaulted ?? false, StringComparer.OrdinalIgnoreCase);

                // Fetch relic drop data from WDD API
                SetProgress(10, "Fetching relic drop data…");
                var relics = await _relicSvc.LoadFromApiAsync(slugLookup, vaultedMap);

                if (relics.Count == 0)
                {
                    ProgressPanel.Visibility = Visibility.Collapsed;
                    StatusLabel.Text = "No relic data returned – check your connection and try again.";
                    LoadButton.IsEnabled = true;
                    return;
                }

                WfmService.ApplyVaultedDrops(relics);

                var uniqueSlugs = relics
                    .SelectMany(r => r.Drops.Select(d => d.UrlName))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .ToList();

                var initialEta = TimeSpan.FromSeconds(Math.Ceiling(uniqueSlugs.Count / PriceRequestsPerSecond));
                SetProgress(15, $"Pricing {uniqueSlugs.Count} items… ETA ~{initialEta:mm\\:ss}");

                var priceMap = new System.Collections.Concurrent.ConcurrentDictionary<string, double?>();
                int priceDone = 0;
                await _svc.BatchedAsync(uniqueSlugs, async slug =>
                {
                    var avg = await _svc.GetAvgPriceAsync(slug);
                    priceMap[slug] = avg;
                    int d = Interlocked.Increment(ref priceDone);
                    Dispatcher.Invoke(() =>
                    {
                        var remaining = Math.Max(0, uniqueSlugs.Count - d);
                        var eta = TimeSpan.FromSeconds(Math.Ceiling(remaining / PriceRequestsPerSecond));
                        var etaLabel = remaining > 0 ? $" · ETA ~{eta:mm\\:ss}" : string.Empty;
                        double pct = 15 + (d / (double)uniqueSlugs.Count) * 82;
                        SetProgress(pct, $"Pricing items… {d}/{uniqueSlugs.Count}{etaLabel}");
                    });
                }, ct: ct);

                foreach (var relic in relics)
                {
                    foreach (var drop in relic.Drops)
                        drop.Price = priceMap.TryGetValue(drop.UrlName, out var p) ? p : null;

                    var priced = relic.Drops.Where(d => d.Price.HasValue).ToList();
                    relic.Avg = priced.Any() ? priced.Sum(d => d.Price!.Value) : null;
                }

                _data = relics
                    .GroupBy(r => r.Tier)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Preserve ownership tags across re-pulls
                foreach (var relic in relics)
                    relic.IsOwned = _ownedRelicUrls.Contains(relic.UrlName);

                var pulledAt = DateTime.UtcNow;
                _pulledAt = pulledAt;

                SetProgress(100, "Saving cache…");
                await CacheService.SaveAsync(_data, _ownedRelicUrls, pulledAt);

                await Task.Delay(400);

                ProgressPanel.Visibility = Visibility.Collapsed;
                LoadButton.Content   = "REFRESH";
                LoadButton.IsEnabled = true;
                StatusLabel.Text = $"Updated  ·  pulled {FormatPulledAt(_pulledAt)}";

                ShowDataUI();
                RenderList();
            }
            catch (OperationCanceledException)
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                LoadButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                StatusLabel.Text = $"Error: {ex.Message}";
                LoadButton.IsEnabled = true;
            }
        }

        private void ShowDataUI()
        {
            TierTabBar.Visibility   = Visibility.Visible;
            SortBar.Visibility      = Visibility.Visible;
            SearchBox.Visibility    = Visibility.Visible;
            SearchLabel.Visibility  = Visibility.Visible;
            ShortcutHint.Visibility = Visibility.Visible;
        }

        private void SetProgress(double pct, string label)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value  = pct;
                ProgressLabel.Text = label;
            });
        }

        // Show pulled_at as local date+time
        private static string FormatPulledAt(DateTime utc)
        {
            var local = utc.ToLocalTime();
            return local.ToString("dd.MM.yyyy 'at' HH:mm");
        }

        //  OWNED TOGGLE
        private async Task ToggleOwnedRelic(RelicData relic)
        {
            if (_ownedRelicUrls.Contains(relic.UrlName))
                _ownedRelicUrls.Remove(relic.UrlName);
            else
                _ownedRelicUrls.Add(relic.UrlName);

            if (_data.Any())
            {
                // Preserve the original pulled_at timestamp when saving ownership changes
                await CacheService.SaveAsync(_data, _ownedRelicUrls, _pulledAt != default ? _pulledAt : null);

                StatusLabel.Text = _pulledAt != default
                    ? $"Loaded from cache  ·  pulled {FormatPulledAt(_pulledAt)}"
                    : string.Empty;
            }

            RenderList();
        }

        //  RENDER
        private void RenderList()
        {
            RelicListPanel.Children.Clear();

            if (!_data.TryGetValue(_activeTier, out var relics) || relics == null)
            {
                CountLabel.Text = "0 relics";
                UpdateSelectionBar();
                return;
            }

            var searchTerm = _searchFilter.ToLower();
            var searchFiltered = string.IsNullOrWhiteSpace(_searchFilter)
                ? relics.ToList()
                : relics.Where(r => r.Name.ToLower().Contains(searchTerm)).ToList();

            // Apply secondary filters (Owned / Unvaulted)  relics only
            var afterFilters = searchFiltered.AsEnumerable();
            if (_filterOwned)
                afterFilters = afterFilters.Where(r => _ownedRelicUrls.Contains(r.UrlName));
            if (_filterUnvaulted)
                afterFilters = afterFilters.Where(r => !r.IsVaulted);

            var filteredRelics = _squadActive
                ? afterFilters.Where(r => _selectedRelicUrls.Contains(r.UrlName)).ToList()
                : afterFilters.ToList();

            // Primary sort
            IEnumerable<RelicData> sorted = _activeSort switch
            {
                "total-desc" or "avg-desc" => filteredRelics.OrderByDescending(r => r.Avg ?? -1),
                "total-asc"  or "avg-asc"  => filteredRelics.OrderBy(r => r.Avg ?? double.MaxValue),
                _                          => filteredRelics.OrderBy(r => r.Name),
            };

            var sortedList = sorted.ToList();

            CountLabel.Text = $"{sortedList.Count} relics";

            foreach (var relic in sortedList)
                RelicListPanel.Children.Add(MakeRelicCard(relic));

            UpdateSelectionBar();
        }

        private void ToggleRelicSelection(RelicData relic)
        {
            if (_selectedRelicUrls.Contains(relic.UrlName))
                _selectedRelicUrls.Remove(relic.UrlName);
            else if (_selectedRelicUrls.Count < MaxSelectedRelics)
                _selectedRelicUrls.Add(relic.UrlName);

            // Auto-activate squad view when all 4 slots are filled;
            // exit squad mode if we drop back below 4 (e.g. user Ctrl+clicked to deselect)
            if (_selectedRelicUrls.Count == MaxSelectedRelics)
            {
                _squadActive = true;
                if (SquadButton != null) SquadButton.Content = "DISBAND";
            }
            else if (_squadActive && _selectedRelicUrls.Count < MaxSelectedRelics)
            {
                _squadActive = false;
                if (SquadButton != null) SquadButton.Content = "SQUAD";
            }

            UpdateSelectionBar();
            RenderList();
        }

        private void ClearSelectedRelics()
        {
            _selectedRelicUrls.Clear();
            _squadActive = false;
            _searchFilter = string.Empty;
            if (SearchBox != null)
                SearchBox.Clear();
            if (SquadButton != null)
            {
                SquadButton.Content    = "SQUAD";
                SquadButton.Visibility = Visibility.Collapsed;
            }
            UpdateSelectionBar();
        }

        private void UpdateSelectionBar()
        {
            if (SelectionPanel == null)
                return;

            SelectionPanel.Children.Clear();

            if (SearchBox != null)
            {
                bool locked = _squadActive || _selectedRelicUrls.Count == MaxSelectedRelics;
                if (locked)
                {
                    SearchBox.Clear();
                    _searchFilter = string.Empty;
                }
                SearchBox.IsEnabled = !locked;
            }

            // Show SQUAD button whenever relics are selected (and not yet in squad mode)
            if (SquadButton != null)
            {
                if (_selectedRelicUrls.Count > 0)
                {
                    SquadButton.Visibility = Visibility.Visible;
                    // Keep label in sync  if squad just became active it was set in the handler
                    if (!_squadActive)
                        SquadButton.Content = "SQUAD";
                }
                else
                {
                    SquadButton.Visibility = Visibility.Collapsed;
                    SquadButton.Content    = "SQUAD";
                }
            }

            if (_selectedRelicUrls.Count == 0)
                return;

            if (!_data.TryGetValue(_activeTier, out var relics) || relics == null)
                return;

            foreach (var url in _selectedRelicUrls)
            {
                var relic = relics.FirstOrDefault(r => string.Equals(r.UrlName, url, StringComparison.OrdinalIgnoreCase));
                var label = relic?.Name ?? url;

                var chip = new Border
                {
                    Background      = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#12151C")),
                    BorderBrush     = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2433")),
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(4),
                    Padding         = new Thickness(10, 4, 8, 4),
                    Margin          = new Thickness(0, 0, 8, 0),
                    Cursor          = Cursors.Hand,
                    Tag             = url,
                };

                var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
                chipContent.Children.Add(new TextBlock
                {
                    Text              = label,
                    Foreground        = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8CFE0")),
                    FontSize          = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                chipContent.Children.Add(new TextBlock
                {
                    Text              = " ✕",
                    Foreground        = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A6478")),
                    FontSize          = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                chip.Child = chipContent;
                chip.MouseLeftButtonUp += (s, e) =>
                {
                    _selectedRelicUrls.Remove(url);
                    // Exit squad mode whenever we drop below the max (including down to 0)
                    if (_squadActive && _selectedRelicUrls.Count < MaxSelectedRelics)
                    {
                        _squadActive = false;
                        if (SquadButton != null) SquadButton.Content = "SQUAD";
                    }
                    UpdateSelectionBar();
                    RenderList();
                };

                SelectionPanel.Children.Add(chip);
            }
        }

        //  RELIC CARD
        private UIElement MakeRelicCard(RelicData relic)
        {
            var tierColor = TierColors[relic.Tier];
            var tierBrush = new SolidColorBrush(tierColor);
            bool isOwned  = _ownedRelicUrls.Contains(relic.UrlName);

            var card = new Border
            {
                Background      = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#12151C")),
                BorderBrush     = _selectedRelicUrls.Contains(relic.UrlName)
                    ? tierBrush
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2433")),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 0, 6),
                Cursor          = Cursors.Hand,
            };

            var stack = new StackPanel();
            card.Child = stack;

            //  Header 
            var header = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

            // Tier dot
            var dot = new Ellipse
            {
                Width             = 8,
                Height            = 8,
                Fill              = tierBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(dot, 0);

            // Name + vaulted square inline
            var nameColor = isOwned
                ? (Color)ColorConverter.ConvertFromString("#7EC8E3")
                : (Color)ColorConverter.ConvertFromString("#EAF0FF");

            var namePanel = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(10, 0, 0, 0),
            };

            var nameBlock = new TextBlock
            {
                Text              = relic.Name,
                Foreground        = new SolidColorBrush(nameColor),
                FontFamily        = new FontFamily("Segoe UI Semibold"),
                FontSize          = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var vaultedDot = new Border
            {
                Width             = 7,
                Height            = 7,
                Background        = tierBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0),
                Visibility        = relic.IsVaulted ? Visibility.Visible : Visibility.Collapsed,
            };
            ToolTipService.SetToolTip(vaultedDot, "Vaulted");

            namePanel.Children.Add(nameBlock);
            namePanel.Children.Add(vaultedDot);
            Grid.SetColumn(namePanel, 1);

            var avgBlock = new TextBlock
            {
                Text              = relic.TotalDisplay,
                Foreground        = tierBrush,
                FontFamily        = new FontFamily("Segoe UI Semibold"),
                FontSize          = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment     = TextAlignment.Right,
                Margin            = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(avgBlock, 2);

            var chevron = new TextBlock
            {
                Text              = "▼",
                Foreground        = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A6478")),
                FontSize          = 9,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(chevron, 3);

            header.Children.Add(dot);
            header.Children.Add(namePanel);
            header.Children.Add(avgBlock);
            header.Children.Add(chevron);
            stack.Children.Add(header);

            //  Drop list 
            var dropBorder = new Border
            {
                BorderBrush     = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2433")),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Visibility      = Visibility.Collapsed,
            };

            var dropStack = new StackPanel();

            var rarityOrder = new Dictionary<string, int>
            {
                ["legendary"] = 0, ["rare"] = 1, ["uncommon"] = 2, ["common"] = 3
            };
            var sortedDrops = relic.Drops
                .OrderBy(d => rarityOrder.TryGetValue(d.Rarity?.ToLower() ?? "common", out int o) ? o : 3)
                .ToList();

            foreach (var drop in sortedDrops)
                dropStack.Children.Add(MakeDropRow(drop, tierColor));

            if (!sortedDrops.Any())
            {
                dropStack.Children.Add(new TextBlock
                {
                    Text       = "No drop data available",
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A6478")),
                    FontSize   = 12,
                    Margin     = new Thickness(14, 8, 14, 8),
                });
            }

            dropBorder.Child = dropStack;
            stack.Children.Add(dropBorder);

            //  Context menu (right-click) 
            var ctxMenu = new ContextMenu
            {
                Background      = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#12151C")),
                BorderBrush     = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2433")),
                BorderThickness = new Thickness(1),
            };

            var menuSelect = new MenuItem
            {
                Header     = _selectedRelicUrls.Contains(relic.UrlName) ? "Deselect relic" : "Select relic",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8CFE0")),
                Background = Brushes.Transparent,
            };
            menuSelect.Click += (s, e) => ToggleRelicSelection(relic);

            var menuOwned = new MenuItem
            {
                Header     = isOwned ? "Unmark as owned" : "Mark as owned",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7EC8E3")),
                Background = Brushes.Transparent,
            };
            menuOwned.Click += async (s, e) => await ToggleOwnedRelic(relic);

            ctxMenu.Items.Add(menuSelect);
            ctxMenu.Items.Add(new Separator
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2433"))
            });
            ctxMenu.Items.Add(menuOwned);
            card.ContextMenu = ctxMenu;

            //  Interaction 
            bool expanded = false;

            card.MouseLeftButtonUp += async (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    await ToggleOwnedRelic(relic);
                    return;
                }
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    ToggleRelicSelection(relic);
                    return;
                }

                expanded = !expanded;
                dropBorder.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = expanded ? "▲" : "▼";
                card.BorderBrush = expanded || _selectedRelicUrls.Contains(relic.UrlName)
                    ? tierBrush
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2433"));
            };

            card.MouseEnter += (s, e) =>
            {
                if (!expanded && !_selectedRelicUrls.Contains(relic.UrlName))
                    card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E364A"));
            };
            card.MouseLeave += (s, e) =>
            {
                if (!expanded)
                    card.BorderBrush = _selectedRelicUrls.Contains(relic.UrlName)
                        ? tierBrush
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2433"));
            };

            return card;
        }

        //  DROP ROW
        private UIElement MakeDropRow(DropItem drop, Color tierColor)
        {
            var rarity      = drop.Rarity?.ToLower() ?? "common";
            var rarityColor = RarityColors.TryGetValue(rarity, out var c) ? c
                : (Color)ColorConverter.ConvertFromString("#7EB3D4");
            var rarityLabel = rarity.Length > 0
                ? char.ToUpper(rarity[0]) + rarity.Substring(1)
                : "Common";

            var row = new Border
            {
                BorderBrush     = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161A23")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding         = new Thickness(14, 7, 14, 7),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            // Name + vaulted square inline
            var namePanel = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var nameBlock = new TextBlock
            {
                Text              = drop.Name,
                Foreground        = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8CFE0")),
                FontSize          = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var vaultSquare = new Border
            {
                Width             = 7,
                Height            = 7,
                Background        = new SolidColorBrush(tierColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0),
                Visibility        = drop.Vaulted ? Visibility.Visible : Visibility.Collapsed,
            };
            ToolTipService.SetToolTip(vaultSquare, "Vaulted");

            namePanel.Children.Add(nameBlock);
            namePanel.Children.Add(vaultSquare);
            Grid.SetColumn(namePanel, 0);

            var rarityBadge = new Border
            {
                Background        = new SolidColorBrush(Color.FromArgb(30, rarityColor.R, rarityColor.G, rarityColor.B)),
                Margin            = new Thickness(4, 0, 8, 0),
                Padding           = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            rarityBadge.Child = new TextBlock
            {
                Text       = rarityLabel,
                Foreground = new SolidColorBrush(rarityColor),
                FontSize   = 10,
                FontFamily = new FontFamily("Segoe UI Semibold"),
            };
            Grid.SetColumn(rarityBadge, 1);

            string priceText;
            SolidColorBrush priceBrush;
            if (drop.IsForma)
            {
                priceText  = $"{drop.Price:F0} ℙ";
                priceBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A6478"));
            }
            else if (drop.Price.HasValue)
            {
                priceText  = $"{drop.Price.Value:F1} ℙ";
                priceBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8CFE0"));
            }
            else
            {
                priceText  = "N/A";
                priceBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A6478"));
            }

            var priceBlock = new TextBlock
            {
                Text              = priceText,
                Foreground        = priceBrush,
                FontFamily        = new FontFamily("Segoe UI Semibold"),
                FontSize          = 12,
                TextAlignment     = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(priceBlock, 2);

            grid.Children.Add(namePanel);
            grid.Children.Add(rarityBadge);
            grid.Children.Add(priceBlock);
            row.Child = grid;
            return row;
        }

        //  TAB / SORT HANDLERS
        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            _activeTier = rb.Name switch
            {
                "TabLith" => RelicTier.Lith,
                "TabMeso" => RelicTier.Meso,
                "TabNeo"  => RelicTier.Neo,
                "TabAxi"  => RelicTier.Axi,
                _         => RelicTier.Lith,
            };
            ClearSelectedRelics();
            if (_data.Any()) RenderList();
        }

        private void Sort_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            _activeSort = rb.Tag?.ToString() ?? "name";
            if (_data.Any()) RenderList();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            _filterOwned     = FilterOwned?.IsChecked == true;
            _filterUnvaulted = FilterUnvaulted?.IsChecked == true;
            if (_data.Any()) RenderList();
        }

        private void SquadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_squadActive)
            {
                // DISBAND  clear everything and go back to normal list
                _squadActive = false;
                ClearSelectedRelics();
                RenderList();
            }
            else
            {
                // SQUAD  activate filtered view
                _squadActive = true;
                SquadButton.Content = "DISBAND";
                RenderList();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchFilter = (sender as TextBox)?.Text ?? string.Empty;
            if (_data.Any()) RenderList();
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                ((Button)sender).Content = "FULLSCREEN";
            }
            else
            {
                WindowState = WindowState.Maximized;
                ((Button)sender).Content = "MINIMIZE";
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e) { }
        private void Window_MouseMove(object sender, MouseEventArgs e) { }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
