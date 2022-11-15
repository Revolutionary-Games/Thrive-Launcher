namespace ThriveLauncher.Controls;

using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

public class PopupDialog : ContentControl
{
    public static readonly StyledProperty<bool> ShowPopupProperty =
        AvaloniaProperty.Register<PopupDialog, bool>(nameof(Background));

    public static readonly StyledProperty<bool> ShowCloseXProperty =
        AvaloniaProperty.Register<PopupDialog, bool>(nameof(ShowCloseXProperty));

    public static readonly StyledProperty<bool> ShowTitleProperty =
        AvaloniaProperty.Register<PopupDialog, bool>(nameof(ShowTitleProperty));

    public static readonly StyledProperty<bool> ShowOKButtonProperty =
        AvaloniaProperty.Register<PopupDialog, bool>(nameof(ShowOKButton));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PopupDialog, string>(nameof(Title));

    public static readonly StyledProperty<ICommand> CloseCommandProperty =
        AvaloniaProperty.Register<PopupDialog, ICommand>(nameof(CloseCommand));

    public static readonly StyledProperty<IBrush> DarkenerBackgroundProperty =
        AvaloniaProperty.Register<PopupDialog, IBrush>(nameof(DarkenerBackground));

    public static readonly StyledProperty<IBrush> TitleSeparatorColourProperty =
        AvaloniaProperty.Register<PopupDialog, IBrush>(nameof(TitleSeparatorColour));

    public static readonly StyledProperty<ScrollBarVisibility> HorizontalScrollBarVisibilityProperty =
        AvaloniaProperty.Register<PopupDialog, ScrollBarVisibility>(nameof(HorizontalScrollBarVisibility));

    public static readonly StyledProperty<BoxShadows> BoxShadowProperty =
        AvaloniaProperty.Register<PopupDialog, BoxShadows>(nameof(BoxShadow));

    public static readonly StyledProperty<FontWeight> TitleFontWeightProperty =
        AvaloniaProperty.Register<PopupDialog, FontWeight>(nameof(TitleFontWeight));

    public static readonly StyledProperty<double> TitleFontSizeProperty =
        AvaloniaProperty.Register<PopupDialog, double>(nameof(TitleFontSize));

    public bool ShowPopup
    {
        get => GetValue(ShowPopupProperty);
        set => SetValue(ShowPopupProperty, value);
    }

    public bool ShowCloseX
    {
        get => GetValue(ShowCloseXProperty);
        set => SetValue(ShowCloseXProperty, value);
    }

    public bool ShowTitle
    {
        get => GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public bool ShowOKButton
    {
        get => GetValue(ShowOKButtonProperty);
        set => SetValue(ShowOKButtonProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ICommand CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public IBrush DarkenerBackground
    {
        get => GetValue(DarkenerBackgroundProperty);
        set => SetValue(DarkenerBackgroundProperty, value);
    }

    public IBrush TitleSeparatorColour
    {
        get => GetValue(TitleSeparatorColourProperty);
        set => SetValue(TitleSeparatorColourProperty, value);
    }

    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => GetValue(HorizontalScrollBarVisibilityProperty);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    public BoxShadows BoxShadow
    {
        get => GetValue(BoxShadowProperty);
        set => SetValue(BoxShadowProperty, value);
    }

    public FontWeight TitleFontWeight
    {
        get => GetValue(TitleFontWeightProperty);
        set => SetValue(TitleFontWeightProperty, value);
    }

    public double TitleFontSize
    {
        get => GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }
}
