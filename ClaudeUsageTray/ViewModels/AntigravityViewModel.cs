using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class AntigravityViewModel : ObservableObject
{
    private readonly AntigravityUsageMonitor _monitor;

    [ObservableProperty] private bool _hasData = false;
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _tierName = "";
    [ObservableProperty] private string _paidTierName = "";
    [ObservableProperty] private IReadOnlyList<AntigravityModelRow> _models = System.Array.Empty<AntigravityModelRow>();
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private double _percent = 0.0;

    public AntigravityViewModel(AntigravityUsageMonitor monitor)
    {
        _monitor = monitor;
    }

    public async Task RefreshAsync()
    {
        if (!IsEnabled)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasData = false;
                HasError = false;
                ErrorMessage = "";
                Models = System.Array.Empty<AntigravityModelRow>();
            });
            return;
        }

        AntigravitySnapshot snap;
        try
        {
            snap = await _monitor.GetSnapshotAsync();
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasData = false;
                HasError = true;
                ErrorMessage = ex.Message;
                Models = System.Array.Empty<AntigravityModelRow>();
            });
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!snap.HasData)
            {
                HasData = false;
                bool isInformational =
                    string.Equals(snap.ErrorMessage, "Antigravity not signed in", StringComparison.Ordinal)
                    || string.Equals(snap.ErrorMessage, "no refresh_token in credstore", StringComparison.Ordinal)
                    || string.Equals(snap.ErrorMessage, "Antigravity OAuth client not configured", StringComparison.Ordinal);
                HasError = !isInformational && !string.IsNullOrEmpty(snap.ErrorMessage);
                ErrorMessage = HasError ? (snap.ErrorMessage ?? "") : "";
                Models = System.Array.Empty<AntigravityModelRow>();
                return;
            }

            HasData = true;
            HasError = false;
            ErrorMessage = "";
            TierName = snap.TierName ?? "";
            PaidTierName = snap.PaidTierName ?? "";

            double totalUsed = 0;
            int modelCount = 0;
            var rows = new List<AntigravityModelRow>(snap.Models.Count);
            foreach (var m in snap.Models)
            {
                if (m.ResetTime is null) continue;
                if (m.ModelId.StartsWith("chat_", StringComparison.Ordinal) ||
                    m.ModelId.StartsWith("tab_",  StringComparison.Ordinal))
                    continue;

                double used = Math.Clamp(1.0 - m.RemainingFraction, 0.0, 1.0);
                totalUsed += used;
                modelCount++;

                if (used <= 0) continue;

                rows.Add(new AntigravityModelRow
                {
                    ModelId = m.ModelId,
                    DisplayName = FormatModelName(m.ModelId),
                    UsagePercent = used,
                    UsageLabel = $"{used * 100:0}% used",
                    ResetAtLabel = FormatResetLabel(m.ResetTime),
                });
            }
            rows.Sort((a, b) => b.UsagePercent.CompareTo(a.UsagePercent));
            Models = rows;

            Percent = modelCount > 0 ? totalUsed / modelCount : 0.0;
        });
    }

    internal static string FormatModelName(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return "(unknown)";
        var parts = modelId.Split('-');
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            var p = parts[i];
            if (p.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(p[0]));
            if (p.Length > 1) sb.Append(p[1..]);
        }
        return sb.ToString();
    }

    internal static string FormatResetLabel(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return "";
        var diff = resetAt.Value - DateTimeOffset.Now;
        if (diff.TotalSeconds <= 0) return "";
        string time;
        if (diff.TotalMinutes < 10) time = $"{(int)diff.TotalMinutes}m {diff.Seconds:D2}s";
        else if (diff.TotalHours < 1) time = $"{(int)diff.TotalMinutes}m";
        else if (diff.TotalDays < 1) time = $"{(int)diff.TotalHours}h {diff.Minutes}m";
        else time = $"{(int)diff.TotalDays}d {diff.Hours}h";
        return Loc.ResetsIn(time);
    }
}
