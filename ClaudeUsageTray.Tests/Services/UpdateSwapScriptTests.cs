using System.Diagnostics;
using System.IO;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// exe 교체 스크립트를 실제 PowerShell 로 돌려 검증한다. 이 경로가 잘못되면 사용자의 앱이 사라지므로
/// 문자열 검사로 갈음하지 않고 진짜로 실행한다.
/// 대상 파일 이름은 'ClaudeUsageTray' 가 아닌 더미로 두어, 스크립트의 프로세스 종료 단계가
/// 테스트 실행 중인 실제 앱을 건드리지 않게 한다.
/// </summary>
[Trait("Category", "Integration")]
public class UpdateSwapScriptTests : IDisposable
{
    private readonly string _dir;
    private readonly string _oldExe;
    private readonly string _newExe;
    private readonly string _log;
    private readonly string _ps1;

    public UpdateSwapScriptTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"swaptest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _oldExe = Path.Combine(_dir, "SwapTestApp.exe");
        _newExe = Path.Combine(_dir, "SwapTestApp_new.exe");
        _log    = Path.Combine(_dir, "swap.log");
        _ps1    = Path.Combine(_dir, "swap.ps1");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 정리 실패는 테스트 결과와 무관 */ }
        GC.SuppressFinalize(this);
    }

    private void RunScript()
    {
        var script = UpdateService.BuildSwapScript(_oldExe, _newExe, _log, _ps1);
        File.WriteAllText(_ps1, script, new System.Text.UTF8Encoding(true));

        using var proc = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{_ps1}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        proc.WaitForExit(120_000);
    }

    private string ReadLog() => File.Exists(_log) ? File.ReadAllText(_log) : "";

    [Fact]
    public void Swap_ReplacesExecutable_AndRemovesBackup()
    {
        File.WriteAllText(_oldExe, "OLD");
        File.WriteAllText(_newExe, "NEW");

        RunScript();

        Assert.True(File.Exists(_oldExe), "교체 후 실행 파일이 존재해야 한다.");
        Assert.Equal("NEW", File.ReadAllText(_oldExe));
        Assert.False(File.Exists(_oldExe + ".bak"), "성공 시 백업은 남기지 않는다.");
        Assert.Contains("Move successful.", ReadLog());
    }

    // 핵심 회귀: 교체가 실패해도 원본이 사라지면 안 된다.
    // 이전 구현은 원본을 먼저 Remove-Item 으로 지웠기 때문에, 이동이 실패하면 앱이 영구 소실됐다.
    [Fact]
    public void Swap_RestoresOriginal_WhenReplacementIsMissing()
    {
        File.WriteAllText(_oldExe, "OLD");
        // _newExe 를 만들지 않는다 → Move-Item 이 매 시도마다 실패한다.

        RunScript();

        Assert.True(File.Exists(_oldExe), "교체 실패 시 원본이 제자리에 복구돼야 한다.");
        Assert.Equal("OLD", File.ReadAllText(_oldExe));
        Assert.False(File.Exists(_oldExe + ".bak"), "복구 후에는 백업 사본이 남지 않는다.");

        var log = ReadLog();
        Assert.Contains("CRITICAL ERROR", log);
        Assert.Contains("Rolled back", log);
    }

    // 스크립트는 교체 대상 exe 이름으로 프로세스를 찾아야 한다.
    // 'ClaudeUsageTray' 를 하드코딩하면 다른 경로의 앱이나 테스트 환경의 실행 중인 앱을 죽인다.
    [Fact]
    public void Script_DerivesProcessName_FromTargetExecutable()
    {
        var script = UpdateService.BuildSwapScript(
            @"C:\Apps\MyTray.exe", @"C:\Temp\new.exe", _log, _ps1);

        Assert.Contains("$procName = 'MyTray'", script);
        Assert.DoesNotContain("'ClaudeUsageTray'", script);
    }
}
