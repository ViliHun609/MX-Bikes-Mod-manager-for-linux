using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MXBikesModManager.Behaviors;

/// <summary>
/// Opt-in eased wheel scrolling for a <see cref="ScrollViewer"/>. Wheel notches move a target
/// offset and a short timer glides the viewer toward it; grabbing the scrollbar or pressing
/// the mouse cancels the glide immediately.
/// </summary>
public static class SmoothScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(SmoothScroll));

    private static readonly ConditionalWeakTable<ScrollViewer, SmoothScrollState> States = new();

    static SmoothScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) =>
        {
            if (args.NewValue is true)
            {
                Attach(viewer);
            }
            else
            {
                Detach(viewer);
            }
        });
    }

    public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer viewer, bool value) =>
        viewer.SetValue(IsEnabledProperty, value);

    private static void Attach(ScrollViewer viewer)
    {
        if (States.TryGetValue(viewer, out _)) return;

        var state = new SmoothScrollState(viewer);
        States.Add(viewer, state);

        viewer.AddHandler(InputElement.PointerWheelChangedEvent, state.OnWheel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerPressedEvent, state.OnPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.ScrollChanged += state.OnScrollChanged;
    }

    private static void Detach(ScrollViewer viewer)
    {
        if (!States.TryGetValue(viewer, out var state)) return;
        States.Remove(viewer);
        state.Dispose();
    }

    private sealed class SmoothScrollState : IDisposable
    {
        private const double EaseFactor = 0.28;

        private readonly ScrollViewer _viewer;
        private readonly DispatcherTimer _timer;
        private Vector _target;
        private Vector _lastApplied;
        private bool _applying;

        public SmoothScrollState(ScrollViewer viewer)
        {
            _viewer = viewer;
            _target = viewer.Offset;
            _lastApplied = viewer.Offset;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTick;
        }

        public void OnWheel(object? sender, PointerWheelEventArgs e)
        {
            var maximum = _viewer.ScrollBarMaximum;
            if (maximum.X <= 0 && maximum.Y <= 0) return;

            // If the view was moved externally (scrollbar/code) since our last frame, adopt it.
            if (!_timer.IsEnabled) _target = _viewer.Offset;

            var delta = e.Delta;

            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                var step = SmoothScrollMath.WheelStep(_viewer.SmallChange.Width);
                var horizontal = delta.X != 0 ? delta.X : delta.Y;
                _target = new Vector(
                    SmoothScrollMath.Clamp(_target.X - horizontal * step, maximum.X),
                    _target.Y);
            }
            else
            {
                var step = SmoothScrollMath.WheelStep(_viewer.SmallChange.Height);
                _target = new Vector(
                    _target.X,
                    SmoothScrollMath.Clamp(_target.Y - delta.Y * step, maximum.Y));
            }

            e.Handled = true;
            if (!_timer.IsEnabled) _timer.Start();
        }

        public void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Manual interaction wins over any residual glide.
            _timer.Stop();
            _target = _viewer.Offset;
            _lastApplied = _target;
        }

        public void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_applying) return;

            // Ignore the asynchronous notifications caused by our own Offset assignments.
            var current = _viewer.Offset;
            if (Math.Abs(current.X - _lastApplied.X) < 0.01 &&
                Math.Abs(current.Y - _lastApplied.Y) < 0.01)
            {
                return;
            }

            // The scrollbar (or code) moved the view; follow it instead of fighting it.
            _target = current;
            _lastApplied = current;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var maximum = _viewer.ScrollBarMaximum;
            _target = new Vector(
                SmoothScrollMath.Clamp(_target.X, maximum.X),
                SmoothScrollMath.Clamp(_target.Y, maximum.Y));

            var current = _viewer.Offset;
            var next = new Vector(
                SmoothScrollMath.Step(current.X, _target.X, EaseFactor),
                SmoothScrollMath.Step(current.Y, _target.Y, EaseFactor));

            if (SmoothScrollMath.IsClose(next.X, _target.X)) next = new Vector(_target.X, next.Y);
            if (SmoothScrollMath.IsClose(next.Y, _target.Y)) next = new Vector(next.X, _target.Y);

            _applying = true;
            try
            {
                _viewer.Offset = next;
                _lastApplied = next;
            }
            finally
            {
                _applying = false;
            }

            if (SmoothScrollMath.IsClose(next.X, _target.X) &&
                SmoothScrollMath.IsClose(next.Y, _target.Y))
            {
                _timer.Stop();
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
            _viewer.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            _viewer.ScrollChanged -= OnScrollChanged;
        }
    }
}

/// <summary>Pure scroll maths, kept separate so it can be unit-tested without a UI.</summary>
public static class SmoothScrollMath
{
    public const double SnapDistance = 0.6;

    public static double Clamp(double value, double maximum) =>
        Math.Clamp(value, 0, Math.Max(0, maximum));

    public static double Step(double current, double target, double factor) =>
        current + (target - current) * factor;

    public static bool IsClose(double current, double target) =>
        Math.Abs(target - current) < SnapDistance;

    /// <summary>Wheel distance per notch, based on the viewer's line size with a sane minimum.</summary>
    public static double WheelStep(double smallChange) =>
        Math.Max(64, smallChange * 3);
}
