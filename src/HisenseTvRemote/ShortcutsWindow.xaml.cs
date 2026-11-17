using System.Windows;
using System.Windows.Controls;
using HisenseTvRemote.Core;

namespace HisenseTvRemote;

/// <summary>Aide (F1) : générée depuis <see cref="KeyboardShortcuts"/>, donc toujours à jour.</summary>
public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        InitializeComponent();
        foreach (var group in KeyboardShortcuts.ForHelp())
        {
            Groups.Children.Add(new TextBlock
            {
                Text = group.Key.ToUpperInvariant(),
                Style = (Style)FindResource("SectionTitle"),
                Margin = new Thickness(2, 10, 0, 4),
            });
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (var s in group)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var row = grid.RowDefinitions.Count - 1;
                var keys = new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 2, 12, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = s.Display, FontFamily = new System.Windows.Media.FontFamily("Consolas") },
                };
                Grid.SetRow(keys, row);
                var label = new TextBlock { Text = s.Label, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(label, row);
                Grid.SetColumn(label, 1);
                grid.Children.Add(keys);
                grid.Children.Add(label);
            }
            Groups.Children.Add(grid);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
