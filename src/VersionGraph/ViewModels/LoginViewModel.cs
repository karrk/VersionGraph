using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VersionGraph.Services;

namespace VersionGraph.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly GitHubAuthService _authService;
    private CancellationTokenSource? _deviceFlowCts;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isAuthorizing;

    /// <summary>브라우저에서 입력해야 하는 인증 코드. 진행 중이 아니면 null.</summary>
    [ObservableProperty]
    private string? _userCode;

    /// <summary>검증 성공 시 (token, login) 전달.</summary>
    public event EventHandler<(string Token, string Login)>? LoggedIn;

    public LoginViewModel(GitHubAuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task LoginWithDeviceFlowAsync()
    {
        ErrorMessage = null;
        IsAuthorizing = true;
        _deviceFlowCts = new CancellationTokenSource();

        try
        {
            var deviceFlow = await _authService.InitiateDeviceFlowAsync();
            UserCode = deviceFlow.UserCode;

            // 코드를 바로 붙여넣을 수 있게 클립보드에 복사하고 인증 페이지를 열어줌
            Clipboard.SetText(deviceFlow.UserCode);
            Process.Start(new ProcessStartInfo(deviceFlow.VerificationUri) { UseShellExecute = true });

            var token = await _authService.CompleteDeviceFlowAsync(deviceFlow, _deviceFlowCts.Token);
            var login = await _authService.ValidateAsync(token);

            if (login is null)
            {
                ErrorMessage = "인증에 실패했습니다. 다시 시도해주세요.";
                return;
            }

            TokenStore.Save(token);
            LoggedIn?.Invoke(this, (token, login));
        }
        catch (OperationCanceledException)
        {
            // 사용자가 취소 - 에러로 표시하지 않음
        }
        catch (Exception ex)
        {
            ErrorMessage = $"로그인 중 오류가 발생했습니다: {ex.Message}";
        }
        finally
        {
            IsAuthorizing = false;
            UserCode = null;
            _deviceFlowCts = null;
        }
    }

    [RelayCommand]
    private void CancelDeviceFlow()
    {
        _deviceFlowCts?.Cancel();
    }
}
