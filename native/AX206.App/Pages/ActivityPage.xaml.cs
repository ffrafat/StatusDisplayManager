using System.Collections.ObjectModel;
using AX206.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AX206.Pages;

/// <summary>Recent log entries, newest first; includes what happened while the window was closed.</summary>
public sealed partial class ActivityPage : UserControl
{
    private const int MaxRows = 300;
    private readonly ObservableCollection<LogEntry> _rows;

    public ActivityPage()
    {
        InitializeComponent();
        var log = App.Services.Log;
        _rows = new ObservableCollection<LogEntry>(log.Snapshot().Reverse());
        LogList.ItemsSource = _rows;
        log.Added += entry => DispatcherQueue.TryEnqueue(() =>
        {
            _rows.Insert(0, entry);
            while (_rows.Count > MaxRows) _rows.RemoveAt(_rows.Count - 1);
        });
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _rows.Clear();
}
