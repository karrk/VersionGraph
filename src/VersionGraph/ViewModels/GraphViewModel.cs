using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullScreenButtonText))]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    private bool _isFullScreen;

    public string FullScreenButtonText => IsFullScreen ? "EXIT FULL SCREEN" : "FULL SCREEN";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailVisible))]
    private CommitDetail? _selectedDetail;

    public bool IsDetailVisible => IsFullScreen && SelectedDetail is not null;

    public string RepoLabel { get; }

    /// <summary>"STOP TRACE" 버튼 클릭 시 발생. MainWindow가 구독해서 레포 선택 화면으로 전환한다.</summary>
    public event EventHandler? StopTraceRequested;

    /// <summary>"FULL SCREEN" 버튼 클릭 시 발생. MainWindow가 구독해서 창 테두리를 없앤 전체화면을 토글한다.</summary>
    public event EventHandler? FullScreenToggleRequested;

    public GraphViewModel(string owner, string name, string localPath, string token)
    {
        RepoLabel = $"{owner}/{name}";
        _dispatcher = Dispatcher.CurrentDispatcher;

        _gitService = new GitRepositoryService(localPath, token);

        // Watcher(FileSystemWatcher)와 Poller(Timer) 콜백은 모두 백그라운드 스레드에서 호출되므로
        // 그래프/상태 갱신은 항상 Dispatcher를 거쳐 UI 스레드로 마샬링한다.
        _watcher = new RepoWatcher(localPath);
        _watcher.Changed += (_, _) => RequestRefresh(fromRemote: false);

        _poller = new RemotePoller(_gitService, PollInterval);
        _poller.RemoteChanged += (_, _) => RequestRefresh(fromRemote: true);
        _poller.FetchFailed += (_, ex) => _dispatcher.Invoke(() => StatusText = $"원격 갱신 실패(재시도 예정): {ex.Message}");

        RequestRefresh(fromRemote: false);
        _poller.Start();
    }

    [RelayCommand]
    private void StopTrace() => StopTraceRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleFullScreen()
    {
        IsFullScreen = !IsFullScreen;
        // 전체화면 해제 = 상세 패널 기능도 함께 종료
        if (!IsFullScreen)
            SelectedDetail = null;
        FullScreenToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectCommit(CommitNode commit) => LoadDetailAsync(commit.Sha);

    // diff 계산이 무거울 수 있어 백그라운드에서 만들고 결과만 UI 스레드로 반영.
    // 연타 클릭 시 늦게 끝난 이전 요청이 최신 선택을 덮어쓰지 않도록 sha를 대조한다
    private string? _pendingDetailSha;

    private async void LoadDetailAsync(string sha)
    {
        _pendingDetailSha = sha;
        try
        {
            var detail = await Task.Run(() => _gitService.GetCommitDetail(sha));
            if (_pendingDetailSha == sha)
                SelectedDetail = detail;
        }
        catch (Exception ex)
        {
            StatusText = $"커밋 상세 조회 실패: {ex.Message}";
        }
    }

    private void RequestRefresh(bool fromRemote) => _dispatcher.Invoke(() => RefreshOnUiThread(fromRemote));

    private void RefreshOnUiThread(bool fromRemote)
    {
        try
        {
            var previousHeadSha = Graph.Commits.Count > 0 ? Graph.Commits[0].Sha : null;
            Graph = _gitService.BuildGraph();
            StatusText = $"추적 중... (마지막 갱신 {DateTime.Now:HH:mm:ss})";

            // 원격에서 새 커밋이 들어왔고 전체화면이면 최신 커밋 상세를 자동 표시
            var newHeadSha = Graph.Commits.Count > 0 ? Graph.Commits[0].Sha : null;
            if (fromRemote && IsFullScreen && newHeadSha is not null && newHeadSha != previousHeadSha)
                LoadDetailAsync(newHeadSha);
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
