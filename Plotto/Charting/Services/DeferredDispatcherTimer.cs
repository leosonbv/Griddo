using System.Windows.Threading;

namespace Plotto.Charting.Services;

/// <summary>
/// One-shot delayed callback on the UI dispatcher (SRP: timer lifecycle for deferred UI such as context menus).
/// </summary>
public sealed class DeferredDispatcherTimer
{
    private DispatcherTimer? _timer;
    private Action? _onElapsed;

    public void Cancel()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
        _onElapsed = null;
    }

    public void Schedule(Dispatcher dispatcher, int delayMilliseconds, Action onElapsed)
    {
        Cancel();
        _onElapsed = onElapsed;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(delayMilliseconds)
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var cb = _onElapsed;
        Cancel();
        cb?.Invoke();
    }
}
