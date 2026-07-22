using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VersionGraph.Models;
using VersionGraph.Services;

namespace VersionGraph.ViewModels;

public sealed partial class RepoSelectViewModel : ObservableObject
{
    private readonly GitHubAuthService _authService;
    private readonly string _token;

    public ObservableCollection<RepoListItem> Repos { get; } = [];

    [ObservableProperty]
    private RepoListItem? _selectedRepo;

    [ObservableProperty]
    private string? _localPath;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>레포/로컬 경로 확정 시 (owner, name, localPath) 전달.</summary>
    public event EventHandler<(string Owner, string Name, string LocalPath)>? RepoReady;

    public RepoSelectViewModel(GitHubAuthService authService, string token)
    {
        _authService = authService;
        _token = token;
    }

    public async Task LoadReposAsync()
    {
        IsBusy = true;
        StatusMessage = "레포지토리 목록을 불러오는 중...";

        var config = AppConfigStore.Load();
        var repos = await _authService.ListReposAsync(_token);
        Repos.Clear();
        foreach (var repo in repos)
            Repos.Add(new RepoListItem(repo, config.GetRepoLocalPath(repo.Owner, repo.Name)));

        IsBusy = false;
        StatusMessage = null;
    }

    // 레포를 고르면 기록된 경로가 있는지 확인해서 자동으로 채운다.
    // 기록된 경로가 실제로는 사라진 경우(폴더 삭제/이동) 그 기록은 무효화하고 사용자가 다시 지정하게 한다.
    partial void OnSelectedRepoChanged(RepoListItem? value)
    {
        if (value?.StoredPath is not { } storedPath)
        {
            LocalPath = null;
            return;
        }

        if (Directory.Exists(storedPath))
        {
            LocalPath = storedPath;
            return;
        }

        AppConfigStore.Update(c => c.RemoveRepoLocalPath(value.Repo.Owner, value.Repo.Name));
        value.StoredPath = null;
        LocalPath = null;
        StatusMessage = "이전에 등록된 경로를 찾을 수 없습니다. 폴더를 다시 지정해주세요.";
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "로컬 클론 폴더 선택" };
        if (dialog.ShowDialog() == true)
            LocalPath = dialog.FolderName;
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (SelectedRepo is null)
        {
            StatusMessage = "레포지토리를 선택해주세요.";
            return;
        }

        if (string.IsNullOrWhiteSpace(LocalPath))
        {
            StatusMessage = "로컬 폴더를 지정해주세요.";
            return;
        }

        var repo = SelectedRepo.Repo;
        IsBusy = true;

        try
        {
            var gitDirExists = Directory.Exists(Path.Combine(LocalPath, ".git"));

            if (!gitDirExists)
            {
                if (Directory.Exists(LocalPath) && Directory.EnumerateFileSystemEntries(LocalPath).Any())
                {
                    StatusMessage = "선택한 폴더가 비어 있지 않습니다. 빈 폴더를 지정해주세요.";
                    return;
                }

                StatusMessage = "클론하는 중...";
                await Task.Run(() => GitRepositoryService.Clone(repo.CloneUrl, LocalPath, _token));
            }
            else if (!GitRepositoryService.MatchesRemote(LocalPath, repo.CloneUrl))
            {
                // 기록된 경로를 자동으로 채웠는데 실제로는 다른 레포였던 경우 그 기록을 지워서
                // 다음에 같은 잘못된 경로로 다시 유도되지 않게 한다.
                AppConfigStore.Update(c => c.RemoveRepoLocalPath(repo.Owner, repo.Name));
                SelectedRepo.StoredPath = null;
                StatusMessage = "선택한 폴더는 다른 레포지토리의 클론입니다. 다른 폴더를 지정해주세요.";
                return;
            }

            AppConfigStore.Update(c => c.SetRepoLocalPath(repo.Owner, repo.Name, LocalPath));
            RepoReady?.Invoke(this, (repo.Owner, repo.Name, LocalPath));
        }
        catch (Exception ex)
        {
            StatusMessage = $"클론 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>레포 선택 목록의 한 행. 기록된 로컬 경로가 있으면 우측에 표시하기 위한 래퍼.</summary>
public sealed partial class RepoListItem : ObservableObject
{
    public RepoSummary Repo { get; }

    [ObservableProperty]
    private string? _storedPath;

    public RepoListItem(RepoSummary repo, string? storedPath)
    {
        Repo = repo;
        StoredPath = storedPath;
    }
}
