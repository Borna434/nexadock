using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace NexaDock.UI.Controls;

/// <summary>
/// A slider for the track position: it follows playback, but while you drag (or click) it stops
/// following, and on release it runs <see cref="SeekCommand"/> with the chosen percentage.
/// </summary>
public sealed class SeekSlider : Slider
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(SeekSlider), new PropertyMetadata(0.0, (d, e) => ((SeekSlider)d).Follow()));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(SeekSlider));

    private bool _user;
    private bool _dragging;

    /// <summary>Playback position in percent (one way, from the player).</summary>
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    private void Follow()
    {
        if (!_user)
        {
            Value = Position;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _user = true;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnThumbDragStarted(DragStartedEventArgs e)
    {
        _user = true;
        _dragging = true;
        base.OnThumbDragStarted(e);
    }

    protected override void OnThumbDragCompleted(DragCompletedEventArgs e)
    {
        base.OnThumbDragCompleted(e);
        _dragging = false;
        Commit();
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (_user && !_dragging)
        {
            Commit();
        }
    }

    private void Commit()
    {
        if (!_user)
        {
            return;
        }

        _user = false;
        if (SeekCommand?.CanExecute(Value) == true)
        {
            SeekCommand.Execute(Value);
        }
    }
}
