using System.IO;
using System.Windows;
using System.Windows.Controls;
using VersionGraph.Models;
using VersionGraph.Services;
using VersionGraph.ViewModels;
using VersionGraph.Views;
using Forms = System.Windows.Forms;

namespace VersionGraph;

/// <summary>
/// 화면 전환(로그인 → 레포 선택 → 그래프)과 트레이/종료 생명주기를 담당하는 셸.
/// 그래프 추적 자체의 로직은 GraphViewModel이 갖고 있고, 여기서는 그 인스턴스의
/// 시작/종료(Dispose) 시점만 결정한다.
/// </summary>
public partial class MainWindow : Window
{
    private readonly GitHubAuthService _authService = new();
    private Forms.NotifyIcon? _notifyIcon;
    private GraphViewModel? _graphViewModel;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
        Loaded += async (_, _) => await StartAsync();
    }

    private void SetupTrayIcon()
    {
        var exePath = Environment.ProcessPath ?? Forms.Application.ExecutablePath;
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => RestoreWindow());
        menu.Items.Add("종료", null, (_, _) => Close());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "VersionGraph",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreWindow();
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // 최소화 버튼 = 트레이로 내려가되 추적(Watcher/Poller)은 계속 실행
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 창 닫기(X) 또는 트레이 "종료" = 추적 중지 지점
        _graphViewModel?.Dispose();
        _notifyIcon?.Dispose();
    }

    private async Task StartAsync()
    {
        ShowContent(new TextBlock
        {
            Text = "로그인 확인 중...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        // 1) 이전에 저장해둔 토큰 (재방문 사용자)
        var savedToken = TokenStore.Load();
        if (!string.IsNullOrEmpty(savedToken) && await _authService.ValidateAsync(savedToken) is not null)
        {
            await OnLoggedInAsync(savedToken);
            return;
        }

        // 2) 이 PC에 이미 로그인된 GitHub 계정(gh CLI / Windows 자격 증명 관리자) 자동 감지
        var detectedToken = await LocalGitCredentialService.TryDetectTokenAsync();
        if (!string.IsNullOrEmpty(detectedToken) && await _authService.ValidateAsync(detectedToken) is not null)
        {
            TokenStore.Save(detectedToken);
            await OnLoggedInAsync(detectedToken);
            return;
        }

        ShowLogin();
    }

    private void ShowLogin()
    {
        var vm = new LoginViewModel(_authService);
        vm.LoggedIn += async (_, e) => await OnLoggedInAsync(e.Token);
        ShowContent(new LoginView { DataContext = vm });
    }

    private async Task OnLoggedInAsync(string token)
    {
        var config = AppConfigStore.Load();
        var hasStoredRepo = config.RepoOwner is not null && config.RepoName is not null && config.LocalPath is not null
            && Directory.Exists(Path.Combine(config.LocalPath, ".git"));

        if (hasStoredRepo)
        {
            StartGraph(config.RepoOwner!, config.RepoName!, config.LocalPath!, token);
            return;
        }

        await ShowRepoSelectAsync(token);
    }

    private async Task ShowRepoSelectAsync(string token)
    {
        var vm = new RepoSelectViewModel(_authService, token);
        vm.RepoReady += (_, e) =>
        {
            AppConfigStore.Save(new AppConfig { RepoOwner = e.Owner, RepoName = e.Name, LocalPath = e.LocalPath });
            StartGraph(e.Owner, e.Name, e.LocalPath, token);
        };

        ShowContent(new RepoSelectView { DataContext = vm });
        await vm.LoadReposAsync();
    }

    private void StartGraph(string owner, string name, string localPath, string token)
    {
        _graphViewModel?.Dispose();
        _graphViewModel = new GraphViewModel(owner, name, localPath, token);
        ShowContent(new GraphView { DataContext = _graphViewModel });
    }

    private void ShowContent(UIElement element)
    {
        RootContent.Children.Clear();
        RootContent.Children.Add(element);
    }
}
