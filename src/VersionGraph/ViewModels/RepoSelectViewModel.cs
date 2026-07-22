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

    public ObservableCollection<RepoSummary> Repos { get; } = [];

    [ObservableProperty]
    private RepoSummary? _selectedRepo;

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

        var repos = await _authService.ListReposAsync(_token);
        Repos.Clear();
        foreach (var repo in repos)
            Repos.Add(repo);

        IsBusy = false;
        StatusMessage = null;
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
                await Task.Run(() => GitRepositoryService.Clone(SelectedRepo.CloneUrl, LocalPath, _token));
            }
            else if (!GitRepositoryService.MatchesRemote(LocalPath, SelectedRepo.CloneUrl))
            {
                StatusMessage = "선택한 폴더는 다른 레포지토리의 클론입니다. 다른 폴더를 지정해주세요.";
                return;
            }

            RepoReady?.Invoke(this, (SelectedRepo.Owner, SelectedRepo.Name, LocalPath));
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
