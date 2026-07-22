namespace VersionGraph.Services;

/// <summary>
/// 주기적으로 git fetch를 실행해 원격에서 들어온 push를 감지. 다른 클론에서 발생한
/// 변경은 로컬 FileSystemWatcher로 잡을 수 없으므로 폴링으로 보완.
/// </summary>
public sealed class RemotePoller : IDisposable
{
    private readonly GitRepositoryService _gitService;
    private readonly System.Timers.Timer _timer;

    public event EventHandler? RemoteChanged;
    public event EventHandler<Exception>? FetchFailed;

    public RemotePoller(GitRepositoryService gitService, TimeSpan interval)
    {
        _gitService = gitService;
        _timer = new System.Timers.Timer(interval.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += async (_, _) => await PollAsync();
    }

    public void Start() => _timer.Start();

    private async Task PollAsync()
    {
        try
        {
            await Task.Run(_gitService.Fetch);
            RemoteChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // 네트워크 단절 등은 다음 폴링에서 재시도하면 되므로 앱을 죽이지 않고 알림만 전파
            FetchFailed?.Invoke(this, ex);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
