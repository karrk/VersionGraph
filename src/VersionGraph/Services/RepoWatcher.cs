using System.IO;

namespace VersionGraph.Services;

/// <summary>
/// .git 디렉터리(HEAD, refs/**, packed-refs, logs/HEAD)를 감시해 로컬 커밋/브랜치 변경을 즉시 감지.
/// </summary>
public sealed class RepoWatcher : IDisposable
{
    // rebase 등에서 refs 파일이 연쇄적으로 여러 번 바뀌는 것을 하나의 갱신으로 묶기 위한 디바운스 간격
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounceTimer;

    public event EventHandler? Changed;

    public RepoWatcher(string localPath)
    {
        var gitDir = Path.Combine(localPath, ".git");

        _debounceTimer = new System.Timers.Timer(DebounceInterval.TotalMilliseconds) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);

        _watcher = new FileSystemWatcher(gitDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };
        _watcher.Changed += OnGitDirEvent;
        _watcher.Created += OnGitDirEvent;
        _watcher.Deleted += OnGitDirEvent;
        _watcher.Renamed += OnGitDirEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnGitDirEvent(object sender, FileSystemEventArgs e)
    {
        // 스캔은 debounce 뒤 Changed 이벤트에서 그래프를 재구성하는 쪽 책임
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
