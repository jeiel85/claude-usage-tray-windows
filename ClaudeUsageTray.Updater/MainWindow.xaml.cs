using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ClaudeUsageTray.Updater;

public partial class MainWindow : Window
{
    // Command line args:
    // arg[0] = new exe path (downloaded update)
    // arg[1] = current exe path (to be replaced)
    // arg[2] = current exe directory

    private string _newExePath = "";
    private string _currentExePath = "";
    private string _targetDir = "";

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        var args = Environment.GetCommandLineArgs();
        if (args.Length < 4)
        {
            MessageBox.Show("업dater가 잘못된 인자로 실행되었습니다.", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        _newExePath = args[1];
        _currentExePath = args[2];
        _targetDir = args[3];

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

            // Phase 2: Verify new exe exists
            UpdateStatus("파일 검증 중...", 30);
            if (!File.Exists(_newExePath))
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("업데이트 파일을 찾을 수 없습니다.", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                });
                return;
            }

            // Phase 3: Copy new exe
            UpdateStatus("파일 복사 중...", 40);
            await CopyNewExe();

            // Phase 4: Start new process
            UpdateStatus("새 프로그램 실행 중...", 80);
            await StartNewProcess();

            // Phase 5: Cleanup
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

            var progress = (int)((i / (double)maxAttempts) * 25);
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

    private async Task CopyNewExe()
    {
        var newFileName = Path.GetFileName(_newExePath);
        var destPath = Path.Combine(_targetDir, newFileName);

        // Use simple file copy with retry
        int maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // Delete existing file first
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
        var exeName = Path.GetFileName(_currentExePath);
        var startPath = Path.Combine(_targetDir, exeName);

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