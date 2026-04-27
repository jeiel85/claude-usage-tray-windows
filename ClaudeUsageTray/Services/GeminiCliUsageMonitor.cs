using System.IO;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class GeminiCliUsageMonitor
{
    private static readonly string GeminiDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini");

    private static readonly string SessionTmpPath = Path.Combine(GeminiDir, "tmp");

    public ProviderUsageSnapshot GetTodaySnapshot()
    {
        var snapshot = new ProviderUsageSnapshot();

        if (!Directory.Exists(GeminiDir))
        {
            snapshot.ErrorMessage = Loc.GeminiCliSourceNotFound;
            return snapshot;
        }

        if (!Directory.Exists(SessionTmpPath))
        {
            snapshot.ErrorMessage = Loc.GeminiCliSessionPathMissing;
            return snapshot;
        }

        var sessionFiles = Directory.GetFiles(SessionTmpPath, "session-*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(info => info.LastWriteTime.Date == DateTime.Now.Date)
            .ToList();

        snapshot.SessionCount = sessionFiles.Count;

        if (sessionFiles.Count == 0)
        {
            snapshot.ErrorMessage = Loc.GeminiCliNoUsageToday;
            return snapshot;
        }

        snapshot.HasData = true;
        snapshot.IsLimited = true;
        snapshot.ErrorMessage = Loc.GeminiCliEstimateOnly;
        return snapshot;
    }
}
