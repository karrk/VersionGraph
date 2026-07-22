using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _glitchTimer = new();
    private readonly DispatcherTimer _rollingBarTimer = new();
    private readonly Random _glitchRandom = new();
    private double _screenHeight;
    private Forms.NotifyIcon? _notifyIcon;
    private GraphViewModel? _graphViewModel;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
        SourceInitialized += (_, _) => ApplyNativeChromeTweaks();
        Loaded += async (_, _) => await StartAsync();
        Loaded += (_, _) => StartGlitchEffect();
        Loaded += (_, _) => StartRollingBarEffect();
    }

    // 롤링 바 한 번(화면 밖 위 → 화면 밖 아래)이 지나가는 데 걸리는 시간
    private static readonly TimeSpan RollingBarPassDuration = TimeSpan.FromSeconds(7);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;

    // 네이티브 타이틀바 다크 모드 + 창 자체 라운드 코너 끄기.
    // Windows 11은 최상위 창 모서리를 자체적으로 둥글리는데, 이게 우리 CRT 베젤의
    // 라운드 코너와 이중으로 겹치면서 모서리에 흰 이음새가 생겨 여기서 꺼버린다.
    private void ApplyNativeChromeTweaks()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var useDark = 1;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));

        var doNotRound = DwmwcpDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref doNotRound, sizeof(int));
    }

    // 신호 글리치: 화면 임의 위치에 얇은 색 번짐 줄을 짧게 반짝였다가 다음 번은
    // 또 다른 무작위 간격으로 예약해 규칙적이지 않은 CRT 잡신호 느낌을 낸다
    private void StartGlitchEffect()
    {
        _glitchTimer.Tick += (_, _) =>
        {
            GlitchTransform.Y = _glitchRandom.NextDouble() * ScreenArea.ActualHeight;
            var flash = new DoubleAnimation(0, 0.22, TimeSpan.FromMilliseconds(70)) { AutoReverse = true };
            GlitchLine.BeginAnimation(OpacityProperty, flash);
            _glitchTimer.Interval = TimeSpan.FromSeconds(_glitchRandom.Next(6, 14));
        };
        _glitchTimer.Interval = TimeSpan.FromSeconds(_glitchRandom.Next(6, 14));
        _glitchTimer.Start();
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

    // Clip은 크기를 자동으로 따라오지 않아 SizeChanged마다 다시 그려줘야
    // 창을 리사이즈해도 CRT 화면의 둥근 모서리가 유지된다
    private void ScreenArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScreenArea.Clip = new RectangleGeometry(new Rect(e.NewSize), 14, 14);
        // 이동 거리(-바 높이 ~ 화면 높이)가 창 크기에 종속적이라 다음 재생 때 쓸 값만 갱신해둔다
        _screenHeight = e.NewSize.Height;
    }

    // 롤링 바: 실제 브라운관 동기 이탈처럼 항상 도는 게 아니라, 아주 가끔 한 번씩만
    // 지나가야 자연스럽다. 글리치와 같은 방식(무작위 간격 재예약)으로 등장 빈도를 낮춘다
    private void StartRollingBarEffect()
    {
        // 기본 Y=0(화면 최상단)에 그대로 걸쳐 있으면 첫 재생 전까지 고정된 채 보여버리므로,
        // 애니메이션을 걸기 전에 화면 밖 대기 위치로 미리 치워둔다
        RollingBarTransform.Y = -RollingBar.Height;

        _rollingBarTimer.Tick += (_, _) =>
        {
            PlayRollingBarPass();
            _rollingBarTimer.Interval = TimeSpan.FromSeconds(_glitchRandom.Next(3, 21));
        };
        _rollingBarTimer.Interval = TimeSpan.FromSeconds(_glitchRandom.Next(3, 21));
        _rollingBarTimer.Start();
    }

    private void PlayRollingBarPass()
    {
        if (_screenHeight <= 0)
            return;

        var roll = new DoubleAnimation(-RollingBar.Height, _screenHeight, RollingBarPassDuration);
        RollingBarTransform.BeginAnimation(TranslateTransform.YProperty, roll);

        // 실제 아날로그 롤바는 밝기가 일정하지 않고 지글거린다. 불규칙한 간격마다
        // 불규칙한 밝기로 뚝뚝 끊어 바뀌는 Discrete 키프레임을 랜덤 생성해 그 노이즈를 흉내내되,
        // 지나가는 동안(RollingBarPassDuration)만 유지하고 끝나면 다시 숨는다
        var flicker = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var t = TimeSpan.Zero;
        while (t < RollingBarPassDuration)
        {
            t += TimeSpan.FromMilliseconds(_glitchRandom.Next(40, 150));
            var brightness = 0.4 + _glitchRandom.NextDouble() * 0.6;
            flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(brightness, KeyTime.FromTimeSpan(t)));
        }
        RollingBar.BeginAnimation(OpacityProperty, flicker);
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
        _glitchTimer.Stop();
        _rollingBarTimer.Stop();
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
            AppConfigStore.Update(c =>
            {
                c.RepoOwner = e.Owner;
                c.RepoName = e.Name;
                c.LocalPath = e.LocalPath;
            });
            StartGraph(e.Owner, e.Name, e.LocalPath, token);
        };

        ShowContent(new RepoSelectView { DataContext = vm });
        await vm.LoadReposAsync();
    }

    private void StartGraph(string owner, string name, string localPath, string token)
    {
        _graphViewModel?.Dispose();
        _graphViewModel = new GraphViewModel(owner, name, localPath, token);
        _graphViewModel.StopTraceRequested += async (_, _) => await OnStopTraceAsync(token);
        ShowContent(new GraphView { DataContext = _graphViewModel });
    }

    private async Task OnStopTraceAsync(string token)
    {
        // 추적 중지 = 저장된 레포 설정도 함께 비워야 다음 재시작 때 이 레포로 자동 진입하지 않는다
        _graphViewModel?.Dispose();
        _graphViewModel = null;
        AppConfigStore.ClearActiveRepo();
        await ShowRepoSelectAsync(token);
    }

    private void ShowContent(UIElement element)
    {
        RootContent.Children.Clear();
        RootContent.Children.Add(element);
    }
}
