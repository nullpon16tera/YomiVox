using System.Collections.ObjectModel;
using System.Windows;
using YomiVox.Services;

namespace YomiVox;

public partial class CustomCommandsSettingsWindow : Window
{
    private readonly ObservableCollection<CustomChatCommandEntry> _rows = new();

    public CustomCommandsSettingsWindow(MainWindow owner)
    {
        Owner = owner;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Load();
        _rows.Clear();
        foreach (var entry in s.CustomChatCommands)
            _rows.Add(new CustomChatCommandEntry
            {
                CommandTrigger = entry.CommandTrigger,
                ResponseText = entry.ResponseText
            });

        CommandsGrid.ItemsSource = _rows;
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        _rows.Add(new CustomChatCommandEntry());
        CommandsGrid.SelectedItem = _rows[^1];
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (CommandsGrid.SelectedItem is CustomChatCommandEntry row)
            _rows.Remove(row);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsStore.Load();
        s.CustomChatCommands = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.CommandTrigger) && !string.IsNullOrWhiteSpace(r.ResponseText))
            .Select(r => new CustomChatCommandEntry
            {
                CommandTrigger = r.CommandTrigger.Trim(),
                ResponseText = r.ResponseText.Trim()
            })
            .ToList();
        SettingsStore.Save(s);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
