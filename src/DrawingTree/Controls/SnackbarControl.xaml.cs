using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using UserControl = System.Windows.Controls.UserControl;

namespace DrawingTree.Controls;

/// <summary>
/// SnackbarControl.xaml.cs
/// Lightweight snackbar notification overlay, similar to Material UI Snackbar.
/// Fades in on Show(), then auto-dismisses after 3 seconds.
/// </summary>
public partial class SnackbarControl : UserControl
{
    private readonly DispatcherTimer _timer;

    public SnackbarControl()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            SnackbarBorder.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(300))));
        };
    }

    /// <summary>
    /// Display a snackbar message. Re-calling while visible resets the timer and updates the text.
    /// </summary>
    /// <param name="message">Message to display</param>
    public void Show(string message)
    {
        MessageText.Text = message;
        _timer.Stop();
        SnackbarBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200))));
        _timer.Start();
    }
}
