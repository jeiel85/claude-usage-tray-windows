# Changelog

모든 주요 변경 사항을 이 파일에 기록합니다.
[Keep a Changelog](https://keepachangelog.com/ko/1.0.0/) 형식을 따릅니다.

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