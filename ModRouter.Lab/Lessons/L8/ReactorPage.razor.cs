using ModRouter.Components;

namespace ModRouter.Lab.Lessons.L8;

// A route that declares KeepAlive stays rendered but hidden when the user leaves it, with all its state.
// A hidden page keeps running, so a page with a timer should implement IKeepAliveRoute and stop work while it is hidden.
public partial class ReactorPage : IKeepAliveRoute, IDisposable
{
    Timer? _timer;
    int _ticks;
    int _pauses;
    bool _paused;

    protected override void OnInitialized() => Start();

    public ValueTask OnDeactivatedAsync()
    {
        _paused = true;
        _pauses++;
        _timer?.Dispose();
        _timer = null;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnActivatedAsync()
    {
        _paused = false;
        Start();
        StateHasChanged();

        return ValueTask.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    void Start()
        => _timer = new Timer(_ => _ = InvokeAsync(() =>
        {
            _ticks++;
            StateHasChanged();
        }), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
}
