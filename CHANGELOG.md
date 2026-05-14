# Changelog

모든 주요 변경 사항을 이 파일에 기록합니다.
[Keep a Changelog](https://keepachangelog.com/ko/1.0.0/) 형식을 따릅니다.

## [1.29.3] - 2026-05-14

<!-- ko -->
### 개선
- **팝업 날씨 카드 디자인 정리** — 헤더 아래에 떠 있던 한 줄 텍스트를 제거하고, "API 할당량" 섹션과 같은 레벨에 "오늘의 날씨" 섹션을 추가했습니다. 기상 아이콘, 위치, 기온, 상태가 카드 형태로 표시되어 전체 디자인과 톤이 일치합니다.
- **트레이 툴팁에 현재 날씨 노출** — 툴팁 길이 제한을 63자 → 127자로 확장(Vista+ NotifyIcon 한도)하여 에이전트 사용량 뒤에 잘리지 않고 날씨 줄이 표시됩니다. 위치는 짧은 형식(첫 구역명)을 사용합니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Popup weather card redesign** — Replaced the floating single-line text under the header with a proper "Today's Weather" section at the same level as "API Quota". The card now shows a condition icon, short location, temperature, and condition label in a layout consistent with the rest of the popup.
- **Current weather in tray tooltip** — Bumped the tooltip cap from 63 → 127 chars (Vista+ NotifyIcon limit) so the weather line no longer gets truncated after the agent usage summary. The tooltip uses a short location form (first segment) to stay readable.
<!-- /en -->

## [1.29.2] - 2026-05-14

<!-- ko -->
### 추가
- **사용량 팝업에 현재 날씨 표시** — 날씨가 활성화되고 위치가 설정된 경우, 팝업 헤더 아래에 현재 기온과 기상 상태를 표시합니다 (`📍 Seoul 22°C Clear`).
### 개선
- **위치 검색 UX 개선** — 검색 버튼과 현재 위치(📍) 버튼을 검색창과 한 줄로 배치하여 공간을 절약하고 직관성을 높였습니다.
<!-- /ko -->

<!-- en -->
### Added
- **Weather display in usage popup** — When weather is enabled and location is set, shows current temperature and condition below the popup header (`📍 Seoul 22°C Clear`).
### Improved
- **Location search UX** — Search button and current-location (📍) button now sit in a single row with the search box for a cleaner, more compact layout.
<!-- /en -->

## [1.29.1] - 2026-05-14

<!-- ko -->
### 개선
- **날씨 탭 레이아웃 개선** — 탭 최대 높이를 720px로 확장하고 내부에 ScrollViewer를 추가하여 콘텐츠가 짤리지 않도록 수정했습니다.
- **현재 위치 사용 버튼 추가** — Windows Geolocator API로 현재 위치를 가져오고, Nominatim reverse geocoding으로 주소명을 조회하여 날씨 위치로 지정할 수 있습니다.
- TargetFramework를 `net9.0-windows` → `net9.0-windows10.0.17763.0`으로 변경 (Geolocator API 지원)
<!-- /ko -->

<!-- en -->
### Improved
- **Weather tab layout** — Increased max height to 720px and added ScrollViewer to prevent content clipping.
- **Use Current Location button** — Fetches current position via Windows Geolocator API and resolves address name using Nominatim reverse geocoding.
- Changed TargetFramework from `net9.0-windows` to `net9.0-windows10.0.17763.0` for Geolocator API support.
<!-- /en -->

## [1.29.0] - 2026-05-14

<!-- ko -->
### 추가
- **날씨 알림 및 트레이 날씨 정보 표시 (#63)** — 설정 창에 날씨 탭을 추가하고, Open-Meteo Geocoding으로 수동 위치 검색을 지원합니다.
  - Open-Meteo Forecast API로 현재 날씨(기온, 체감온도, 기상 상태, 풍속)와 3일 예보를 조회합니다.
  - 트레이 툴팁에 위치명, 현재기온, 기상 상태를 표시합니다 (`Seoul 22°C 맑음`).
  - 매일 오전 지정 시각에 일일 예보 알림을 Windows 알림 + ntfy 경로로 발송합니다.
  - 조건 기반 알림: 강수확률, 폭염, 한파, 강풍 임계값 초과 시 알림을 발송합니다.
  - 미국 위치에서 NWS(미국 기상청) 공식 기상 특보를 조회하여 새 특보 발생 시 알림을 발송합니다.
  - 중복 알림 방지를 위한 dedup 키 캐시를 포함합니다.
  - 다국어(ko, zh, ja, en) 35개 날씨 문자열을 추가했습니다.
<!-- /ko -->

<!-- en -->
### Added
- **Weather alerts and tray weather display (#63)** — Added a Weather tab in Settings with manual location search via Open-Meteo Geocoding.
  - Fetches current weather (temperature, feels-like, condition, wind) and 3-day forecast from Open-Meteo Forecast API.
  - Displays location name, temperature, and weather condition in tray tooltip (`Seoul 22°C Clear`).
  - Sends daily forecast alerts at a user-configurable time via Windows notifications + ntfy.
  - Condition-based alerts for rain probability, heat, cold, and wind thresholds.
  - NWS (National Weather Service) official alerts for US locations.
  - Dedup key cache prevents duplicate notifications.
  - Added 35 weather localization strings in ko, zh, ja, en.
<!-- /en -->

## [1.28.4] - 2026-05-14

<!-- ko -->
### 수정
- **7일 사용 추이 자정 이후 추가 바 표시 버그 수정** — 자정 이후 히스토리 데이터가 8일치로 반환될 수 있던 문제를 `.Take(days)` 안전장치로 해결했습니다.
- **설정 창 footer 레이아웃 개선** — disclaimer 텍스트와 링크 버튼을 가로 3열에서 세로 2단으로 재배치하여 텍스트가 충분한 가로폭을 확보하도록 했습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **7-day trend chart extra bar after midnight** — Added `.Take(days)` safeguard to prevent more than N days from being returned after midnight edge cases.
- **Settings footer layout improved** — Restructured from a cramped 3-column horizontal layout to a 2-row vertical stack so the disclaimer text has full width.
<!-- /en -->

## [1.28.3] - 2026-05-13

<!-- ko -->
### 추가
- **설정 창 footer에 프로젝트 페이지(GitHub.io) 링크 추가** — footer 우측에 "프로젝트 페이지 ↗" 링크를 추가하여 GitHub.io 프로젝트 페이지에 쉽게 접근할 수 있습니다.
<!-- /ko -->

<!-- en -->
### Added
- **Project page (GitHub.io) link in Settings footer** — Added a "Project Page ↗" link in the footer to provide quick access to the GitHub.io project page.
<!-- /en -->

## [1.28.2] - 2026-05-13

<!-- ko -->
### 개선
- **소진 예측 문구 개선** — "추세대로면 ~경 소진" → "이 속도면 ~경 조기 소진" 으로 변경하여 예상보다 빠르게 소진될 수 있다는 경고 의미를 강화했습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Depletion forecast wording** — Changed Korean wording from "추세대로면" to "이 속도면 ... 조기 소진" (all 4 locales updated) to emphasize early-depletion warning.
<!-- /en -->

## [1.28.1] - 2026-05-09

<!-- ko -->
### 수정
- **타임존 보정 (오늘 사용량 0 표시 버그)** — KST(UTC+9) 등 UTC 외 타임존에서 자정~오전 9시 사이에 시작된 Claude 세션이 "오늘 사용량" 통계와 7일 차트의 오늘 막대에서 누락되던 문제를 수정했습니다. API의 5h/7d 윈도우는 정상 응답을 받았지만 로컬 세션 스캔이 UTC 날짜 기준으로 필터링되어 "오늘 기록된 사용량이 없습니다"가 표시되는 모순이 발생했습니다.

### 기술
- `SessionMonitor.ScanTodayUsage`: `DateTime.UtcNow.Date` → `DateTime.Today`, 타임스탬프 비교를 `ToUniversalTime()` → `ToLocalTime()` 로 전환.
- `HistoryService` (`RecordTodayCore` / `GetLastCore` / `GetRecentMaxTotalTokensCore` / `TrimOldEntries`): "오늘" 키와 cutoff 를 모두 로컬 시간 기준으로 통일.
- `UsagePopup.DrawHistoryChart`: 차트의 "오늘" 하이라이트 키도 로컬 날짜 기준으로 변경.
<!-- /ko -->

<!-- en -->
### Fixed
- **Timezone Handling (Today's Usage Showing Zero)** — Fixed an issue where Claude sessions started between local midnight and 9 AM (in non-UTC time zones such as KST/UTC+9) were excluded from "Today's Usage" totals and the today bar in the 7-day chart. The API-driven 5h/7d windows reported usage correctly, but local session scanning filtered by UTC date, producing the contradictory "No usage recorded today" placeholder.

### Technical
- `SessionMonitor.ScanTodayUsage`: switched `DateTime.UtcNow.Date` → `DateTime.Today` and timestamp comparison from `ToUniversalTime()` → `ToLocalTime()`.
- `HistoryService` (`RecordTodayCore` / `GetLastCore` / `GetRecentMaxTotalTokensCore` / `TrimOldEntries`): unified "today" keys and cutoffs to local time.
- `UsagePopup.DrawHistoryChart`: today-highlight key also now uses local date.
<!-- /en -->

---

## [1.28.0] - 2026-05-08

<!-- ko -->
### 수정
- **"조직 OAuth API 미활성" 안내가 24시간 지나도 똑같이 보이던 문제** — 기존 분기는 시간 추적 없이 문자열 패턴(`HTTP 403 + permission_error + currently not allowed`) 만 보고 *"24시간 후에도 동일하면..."* 이라는 유예 톤 안내문을 무한 반복 표시했습니다. 이제 첫 감지 시각을 settings 에 영속화해 24h 미만이면 기존 유예 안내, 24h 이상이면 *"X시간/일째 지속 중 — console.anthropic.com 에서 워크스페이스 설정을 확인하거나 Anthropic 지원에 문의하세요"* 식의 에스컬레이션 안내로 자동 전환합니다. API 가 정상 응답을 주거나 다른 종류의 에러로 전이되면 추적 상태가 자동 정리됩니다.

### 기술
- `NotificationSettings.OAuthNotAllowedFirstSeenUtc` (`DateTime?`) 신규 영속화 필드 — 미감지 상태에서는 `null`. 디스크 쓰기는 상태 전이(미감지→감지, 감지→해소) 시에만 발생하며 폴링 2분마다 매번 쓰지 않습니다.
- `MainViewModel.ResolveOAuthNotAllowedNote()` / `ClearOAuthNotAllowedFirstSeenIfNeeded()` / `EstimateOAuthNotAllowedFirstSeenUtc()` 신규 private helper. 성공 분기, 다른 permission_error 분기, 다른 에러 분기 모두에서 추적 상태를 정리합니다. 쿨다운 분기는 OAuth 와 무관해 추적 상태를 유지합니다.
- **첫 감지 시각 추정 휴리스틱** — 새 버전이 설치되어 처음 OAuth-not-allowed 가 감지될 때 history DB 에 24h+ 전 사용 기록이 있으면 사용자가 이미 그만큼 앱을 써온 것으로 간주, `firstSeen` 을 가장 오래된 history 시점(자정 UTC)으로 추정해 곧바로 에스컬레이션 톤으로 진입합니다. history 가 비어있거나 모든 기록이 24h 이내라면 신규 사용자로 보고 `firstSeen=now` 로 잡아 기존 유예 톤을 유지. 이로써 *"24시간이 지났는데도 똑같은 안내가 나오네"* 가 패치 이후에도 24시간 더 이어지지 않습니다.
- `Loc.ApiOAuthNotAllowedEscalatedNote(elapsedLabel)` + `Loc.ElapsedDurationLabel(span)` ko/zh/ja/en 4개 언어 추가. 경과 시간은 2일 미만이면 시간, 그 이상이면 일 단위로 자연스럽게 표기.
- `MainViewModel.SaveSettings` 에서 새 객체 직렬화 시 `OAuthNotAllowedFirstSeenUtc = existing.OAuthNotAllowedFirstSeenUtc` 보존 라인 추가 — 설정창 저장으로 추적 시각이 사라지지 않도록 보장.
<!-- /ko -->

<!-- en -->
### Fixed
- **"Organization OAuth API not active" notice no longer freezes in grace-period tone past 24h** — the old branch only matched strings (`HTTP 403 + permission_error + currently not allowed`), with no time tracking, so the *"if it persists past 24 hours..."* grace-tone note kept showing forever. The first-seen timestamp now persists in settings; under 24h shows the original grace-tone note, past 24h auto-escalates to *"Organization OAuth API has been inactive for X hours/days — past the new-account grace window. Review your workspace settings at console.anthropic.com or contact Anthropic support."* The tracker is cleared automatically on a successful API response or a different error type.

### Technical
- `NotificationSettings.OAuthNotAllowedFirstSeenUtc` (`DateTime?`) — new persisted field, `null` when not tripped. Disk writes happen only on state transitions (clear→set, set→clear), not on every 2-min poll.
- New private helpers `MainViewModel.ResolveOAuthNotAllowedNote()` / `ClearOAuthNotAllowedFirstSeenIfNeeded()` / `EstimateOAuthNotAllowedFirstSeenUtc()`. Success branch, other-permission_error branch, and generic-error branch all clear the tracker. Cooldown branch leaves it alone (cooldown is unrelated to OAuth).
- **First-seen estimation heuristic** — when this build is freshly installed and OAuth-not-allowed trips for the first time, the helper checks the history DB for usage records older than 24h. If found, `firstSeen` snaps to the oldest history date (midnight UTC) so the escalated tone shows immediately. If history is empty or all entries are within 24h (genuinely new user), `firstSeen=now` so the original grace-tone note still gets its first day. This prevents *"the notice has been the same for 24 hours"* from extending another 24h post-upgrade.
- `Loc.ApiOAuthNotAllowedEscalatedNote(elapsedLabel)` + `Loc.ElapsedDurationLabel(span)` added in ko/zh/ja/en. Elapsed time renders in hours under 2 days, in days afterwards.
- `MainViewModel.SaveSettings` now propagates `OAuthNotAllowedFirstSeenUtc = existing.OAuthNotAllowedFirstSeenUtc` when persisting a new settings instance — prevents the tracker from being wiped when the user saves the Settings window.
<!-- /en -->

---

## [1.27.0] - 2026-05-07

<!-- ko -->
### 추가
- **ntfy 가이드 README 섹션** — README 에 `<a id="ntfy-guide"></a>` 앵커가 있는 본격 ntfy 가이드 섹션 작성. 앱 설치 / 토픽 보안 (20자+ 권장 이유) / 앱 구독 / 트레이 입력 / 테스트 / 멀티-PC / FAQ 까지 한 페이지로 정리. v1.24.0 에서 압축한 "ntfy 가이드 ↗" 링크가 실제로 클릭 가능한 가이드를 가리키지 않던 문제를 해소.
- **표시 옵션 토글 2종** (설정 → 트레이 탭 하단 "표시 옵션" 섹션):
  - **Codex 플랜 배지 표시** (기본 ON) — Codex 섹션 위쪽의 "ChatGPT Plus" / "ChatGPT Pro" 같은 플랜명 라벨을 끌 수 있음.
  - **리셋 라벨에 절대 시각 병기** (기본 OFF) — `1h 23m 후 리셋` → `1h 23m 후 리셋 (18:30)` 처럼 상대 카운트다운에 절대 시각을 병기. 같은 날이 아닌 리셋(7일 윈도우 등)은 `MM/dd HH:mm` 으로 표시. 토글 즉시 4개 reset 라벨이 raw 시간값에서 재포맷되어 API 재호출 없이 반영.

### 변경
- **설정 창 "ntfy 가이드 ↗" 링크의 실제 목적지 수정** — `https://ntfy.sh` 단순 홈으로 이동하던 것을 `README#ntfy-guide` 앵커로 변경 (라벨이 이미 "가이드"였는데 도착지는 다운로드 페이지였던 부조리 해소).

### 수정
- **Popup footer 가 긴 업데이트 메시지에 우측 ⚙/Quit 버튼이 가려지던 문제 수정** — footer Grid 가 ColumnDefinitions 없이 좌/우 StackPanel 을 같은 셀에 겹쳐 두던 구조였습니다. "GitHub API 한도 초과 (HH:mm 에 재시도 가능) — 또는 Releases 페이지에서 직접 받기" 같은 긴 메시지가 표시되면 우측 ⚙ 버튼을 가려 클릭 불가능했던 케이스. 이제 `Auto / * / Auto` 3-컬럼 Grid + 메시지 `TextWrapping="Wrap"` 조합으로, 메시지가 길면 두 줄로 줄바꿈되고 ⚙/Quit 버튼은 항상 우측에서 보호됩니다.
- **설정 창에도 v1.26.0 의 우하단 자동 앵커 적용** — popup 은 이미 `SizeChanged` 시 우하단 모서리로 재정렬되었지만, 설정 창은 같은 처리가 빠져 있어 탭 전환/언어 변경 등으로 컨텐츠 높이가 바뀌면 작업표시줄을 침범하는 동일한 증상이 있었습니다. 이제 `SettingsWindow` 도 `SizeChanged += AnchorToTrayCorner` 로 동일 패턴이 적용되어, 어떤 탭/언어 조합에서도 항상 작업표시줄 바로 위에 정렬됩니다.

### 기술
- `NotificationSettings.ShowCodexPlanBadge` (default `true`), `ShowAbsoluteResetTime` (default `false`) 신규 영속화 필드.
- `MainViewModel.FormatResetLabel` 을 `static` → 인스턴스 메서드로 전환 — `ShowAbsoluteResetTime` 토글을 읽기 위함. 동시에 4개 raw `DateTimeOffset?` 필드(`_rawClaudeShortResetAt` 등)를 도입해 토글 즉시 라벨 재계산.
- `partial void OnShowAbsoluteResetTimeChanged` 핸들러로 토글 변경 시점에 4개 reset 라벨을 raw 값에서 즉시 재포맷.
<!-- /ko -->

<!-- en -->
### Added
- **ntfy guide section in README** — README now hosts a full ntfy walkthrough at `<a id="ntfy-guide"></a>`: app install, topic-name security rationale (why 20+ chars), subscription, tray input, testing, multi-PC behavior, and FAQ. Closes the v1.24.0 gap where the compacted "ntfy guide ↗" link pointed at no real guide.
- **Two display option toggles** (Settings → Tray tab → "Display options"):
  - **Show Codex plan badge** (default ON) — toggles the green "ChatGPT Plus" / "ChatGPT Pro" label above the Codex section.
  - **Show absolute reset time alongside countdown** (default OFF) — turns `resets in 1h 23m` into `resets in 1h 23m (18:30)`. Resets on a different date render as `MM/dd HH:mm`. The four reset labels re-format from cached raw `DateTimeOffset` values the moment the toggle flips — no API call needed.

### Changed
- **Settings "ntfy guide ↗" link points where it claims** — previously navigated to `https://ntfy.sh`, now opens `README#ntfy-guide`. (The label said "guide" but the destination was the download page.)

### Fixed
- **Popup footer no longer hides the ⚙/Quit buttons when the update-check message is long** — the footer Grid had no ColumnDefinitions, so the left and right StackPanels overlapped in the same cell. Long status text like "GitHub API rate limit (retry after HH:mm) — or download from the Releases page" pushed past the right edge and covered the ⚙ button, making it unclickable. Footer is now a 3-column Grid (`Auto / * / Auto`) with `TextWrapping="Wrap"` on the status message — long messages wrap to a second line, and the ⚙/Quit buttons stay anchored on the right.
- **Settings window now auto-anchors to bottom-right too (v1.26.0 parity)** — the popup re-anchored on `SizeChanged` since v1.26.0, but the settings window was missing the same handler. Switching tabs or language could grow the content and push the window into the taskbar. `SettingsWindow` now uses the same `SizeChanged + AnchorToTrayCorner` pattern as `UsagePopup`, so it always sits flush above the taskbar regardless of tab/language.

### Technical
- `NotificationSettings.ShowCodexPlanBadge` (default `true`), `ShowAbsoluteResetTime` (default `false`) — new persisted fields.
- `MainViewModel.FormatResetLabel` changed from `static` to instance method so it can read `ShowAbsoluteResetTime`. Four raw `DateTimeOffset?` fields (`_rawClaudeShortResetAt` etc.) added to enable on-toggle re-format without an API round-trip.
- `partial void OnShowAbsoluteResetTimeChanged` recomputes the four reset labels from raw values when the toggle flips.
<!-- /en -->

---

## [1.26.0] - 2026-05-07

<!-- ko -->
### 수정
- **Popup 이 컴팩트 → 펼침 시 작업표시줄을 침범하던 문제 수정** — v1.25.0 의 Focus + Compact Rows 모델에서 컴팩트 행을 클릭해 다른 공급자 detail 이 펼쳐지면 popup 높이가 늘어나면서 작업표시줄과 겹쳐 잘리는 현상이 있었습니다. 이제 popup 이 자체 `SizeChanged` 이벤트를 감지해 우하단 모서리 앵커를 자동 재계산합니다 — 펼침/접힘 시 popup 이 위로 자라거나 아래로 줄어 항상 작업표시줄 바로 위에 정렬됩니다.
- **`MaxHeight` 안전망 추가** — popup 최대 높이를 작업영역 높이로 자동 클램프(`SystemParameters.WorkArea.Height - 16`). 작은 모니터/공급자 4개 활성 같은 극단 케이스에서도 popup 이 화면 밖으로 밀려나지 않습니다.

### 추가
- **Codex 보조 윈도우 (secondary) 게이지 노출** — 그동안 `CodexUsageMonitor` 가 `rate_limits.secondary` 데이터까지 캡처하고 있었지만 UI 에 노출되지 않던 상태였습니다. 이제 Codex focused 시 단기 윈도우(primary, 기존) 와 장기 윈도우(secondary, 신규) 두 게이지가 Anthropic 의 5h/7d 처럼 나란히 표시됩니다. secondary 응답이 없는 플랜에서는 자동으로 숨김.
- **Codex Plan 라벨링** — 응답에 `plan_type` 이 있으면 "ChatGPT plan" 대신 "ChatGPT Plus" / "ChatGPT Pro" / "ChatGPT Team" 등 구체적인 플랜명을 표시. 사용자가 "내가 무엇 기준의 % 인지" 즉시 인식할 수 있습니다.
- **`ShortWindow` / `LongWindow` 4개 언어 라벨 추가** — Codex 처럼 윈도우 의미가 플랜별 가변일 때 사용. Anthropic 의 시간 기반 5시간/7일 라벨과는 분리.
<!-- /ko -->

<!-- en -->
### Fixed
- **Popup overlapped the taskbar when expanding from compact → focused** — In v1.25.0's Focus + Compact Rows model, clicking a compact row to expand a different provider made the popup grow taller, but its `Top` position stayed pinned, so the new content slid behind the taskbar and was clipped. The popup now hooks `SizeChanged` and re-anchors to the bottom-right corner whenever its height changes — the window grows upward or shrinks downward and always sits flush above the taskbar.
- **`MaxHeight` safety clamp** — The popup's max height is auto-clamped to the available work area (`SystemParameters.WorkArea.Height - 16`), so even on small displays with all four providers active the window can't be pushed off-screen.

### Added
- **Codex secondary-window gauge surfaced** — `CodexUsageMonitor` already captured `rate_limits.secondary` from the API/log payloads, but the data was never bound to the UI. The Codex section, when focused, now shows both a Short window (primary) and a Long window (secondary) gauge side by side — analogous to Anthropic's 5h/7d. The secondary gauge auto-hides on plans where the field isn't present.
- **Codex plan label** — When the response carries `plan_type`, the section now reads "ChatGPT Plus" / "ChatGPT Pro" / "ChatGPT Team" instead of the generic "ChatGPT plan", so the user immediately sees what tier the percentages refer to.
- **`ShortWindow` / `LongWindow` localized labels (4 languages)** added for cases where the window's semantic meaning is plan-dependent (vs Anthropic's strict 5h/7d).
<!-- /en -->

---

## [1.25.0] - 2026-05-07

<!-- ko -->
### 변경
- **Popup 레이아웃 = "Focus + Compact Rows" 모델** — 4개 에이전트가 모두 활성이어도 popup이 무한정 길어지지 않도록 한 번에 하나의 공급자만 상세 펼침. 나머지 3개는 32~40px 짜리 컴팩트 행(badge + name + 핵심 지표) 으로만 표시. 어느 행이든 클릭하면 해당 공급자로 focus 가 전환되고 즉시 펼쳐집니다.
  - **공간 효과**: 4개 활성 시 ~1150px → ~570~700px 정도로 줄어 1080p 에서도 스크롤 없이 들어옴
  - **글랜스 패턴**: 컴팩트 행에서도 핵심 % / 요청 횟수가 보임
  - **선택 영속화**: 클릭한 focus 는 `~/.claude/claude-usage-tray.json` 에 저장되어 앱 재시작 후에도 유지

### 추가
- **`NotificationSettings.FocusedProvider`** — 사용자 선택 영속화 필드. 빈 문자열은 자동 결정(Claude 우선, 비활성 시 첫 활성 공급자) 의미.
- **`MainViewModel.IsClaudeFocused / IsCodexFocused / IsGeminiFocused / IsOpenCodeFocused`** — XAML Visibility 바인딩용 부울 4개. `OnFocusedProviderChanged` partial 핸들러가 자동 동기화.
- **`EnsureValidFocusedProvider()`** — 사용자가 현재 focused 공급자를 표시 OFF 했을 때 자동으로 활성된 다른 공급자로 폴백.

### 기술
- 각 에이전트 섹션 = `<Button Style="AgentRowBtn">` (compact row, 항상 보임) + `<StackPanel Visibility="IsXxxFocused">` (detail body, focused 일 때만)
- `AgentRowBtn` 스타일: hover 시 `#1A1D2E` 배경, 라운드 8px, padding 10x8 — 클릭 가능 affordance
<!-- /ko -->

<!-- en -->
### Changed
- **Popup layout reworked into a "Focus + Compact Rows" model** — When all 4 agents are active, the popup no longer balloons. Only one provider expands its detail at a time; the other three render as 32-40 px compact rows (badge + name + key metric). Click any row to switch focus and instantly expand it.
  - **Space win**: 4 active providers go from ~1150 px → ~570-700 px — fits comfortably on 1080 p without scrolling
  - **Glance friendly**: compact rows still show the key %/request-count
  - **Persistence**: the focused provider is saved to `~/.claude/claude-usage-tray.json` and survives restart

### Added
- **`NotificationSettings.FocusedProvider`** — persisted user choice. Empty string means auto-determine (Claude first, then the first enabled provider).
- **`MainViewModel.IsClaudeFocused / IsCodexFocused / IsGeminiFocused / IsOpenCodeFocused`** — bool ObservableProperties for XAML Visibility binding. `OnFocusedProviderChanged` partial keeps them in sync.
- **`EnsureValidFocusedProvider()`** — auto-rotates focus to another enabled provider if the user disables the currently-focused one.

### Technical
- Each agent section is now `<Button Style="AgentRowBtn">` (compact row, always visible) + `<StackPanel Visibility="IsXxxFocused">` (detail body, only when focused).
- `AgentRowBtn` style: hover background `#1A1D2E`, 8 px corner radius, 10x8 padding — clear clickable affordance.
<!-- /en -->

---

## [1.24.2] - 2026-05-07

<!-- ko -->
### 수정
- **"업데이트 확인 실패" 가 원인을 가리던 문제 수정** — 기존 catch block 이 변수 없이 예외를 통째로 삼키고 있어, GitHub API 무인증 한도(60/hour/IP) 초과나 네트워크 단절 같은 서로 다른 원인이 모두 같은 일반 메시지로 표시되었습니다. 이제 원인이 4 가지 카테고리로 분류되어 각각 명확한 안내가 표시됩니다:
  - **GitHub API rate limit 초과**: 재시도 가능 시각(`X-RateLimit-Reset` / `Retry-After` 헤더 기반)을 같이 보여주며, "Releases 페이지에서 직접 다운로드" 대안 안내
  - **네트워크 도달 불가**: "인터넷 연결 확인" 안내
  - **타임아웃**: "잠시 후 재시도" 안내
  - **그 외 GitHub API 에러**: HTTP 상태 코드 + Releases 페이지 안내
- 분류되지 않은 예외도 `ex.Message` 의 80자 prefix 가 같이 표시되어 진단이 가능해집니다.

### 개선
- **HttpClient 타임아웃을 100초 → 15초로 단축** — 업데이트 확인이 멈춘 줄 알 정도의 긴 기본 타임아웃을 줄여서 사용자 체감 응답성 향상.
- **표시 시간 3초 → 5초로 연장** — 새 안내문구는 이전보다 정보량이 많아져 충분히 읽을 시간 확보.
- **`UpdateCheckException` + `UpdateCheckErrorKind` enum 신설** — 향후 다른 화면에서도 같은 분류 기반으로 라우팅 가능하도록 공개 타입으로 노출.
<!-- /ko -->

<!-- en -->
### Fixed
- **"Update check failed" was hiding the actual reason** — The previous `catch` block discarded the exception variable, so completely different causes (GitHub API unauthenticated rate limit at 60/hour/IP, network failures, transient API errors) all surfaced as the same generic message. Failures are now classified into 4 categories with specific guidance:
  - **GitHub API rate-limited**: shows the retry-after time (from `X-RateLimit-Reset` / `Retry-After` headers) plus a hint to download from the Releases page directly
  - **Network unreachable**: prompts the user to check the internet connection
  - **Timeout**: suggests retrying shortly
  - **Other GitHub API error**: shows the HTTP status code with a Releases-page fallback hint
- Unclassified exceptions now include the first 80 characters of `ex.Message` so users can self-diagnose or report meaningfully.

### Improved
- **HttpClient timeout shortened from 100 s → 15 s** — The default felt like the app was hung. Faster perceived responsiveness on update checks.
- **Failure message display time extended from 3 s → 5 s** — The new categorized notices carry more information; users need a beat longer to read them.
- **Publicly exposed `UpdateCheckException` and `UpdateCheckErrorKind`** so future surfaces can route by failure category as well.
<!-- /en -->

---

## [1.24.1] - 2026-05-07

<!-- ko -->
### 개선
- **403 `permission_error` 안에서 "신규 계정/조직 OAuth 미활성" 케이스 분리 안내** — Anthropic 응답 본문이 "currently not allowed" 패턴을 포함하면(예: "OAuth authentication is currently not allowed for this organization"), 기존의 영구 권한 부족 안내 대신 일시적 검증 게이트임을 알리는 메시지로 분기합니다. "신규 계정/플랜 검증 진행 중일 수 있으며 24시간 후에도 동일하면 워크스페이스 설정/Anthropic 지원 문의 권장" 톤. 4개 언어 모두 추가.
- **403 발생 시 backoff 6시간 → 90분으로 단축** — 신규 계정의 OAuth API 활성화는 보통 수시간~24시간 안에 풀리는 일시 게이트이므로, 6시간 backoff 는 활성화 시점을 너무 늦게 잡습니다. 90분으로 줄여서 활성화 후 평균 ~45분, 최대 90분이면 게이지가 자동 채워지도록 조정.
<!-- /ko -->

<!-- en -->
### Improved
- **403 `permission_error` now distinguishes the "new-account / org OAuth not active yet" case** — When Anthropic's response body contains the `"currently not allowed"` pattern (e.g. "OAuth authentication is currently not allowed for this organization"), the popup shows a softer note framing this as a likely temporary verification gate rather than a permanent block. Wording: "OAuth API for this organization isn't active yet — likely a new-account or plan-verification gate. If it persists past 24 hours, check workspace settings or contact Anthropic support." (4 languages).
- **403 backoff shortened from 6 hours to 90 minutes** — New-account OAuth API activation typically clears within a few hours to 24 hours, so a 6-hour backoff was too long: it would miss the activation window. 90 minutes keeps the average detection latency under ~45 minutes (max 90 min) so the gauges auto-populate soon after activation.
<!-- /en -->

---

## [1.24.0] - 2026-05-07

<!-- ko -->
### 변경
- **설정 창 4탭 구조로 재배치 — 스크롤 제거** — 기존엔 일반/트레이 카드 위에 알림/ntfy 탭이 겹쳐 있어 세로로 길어 스크롤이 발생했습니다. 이제 모든 설정이 동일한 메타포(탭) 안에 들어가며, 폭 360→400px, `MaxHeight=640`로 한 화면에 들어옵니다. 카드↔탭↔footer 가 섞여 있던 시각적 혼선이 해소됩니다.
- **ntfy 탭 압축** — 3줄짜리 단계별 가이드를 "ntfy 가이드 ↗" 단일 링크로 압축. 토픽 입력과 발송 토글, 보안 경고만 한 화면에 노출. 학습 표면이 아니라 조작 표면에 집중.
- **footer 신설** — 면책 문구를 footer 영역으로 이동하고, 우측에 **"기본값 복원"** 링크를 추가했습니다 (사용자 입력 ntfy 토픽은 보존).

### 추가
- **"저장됨 ✓" 인디케이터** — 설정 변경이 영속화되는 순간 헤더에 ✓ 가 짧게 페이드인 → 페이드아웃하여 저장 사실을 시각적으로 알립니다 (별도 저장 버튼 없는 자동 저장 모델 보강).
- **트레이 표시 기준 콤보의 비활성 항목 disable** — "표시 공급자" 체크에서 꺼진 공급자는 트레이 표시 기준 드롭다운에서 자동으로 흐려집니다(클릭 불가). 현재 트레이 모드가 disable 상태가 되면 자동(Auto)으로 폴백.
<!-- /ko -->

<!-- en -->
### Changed
- **Settings window restructured into 4 tabs — scroll removed** — Previously the General / Tray cards stacked above the Alerts / ntfy tabs forced the window to scroll. Everything now lives inside the same tab metaphor; width 360→400px with `MaxHeight=640` keeps every tab on a single screen. The earlier mix of cards / tabs / footer is gone.
- **ntfy tab compacted** — The three-line step-by-step guide is replaced by a single "ntfy guide ↗" link. The tab now focuses on the topic input, send toggle, and security warning — operating surface, not teaching surface.
- **Footer added** — The disclaimer moves to a real footer, and a **"Reset defaults"** link sits on the right (user-entered ntfy topic is preserved).

### Added
- **"Saved ✓" indicator** — Whenever a setting change is persisted, a green ✓ briefly fades in/out next to the header. Reinforces the auto-save model (no explicit save button).
- **Tray-mode dropdown items disabled when their provider is hidden** — Providers unchecked under "Visible providers" are now greyed out (and unclickable) in the "Tray display mode" dropdown. If the currently-selected mode becomes disabled, it falls back to Auto automatically.
<!-- /en -->

---

## [1.23.0] - 2026-05-07

<!-- ko -->
### 추가
- **비-Claude 공급자에 4타일 "오늘의 토큰" 패널 추가** — Codex / Gemini CLI / OpenCode 섹션도 Claude와 동일한 입력 / 출력 / 캐시 읽기 / 캐시 쓰기 4타일 그리드를 표시합니다. 각 공급자가 캐시 쓰기 개념을 지원하지 않는 경우(Codex, Gemini)는 해당 타일에 "—"를 표시합니다.
- **Gemini CLI 요청 횟수 / OpenCode 요청 횟수를 섹션 헤더 옆 인라인으로 표시** — 4타일 영역을 토큰 데이터에 집중시키고 카운터는 메타 정보로 분리.

### 수정
- **Gemini CLI 파서 전면 재작성** — 이전 파서는 `tokens.output` 만 읽고 시간대별 집계를 파일 mtime으로 잘못 계산했습니다. 새 파서는 실제 Gemini CLI v3 로그 스키마(`type:"gemini"` 라인 + 메시지 단위 `timestamp` + `tokens.{input,output,cached,thoughts,tool}`)를 정확히 파싱하여 입력 / 출력 / 캐시 읽기 토큰을 모두 집계하고, 시간대별 차트도 메시지 timestamp 기준으로 정확하게 표시합니다.
- **Codex / Gemini / OpenCode 의 7일 history 가 쌓이지 않던 문제 수정** — 기존에는 Claude 만 `HistoryService.RecordToday()` 를 호출하고 다른 공급자는 누락되어 있었습니다. 이제 모든 공급자가 자기 scope에 자동으로 일별 history를 기록합니다 (provider별 별도 JSON 파일).

### 개선
- **`HistoryService` multi-scope 구조로 리팩터링** — 4개 공급자가 병렬 refresh 시 각자 자기 scope 에 안전하게 기록할 수 있도록 내부 저장소를 다중 scope dict 로 확장. 활성 scope(차트 바인딩이 보는 곳) 와 무관하게 임의 scope 에 기록 가능. 기존 공개 메서드(`SetScope`, `RecordToday`, `GetLast`, `GetRecentMaxTotalTokens`)는 호환성 유지를 위해 그대로 두고 scope-explicit 오버로드를 추가.
- **Gemini / OpenCode 트레이 게이지 비율을 자기 공급자 history 기준으로 계산** — 이전엔 활성 scope(주로 Claude)의 7일 최대값을 분모로 써서 비율이 낮게 표시되던 문제 해결.

### 테스트
- **Gemini 파서 단위 테스트 추가** — 실제 Gemini CLI 로그 스키마에 맞춘 합성 픽스처로 4가지 시나리오(전 토큰 합산, 사용자 라인/과거 라인/손상 JSON 무시, 오늘 데이터 없음, 메시지 timestamp 기반 hourly bucketing) 검증.
<!-- /ko -->

<!-- en -->
### Added
- **4-tile "Today's tokens" panel for non-Claude providers** — Codex / Gemini CLI / OpenCode sections now mirror Claude's grid with input / output / cache-read / cache-write tiles. Tiles show "—" where a provider has no concept of that token type (e.g. Codex and Gemini have no cache-write).
- **Gemini / OpenCode request counts moved to inline meta beside the section header**, freeing the 4-tile area for token data.

### Fixed
- **Full rewrite of the Gemini CLI parser** — The old parser only read `tokens.output` and bucketed hourly by file mtime (very wrong for long sessions). The new parser walks the real Gemini CLI v3 schema (`type:"gemini"` lines + per-message `timestamp` + `tokens.{input,output,cached,thoughts,tool}`) and aggregates input / output / cache-read tokens accurately, with hourly bucketing driven by message timestamps.
- **7-day history was never recorded for Codex / Gemini / OpenCode** — Only the Claude refresh path called `HistoryService.RecordToday()`. Each provider now records into its own scope automatically, so the per-provider history JSON files actually accumulate over time.

### Improved
- **`HistoryService` refactored to a multi-scope store** so the four parallel `Refresh*Async` methods can each write into their own provider scope without contention. The active scope (chart binding) is decoupled from per-provider records. Existing public methods are preserved verbatim; new scope-explicit overloads are additive.
- **Gemini / OpenCode tray gauge percentages now use each provider's own 7-day max** as the denominator, instead of the active scope's max (usually Claude's), which previously produced misleadingly low percentages.

### Tests
- **Added unit tests for the Gemini parser**, with synthetic fixtures that match the real Gemini CLI schema. Covers all-token aggregation, ignoring user / past-day / malformed JSON lines, no-usage-today fallback, and message-timestamp-driven hourly bucketing.
<!-- /en -->

---

## [1.22.5] - 2026-05-07

<!-- ko -->
### 수정
- **업데이트 팝업의 스크롤바가 윈도우 기본 회색으로 노출되던 문제 수정** — 다크 ScrollBar 스타일이 SettingsWindow에만 로컬로 정의되어 있어 UpdateDialog는 윈도우 기본 스크롤바가 그대로 보였습니다. 다크 ScrollBar 스타일을 `App.xaml`로 승격해 앱 전역 implicit 스타일로 적용했습니다.
- **업데이트 팝업의 텍스트 캐럿/선택 색상 테마 일치** — 릴리즈 노트 RichTextBox의 캐럿(`#A78BFA`)과 텍스트 선택(`#8B5CF6`)이 다크 보라톤으로 통일됩니다. 릴리즈 노트 본문에 포함될 수 있는 하이퍼링크도 같은 보라톤(hover 시 밝게)으로 표시됩니다.
- **트레이 팝업 새로고침 버튼 툴팁이 윈도우 기본(흰 배경)으로 노출되던 문제 수정** — `App.xaml`에 다크 ToolTip 전역 implicit 스타일(`#1A1D2E` 배경, `#F1F5F9` 글자, `#2D2F45` 테두리, 라운드 6px)을 추가해 모든 ToolTip이 다크 톤으로 일관 표시됩니다.

### 개선
- **다크 ScrollBar/ToolTip 스타일 일원화** — SettingsWindow에 중복 정의되어 있던 ScrollBar/Thumb 스타일을 제거하고 `App.xaml`의 전역 정의 한 곳에서 관리합니다. 향후 추가되는 윈도우/팝업도 자동으로 다크 테마를 따르게 됩니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Update popup scrollbar showed Windows default light-gray** — The dark `ScrollBar` style was scoped locally to `SettingsWindow.xaml`, so `UpdateDialog.xaml` fell through to the OS default chrome. The style is now promoted to `App.xaml` as an application-wide implicit style.
- **Update popup text caret / selection now match the dark theme** — The release-notes `RichTextBox` now uses the purple caret (`#A78BFA`) and selection (`#8B5CF6`) tones consistent with the rest of the app. Any hyperlinks rendered inside the release notes are themed with the same purple (lighter on hover).
- **Tray popup refresh-button tooltip showed Windows default white** — A dark `ToolTip` implicit style is added to `App.xaml` (`#1A1D2E` background, `#F1F5F9` foreground, `#2D2F45` border, 6px rounded), so every tooltip across the app stays on-theme.

### Improved
- **Unified dark scrollbar / tooltip styling** — The duplicated `ScrollBar`/`Thumb` styles previously living inside `SettingsWindow.xaml` are removed. There is now a single source of truth in `App.xaml`, and any new window/popup picks up the dark theme automatically.
<!-- /en -->

---

## [1.22.4] - 2026-05-07

<!-- ko -->
### 수정
- **첫 429 응답에서 빨간 에러 박스가 뜨던 잔존 버그 수정** — v1.22.3의 쿨다운 분기는 호출 "전" 상태(`skipApi`)로 판정하다 보니 429를 받은 그 호출에서는 항상 빨간 박스로 떨어지고, 다음 새로고침부터만 회색 안내가 나왔습니다. 이제 호출 "후" 갱신된 `_apiRetryAfter` 기준으로 판정해 **첫 429부터 즉시** 회색 톤 "API 응답 대기 중 — HH:MM 자동 재시도"로 일관 표시됩니다.
- **`Retry-After` 헤더 누락 케이스 보강** — 서버가 429를 돌려주면서 `Retry-After`를 안 주거나 파싱 실패한 경우, 앱이 backoff를 못 걸어 같은 429를 2분마다 다시 두드리던 문제를 수정했습니다. 이제 헤더가 없어도 **기본 5분 backoff**가 자동 적용됩니다.

### 개선
- **403 `permission_error` 전용 안내 추가 (4개 언어)** — 토큰 스코프가 부족해 사용량 API 접근이 막힌 계정의 경우, 빨간 raw JSON 대신 친절한 회색 안내문구로 대체합니다 (예: "이 계정의 OAuth 토큰에는 사용량 API 권한이 없습니다 — 5h/7d 게이지는 표시되지 않으며, 로컬 토큰 집계는 정상 동작합니다"). 동일 응답이 반복되지 않도록 **6시간 backoff**도 함께 적용해 부수적으로 IP-기반 429에 걸리는 악순환을 차단합니다.
- **에러 메시지 truncate 80자 → 200자 확장** — Anthropic 응답 본문이 잘려서 진단이 어려웠던 케이스 대응. 이제 `permission_error` 같은 메시지가 한눈에 들어옵니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **First-429 still showed the red error box** — The cooldown branch added in v1.22.3 was evaluated *before* the API call, so the very call that received the 429 always landed in the red branch and only subsequent refreshes were softened. The decision is now based on the *post-call* `_apiRetryAfter`, so even the first 429 routes immediately to the soft gray "Waiting for API — auto retry at HH:MM" note.
- **Missing `Retry-After` header case** — When the 429 response came back without (or with an unparseable) `Retry-After`, no backoff was applied and the app kept hammering the endpoint every 2 minutes. A **default 5-minute backoff** is now applied as a fallback.

### Improved
- **Dedicated 403 `permission_error` notice (4 languages)** — Accounts whose OAuth tokens lack the usage-API scope no longer see a red raw JSON dump. Instead a friendly gray note explains the situation (e.g. "This account's OAuth token lacks permission for the usage API — 5h/7d gauges will not appear, but local token aggregation continues to work."). A **6-hour backoff** is also applied so the same 403 isn't re-hammered every 2 minutes (this also stops the secondary IP-based 429 it used to trigger).
- **Error message truncate raised 80 → 200 chars** — Anthropic error bodies were previously cut off mid-message, hiding key diagnostic info such as `permission_error` codes. The full short-message tail is now visible.
<!-- /en -->

---

## [1.22.3] - 2026-05-07

<!-- ko -->
### 수정
- **공급자별 "오늘 사용 기록 없음" 메시지 중복 표시 제거** — Codex / Gemini CLI / OpenCode 섹션에서 회색 placeholder 안내문구와 동일한 내용이 빨간색 에러 텍스트로 한 번 더 표시되던 문제를 수정했습니다. "오늘 사용 기록 없음"은 정보성 상태로 분류하여 placeholder만 노출되도록 했습니다.
- **Windows 10 환경의 빈 Error Panel(연한 주황색 박스) 표시 수정** — `HasError=true`이지만 `ErrorMessage`가 비어 있을 때 상단에 빈 박스가 그려지던 문제를 수정했습니다. 이제 표시할 메시지가 없으면 패널 자체가 렌더링되지 않습니다.

### 개선
- **Claude API 일시 제한(쿨다운) 표시 톤 다운** — Anthropic 사용량 API가 429를 돌려주는 동안에는 빨간 에러 박스 대신 "API 응답 대기 중 — HH:MM에 자동 재시도" 형식의 회색 안내만 표시합니다. 캐시된 사용량 수치는 그대로 유지되어 새 계정/계정 전환 직후 화면이 과도하게 경고처럼 보이지 않습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Removed duplicate "no usage today" messages** — In the Codex / Gemini CLI / OpenCode sections, the same notice was being shown twice (once as a gray placeholder, once as a red error). "No usage today" is now treated as informational and only the placeholder is shown.
- **Empty Error Panel artifact on Windows 10** — When `HasError=true` but `ErrorMessage` was empty, an empty pinkish-orange box could appear above the API quota section under WPF transparency on Windows 10. The panel is now bound to the message string and stays collapsed when there is nothing to show.

### Improved
- **Softer Claude API cooldown indicator** — While the Anthropic usage endpoint is throttling (HTTP 429), the popup no longer raises a red error box. It shows a subdued gray note "Waiting for API — auto retry at HH:MM" and keeps the last known usage figures so the screen stays informative right after a fresh login or account switch.
<!-- /en -->

---

## [1.22.2] - 2026-05-04

<!-- ko -->
### 수정
- **SQLite 네이티브 라이브러리 번들 추가** — `SQLitePCLRaw.bundle_e_sqlite3` 패키지를 명시적으로 추가하고, `IncludeNativeLibrariesForSelfExtract=true` 설정으로 단일 실행 파일에 네이티브 라이브러리가 포함되도록 수정했습니다.
- **앱 시작 시 SQLite 초기화** — `App.OnStartup`에서 `SQLitePCL.Batteries.Init()`를 호출하여 OpenCode DB 접근 전 SQLitePCLRaw가 초기화되도록 수정했습니다.
- **예외 처리에서 UI 상태 갱신 누락 수정** — 각 공급자(Claude, Codex, Gemini, OpenCode)의 데이터 갱신 중 예외 발생 시 `UpdateOverallStatus()`가 호출되지 않아 Error Panel이 잘못 표시되던 문제를 수정했습니다.
<!-- /ko -->

---

## [1.22.1] - 2026-05-04

<!-- ko -->
### 수정
- **SQLite 초기화 오류 수정** — `Microsoft.Data.Sqlite.Core`를 `Microsoft.Data.Sqlite`로 변경하여 SQLitePCLRaw 번들이 자동으로 포함되도록 수정했습니다. 이제 OpenCode DB 접근 시 "You need to call SQLitePCL.raw.SetProvider()" 오류가 발생하지 않습니다.
- **Error Panel 잔여 표시 버그 수정** — SQLite 오류로 인해 상단에 표시되던 어두운 네모(Error Panel)가 더 이상 나타나지 않습니다.
<!-- /ko -->

---

## [1.22.0] - 2026-05-04

<!-- ko -->
### 수정
- **Extra Usage 인디케이터 표시 버그 수정** — 사용량이 0이거나 특정 공급자가 비활성화된 경우에도 노란색 인디케이터가 잘못 표시되는 문제를 수정했습니다. 이제 Extra Usage 섹션이 `ExtraUsageEnabled` 또는 `IsExtraOnlyMode` 상태일 때만 표시되고, 사용량이 0일 때는 퍼센트 텍스트와 인디케이터가 숨겨집니다.
<!-- /ko -->

---

## [1.21.12] - 2026-04-30

<!-- ko -->
### 기능 추가
- **공급자별 표시 여부 수동 설정** — 이제 설정 화면에서 각 에이전트(Claude, Codex, Gemini, OpenCode)를 개별적으로 켜거나 끌 수 있습니다. 사용하지 않는 에이전트는 트레이와 팝업에서 완전히 제외할 수 있습니다.
### 개선
- **자동 모드 정교화** — '자동' 트레이 표시 모드가 사용자가 수동으로 활성화한 에이전트 중에서만 사용량을 감지하여 작동하도록 개선되었습니다.
<!-- /ko -->

<!-- en -->
### Added
- **Manual Provider Visibility Control** — You can now individually enable or disable each AI agent (Claude, Codex, Gemini, OpenCode) in the Settings window. Unchecked agents will be hidden from the tray and popup.
### Improved
- **Refined 'Auto' Mode** — The "Automatic" tray display logic now only considers agents that have been manually enabled by the user.
<!-- /en -->

---

## [1.21.11] - 2026-04-30

<!-- ko -->
### 개선
- **사용량 없음 UI 일관성 강화** — 모든 공급자(Claude 포함)가 오늘 사용량이 없을 때 게이지와 통계를 그대로 유지하면서 하단에 "오늘 기록된 사용량이 없습니다"라는 안내를 일관되게 표시하도록 수정했습니다.
- **포인터 문서 구조 확립** — `GEMINI.md`를 `AGENTS.md`로 연결되는 포인터로 설정하여 지침 관리 체계를 단일화했습니다.
<!-- /ko -->