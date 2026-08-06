using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.Forecasts;

/// <summary>
/// 예보 데이터 소스. 특보(<see cref="WeatherWarnings.IWeatherWarningProvider"/>)와 달리
/// 지역 제한이 없어 <c>Supports()</c> 가 없다 — 현재 두 구현 모두 전 세계를 커버한다.
/// </summary>
public interface IForecastProvider
{
    /// <summary>설정 파일에 저장되는 안정적인 식별자. UI 표시는 LocalizationService 가 담당한다.</summary>
    string Id { get; }

    /// <summary>
    /// 예보를 조회한다. 데이터가 없으면 <c>null</c> 을 반환해 상위 오케스트레이터가
    /// 다음 provider 로 폴백하게 한다. 네트워크/파싱 실패는 예외로 던진다.
    /// </summary>
    /// <param name="modelId">
    /// provider 별 예보 모델 식별자. 해당 provider 가 모델 선택을 지원하지 않거나
    /// 값이 비어 있으면 무시한다.
    /// </param>
    Task<WeatherReport?> GetForecastAsync(
        WeatherLocation location, string? modelId, CancellationToken ct = default);
}
