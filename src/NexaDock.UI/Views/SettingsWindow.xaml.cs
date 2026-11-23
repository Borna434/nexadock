using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NexaDock.UI.ViewModels;

namespace NexaDock.UI.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        // Show as much as possible without scrolling, but never exceed the work area.
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 48);
        viewModel.PickFiles = PickFiles;
        viewModel.PickBackupSaveFile = () =>
        {
            var dialog = new SaveFileDialog
            {
                Title = Localization.Loc.T("backup.export"),
                Filter = Localization.Loc.T("backup.filter"),
                FileName = $"NexaDock-{DateTime.Now:yyyy-MM-dd}.nexadock",
                DefaultExt = ".nexadock",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
        viewModel.PickBackupOpenFile = () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = Localization.Loc.T("backup.import"),
                Filter = Localization.Loc.T("backup.filter"),
                CheckFileExists = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
        viewModel.Confirm = text => MessageBox.Show(this, text, Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>Shows the tab whose Tag matches (e.g. "calendar"); null leaves the current one.</summary>
    public void SelectTab(string? tag)
    {
        if (tag is null)
        {
            return;
        }

        foreach (var item in Tabs.Items.OfType<System.Windows.Controls.TabItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                Tabs.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>Optional window/header icon supplied by the composition root.</summary>
    public void SetAppIcon(ImageSource? icon)
    {
        Icon = icon;
        HeaderIcon.Source = icon;
    }

    private IReadOnlyList<string> PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = Localization.Loc.T("pinned.dialogTitle"),
            Filter = Localization.Loc.T("pinned.dialogFilter"),
            Multiselect = true,
            DereferenceLinks = false, // keep .lnk files so their icon and arguments are preserved
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        };

        return dialog.ShowDialog(this) == true ? dialog.FileNames : [];
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCityKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && sender is System.Windows.Controls.TextBox box)
        {
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }
    }
}
