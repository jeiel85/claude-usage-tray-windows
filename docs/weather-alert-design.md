# 날씨 알림 및 트레이 날씨 정보 표시 설계

- GitHub Issue: [#63](https://github.com/jeiel85/claude-usage-tray-windows/issues/63)
- 작성일: 2026-05-14
- 분류: 기능
- 상태: 구현 전 설계

## 1. 목표

Claude Usage Tray에 날씨 정보를 부가 기능으로 추가한다. 사용자는 설정 창에서 날씨 알림을 켜고 끌 수 있으며, 위치를 지정하거나 Windows 위치 권한을 통해 현재 위치를 사용할 수 있다. 앱은 현재 기온과 기상 상태를 트레이 툴팁에 표시하고, 일기예보 알림과 기상 특보/속보 알림을 기존 Windows 알림 및 ntfy 발송 경로로 함께 전송한다.

## 2. 현재 코드 구조 분석

### 설정 저장 흐름

- `ClaudeUsageTray/Models/NotificationSettings.cs`
  - 현재 알림, ntfy, 언어, 트레이 표시 옵션이 한 모델에 저장된다.
  - 설정 파일은 `SettingsService`가 사용자 홈 기준 `%USERPROFILE%\.claude\claude-usage-tray.json`에 JSON으로 저장한다.
  - 새 날씨 설정도 이 모델에 추가하는 것이 기존 패턴과 가장 잘 맞는다.

- `ClaudeUsageTray/Services/SettingsService.cs`
  - `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`를 이미 사용하고 있어 프로젝트의 하드코딩 금지 규칙과 맞다.
  - 날씨 캐시 파일이 필요하다면 동일한 방식으로 사용자 홈 아래 `.claude` 경로를 동적으로 구성한다.

- `ClaudeUsageTray/ViewModels/MainViewModel.cs`
  - `LoadSettings()`와 `SaveSettings()`가 설정 모델과 ViewModel 속성을 동기화한다.
  - 날씨 설정 속성, 현재 날씨 상태, 알림 중복 방지 상태를 ViewModel에 추가하면 UI 바인딩과 트레이 갱신이 자연스럽다.

### 알림 흐름

- `ClaudeUsageTray/Services/NotificationService.cs`
  - Windows balloon 알림과 ntfy 발송이 한 서비스에 모여 있다.
  - `ShowUsageAlert`, `ShowRateLimitAlert`, `ShowQuotaResetAlert`, `ShowTestAlertAsync`가 모두 `ShowBalloon()` + `SendNtfy()` 구조를 공유한다.
  - 날씨 알림은 `ShowWeatherForecastAlert(...)`, `ShowWeatherWarningAlert(...)` 같은 메서드를 추가하거나, 더 일반적인 `ShowAlert(title, body, ntfyTopic, priority, tags)` 메서드를 내부 공통 API로 추가하는 방식이 적합하다.

### 트레이 툴팁 흐름

- `ClaudeUsageTray/App.xaml.cs`
  - `OnVmIconPropertyChanged()`에서 `_trayIcon.Text`를 구성한다.
  - WinForms `NotifyIcon.Text`는 63자 제한이 있어 현재 코드도 잘라내고 있다.
  - 날씨 정보를 추가할 경우 반드시 짧은 문자열로 제한해야 한다. 예: `Seoul 22°C Clear`.
  - 날씨가 길어질 수 있으므로 툴팁에는 위치명, 현재기온, 상태만 넣고 상세 예보는 팝업/알림으로 분리한다.

### 설정 UI 흐름

- `ClaudeUsageTray/Views/SettingsWindow.xaml`
  - 현재 탭은 일반, 트레이, 알림, ntfy로 구성되어 있다.
  - 날씨는 기존 알림 탭에 섞기보다 새 `날씨` 탭을 추가하는 편이 설정 밀도와 유지보수성이 좋다.

- `ClaudeUsageTray/Views/SettingsWindow.xaml.cs`
  - `ApplyLocalization()`, `LoadValues()`, `Setting_Changed()`에 날씨 항목을 추가한다.
  - ntfy 토픽 검증은 이미 `20자 이상`과 허용 문자 검사를 하고 있으므로 재사용한다.

### 다국어

- `ClaudeUsageTray/Services/LocalizationService.cs`
  - 새 UI 문자열과 알림 문구는 ko, zh, ja, en 네 언어 모두 추가해야 한다.
  - 기존 `Loc.Name` 패턴을 유지한다.

## 3. 외부 데이터 소스 검토

### 기본 날씨/예보: Open-Meteo

Open-Meteo Weather Forecast API는 좌표 기반 `/v1/forecast` JSON API를 제공하고, `current` 파라미터로 현재 기온, 체감온도, 강수, 날씨 코드, 풍속 등을 받을 수 있다. 공식 문서 기준 현재 조건은 15분 단위 모델 데이터이며, KMA Korea 모델도 데이터 소스 목록에 포함된다.

- 문서: [Open-Meteo Forecast API](https://open-meteo.com/en/docs)
- 주요 파라미터:
  - `latitude`, `longitude`
  - `current=temperature_2m,apparent_temperature,weather_code,precipitation,wind_speed_10m`
  - `hourly=temperature_2m,precipitation_probability,weather_code`
  - `daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max`
  - `timezone=auto`
  - `forecast_days=1` 또는 `forecast_days=3`

장점:
- API 키 없이 시작 가능하다.
- 전 세계 좌표 기반 조회가 가능하다.
- 현재 날씨와 단기/일간 예보를 한 번에 가져올 수 있다.

주의:
- 공식 기상청 특보 원문이 아니라 모델 기반 예보다.
- API 응답의 `weather_code`를 앱 내부에서 다국어 문구로 매핑해야 한다.

### 위치 검색: Open-Meteo Geocoding

Open-Meteo Geocoding API는 도시명 또는 우편번호 검색을 지원하고, 응답에 위도/경도/시간대/국가/행정구역 정보를 포함한다.

- 문서: [Open-Meteo Geocoding API](https://open-meteo.com/en/docs/geocoding-api)
- 엔드포인트:
  - `https://geocoding-api.open-meteo.com/v1/search?name={query}&count=5&language={lang}`

권장:
- 1차 구현은 수동 위치 검색을 기본으로 한다.
- 사용자가 선택한 위치의 `name`, `admin1`, `country_code`, `latitude`, `longitude`, `timezone`을 설정 파일에 저장한다.

### 현재 위치: Windows 위치 API

Windows `Windows.Devices.Geolocation.Geolocator`는 현재 위치 조회 API를 제공하지만, 위치 권한이 필요하고 `RequestAccessAsync()`는 UI 스레드와 foreground 상태에서 호출해야 한다.

- 문서: [Microsoft Geolocator](https://learn.microsoft.com/en-us/uwp/api/windows.devices.geolocation.geolocator)
- 문서: [Geolocator.RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.geolocation.geolocator.requestaccessasync)

권장:
- v1에서는 `수동 위치`를 기본값으로 구현한다.
- `현재 위치 사용` 버튼은 별도 후속 작업으로 구현 가능하게 설계만 열어둔다.
- 현재 위치 기능을 넣는 경우 `Microsoft.Windows.SDK.Contracts` 패키지 검토가 필요하다.

### 기상 특보/속보

미국 지역은 National Weather Service API의 active alert endpoint가 좌표 기반 특보 조회를 지원한다.

- 문서: [NWS Alerts Web Service](https://www.weather.gov/documentation/services-web-alerts)
- 엔드포인트 예:
  - `https://api.weather.gov/alerts/active?point={lat},{lon}`

NWS 공식 문서는 30초보다 잦은 요청을 피하라고 권장한다. 이 앱의 기본 폴링은 2분이므로 기본 사용량에서는 무리가 없지만, 날씨 알림은 별도 최소 10분 간격으로 두는 것이 좋다.

미국 외 지역은 무료/무키 글로벌 공식 특보 API가 일관적이지 않다. 따라서 1차 구현은 아래처럼 분리한다.

- 전 세계: Open-Meteo 예보 기반 조건 알림
  - 비/눈 가능성, 급격한 기온 변화, 강풍, 폭염/한파 등
- 미국: NWS active alerts 기반 공식 특보 알림
- 한국/일본/기타 국가 공식 특보: 후속 provider로 확장
  - 한국 기상청 특보 원문은 공공데이터포털 키가 필요할 가능성이 높으므로 v1 기본 구현 범위에서는 제외한다.

## 4. 기능 범위

### v1 필수 범위

- 설정 창에 `날씨` 탭 추가
- 날씨 알림 전체 on/off
- 수동 위치 검색 및 선택
- 현재 날씨 조회
- 트레이 툴팁에 `위치명 + 현재기온 + 기상 상태` 표시
- 일일 예보 알림
  - 예: 매일 오전 7시 30분에 오늘 최저/최고, 강수확률, 대표 상태 알림
- 조건 기반 예보 알림
  - 비/눈 가능성 높음
  - 강풍
  - 폭염/한파
- 미국 좌표일 때 NWS active alerts 조회 및 새 alert 발송
- 기존 ntfy 토픽과 `NtfySendFromThisPc` 정책 재사용
- 중복 알림 방지

### v1 제외 또는 후속 범위

- 지도 UI
- 레이더/위성 이미지
- 백그라운드 geofencing
- 공식 한국 기상청 특보 API 연동
- OpenWeather 등 유료/키 기반 provider
- 독립적인 날씨 전용 ntfy 토픽

## 5. 사용자 설정 설계

`NotificationSettings`에 아래 필드를 추가한다.

```csharp
public bool WeatherEnabled { get; set; } = false;
public bool WeatherShowInTrayTooltip { get; set; } = true;
public string WeatherLocationMode { get; set; } = "manual"; // manual, windows
public string WeatherLocationName { get; set; } = "";
public string WeatherCountryCode { get; set; } = "";
public double? WeatherLatitude { get; set; }
public double? WeatherLongitude { get; set; }
public string WeatherTimezone { get; set; } = "auto";
public int WeatherRefreshIntervalMinutes { get; set; } = 30;
public bool WeatherDailyForecastEnabled { get; set; } = true;
public string WeatherDailyForecastTime { get; set; } = "07:30";
public bool WeatherConditionAlertsEnabled { get; set; } = true;
public int WeatherRainProbabilityThreshold { get; set; } = 70;
public double WeatherHighTemperatureThresholdC { get; set; } = 33;
public double WeatherLowTemperatureThresholdC { get; set; } = -10;
public double WeatherWindSpeedThresholdKmh { get; set; } = 50;
public bool WeatherOfficialAlertsEnabled { get; set; } = true;
```

주의:
- 기존 설정 JSON과 하위 호환되도록 nullable 좌표를 사용한다.
- `WeatherEnabled=false`가 기본값이어야 기존 사용자에게 새 네트워크 호출이 갑자기 추가되지 않는다.
- 온도 단위는 v1에서 섭씨 고정으로 시작한다. 화씨 지원은 후속으로 분리한다.

## 6. 신규 모델 설계

파일 추가:

- `ClaudeUsageTray/Models/WeatherModels.cs`

주요 타입:

```csharp
public sealed record WeatherLocation(
    string Name,
    string CountryCode,
    string? Admin1,
    double Latitude,
    double Longitude,
    string Timezone);

public sealed record CurrentWeatherSnapshot(
    WeatherLocation Location,
    DateTimeOffset UpdatedAt,
    double TemperatureC,
    double? ApparentTemperatureC,
    int WeatherCode,
    string ConditionKey,
    double? PrecipitationMm,
    double? WindSpeedKmh);

public sealed record DailyWeatherForecast(
    DateOnly Date,
    int WeatherCode,
    double? MinTemperatureC,
    double? MaxTemperatureC,
    int? PrecipitationProbabilityMax);

public sealed record WeatherAlertItem(
    string Source,
    string Id,
    string Event,
    string Severity,
    string Headline,
    DateTimeOffset? Effective,
    DateTimeOffset? Expires);
```

`ConditionKey`는 `clear`, `partly_cloudy`, `rain`, `snow`, `thunderstorm`, `fog`, `unknown`처럼 안정적인 키로 저장하고, 표시 문구는 `LocalizationService`에서 처리한다.

## 7. 신규 서비스 설계

### WeatherService

파일 추가:

- `ClaudeUsageTray/Services/WeatherService.cs`

책임:
- 위치 검색
- 현재 날씨/예보 조회
- WMO weather code를 내부 condition key로 변환
- HTTP timeout과 예외 처리

메서드:

```csharp
public Task<IReadOnlyList<WeatherLocation>> SearchLocationsAsync(string query, string language);
public Task<WeatherReport> GetForecastAsync(WeatherLocation location, CancellationToken ct = default);
```

구현 메모:
- `HttpClient`는 static 재사용
- timeout은 `AppConstants.ApiTimeoutSeconds` 또는 별도 `WeatherTimeoutSeconds = 10`
- API 실패 시 ViewModel은 기존 날씨 데이터를 유지하고 `WeatherHasError=true`로 표시

### WeatherAlertService

파일 추가:

- `ClaudeUsageTray/Services/WeatherAlertService.cs`

책임:
- 조건 기반 예보 알림 판정
- 공식 특보 provider 조회
- 알림 중복 방지 키 생성

중복 방지:
- 설정 파일에 마지막 발송 key들을 저장하기보다 별도 캐시 파일 권장
- 경로: `%USERPROFILE%\.claude\claude-usage-tray-weather-alerts.json`
- 보관: 최근 7일
- 키 예:
  - `daily:{yyyyMMdd}:{lat:F2}:{lon:F2}`
  - `condition:rain:{yyyyMMddHH}:{threshold}:{lat:F2}:{lon:F2}`
  - `nws:{alertId}`

### IWeatherWarningProvider

파일 추가:

- `ClaudeUsageTray/Services/WeatherWarnings/IWeatherWarningProvider.cs`
- `ClaudeUsageTray/Services/WeatherWarnings/NwsWeatherWarningProvider.cs`

인터페이스:

```csharp
public interface IWeatherWarningProvider
{
    bool Supports(WeatherLocation location);
    Task<IReadOnlyList<WeatherAlertItem>> GetActiveAlertsAsync(WeatherLocation location, CancellationToken ct = default);
}
```

NWS provider:
- `Supports()`는 `CountryCode == "US"`일 때 true
- endpoint: `https://api.weather.gov/alerts/active?point={lat},{lon}`
- User-Agent 헤더는 명시한다. 예: `ClaudeUsageTray/{version} (https://github.com/jeiel85/claude-usage-tray-windows)`

## 8. ViewModel 통합 설계

`MainViewModel`에 추가할 상태:

```csharp
[ObservableProperty] private bool _weatherEnabled;
[ObservableProperty] private bool _weatherShowInTrayTooltip = true;
[ObservableProperty] private string _weatherLocationName = "";
[ObservableProperty] private string _weatherCountryCode = "";
[ObservableProperty] private double? _weatherLatitude;
[ObservableProperty] private double? _weatherLongitude;
[ObservableProperty] private string _weatherStatusLabel = "";
[ObservableProperty] private string _weatherTooltipLabel = "";
[ObservableProperty] private bool _weatherHasError;
[ObservableProperty] private string _weatherErrorMessage = "";
```

`RefreshAsync()` 병렬 작업에 `RefreshWeatherInternalAsync()`를 추가한다.

```csharp
var tasks = new List<Task>
{
    RefreshClaudeAsync(),
    RefreshCodexInternalAsync(),
    RefreshGeminiCliInternalAsync(),
    RefreshOpenCodeInternalAsync()
};

if (WeatherEnabled)
    tasks.Add(RefreshWeatherInternalAsync());
```

주의:
- 날씨 refresh interval은 사용량 polling과 다르게 더 길어야 한다.
- `RefreshWeatherInternalAsync()` 내부에서 마지막 조회 시각을 검사해 `WeatherRefreshIntervalMinutes` 전에는 API 호출을 건너뛴다.
- 트레이 툴팁 갱신을 위해 `WeatherTooltipLabel`, `WeatherHasError`, `WeatherShowInTrayTooltip` 변경 시 `App.xaml.cs`의 property changed 조건에 포함한다.

## 9. 트레이 툴팁 설계

현재 `NotifyIcon.Text` 63자 제한 때문에 긴 문장을 넣을 수 없다.

권장 포맷:

```text
Claude Usage Tray
*Claude 42% · Codex 10%
Seoul 22°C Clear
```

실제 적용:
- 63자 초과 시 기존처럼 truncate
- 날씨 줄은 `WeatherTooltipLabel`이 비어 있지 않을 때만 추가
- 예: `Seoul 22°C Rain`
- 한국어일 때도 짧게: `서울 22°C 비`

`App.xaml.cs` 변경 지점:
- `OnVmIconPropertyChanged()`의 property name 목록에 날씨 속성 추가
- tooltip body 구성 후 날씨 줄 append

## 10. 설정 UI 설계

`SettingsWindow.xaml`에 `TabWeather` 추가:

필드:
- `ChkWeatherEnabled`: 날씨 알림 사용
- `ChkWeatherShowInTray`: 트레이 툴팁에 날씨 표시
- `TxtWeatherLocationSearch`: 도시/우편번호 검색
- `BtnWeatherLocationSearch`: 검색
- `CmbWeatherLocationResults`: 검색 결과 선택
- `BtnWeatherUseWindowsLocation`: 현재 위치 사용, v1에서는 비활성 또는 experimental 표시 가능
- `ChkWeatherDailyForecast`: 매일 예보 알림
- `TxtWeatherDailyTime` 또는 ComboBox: 알림 시각
- `ChkWeatherConditionAlerts`: 조건 기반 알림
- 강수확률/고온/저온/강풍 threshold numeric inputs
- `ChkWeatherOfficialAlerts`: 공식 특보 알림, 미국 좌표에서만 동작한다는 hint

코드비하인드:
- `ApplyLocalization()`에 모든 라벨 추가
- `LoadValues()`에 설정 로딩 추가
- `Setting_Changed()`에 설정 저장 추가
- 검색 버튼은 비동기 이벤트로 `WeatherService.SearchLocationsAsync()` 호출

## 11. 알림 설계

### 매일 예보 알림

발송 조건:
- `WeatherEnabled && WeatherDailyForecastEnabled`
- 현재 로컬 시간이 설정 시각 이후
- 오늘 daily key를 아직 보내지 않음

예시 문구:

- 제목: `오늘의 날씨`
- 본문: `서울: 맑음, 18~27°C, 강수확률 20%`

### 조건 기반 알림

발송 조건:
- `WeatherConditionAlertsEnabled`
- 다음 12시간 또는 오늘 예보에서 threshold 초과
- 같은 조건 key를 최근 6시간 내 보내지 않음

예시:
- 비: `서울: 오늘 강수확률 80%`
- 눈: `서울: 눈 예보가 있습니다`
- 폭염: `서울: 최고 34°C 예보`
- 한파: `서울: 최저 -12°C 예보`
- 강풍: `서울: 최대 풍속 55 km/h 예보`

### 공식 특보/속보

발송 조건:
- `WeatherOfficialAlertsEnabled`
- provider가 위치를 지원
- active alert 중 새 `Id` 발견

예시:
- 제목: `기상 특보`
- 본문: `{Event} · {Severity}\n{Headline}`

ntfy:
- 기존 topic과 `NtfySendFromThisPc`를 재사용한다.
- weather warning은 priority 4 또는 5, forecast는 priority 3 권장.
- tags는 forecast `sunny`/`umbrella`, warning `warning` 등으로 분리 가능하다.

## 12. 로컬라이제이션 키

`LocalizationService.cs`에 아래 키를 4개 언어 모두 추가한다.

- `WeatherTab`
- `WeatherSettingsTitle`
- `WeatherEnabled`
- `WeatherShowInTrayTooltip`
- `WeatherLocation`
- `WeatherSearchPlaceholder`
- `WeatherSearch`
- `WeatherUseCurrentLocation`
- `WeatherDailyForecast`
- `WeatherDailyForecastTime`
- `WeatherConditionAlerts`
- `WeatherRainProbabilityThreshold`
- `WeatherHighTemperatureThreshold`
- `WeatherLowTemperatureThreshold`
- `WeatherWindSpeedThreshold`
- `WeatherOfficialAlerts`
- `WeatherOfficialAlertsHint`
- `WeatherSearchNoResults`
- `WeatherSearchFailed`
- `WeatherCurrentUnavailable`
- `WeatherForecastTitle`
- `WeatherWarningTitle`
- WMO 상태 키: `WeatherClear`, `WeatherPartlyCloudy`, `WeatherCloudy`, `WeatherFog`, `WeatherDrizzle`, `WeatherRain`, `WeatherSnow`, `WeatherThunderstorm`, `WeatherUnknown`

## 13. 구현 순서

1. README 이슈와 GitHub Issue 확인
2. `NotificationSettings`에 날씨 설정 필드 추가
3. `WeatherModels.cs` 추가
4. `WeatherService.cs` 추가
5. `WeatherAlertService.cs`와 NWS provider 추가
6. `NotificationService`에 일반 알림/날씨 알림 메서드 추가
7. `MainViewModel`에 날씨 설정/상태/refresh 통합
8. `SettingsWindow.xaml`에 날씨 탭 추가
9. `SettingsWindow.xaml.cs`에 localization/load/save/search 이벤트 연결
10. `App.xaml.cs` 트레이 툴팁에 날씨 줄 추가
11. `LocalizationService.cs` 4개 언어 문자열 추가
12. 단위 테스트 또는 최소 서비스 파싱 테스트 추가
13. `dotnet build`와 수동 실행 검증

## 14. 테스트 계획

### 자동 테스트

- Open-Meteo forecast JSON parsing
- weather code mapping
- geocoding search JSON parsing
- 조건 기반 알림 중복 key 생성
- NWS alert JSON parsing

### 수동 테스트

- 설정 창에서 날씨 탭 표시
- 도시 검색 후 저장
- 앱 재시작 후 위치 유지
- 날씨 enabled off일 때 네트워크 호출 없음
- 트레이 툴팁에 짧은 날씨 표시
- ntfy topic이 없으면 Windows 알림만 발송
- `NtfySendFromThisPc=false`이면 ntfy 발송 생략
- 미국 위치에서 NWS alert endpoint 호출 및 alert 없음/있음 처리
- 네트워크 실패 시 앱 전체 refresh 실패로 번지지 않음

## 15. 위험과 대응

- `NotifyIcon.Text` 63자 제한
  - 짧은 `WeatherTooltipLabel`만 사용하고 초과 시 truncate한다.

- 외부 API 장애
  - 날씨 실패는 `WeatherHasError`로만 표시하고 Claude/Codex/Gemini/OpenCode refresh에는 영향을 주지 않는다.

- 위치 권한 UX
  - v1은 수동 위치를 기본으로 한다. Windows 현재 위치는 후속 또는 experimental로 둔다.

- 공식 특보의 국가별 차이
  - provider 인터페이스로 분리하고 v1은 NWS만 공식 특보로 지원한다. 다른 국가는 예보 기반 조건 알림으로 대체한다.

- 알림 과다 발송
  - daily/condition/warning별 dedupe key와 cooldown을 둔다.

- 다국어 누락
  - 새 문자열은 반드시 ko, zh, ja, en 네 언어를 동시에 추가한다.

## 16. 다음 세션 시작 프롬프트

아래 프롬프트를 다음 세션 첫 메시지로 사용한다.

```text
D:\Project\claude-usage-tray-windows 에서 GitHub Issue #63 "날씨 알림 및 트레이 날씨 정보 표시 설계" 구현을 시작해줘.

반드시 먼저 AGENTS.md 규칙대로 `git fetch origin && git pull origin master && git status`를 실행하고, README.md 이슈 섹션에 #63이 등록되어 있는지 확인해줘. 기존 사용자 변경 사항이 있으면 되돌리지 말고 피해서 작업해줘.

설계 문서는 `docs/weather-alert-design.md`에 있다. v1 범위는 다음으로 잡아줘:
- 설정 창에 날씨 탭 추가
- 수동 위치 검색(Open-Meteo Geocoding)과 선택 위치 저장
- Open-Meteo Forecast API로 현재 날씨와 예보 조회
- 트레이 툴팁에 위치명, 현재기온, 기상 상태 표시
- 매일 예보 알림과 조건 기반 알림을 기존 Windows 알림 + ntfy 경로로 발송
- 미국 위치일 때 NWS active alerts를 공식 특보 provider로 조회
- 중복 알림 방지
- LocalizationService.cs에 ko, zh, ja, en 문자열 모두 추가

구현 후 `dotnet build`를 실행하고, 가능하면 날씨 서비스 파싱/알림 판정 테스트를 추가해줘. 기능 구현이 끝나 master에 푸시하는 경우 프로젝트 규칙에 따라 버전 bump, CHANGELOG 갱신, 태그 push, GitHub Actions/Release 자산 확인까지 완료해야 해.
```

