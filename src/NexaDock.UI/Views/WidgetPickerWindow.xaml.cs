using System.Windows;
using System.Windows.Media;
using NexaDock.UI.ViewModels;

namespace NexaDock.UI.Views;

public partial class WidgetPickerWindow : Window
{
    public WidgetPickerWindow(WidgetPickerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 48);
        Closed += (_, _) => viewModel.Dispose();
    }

    public void SetAppIcon(ImageSource? icon) => Icon = icon;

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();
}
