using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// Claude 모델 한정 한도(#187)를 응답에서 골라내는 규칙. 응답 모양은 공개 클라이언트가 남긴 실측 예시를 그대로 옮겼다
/// (steipete/CodexBar ClaudeOAuthTests 의 2026-07-03 Fable 프로모션 응답, jens-duttke/usage-monitor-for-claude
/// docs/api-reference.md 의 2026-09 응답 — 둘 다 익명화된 값).
/// </summary>
public sealed class ClaudeModelLimitsTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 10, 12, 2, 0, 0, TimeSpan.Zero);

    // UsageApiService 와 같은 옵션으로 읽는다 — 실제 조회 경로와 같은 역직렬화를 거쳐야 의미가 있다.
    private static UsageResponse Parse(string json) =>
        JsonSerializer.Deserialize<UsageResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void Read_FablePromotionShape_SurfacesTheScopedWeeklyLimitOnly()
    {
        var usage = Parse("""
            {
              "five_hour": { "utilization": 11.0, "resets_at": "2026-07-03T00:30:00.282668+00:00" },
              "seven_day": { "utilization": 9.0, "resets_at": "2026-07-08T09:00:00.282694+00:00" },
              "seven_day_opus": null,
              "seven_day_sonnet": null,
              "limits": [
                { "kind": "session", "group": "session", "percent": 11,
                  "resets_at": "2026-07-03T00:30:00.282668+00:00", "scope": null, "is_active": true },
                { "kind": "weekly_all", "group": "weekly", "percent": 9,
                  "resets_at": "2026-07-08T09:00:00.282694+00:00", "scope": null, "is_active": false },
                { "kind": "weekly_scoped", "group": "weekly", "percent": 5,
                  "resets_at": "2026-07-08T09:00:00.283070+00:00",
                  "scope": { "model": { "id": null, "display_name": "Fable" }, "surface": null },
                  "is_active": false }
              ]
            }
            """);

        var window = Assert.Single(ClaudeModelLimits.Read(usage, ObservedAt));

        // 기본 5시간·7일(session / weekly_all)은 이미 게이지가 있으니 빼고, is_active=false 여도 모델 한도는 남긴다.
        Assert.Equal("Fable", window.LimitName);
        Assert.Equal("weekly:fable", window.LimitId);
        Assert.Equal(0.05, window.UsagePercent, 6);
        Assert.Equal(10080, window.WindowMinutes);
        Assert.Equal(DateTimeOffset.Parse("2026-07-08T09:00:00.283070+00:00"), window.ResetAt);
        Assert.Equal(ObservedAt, window.ObservedAt);
        // 기본 창 파싱은 그대로여야 한다.
        Assert.Equal(11.0, usage.FiveHour!.Utilization);
    }

    // 2026-09 실측: Sonnet 한도가 limits[] 에 대응 항목 없이 옛 필드로만 왔다 — 옛 필드를 버리면 이 한도가 사라진다.
    [Fact]
    public void Read_SonnetOnlyInLegacyField_IsStillShown()
    {
        var usage = Parse("""
            {
              "five_hour": { "utilization": 48.0, "resets_at": "2026-09-05T12:59:59.966454+00:00" },
              "seven_day": { "utilization": 64.0, "resets_at": "2026-09-11T20:59:59.966479+00:00" },
              "seven_day_opus": null,
              "seven_day_sonnet": { "utilization": 2.0, "resets_at": "2026-09-11T20:59:59.966479+00:00" },
              "limits": [
                { "kind": "session", "group": "session", "percent": 48, "severity": "normal",
                  "resets_at": "2026-09-05T12:59:59.966454+00:00", "scope": null, "is_active": true },
                { "kind": "weekly_all", "group": "weekly", "percent": 64, "severity": "normal",
                  "resets_at": "2026-09-11T20:59:59.966479+00:00", "scope": null, "is_active": false }
              ]
            }
            """);

        var window = Assert.Single(ClaudeModelLimits.Read(usage, ObservedAt));

        Assert.Equal("Sonnet", window.LimitName);
        Assert.Equal("seven_day_sonnet", window.LimitId);
        Assert.Equal(0.02, window.UsagePercent, 6);
        Assert.Equal(10080, window.WindowMinutes);
        Assert.NotNull(window.ResetAt);
    }

    // 같은 모델이 두 곳에 다 오면 한 줄만 — 서버 표시 이름이 있는 limits[] 쪽을 쓴다.
    // 표시 이름이 "Sonnet 4.6" · "Claude Sonnet" 처럼 길어도 같은 모델로 본다.
    [Theory]
    [InlineData("Sonnet")]
    [InlineData("Sonnet 4.6")]
    [InlineData("Claude Sonnet")]
    public void Read_SameModelInBothPlaces_IsShownOnce(string displayName)
    {
        var usage = Parse($$"""
            {
              "seven_day": { "utilization": 30.0, "resets_at": "2026-10-15T09:00:00+00:00" },
              "seven_day_sonnet": { "utilization": 55.0, "resets_at": "2026-10-15T09:00:00+00:00" },
              "limits": [
                { "kind": "weekly_scoped", "group": "weekly", "percent": 50, "resets_at": "2026-10-15T09:00:00+00:00",
                  "scope": { "model": { "id": null, "display_name": "{{displayName}}" } } }
              ]
            }
            """);

        var window = Assert.Single(ClaudeModelLimits.Read(usage, ObservedAt));

        Assert.Equal(displayName, window.LimitName);
        Assert.Equal(0.5, window.UsagePercent, 6);
    }

    // 리뷰 지적 재현: group 이 빠져 와도 kind(weekly_scoped)로 주간 창을 알고, 옛 필드와 두 줄로 그리지 않는다.
    // kind 로도 창을 모르는 항목이어도 같은 모델이면 같은 한도로 본다.
    [Theory]
    [InlineData("\"kind\": \"weekly_scoped\",", 10080)]
    [InlineData("\"kind\": \"scoped\",", null)]
    public void Read_ScopedLimitWithoutGroup_StillDedupesTheLegacyField(string kindJson, int? expectedWindow)
    {
        var usage = Parse($$"""
            {
              "seven_day_sonnet": { "utilization": 55.0, "resets_at": "2026-10-15T09:00:00+00:00" },
              "limits": [
                { {{kindJson}} "percent": 50, "resets_at": "2026-10-15T09:00:00+00:00",
                  "scope": { "model": { "display_name": "Sonnet" } } }
              ]
            }
            """);

        var window = Assert.Single(ClaudeModelLimits.Read(usage, ObservedAt));

        Assert.Equal(expectedWindow, window.WindowMinutes);
        Assert.Equal(0.5, window.UsagePercent, 6);
    }

    // 다른 모델이면 둘 다 — Opus 옛 필드와 Fable 범위 한도는 서로 다른 한도다.
    [Fact]
    public void Read_DifferentModels_AreAllShown_ScopedFirst()
    {
        var usage = Parse("""
            {
              "seven_day_opus": { "utilization": 12.0, "resets_at": "2026-10-15T09:00:00+00:00" },
              "limits": [
                { "kind": "weekly_scoped", "group": "weekly", "percent": 29, "resets_at": "2026-10-15T09:00:00+00:00",
                  "scope": { "model": { "id": null, "display_name": "Fable" } } }
              ]
            }
            """);

        var windows = ClaudeModelLimits.Read(usage, ObservedAt);

        Assert.Equal(["Fable", "Opus"], windows.Select(window => window.LimitName));
    }

    // 범위가 없거나, 모델 이름이 없거나, "All models"(기본 주간과 같은 한도)면 모델 한도가 아니다.
    // 같은 모델이 두 번 와도 한 번만.
    [Fact]
    public void Read_SkipsEntriesThatAreNotModelLimits_AndDuplicates()
    {
        var usage = Parse("""
            {
              "limits": [
                { "kind": "weekly_scoped", "group": "weekly", "percent": 5, "scope": { "model": null, "surface": null } },
                { "kind": "weekly_scoped", "group": "weekly", "percent": 5, "scope": null },
                { "kind": "weekly_scoped", "group": "weekly", "percent": 5, "scope": { "model": { "display_name": "  " } } },
                { "kind": "weekly_scoped", "group": "weekly", "percent": 9, "scope": { "model": { "display_name": "All models" } } },
                { "kind": "weekly_scoped", "group": "weekly", "percent": 9, "scope": { "model": { "id": "claude-all-models", "display_name": "Everything" } } },
                { "kind": "weekly_all", "group": "weekly", "percent": 9, "scope": { "model": { "display_name": "Claude" } } },
                { "kind": "weekly_scoped", "group": "weekly", "percent": 7, "scope": { "model": { "display_name": "Fable" } } },
                { "kind": "weekly_scoped", "group": "weekly", "percent": 8, "scope": { "model": { "display_name": "Fable" } } }
              ]
            }
            """);

        var window = Assert.Single(ClaudeModelLimits.Read(usage, ObservedAt));

        Assert.Equal("Fable", window.LimitName);
        Assert.Equal(0.07, window.UsagePercent, 6);
    }

    // 아직 쓰지 않은 모델 한도는 리셋 창 없이 0% 로 온다 — 한도가 있다는 사실은 보여 준다(시간선은 그리지 않는다).
    // 창 길이를 모르는 group 이면 지어내지 않는다.
    [Fact]
    public void Read_UnusedLimitAndUnknownGroup_KeepWhatTheServerSaid()
    {
        var usage = Parse("""
            {
              "limits": [
                { "kind": "weekly_scoped", "group": "weekly", "percent": 0, "resets_at": null,
                  "scope": { "model": { "display_name": "Fable" } } },
                { "kind": "monthly_scoped", "group": "monthly", "percent": 140, "resets_at": "2026-11-01T00:00:00Z",
                  "scope": { "model": { "display_name": "Opus" } } }
              ]
            }
            """);

        var windows = ClaudeModelLimits.Read(usage, ObservedAt);

        Assert.Equal(2, windows.Count);
        Assert.Null(windows[0].ResetAt);
        Assert.Equal(0, windows[0].UsagePercent);
        Assert.Equal(10080, windows[0].WindowMinutes);

        Assert.Null(windows[1].WindowMinutes);
        Assert.Equal(1.0, windows[1].UsagePercent); // 100% 를 넘겨 와도 게이지는 가득 찬 데서 멈춘다.
    }

    // 문서화되지 않은 필드다 — 모양이 달라져도 기본 5시간·7일 조회까지 함께 실패하면 안 된다.
    [Theory]
    [InlineData("""{ "kind": "weekly_scoped" }""")]
    [InlineData("\"weekly_scoped\"")]
    [InlineData("null")]
    [InlineData("""[ "x", 3, null, { "scope": "Fable" }, { "scope": { "model": "Fable" }, "percent": 5 } ]""")]
    [InlineData("""[ { "kind": "weekly_scoped", "group": "weekly", "percent": { "value": 5 }, "scope": { "model": { "display_name": "Fable" } } } ]""")]
    public void Read_MalformedLimits_DoNotBreakTheBaseWindows(string limitsJson)
    {
        var usage = Parse($$"""
            {
              "five_hour": { "utilization": 48.0, "resets_at": "2026-09-05T12:59:59+00:00" },
              "limits": {{limitsJson}}
            }
            """);

        Assert.Equal(48.0, usage.FiveHour!.Utilization);
        Assert.Empty(ClaudeModelLimits.Read(usage, ObservedAt));
    }

    // 숫자를 문자열로 보내도 읽는다(invariant).
    [Fact]
    public void Read_PercentAsString_IsAccepted()
    {
        var usage = Parse("""
            { "limits": [ { "kind": "weekly_scoped", "group": "weekly", "percent": "30.5",
                            "scope": { "model": { "display_name": "Fable" } } } ] }
            """);

        Assert.Equal(0.305, Assert.Single(ClaudeModelLimits.Read(usage, ObservedAt)).UsagePercent, 6);
    }

    // limits 필드 자체가 없던 옛 응답 — 옛 필드만 본다.
    [Fact]
    public void Read_ResponseWithoutLimits_UsesLegacyFieldsOnly()
    {
        var usage = Parse("""{ "five_hour": { "utilization": 1.0, "resets_at": null } }""");

        Assert.Empty(ClaudeModelLimits.Read(usage, ObservedAt));
    }
}
