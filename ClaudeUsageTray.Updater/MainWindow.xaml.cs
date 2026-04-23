using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;

namespace ClaudeUsageTray.Updater;

public partial class MainWindow : Window
{
    // Command line args:
    // arg[0] = downloadUrl (GitHub asset URL)
    // arg[1] = sha256Url (optional, GitHub SHA256 file URL)
    // arg[2] = current exe path (to be replaced)
    // arg[3] = target directory

    private string _downloadUrl = "";
    private string _sha256Url = "";
    private string _currentExePath = "";
    private string _targetDir = "";
    private string _newExePath = "";

    private static readonly HttpClient Http = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    static MainWindow()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "ClaudeUsageTray-Updater");
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        var args = Environment.GetCommandLineArgs();
        if (args.Length < 5)
        {
            MessageBox.Show("Updater가 잘못된 인자로 실행되었습니다. (인자 부족)", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        _downloadUrl = args[1];
        _sha256Url = args[2];
        _currentExePath = args[3];
        _targetDir = args[4];

        // Start update process in background
        Task.Run(UpdateProcess);
    }

    private async Task UpdateProcess()
    {
        try
        {
            // Phase 1: Wait for old process to exit
            UpdateStatus("기존 프로세스 종료 대기 중...", 0);
            await WaitForProcessExit();

            // Phase 2: Download new exe
            UpdateStatus("다운로드 중...", 10);
            await DownloadNewExe();

            // Phase 3: Verify SHA256
            UpdateStatus("파일 검증 중...", 70);
            await VerifySha256();

            // Phase 4: Copy new exe
            UpdateStatus("파일 설치 중...", 80);
            await CopyNewExe();

            // Phase 5: Start new process
            UpdateStatus("새 프로그램 실행 중...", 95);
            await StartNewProcess();

            // Phase 6: Cleanup
            UpdateStatus("완료!", 100);
            await Task.Delay(1500);

            Dispatcher.Invoke(() => Close());
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"업데이트 중 오류가 발생했습니다:\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            });
        }
    }

    private async Task WaitForProcessExit()
    {
        const string processName = "ClaudeUsageTray";
        int maxAttempts = 60; // 60 seconds max

        for (int i = 0; i < maxAttempts; i++)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0) break;

            foreach (var p in processes) p.Dispose();

            var progress = (int)((i / (double)maxAttempts) * 10);
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = progress;
                PercentText.Text = $"{progress}%";
            });

            await Task.Delay(1000);
        }

        // Extra wait to ensure process is fully terminated
        await Task.Delay(500);
    }

    private async Task DownloadNewExe()
    {
        var tempDir = Path.GetTempPath();
        _newExePath = Path.Combine(tempDir, "ClaudeUsageTray_update.exe");

        using var response = await Http.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var srcStream = await response.Content.ReadAsStreamAsync();
        using var destStream = new FileStream(_newExePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;

        while ((read = await srcStream.ReadAsync(buffer)) > 0)
        {
            await destStream.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;

            if (totalBytes > 0)
            {
                var pct = 10 + (int)((downloaded * 60) / totalBytes); // 10-70%
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value = pct;
                    PercentText.Text = $"{pct}%";
                });
            }
        }
    }

    private async Task VerifySha256()
    {
        if (string.IsNullOrEmpty(_sha256Url)) return;

        try
        {
            var sha256Raw = await Http.GetStringAsync(_sha256Url);
            var expectedHash = sha256Raw.Split(' ')[0].Trim().ToLowerInvariant();

            using var exeStream = File.OpenRead(_newExePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(exeStream)).ToLowerInvariant();

            if (actualHash != expectedHash)
            {
                File.Delete(_newExePath);
                throw new InvalidOperationException("SHA256 검증 실패");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // SHA256 fetch/parse failed — continue without verification
            // (old releases before sha256 support)
        }
    }

    private async Task CopyNewExe()
    {
        var destPath = Path.Combine(_targetDir, "ClaudeUsageTray.exe");

        int maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (File.Exists(destPath))
                    File.Delete(destPath);

                File.Copy(_newExePath, destPath);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                await Task.Delay(1000);
            }
        }

        throw new IOException("파일을 복사할 수 없습니다.");
    }

    private async Task StartNewProcess()
    {
        var startPath = Path.Combine(_targetDir, "ClaudeUsageTray.exe");

        if (!File.Exists(startPath))
            throw new FileNotFoundException("대상 프로그램을 찾을 수 없습니다.");

        Process.Start(new ProcessStartInfo(startPath)
        {
            UseShellExecute = true
        });

        await Task.Delay(500);
    }

    private void UpdateStatus(string message, int percent)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
            ProgressBar.Value = percent;
            PercentText.Text = $"{percent}%";
        });
    }
}