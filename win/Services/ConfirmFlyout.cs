using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ace_run.Services;

/// <summary>
/// A yes/no confirmation anchored to the button that asked for it.
/// </summary>
/// <remarks>
/// A <see cref="Flyout"/> and not a <see cref="ContentDialog"/> because both callers are
/// themselves inside a dialog, and WinUI allows one at a time — a nested one silently fails to
/// open, which would make Delete look broken.
/// </remarks>
internal static class ConfirmFlyout
{
    public static void Show(FrameworkElement target, string message, string confirmText, Action onConfirm)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 220
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // Declared before the handlers so they can close the flyout they belong to.
        Flyout? flyout = null;

        var confirm = new Button { Content = confirmText };
        confirm.Click += (_, _) =>
        {
            flyout?.Hide();
            onConfirm();
        };

        var cancel = new Button { Content = Loc.GetString("CancelButton") };
        cancel.Click += (_, _) => flyout?.Hide();

        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        flyout = new Flyout { Content = panel };
        flyout.ShowAt(target);
    }
}
