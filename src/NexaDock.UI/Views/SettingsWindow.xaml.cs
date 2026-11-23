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
        Closed += (_, _) => _viewModel.Dispose();
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
            Title = "Add shortcuts to the dock",
            Filter = "Apps and shortcuts (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|All files (*.*)|*.*",
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
