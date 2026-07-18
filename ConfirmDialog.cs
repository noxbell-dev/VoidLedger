using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace VoidLedger
{
    /// Minimal themed confirm dialog matching the VoidLedger dark aesthetic.
    /// Returns true if the user clicks Confirm, false/null otherwise.
    public class ConfirmDialog : Window
    {
        public ConfirmDialog(string title, string message, Window owner)
        {
            Owner                 = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle           = WindowStyle.None;
            ResizeMode            = ResizeMode.NoResize;
            Width                 = 400;
            Height                = 170;
            Background            = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1C));
            ShowInTaskbar         = false;

            var chrome = new WindowChrome
            {
                CaptionHeight         = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness   = new Thickness(0),
                CornerRadius          = new CornerRadius(0),
            };
            WindowChrome.SetWindowChrome(this, chrome);

            var outerBorder = new Border
            {
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x33)),
                BorderThickness = new Thickness(1),
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Message area
            var msgPanel = new StackPanel { Margin = new Thickness(28, 24, 28, 16) };

            var titleBlock = new TextBlock
            {
                Text       = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF0, 0xFF)),
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize   = 14,
                Margin     = new Thickness(0, 0, 0, 10),
            };

            var msgBlock = new TextBlock
            {
                Text         = message,
                Foreground   = new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xE0)),
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = 12,
                TextWrapping = TextWrapping.Wrap,
            };

            msgPanel.Children.Add(titleBlock);
            msgPanel.Children.Add(msgBlock);
            Grid.SetRow(msgPanel, 0);

            // Button bar
            var btnBar = new Border
            {
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x33)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding         = new Thickness(20, 12, 20, 12),
            };

            var btnPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var cancelBtn  = MakeButton("CANCEL",    "#1E2433", "#C8CFE0", "#2E364A");
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };

            var confirmBtn = MakeButton("PULL DATA", "#A78BFA", "#0D0F14", "#9370e8");
            confirmBtn.Click += (s, e) => { DialogResult = true; Close(); };

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(confirmBtn);
            btnBar.Child = btnPanel;
            Grid.SetRow(btnBar, 1);

            root.Children.Add(msgPanel);
            root.Children.Add(btnBar);
            outerBorder.Child = root;
            Content = outerBorder;

            MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };
            KeyDown   += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        }

        private static Button MakeButton(string text, string bg, string fg, string hoverBg)
        {
            var bgBrush    = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            var fgBrush    = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
            var hoverBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hoverBg));

            // Nest a Border inside the button content so we fully control the background
            // without needing a ControlTemplate or RelativeSource binding.
            var inner = new Border
            {
                Background = bgBrush,
                Padding    = new Thickness(18, 7, 18, 7),
                Child      = new TextBlock
                {
                    Text                = text,
                    Foreground          = fgBrush,
                    FontFamily          = new FontFamily("Segoe UI Semibold"),
                    FontSize            = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            };

            var btn = new Button
            {
                Content         = inner,
                Padding         = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background      = Brushes.Transparent,
                Cursor          = Cursors.Hand,
                MinWidth        = 100,
                // Strip the default WPF button chrome so our Border shows through cleanly
                Template        = BuildBlankTemplate(),
            };

            btn.MouseEnter += (s, e) => inner.Background = hoverBrush;
            btn.MouseLeave += (s, e) => inner.Background = bgBrush;

            return btn;
        }

        /// A bare-bones ControlTemplate that just renders the button's Content.
        private static ControlTemplate BuildBlankTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var factory  = new FrameworkElementFactory(typeof(ContentPresenter));
            factory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            factory.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Stretch);
            template.VisualTree = factory;
            return template;
        }
    }
}
