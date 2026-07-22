using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VersionGraph.Models;
using VersionGraph.Services;

namespace VersionGraph.ViewModels;

/// <summary>
/// 선택된 레포를 추적하는 동안 살아있는 뷰모델. Dispose되면 감시/폴링이 모두 멈춘다 —
/// 창 X(종료) 시 추적을 중지해야 하므로 이 Dispose 호출이 곧 "추적 중지" 지점이다.
/// </summary>
public sealed partial class GraphViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly GitRepositoryService _gitService;
    private readonly RepoWatcher _watcher;
    private readonly RemotePoller _poller;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    private GraphModel _graph = GraphModel.Empty;

    [ObservableProperty]
    private string _statusText = "추적 중...";

    public string RepoLabel { get; }

    public GraphViewModel(string owner, string name, string localPath, string token)
    {
        RepoLabel = $"{owner}/{name}";
        _dispatcher = Dispatcher.CurrentDispatcher;

        _gitService = new GitRepositoryService(localPath, token);

        // Watcher(FileSystemWatcher)와 Poller(Timer) 콜백은 모두 백그라운드 스레드에서 호출되므로
        // 그래프/상태 갱신은 항상 Dispatcher를 거쳐 UI 스레드로 마샬링한다.
        _watcher = new RepoWatcher(localPath);
        _watcher.Changed += (_, _) => RequestRefresh();

        _poller = new RemotePoller(_gitService, PollInterval);
        _poller.RemoteChanged += (_, _) => RequestRefresh();
        _poller.FetchFailed += (_, ex) => _dispatcher.Invoke(() => StatusText = $"원격 갱신 실패(재시도 예정): {ex.Message}");

        RequestRefresh();
        _poller.Start();
    }

    private void RequestRefresh() => _dispatcher.Invoke(RefreshOnUiThread);

    private void RefreshOnUiThread()
    {
        try
        {
            Graph = _gitService.BuildGraph();
            StatusText = $"추적 중... (마지막 갱신 {DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            StatusText = $"그래프 갱신 실패: {ex.Message}";
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _poller.Dispose();
        _gitService.Dispose();
    }
}
