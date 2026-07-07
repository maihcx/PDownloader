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

namespace PDownloader.Controls;

public class AcrylicPanel : ContentControl
{
    static AcrylicPanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AcrylicPanel),
            new FrameworkPropertyMetadata(typeof(AcrylicPanel)));
    }

    public static readonly DependencyProperty BackgroundSourceProperty =
        DependencyProperty.Register(nameof(BackgroundSource), typeof(Visual), typeof(AcrylicPanel),
            new PropertyMetadata(null, OnBackgroundSourceChanged));

    public Visual? BackgroundSource
    {
        get => (Visual?)GetValue(BackgroundSourceProperty);
        set => SetValue(BackgroundSourceProperty, value);
    }

    public static readonly DependencyProperty BlurRadiusProperty =
        DependencyProperty.Register(nameof(BlurRadius), typeof(double), typeof(AcrylicPanel),
            new PropertyMetadata(24.0));

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public static readonly DependencyProperty TintBrushProperty =
        DependencyProperty.Register(nameof(TintBrush), typeof(Brush), typeof(AcrylicPanel),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))));

    public Brush TintBrush
    {
        get => (Brush)GetValue(TintBrushProperty);
        set => SetValue(TintBrushProperty, value);
    }

    private static readonly DependencyPropertyKey BlurBrushPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(BlurBrush), typeof(Brush), typeof(AcrylicPanel),
            new PropertyMetadata(null));

    public static readonly DependencyProperty BlurBrushProperty = BlurBrushPropertyKey.DependencyProperty;

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(AcrylicPanel),
            new PropertyMetadata(new CornerRadius(0)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Brush? BlurBrush => (Brush?)GetValue(BlurBrushProperty);

    private VisualBrush? _visualBrush;

    private Grid? _rootGrid;

    private RectangleGeometry? _clipGeometry;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _rootGrid = GetTemplateChild("PART_RootGrid") as Grid;

        if (_rootGrid != null)
        {
            _clipGeometry = new RectangleGeometry();
            _rootGrid.Clip = _clipGeometry;
        }

        Loaded += (_, _) => UpdateVisualBrush();
        SizeChanged += (_, _) =>
        {
            UpdateClip();
            UpdateViewbox();
        };

        LayoutUpdated += OnLayoutUpdated;
        Unloaded += (_, _) => LayoutUpdated -= OnLayoutUpdated;

        UpdateClip();
    }

    private void UpdateClip()
    {
        if (_clipGeometry == null)
        {
            return;
        }

        _clipGeometry.Rect = new Rect(0, 0, ActualWidth, ActualHeight);

        double radius = CornerRadius.TopLeft;

        _clipGeometry.RadiusX = radius;
        _clipGeometry.RadiusY = radius;
    }

    private static void OnBackgroundSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AcrylicPanel)d).UpdateVisualBrush();

    private void UpdateVisualBrush()
    {
        if (BackgroundSource is null)
        {
            SetValue(BlurBrushPropertyKey, null);
            _visualBrush = null;
            return;
        }

        _visualBrush = new VisualBrush(BackgroundSource)
        {
            Stretch = Stretch.None,
            ViewboxUnits = BrushMappingMode.Absolute,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };

        SetValue(BlurBrushPropertyKey, _visualBrush);
        UpdateViewbox();
    }

    // Cập nhật vùng "cắt" ra khỏi visual gốc mỗi khi panel di chuyển/resize/scroll
    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdateViewbox();

    private void UpdateViewbox()
    {
        if (_visualBrush is null || BackgroundSource is null || !IsLoaded)
        {
            return;
        }

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        GeneralTransform transform;
        try
        {
            transform = TransformToVisual(BackgroundSource);
        }
        catch (InvalidOperationException)
        {
            return; // chưa nằm trong cùng visual tree
        }

        Point topLeft = transform.Transform(new Point(0, 0));
        _visualBrush.Viewbox = new Rect(topLeft, new Size(ActualWidth, ActualHeight));
    }
}
