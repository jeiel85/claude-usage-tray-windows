# Changelog

모든 주요 변경 사항을 이 파일에 기록합니다.
[Keep a Changelog](https://keepachangelog.com/ko/1.0.0/) 형식을 따릅니다.

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