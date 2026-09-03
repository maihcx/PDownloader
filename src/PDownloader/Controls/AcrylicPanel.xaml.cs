// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PDownloader.Controls;

public class AcrylicPanel : ContentControl
{
    private VisualBrush? _visualBrush;
    private Grid? _rootGrid;
    private Rectangle? _blurLayer;
    private RectangleGeometry? _clipGeometry;
    private BlurEffect? _blurEffect;

    static AcrylicPanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(typeof(AcrylicPanel)));
    }

    public AcrylicPanel()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    #region BackgroundSource

    public static readonly DependencyProperty BackgroundSourceProperty =
        DependencyProperty.Register(
            nameof(BackgroundSource),
            typeof(Visual),
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnBackgroundSourceChanged));

    public Visual? BackgroundSource
    {
        get => (Visual?)GetValue(BackgroundSourceProperty);
        set => SetValue(BackgroundSourceProperty, value);
    }

    private static void OnBackgroundSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is AcrylicPanel panel)
        {
            panel.UpdateVisualBrush();
        }
    }

    #endregion

    #region BlurRadius

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(
            nameof(BlurRadius),
            typeof(double),
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(
                24.0,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnBlurRadiusChanged,
                CoerceBlurRadius));

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    private static void OnBlurRadiusChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not AcrylicPanel panel ||
            panel._blurEffect is null)
        {
            return;
        }

        panel._blurEffect.Radius = (double)eventArgs.NewValue;
    }

    private static object CoerceBlurRadius(
        DependencyObject dependencyObject,
        object baseValue)
    {
        double radius = (double)baseValue;

        if (double.IsNaN(radius) ||
            double.IsInfinity(radius))
        {
            return 0.0;
        }

        return Math.Max(0.0, radius);
    }

    #endregion

    #region TintBrush

    public static readonly DependencyProperty TintBrushProperty =
        DependencyProperty.Register(
            nameof(TintBrush),
            typeof(Brush),
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(
                CreateDefaultTintBrush(),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TintBrush
    {
        get => (Brush)GetValue(TintBrushProperty);
        set => SetValue(TintBrushProperty, value);
    }

    private static Brush CreateDefaultTintBrush()
    {
        var brush = new SolidColorBrush(
            Color.FromArgb(90, 255, 255, 255));

        brush.Freeze();

        return brush;
    }

    #endregion

    #region BlurBrush

    private static readonly DependencyPropertyKey BlurBrushPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(BlurBrush),
            typeof(Brush),
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BlurBrushProperty =
        BlurBrushPropertyKey.DependencyProperty;

    public Brush? BlurBrush =>
        (Brush?)GetValue(BlurBrushProperty);

    #endregion

    #region CornerRadius

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(
                new CornerRadius(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnCornerRadiusChanged));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private static void OnCornerRadiusChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is AcrylicPanel panel)
        {
            panel.UpdateClip();
        }
    }

    #endregion

    #region Command

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(AcrylicPanel),
            new PropertyMetadata(null));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(AcrylicPanel),
            new PropertyMetadata(null));

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    #endregion

    #region IsPressed

    private static readonly DependencyPropertyKey IsPressedPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsPressed),
            typeof(bool),
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsPressedProperty =
        IsPressedPropertyKey.DependencyProperty;

    public bool IsPressed =>
        (bool)GetValue(IsPressedProperty);

    #endregion

    public override void OnApplyTemplate()
    {
        if (_blurLayer is not null)
        {
            _blurLayer.Effect = null;
        }

        _rootGrid = null;
        _blurLayer = null;
        _blurEffect = null;
        _clipGeometry = null;

        base.OnApplyTemplate();

        _rootGrid = GetTemplateChild("PART_RootGrid") as Grid;
        _blurLayer = GetTemplateChild("PART_BlurLayer") as Rectangle;

        if (_rootGrid is not null)
        {
            _clipGeometry = new RectangleGeometry();
            _rootGrid.Clip = _clipGeometry;
        }

        if (_blurLayer is not null)
        {
            _blurEffect = new BlurEffect
            {
                Radius = BlurRadius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality
            };

            _blurLayer.Effect = _blurEffect;
        }

        UpdateClip();
        UpdateVisualBrush();
        UpdateViewbox();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        LayoutUpdated += OnLayoutUpdated;

        UpdateClip();
        UpdateVisualBrush();
        UpdateViewbox();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
    }

    private void OnSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdateClip();
        UpdateViewbox();
    }

    private void OnLayoutUpdated(
        object? sender,
        EventArgs e)
    {
        UpdateViewbox();
    }

    private void UpdateClip()
    {
        if (_clipGeometry is null ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        _clipGeometry.Rect = new Rect(
            0,
            0,
            ActualWidth,
            ActualHeight);

        double radius = Math.Max(
            0.0,
            CornerRadius.TopLeft);

        _clipGeometry.RadiusX = radius;
        _clipGeometry.RadiusY = radius;
    }

    private void UpdateVisualBrush()
    {
        if (BackgroundSource is null)
        {
            _visualBrush = null;
            SetValue(BlurBrushPropertyKey, null);
            return;
        }

        _visualBrush = new VisualBrush(BackgroundSource)
        {
            Stretch = Stretch.None,
            ViewboxUnits = BrushMappingMode.Absolute,
            ViewportUnits = BrushMappingMode.Absolute,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };

        SetValue(BlurBrushPropertyKey, _visualBrush);

        UpdateViewbox();
    }

    private void UpdateViewbox()
    {
        if (_visualBrush is null ||
            BackgroundSource is null ||
            !IsLoaded ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        try
        {
            GeneralTransform transform =
                TransformToVisual(BackgroundSource);

            Point topLeft =
                transform.Transform(new Point(0, 0));

            _visualBrush.Viewbox = new Rect(
                topLeft.X,
                topLeft.Y,
                ActualWidth,
                ActualHeight);

            _visualBrush.Viewport = new Rect(
                0,
                0,
                ActualWidth,
                ActualHeight);
        }
        catch (InvalidOperationException)
        {

        }
        catch (ArgumentException)
        {

        }
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (Command is null)
        {
            return;
        }

        SetValue(IsPressedPropertyKey, true);

        CaptureMouse();

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        bool wasPressed = IsPressed;
        bool isInside = IsMouseInside(e);

        SetValue(IsPressedPropertyKey, false);

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (wasPressed &&
            isInside &&
            Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }

        if (wasPressed)
        {
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(
        MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        SetValue(IsPressedPropertyKey, false);
    }

    private bool IsMouseInside(MouseEventArgs e)
    {
        Point position = e.GetPosition(this);

        return position.X >= 0 &&
               position.Y >= 0 &&
               position.X <= ActualWidth &&
               position.Y <= ActualHeight;
    }
}