# Changelog

모든 주요 변경 사항을 이 파일에 기록합니다.
[Keep a Changelog](https://keepachangelog.com/ko/1.0.0/) 형식을 따릅니다.

## [1.40.0] - 2026-08-20

<!-- ko -->
### 추가
- **`오늘 N개 세션` 을 누르면 그 세션들이 목록으로 펼쳐집니다** — 줄마다 프로젝트 이름, 브랜치, 오늘 그 세션이 쓴 토큰, 마지막 활동 시각이 나옵니다. 최근 활동 순으로 정렬되고, 최근 10분 안에 기록이 있는 세션은 초록 점으로 구분합니다. 줄에 마우스를 올리면 그 세션의 첫 프롬프트와 전체 경로를 볼 수 있습니다. 기본은 접힌 상태라 기존 화면 높이는 그대로입니다.
- 워크트리에서 돌린 세션은 마지막 폴더 이름(`auto-update-logic-3db433` 같은 해시 이름) 대신 저장소 이름으로 표시하고, 어느 워크트리인지는 브랜치로 구분합니다.
- 다중 PC 동기화로 다른 PC 의 세션이 합계에 섞여 있으면, 목록 아래에 `다른 PC 세션 N개는 목록에 없음` 이라고 적습니다(트랜스크립트는 그 PC 에만 있어 목록으로 만들 수 없습니다).

### 참고
- 이 수는 **지금 실행 중인 세션이 아니라 오늘 기록이 남은 세션**의 수입니다. 세션 종료는 트랜스크립트에 남지 않아 정확한 "활성" 판정은 불가능하며, 초록 점은 "최근 10분 안에 기록이 있었다"는 뜻입니다.
<!-- /ko -->

<!-- en -->
### Added
- **Clicking `N session(s) today` expands the list of those sessions** — each row shows the project name, branch, tokens that session spent today, and its last activity time. Rows are ordered by most recent activity, and sessions written to within the last 10 minutes get a green dot. Hovering a row reveals its first prompt and full path. The list starts collapsed, so the popup keeps its current height.
- Sessions run from a worktree are listed under the repository name instead of the hash-suffixed worktree folder (`auto-update-logic-3db433`); the branch tells the worktrees apart.
- When multi-PC sync mixes other devices into the session count, the list notes `N session(s) from other PCs not listed` — their transcripts live on those PCs and cannot be listed here.

### Note
- The count is **sessions with activity recorded today, not sessions running right now**. Transcripts do not record when a session ends, so "active" cannot be determined exactly; the green dot means "written to within the last 10 minutes".
<!-- /en -->

## [1.39.2] - 2026-08-12

<!-- ko -->
### 변경
- **Antigravity 게이지가 5시간 → 주간 순으로 고정됩니다** — 사용량이 많은 순으로 세우고 있어서 새로고침할 때마다 네 칸의 자리가 뒤바뀌었고, 5시간·주간이 섞여 나와 Claude·Codex 와 읽는 방식이 달랐습니다. 이제 모델 그룹별로 묶고 그 안에서 짧은 창이 위로 갑니다.
- **게이지 아래에 `45% 사용 · 잔량 55%` 요약이 추가됐습니다** — Claude·Codex 게이지에만 있던 한 줄입니다.
- **게이지 툴팁이 페이스 문구로 통일됐습니다** — `시간 50% 경과 · 사용 45% · 5%p 여유` 처럼 Claude·Codex 와 같은 내용을 보여주고, 리셋 절대 시각은 그 아래 줄에 남습니다.
- **상세 맨 아래에 데이터 출처 안내가 생겼습니다** — 다른 에이전트와 같은 자리입니다. 다른 PC 가 올린 값을 보고 있을 때는 오른쪽에 그 PC 와 관측 시각을 적습니다.
<!-- /ko -->

<!-- en -->
### Changed
- **Antigravity gauges are pinned to 5-hour → weekly order** — They were sorted by usage, so the four rows swapped places on every refresh and the 5-hour and weekly windows came out interleaved, unlike Claude and Codex. Rows are now grouped by model group, shortest window first.
- **Each gauge carries a `45% used · 55% remaining` summary line** — the single line that previously existed only on the Claude and Codex gauges.
- **Gauge tooltips now show the same pace text as the other agents** — `Time 50% · Used 45% · 5pp behind`, with the absolute reset stamp on the line below.
- **The detail ends with a data-source note** — same position as the other agents. When another PC's snapshot is on screen, that device and observation time are shown on the right.
<!-- /en -->

## [1.39.1] - 2026-08-12

<!-- ko -->
### 변경
- **Antigravity 상세가 다른 에이전트와 같은 모양으로 표시됩니다** — 혼자만 별도 카드 안에 그려져 제목·퍼센트·리셋 시각이 따로 놀았습니다. 이제 Claude·Codex·OpenCode 와 같은 한 줄 구성(`이름 · 사용률 · 리셋까지 남은 시간` + 게이지)을 씁니다. 요금제 이름은 Codex 의 플랜 배지와 같은 자리에 놓았고, 창 목록 안의 별도 스크롤도 없앴습니다.
- **Antigravity 게이지에도 시간선이 표시됩니다** — 창이 얼마나 흘렀는지 알려 주는 세로 마커를 다른 에이전트와 같은 모양으로 추가했습니다. 마커보다 게이지가 앞서 있으면 이번 창을 평소보다 빨리 쓰고 있다는 뜻입니다. 창 길이(주간·5시간)를 알 수 없는 항목에는 위치를 지어내지 않고 마커를 그리지 않습니다.
- **창 이름을 `Gemini 모델 · 주간` 처럼 줄였습니다** — 퍼센트가 사용률인데 이름은 `잔여량` 이라 값이 반대로 읽혔고, 이름이 길어 잘리기도 했습니다. 리셋 시각의 절대 표기(`(08-14 18:44)`)는 한 줄을 넘기므로 게이지 툴팁으로 옮겼습니다. 전체 이름도 툴팁에서 볼 수 있습니다.
<!-- /ko -->

<!-- en -->
### Changed
- **The Antigravity detail now matches the other agents** — It was the only section drawn inside its own card, with the name, percentage and reset time each on a separate line. It now uses the same single-row layout as Claude, Codex and OpenCode (`name · usage · time to reset` plus a gauge). The plan name sits where Codex shows its plan badge, and the separate scrollbar inside the window list is gone.
- **The Antigravity gauges show a timeline marker** — The vertical marker for how far the window has elapsed is now drawn the same way as for the other agents. A gauge ahead of the marker means the window is being consumed faster than the clock. Entries whose window length is unknown get no marker rather than an invented position.
- **Window names are shortened to `Gemini Models · Weekly`** — The percentage is usage, but the name said `Remaining`, so the value read backwards; the long names were also being truncated. The absolute reset stamp (`(08-14 18:44)`) does not fit on one line and moved to the gauge tooltip, which also carries the full name.
<!-- /en -->

## [1.39.0] - 2026-08-11

<!-- ko -->
### 수정
- **Antigravity 게이지가 사전 설정 없이는 뜨지 않던 문제** — 사용량을 조회하기도 전에 사용자가 직접 만들어야 하는 OAuth client 파일을 요구해, 그 파일이 없는 대부분의 PC 에서 섹션이 조용히 사라졌습니다. 파일을 만들려면 실행 중인 `language_server.exe` 의 400MB 메모리 덤프를 떠서 값을 뽑아내야 했습니다. 그런데 Windows 자격 증명 관리자에는 refresh token 뿐 아니라 **아직 유효한 access token 이 함께** 저장돼 있어서, 이 과정 자체가 필요 없었습니다. 이제 저장된 토큰을 먼저 쓰고, 만료됐을 때만 설치된 `language_server.exe` 에서 client 값을 자동으로 찾아 갱신합니다. Antigravity 에 로그인만 되어 있으면 **설정할 것이 없습니다.**

### 변경
- **Antigravity 사용량이 앱 화면과 같은 게이지로 표시됩니다** — 기존에는 `retrieveUserQuota` 가 주는 모델별 요청 잔량을 보여줘서, Antigravity 의 `Models & Usage` 화면에 있는 게이지와 서로 다른 값이었습니다. 이제 같은 출처(`retrieveUserQuotaSummary`)를 사용해 `Gemini Models` 와 `Claude and GPT models` 각각의 **주간·5시간 잔여량** 네 칸을 그대로 보여줍니다.
- **아직 쓰지 않은 창도 표시합니다** — 사용률이 0% 인 항목을 목록에서 빼고 있어서, 그 주에 아무것도 쓰지 않았으면 상세를 펼쳐도 빈 화면이었습니다. Antigravity 앱은 이 경우에도 100% 게이지를 보여줍니다.
- **트레이 게이지는 가장 많이 쓴 창을 기준으로 합니다** — 창마다 한도가 따로 걸리므로 평균을 쓰면 급한 제약이 가려집니다(주간 90% + 5시간 0% → 45%로 표시되던 문제).
- **게이지 이름이 앱 언어로 표시됩니다** — 서버는 이름을 영어로만 내려주므로 `Gemini 모델 · 주간 잔여량` 처럼 네 가지 언어로 옮겨 보여줍니다. 아직 모르는 항목이 오면 서버가 준 문구를 그대로 씁니다.
- Antigravity 를 실행하지 않아도 조회됩니다. 앱의 로컬 서버가 아니라 Google 백엔드를 직접 호출합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **The Antigravity gauge never appeared without manual setup** — Before reading any usage, the app demanded an OAuth client file that users had to create themselves, so on most PCs the section simply vanished. Producing that file meant taking a 400MB memory dump of the running `language_server.exe` and extracting the values by hand. Yet the Windows Credential Manager already stores **a still-valid access token** alongside the refresh token, which made the whole procedure unnecessary. The stored token is now used first, and the client values are located automatically from the installed `language_server.exe` only when the token has expired. If you are signed in to Antigravity, **there is nothing to configure.**

### Changed
- **Antigravity usage now shows the same gauge as the app itself** — The old call (`retrieveUserQuota`) returned per-model request allowances, which did not match the gauge on Antigravity's `Models & Usage` screen. The app now reads the same source (`retrieveUserQuotaSummary`) and shows the four windows verbatim: weekly and five-hour remaining for `Gemini Models` and for `Claude and GPT models`.
- **Windows you have not used yet are kept on screen** — Rows at 0% usage were filtered out, so during a week with no activity the expanded view was blank. Antigravity itself shows a full gauge in that situation.
- **The tray gauge follows the most consumed window** — Each window carries its own limit, so averaging hides the binding constraint (90% weekly + 0% five-hour used to read as 45%).
- **Gauge labels follow the app language** — The server sends these names in English only, so they are now translated into all four languages (for example `Gemini 모델 · 주간 잔여량`). Unrecognized entries keep the server's own wording.
- Usage is read even when Antigravity is not running, since the request goes to Google's backend rather than the app's local server.
<!-- /en -->

## [1.38.11] - 2026-08-11

<!-- ko -->
### 수정
- **PC 재부팅 뒤 OpenCode 로그인이 풀린 것처럼 보이던 문제** — 부팅 직후에는 네트워크가 아직 올라오지 않아 자동 조회의 탐색 자체가 실패하는데, 앱은 이 실패를 로그아웃과 똑같이 다뤄 `OpenCode 로그인` 버튼을 띄우고 30분 동안 재시도하지 않았습니다. 세션 쿠키는 멀쩡한데도 다시 로그인해야 하는 것처럼 보였습니다. 이제 서버가 로그인 페이지로 되돌린 경우에만 로그인이 풀린 것으로 판정하고, 탐색 실패·응답 없음처럼 확인 자체를 못 한 경우에는 버튼 대신 `공식 할당량을 잠시 못 읽었습니다 · 곧 다시 시도` 안내를 표시하며 1분부터 시작하는 짧은 간격으로 다시 확인합니다. 조용히 넘어가던 탐색 실패도 이유가 기록됩니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **OpenCode looked signed out after a PC reboot** — Right after boot the network is not up yet, so the automatic read's navigation fails outright, and the app treated that failure exactly like a sign-out: it showed the `Sign in to OpenCode` button and stopped retrying for 30 minutes, even though the session cookie was still valid. A sign-out is now recognized only when the server redirects to the login page. When the check itself could not be completed — navigation failure, no response — the button is replaced with `Couldn't read the official quota right now · retrying soon`, and the next check starts one minute later with a short back-off. Navigation failures that used to pass silently are now recorded with a reason.
<!-- /en -->

## [1.38.10] - 2026-08-10

<!-- ko -->
### 수정
- **동기화 값만 쓰는 PC에서 로그인이 풀린 것처럼 보이던 문제** — 다른 PC가 관측한 OpenCode 공식 할당량이 유효시간(최대 40분)을 넘기면 게이지가 사라지고 `OpenCode 로그인` 버튼이 노출됐습니다. 그 PC는 로그인한 적도, 할 이유도 없는데 로그인을 요구하는 화면이라 동기화가 끊긴 것으로 오해하기 쉬웠습니다. 이제 오늘 다른 PC가 관측한 이력이 있으면 버튼 대신 `기기명의 15:02 값 · 갱신 대기 중` 안내를 표시하고, 관측 이력이 아예 없을 때만 로그인 버튼을 남깁니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **A PC that only consumes synchronized values looked signed out** — Once the OpenCode official quota observed by another PC passed its lifetime (up to 40 minutes), the gauges disappeared and the `Sign in to OpenCode` button took their place, even though that PC had never signed in and had no reason to. The screen now shows `<device> value from 15:02 · awaiting refresh` whenever another PC observed the quota today, and keeps the sign-in button only when there is no observation at all.
<!-- /en -->

## [1.38.9] - 2026-08-10

<!-- ko -->
### 수정
- **다른 PC에서만 쓰는 공급자 섹션이 통째로 보이지 않던 문제** — 동기화 합계를 화면에 반영할지 판단할 때 "사용량이 있는 기기"가 2대 이상일 것을 요구했습니다. 이 PC에서 한 번도 쓰지 않은 공급자(예: OpenCode)는 기기 수가 1이 되어 다른 PC 값이 통째로 버려졌고, "데이터 없는 공급자 자동 숨김"과 맞물려 섹션 자체가 사라졌습니다. 이제 합계에 데이터가 있으면 기기 수와 무관하게 표시합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **A provider section was missing entirely when only another PC used it** — Applying synchronized totals required at least two devices with usage. A provider never used on this PC (for example OpenCode) produced a device count of one, so the other PC's totals were discarded, and combined with "hide providers without data" the section disappeared. Totals that contain data are now displayed regardless of device count.
<!-- /en -->

## [1.38.8] - 2026-08-10

<!-- ko -->
### 수정
- **OpenCode 로그인이 반복해서 풀린 것처럼 보이던 문제** — 자동 조회 때 WebView2가 이미 열고 있는 워크스페이스 주소를 WPF `Source`에 다시 지정하면 같은 값으로 처리되어 탐색이 시작되지 않았습니다. 20초 타임아웃을 로그인 해제로 잘못 표시하던 경로를 고쳐, 같은 주소에도 명시적으로 새 탐색을 실행합니다.
- **OneDrive 동기화 파일이 있어도 다른 PC에 OpenCode 게이지가 나오지 않던 문제** — 공식 값의 5분 유효시간이 30분 웹 재시도와 2분 폴링·클라우드 전송 지연보다 짧아 다른 PC가 읽을 기회를 놓쳤습니다. 마지막 정상 관측 시각을 보존하면서 OpenCode에만 최대 40분의 제한된 보조 유효시간을 적용하고, 롤링 초기화가 지난 값은 즉시 폐기합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **OpenCode repeatedly appeared signed out** — Assigning the already-open workspace URL to the WPF WebView2 `Source` again was treated as an unchanged value, so no navigation started. The resulting 20-second timeout was incorrectly presented as a sign-out. Automatic reads now explicitly navigate even when the URL is unchanged.
- **OpenCode gauges did not appear on another PC despite a synchronized OneDrive file** — The five-minute official-value lifetime was shorter than the 30-minute web retry plus two-minute polling and cloud transfer delay, leaving the receiving PC little opportunity to read it. OpenCode now preserves the original observation time and uses a bounded 40-minute fallback, while immediately rejecting values after the rolling reset.
<!-- /en -->

## [1.38.7] - 2026-08-10

<!-- ko -->
### 추가
- **OpenCode 공식 사용량 다중 PC 동기화** — 한 PC에서 읽은 OpenCode Go 공식 롤링·주간·월간 사용률과 초기화 시각을 기존 공유 폴더 스냅샷에 포함합니다. 현재 PC에서 공식 값을 읽지 못하면 5분 이내에 다른 PC가 관측한 최신 값으로 같은 게이지와 시간선을 표시합니다.

### 보안
- **인증 정보는 동기화 대상에서 제외** — OpenCode 로그인 쿠키, 웹 세션, 워크스페이스 URL은 공유하지 않습니다. 로컬 토큰 합계는 기존처럼 PC별 스냅샷을 병합합니다.
<!-- /ko -->

<!-- en -->
### Added
- **Multi-PC sync for official OpenCode usage** — Official OpenCode Go rolling, weekly, and monthly utilization and reset times observed on one PC are now included in the existing shared-folder snapshot. If the current PC cannot read official usage, it displays the newest value observed by another PC within five minutes, including matching gauges and timelines.

### Security
- **Authentication data stays local** — OpenCode sign-in cookies, web sessions, and workspace URLs are never synchronized. Local token totals continue to be merged from per-device snapshots as before.
<!-- /en -->

## [1.38.6] - 2026-08-10

<!-- ko -->
### 수정
- **OpenCode 로그인 버튼이 존재하지 않는 주소를 열던 문제** — 앱이 로그인과 자동 조회를 현재 404인 `https://opencode.ai/workspace`에서 시작하고 있었습니다. OpenCode 공식 페이지가 연결하는 정상 로그인 진입점 `https://opencode.ai/auth`를 사용하도록 수정했습니다.
- **OpenCode 로그인이 5분 뒤 풀린 것처럼 보이던 문제** — 로그인 후 확인한 `https://opencode.ai/workspace/{id}/go` 주소를 보존하지 않아 캐시 만료 때 다시 404로 이동하고, 조회 실패를 로그인 해제로 표시했습니다. 성공한 워크스페이스 주소를 엄격히 검증해 앱 데이터에 저장하고 자동 새로고침과 앱 재시작에서 재사용합니다. 기존 앱 전용 WebView2 쿠키 저장소는 그대로 유지합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **The OpenCode sign-in button opened a nonexistent URL** — Sign-in and automatic reads started at `https://opencode.ai/workspace`, which now returns 404. The app now uses `https://opencode.ai/auth`, the valid sign-in entry point linked by the official OpenCode site.
- **OpenCode appeared signed out after five minutes** — The successful `https://opencode.ai/workspace/{id}/go` route was discarded, so cache expiry navigated back to the 404 and presented the read failure as a sign-out. Successful workspace routes are now strictly validated, stored in app data, and reused for automatic refreshes and app restarts. The existing app-owned WebView2 cookie store remains unchanged.
<!-- /en -->

## [1.38.5] - 2026-08-10

<!-- ko -->
### 개선
- **OpenCode 상세 화면에서 중복 기간 통계 제거** — 공식 롤링·주간·월간 할당량 게이지 아래에 별도로 표시되던 로컬 DB 기준 `최근 5시간`, `최근 7일`, `이번 달` 누적 행과 중복 할당량 문구를 제거했습니다. 공식 게이지, 오늘의 토큰 타일, 데이터 출처만 남겨 Claude·Codex와 같은 정보 밀도로 정리했습니다. 로컬 기간 집계 자체는 비활성 공급자 판정을 위해 내부적으로 유지합니다.
- **OpenCode 공식 게이지에 시간선 추가** — Claude·Codex와 동일한 현재 시간 위치선을 롤링·주간·월간 게이지에 추가했습니다. 롤링은 5시간, 주간은 7일, 월간은 실제 달 길이를 기준으로 매초 위치를 갱신하며 만료된 창에는 표시하지 않습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Removed duplicate period totals from the OpenCode details** — Removed the local-database `Last 5 hours`, `Last 7 days`, and `This month` rows and the redundant quota caption below the official rolling, weekly, and monthly gauges. The panel now keeps only the official gauges, today's token tiles, and the data-source note, matching the information density of Claude and Codex. Local period aggregation remains available internally for inactive-provider detection.
- **Added timelines to the official OpenCode gauges** — Added the same current-time marker used by Claude and Codex to the rolling, weekly, and monthly gauges. Positions update every second using a 5-hour rolling window, a 7-day weekly window, and the actual calendar-month length; expired windows hide the marker.
<!-- /en -->

## [1.38.4] - 2026-08-10

<!-- ko -->
### 개선
- **OpenCode 게이지를 Claude·Codex와 같은 모습으로 통일** — OpenCode Go의 롤링·주간·월간 사용량에서 별도의 큰 배경 카드를 제거하고, 각 항목을 다른 공급자와 같은 `이름 / 사용률+초기화 / 얇은 게이지` 구조와 간격으로 정리했습니다. OpenCode 제목 행도 로그인된 경우 요청 횟수 대신 롤링 사용률을 표시하며, 주황색 게이지에는 다른 공급자와 같은 방향의 그라데이션을 적용했습니다.
- **OpenCode 초기화 문구 중복 제거** — `초기화 · 3시간 후 초기화`처럼 같은 의미가 반복되던 문구를 Claude·Codex와 동일한 초기화 표기로 맞췄습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Matched OpenCode gauges to the Claude and Codex presentation** — Removed the separate large background card around OpenCode Go rolling, weekly, and monthly usage. Each item now follows the same `label / percentage + reset / slim gauge` structure and spacing as the other providers. When signed in, the OpenCode header shows rolling usage instead of request count, and its orange gauge now uses the same gradient direction as the other providers.
- **Removed duplicate OpenCode reset wording** — Reset labels no longer repeat the same meaning, such as `Resets · resets in 3 hours`, and now use the same reset format as Claude and Codex.
<!-- /en -->

## [1.38.3] - 2026-08-10

<!-- ko -->
### 수정
- **OpenCode 로그인 창이 화면 밖에 남아 보이지 않던 문제** — WebView2 초기화를 위해 창을 화면 밖에 한 번 표시한 뒤 자동 중앙 배치에 맡겼지만, 이미 표시된 WPF 창은 위치를 다시 계산하지 않아 그대로 숨을 수 있었습니다. 로그인할 때 현재 모니터 작업 영역의 중앙 좌표를 직접 계산해 창을 복원하고 전면 활성화합니다. 음수 좌표를 쓰는 보조 모니터와 작은 작업 영역도 처리합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **The OpenCode sign-in window could remain invisible off-screen** — The window was first shown outside the desktop to initialize WebView2 and then relied on automatic centering, but WPF does not reposition a window that has already been shown. Sign-in now restores the window, calculates explicit centered coordinates inside the current monitor's work area, and brings it to the foreground. Negative-coordinate secondary monitors and small work areas are covered as well.
<!-- /en -->

## [1.38.2] - 2026-08-10

<!-- ko -->
### 추가
- **OpenCode Go 실제 사용량 게이지** — 앱 전용 로그인 창에서 OpenCode에 한 번 로그인하면 공식 콘솔이 제공하는 롤링·주간·월간 사용률과 초기화 시각을 트레이 팝업에 표시합니다. Chrome·Edge 프로필이나 기존 브라우저 쿠키는 읽지 않으며 로그인 세션은 앱 전용 WebView2 데이터 폴더에 격리합니다.
- **안전한 로컬 통계 폴백** — 로그인하지 않았거나 웹 인증·내부 응답 파싱이 실패해도 OpenCode 로컬 DB의 토큰·요청·비용 통계는 그대로 표시합니다. 임의 퍼센트는 다시 만들지 않습니다.
<!-- /ko -->

<!-- en -->
### Added
- **Actual OpenCode Go usage gauges** — After a one-time sign-in in the app-owned login window, the tray popup shows the rolling, weekly, and monthly usage percentages and reset times supplied by the official OpenCode console. The app does not read Chrome or Edge profiles or reuse existing browser cookies; its session stays isolated in an app-owned WebView2 data folder.
- **Safe local-statistics fallback** — OpenCode token, request, and cost totals from the local database remain available when the user has not signed in or web authentication/internal-response parsing fails. No estimated percentage is reintroduced.
<!-- /en -->

## [1.38.1] - 2026-08-10

<!-- ko -->
### 개선
- **OpenCode 사용량을 실제 할당량처럼 보이게 하던 추정 퍼센트 제거** — 기존 막대는 오늘 출력 토큰을 최근 7일 중 가장 많이 쓴 날과 비교했을 뿐인데, 일반 할당량 막대와 같은 모양이라 남은 무료 한도로 오해할 수 있었습니다. OpenCode가 무료·Zen 총량을 로컬 DB에 제공하지 않으므로 임의 퍼센트를 더 이상 만들지 않습니다.
- **OpenCode 기간별 실제 누적 사용량 추가** — 로컬 DB에서 최근 5시간·7일·이번 달의 토큰, 요청 수, 기록된 비용을 집계해 표시합니다. 오늘 사용이 없어도 기간 내 기록이 있으면 OpenCode 패널을 유지합니다.
- **OpenCode 무료·Go 한도 소진 상태 표시** — OpenCode가 저장한 `FreeUsageLimitError` 또는 `GoUsageLimitError`와 `retry-after`를 읽어, 한도가 실제로 유효한 동안 소진 상태와 초기화 시각을 표시합니다. 재시도 시각이 지난 오류는 현재 한도 소진으로 표시하지 않습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Removed the estimated percentage that made OpenCode usage look like a real quota** — The old bar only compared today's output tokens with the busiest day in the previous seven days, but its quota-style presentation could be mistaken for remaining free allowance. OpenCode does not expose free or Zen quota totals in its local database, so the app no longer invents a percentage.
- **Added actual OpenCode usage totals by period** — The panel now aggregates tokens, request count, and recorded cost from the local database for the last five hours, seven days, and current month. It remains available when there is no usage today but the selected periods contain records.
- **Added OpenCode free and Go limit status** — Stored `FreeUsageLimitError` / `GoUsageLimitError` data and `retry-after` are used to show an active limit and its reset time. Expired retry windows are not presented as a current limit.
<!-- /en -->

## [1.38.0] - 2026-08-07

<!-- ko -->
### 수정
- **오늘 요청이 없는 PC 에서 Codex 시간선이 막대 오른쪽 끝에 박히던 문제** — 시간선(지금이 창의 어디쯤인지 알려주는 세로 마커)의 위치는 리셋 시각에서 역산하는데, 리셋이 이미 지난 값을 "100% 경과"로 잘라서 쓰고 있었습니다. Codex 사용량을 로컬 로그에서 읽을 때 며칠 전 세션의 이미 끝난 창을 그대로 집어 오기 때문에, 오늘 Codex 를 쓰지 않은 PC 는 항상 이 상태였습니다. 같은 화면에서 리셋 표시는 비어 있는데 시간선만 100% 였습니다. 이제 지금이 창 밖이면 위치를 지어내지 않고 마커를 숨기며, 이미 끝난 창의 사용률도 함께 버립니다.
- **끝난 창의 사용률이 계속 표시되던 문제** — 위와 같은 원인으로, 며칠 전에 찍힌 "69% 사용" 같은 숫자가 오늘 것처럼 남아 있었습니다.
- **`resets_at` 이 0 이거나 로그가 며칠 전 것일 때 리셋 시각이 과거로 계산되던 문제** — 0 을 그대로 받으면 1970년이 되고, 리셋 추정치의 기준 시각도 날짜를 보지 않아 옛날 로그의 첫 줄이 잡혔습니다. 둘 다 시간선을 오른쪽 끝으로 밀던 경로입니다.
- **앱 종료 중 언어 변경이 예외를 내던 문제** — 언어 변경 이벤트는 정적 이벤트라 구독을 해제하지 않으면 화면이 사라진 뒤에도 붙어 있었습니다.

### 추가
- **에이전트 전체 다중 PC 동기화** — 지금까지는 Claude 의 할당량만 다른 PC 값으로 채워졌고, Codex·Gemini CLI·OpenCode 는 스냅샷을 저장만 하고 아무도 읽지 않았으며 Antigravity 는 동기화 자체가 없었습니다. 이제 계정 단위로 내려오는 할당량(Codex·Antigravity)은 이 PC 에 데이터가 없을 때 다른 PC 가 관측한 값으로 채웁니다. 어느 PC 에서 보든 같은 사용률과 같은 시간선 위치가 나옵니다. 창 길이(주간/5시간)도 함께 동기화하므로 주간 창 계정에서도 마커가 제자리에 섭니다.
- **Antigravity 다중 PC 동기화** — 모델별 잔여 할당량과 등급이 기기 간에 공유됩니다. 이 PC 에서 Antigravity 에 로그인하지 않았어도 다른 PC 가 올린 값으로 같은 패널을 볼 수 있습니다.

### 개선
- **Gemini CLI·OpenCode 막대가 합산 기준으로 바뀜** — 이 둘은 서버 할당량이 없어 "최근 가장 많이 쓴 날" 대비로 막대를 그립니다. 여러 PC 를 합산하면 숫자는 합계인데 막대만 이 PC 몫이라 어긋나 있었습니다. 이제 막대도 합산 토큰으로 계산합니다. 기기마다 기준이 다른 값이라 percent 자체는 기기 간에 주고받지 않습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **The Codex timeline marker sat pinned to the far right on PCs with no requests today** — The marker's position is derived backwards from the reset time, and a reset that had already passed was clamped to "100% elapsed". Reading Codex usage from local logs picks up the most recent `rate_limits` regardless of date, so a PC that hasn't used Codex today always landed in that state — showing a blank reset label next to a 100%-elapsed marker. When the current moment falls outside the window, the marker is now hidden rather than placed somewhere invented, and the finished window's usage is discarded with it.
- **Usage from an already-closed window kept showing** — Same cause: a "69% used" figure recorded days ago stayed on screen as if it were today's.
- **Reset times computed into the past from `resets_at: 0` or from older logs** — A literal 0 became 1970, and the estimated-reset anchor ignored the date, so the first line of an old log was used. Both pushed the marker to the far right.
- **Language changes threw during shutdown** — The language-changed event is static, and the subscription was never released, so it stayed attached after the window was gone.

### Added
- **Multi-PC sync for every agent** — Only Claude's quota was ever read back from other PCs; Codex, Gemini CLI, and OpenCode wrote snapshots nobody read, and Antigravity had no sync at all. Account-level quotas (Codex, Antigravity) now fill in from whichever PC observed them when the local machine has nothing, so the usage figure and timeline position match on every PC. Window length (weekly / 5-hour) travels with the quota, so the marker lands correctly on weekly-window accounts too.
- **Antigravity multi-PC sync** — Per-model remaining quota and tier are shared between machines. A PC that isn't signed in to Antigravity can show the same panel from another PC's reading.

### Improved
- **Gemini CLI and OpenCode bars now reflect merged totals** — Neither has a server quota, so their bars are drawn against your busiest recent day. With several PCs merged, the numbers were a sum while the bar showed only this machine's share. The bar is now computed from the merged tokens. The percentage itself is never exchanged between machines, since each one measures against its own baseline.
<!-- /en -->

## [1.37.1] - 2026-08-06

<!-- ko -->
### 수정
- **예보 제공처가 "자동"이면 모델 선택이 무시되던 문제** — 설정에서 제공처를 자동으로 둔 채 예보 모델만 바꾸면, 화면에는 선택된 것으로 보이고 설정도 저장되지만 실제 조회에는 반영되지 않았습니다. 제공처 기본값이 자동이라 모델만 바꾸는 것이 가장 자연스러운 조작인데, 그 경로가 통째로 동작하지 않았습니다. 한국에서는 기본 모델이 실제보다 2~3도 낮게 잡히기 때문에(8월 6일 서울 기준 자동 35.3°C 대 ECMWF 37.8°C) 폭염 알림이 갈지 말지가 이 설정에 달려 있습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Model selection was ignored when the forecast source was "Automatic"** — Leaving the source on Automatic and changing only the model appeared to work and was saved to settings, but never reached the actual request. Since Automatic is the default source, changing just the model is the natural thing to do — and that path did nothing at all. In Korea the default model reads 2–3°C low (35.3°C vs ECMWF's 37.8°C for Seoul on 6 August), which decides whether a heat alert is sent.
<!-- /en -->

## [1.37.0] - 2026-08-06

<!-- ko -->
### 수정
- **폭염·한파·비 알림이 매시간 반복 발송되던 문제** — 같은 알림을 두 번 보내지 않으려고 쓰는 열쇠값에 시각이 "시간 단위"로 들어가 있어서, 한 시간이 지나면 같은 내용이 새 알림으로 취급됐습니다. 실제로 폭염이 이어지는 날에는 하루에 열 번 넘게 같은 알림이 나갔습니다. 이 세 알림은 그날 하루 예보 하나만 보고 판정하므로, 이제 하루에 한 번만 보냅니다. 현재 풍속으로 판정하는 강풍 알림은 6시간 간격을 유지합니다.
- **발송 기록이 무한히 쌓이던 문제** — 오래된 기록을 지우는 코드가 실제로는 공식 특보 기록만 통째로 지우고 나머지는 전혀 지우지 않았습니다. 그래서 기록 파일은 계속 커지고, 정작 공식 특보는 중복 방지가 풀려 같은 특보가 반복 발송될 수 있었습니다. 이제 발송 시각을 함께 남겨 7일이 지난 기록만 정리합니다. 기존 기록은 자동으로 새 형식으로 옮겨집니다.
- **앱 종료 중 예외로 종료 절차가 중단되던 문제** — 중복 실행 방지용 잠금을 해제하는 코드가, 종료가 다른 스레드에서 시작된 경우 예외를 던져 이후 정리 과정이 통째로 건너뛰어졌습니다.

### 추가
- **예보 제공처와 모델 선택** — 설정 → 날씨 탭에서 예보를 어디서 받을지 고를 수 있습니다. 기존 Open-Meteo 에 더해 MET Norway 를 쓸 수 있고, Open-Meteo 안에서는 ECMWF·NOAA GFS·DWD ICON·UK Met Office·JMA·MET Norway 모델을 직접 지정할 수 있습니다. 고른 소스가 응답하지 않으면 다른 소스로 자동 전환합니다.
- **일본 기상청(JMA) 공식 특보** — 일본 위치에서 기상청이 발표한 경보를 받습니다. 주의보는 한 지역에 백 건 넘게 걸리는 일이 흔해 알리지 않고, 경보와 특별경보만 발송합니다.

### 개선
- **한국에서 기온이 실제보다 낮게 잡히던 문제 해결 수단** — 기상청(KMA) 이 2026년 3월 말 예보 모델을 바꾸면서 Open-Meteo 로 오던 한국 데이터가 끊겼고, 그 결과 자동 선택이 일본 JMA 모델로 넘어가 있었습니다. 8월 6일 서울 기준으로 자동 선택은 34.8°C 인데 ECMWF 는 37.8°C, NOAA GFS 는 38.4°C 였습니다. 폭염·한파 알림은 기준 온도를 넘는지로 판정하므로 이 차이가 알림 발송 여부를 바꿉니다. 이제 모델을 직접 고를 수 있습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Heat, cold, and rain alerts fired every hour** — The key used to avoid sending the same alert twice included the hour, so once an hour passed the identical alert counted as new. On a stretch of hot days that meant more than ten identical notifications a day. These three alerts are decided from a single daily forecast, so they now go out once per day. Wind alerts, which read the current observed speed, keep their 6-hour interval.
- **The sent-alert log grew without bound** — The cleanup code only ever deleted official-warning entries and never touched the rest. The file kept growing, while official warnings lost their duplicate protection and could be re-sent repeatedly. Entries now carry a timestamp and are pruned after 7 days; existing records migrate automatically.
- **An exception during shutdown aborted the rest of the cleanup** — Releasing the single-instance lock threw when shutdown began on a different thread, skipping everything after it.

### Added
- **Forecast source and model selection** — Settings → Weather now lets you choose where forecasts come from. MET Norway joins Open-Meteo as a source, and within Open-Meteo you can pick the ECMWF, NOAA GFS, DWD ICON, UK Met Office, JMA, or MET Norway model directly. If the chosen source returns nothing, another one takes over automatically.
- **Official JMA warnings for Japan** — Locations in Japan now receive warnings issued by the Japan Meteorological Agency. Advisories are omitted (a single region routinely carries over a hundred), leaving warnings and emergency warnings.

### Improved
- **A way out of under-reported temperatures in Korea** — When the KMA switched forecast models in late March 2026, its data stopped reaching Open-Meteo, and automatic selection quietly fell back to Japan's JMA model. For Seoul on 6 August, automatic selection read 34.8°C while ECMWF read 37.8°C and NOAA GFS 38.4°C. Heat and cold alerts trigger on crossing a fixed threshold, so that gap decides whether an alert is sent at all. You can now pick the model yourself.
<!-- /en -->

## [1.36.2] - 2026-08-06

<!-- ko -->
### 수정
- **Codex 사용량 막대에 시간선이 보이지 않던 문제** — 시간선(지금이 창의 어디쯤인지 알려주는 세로 마커)의 위치를 언제나 5시간/7일 창 기준으로 계산하고 있었습니다. Codex 는 계정에 따라 주간 창을 쓰기 때문에, 이 경우 진행률이 음수로 나와 0 으로 잘리면서 마커가 막대 왼쪽 끝에 숨어 한 번도 보이지 않았습니다. 이제 응답이 알려주는 실제 창 길이로 계산합니다.

### 개선
- **Codex 막대에도 페이스 툴팁 추가** — 막대에 마우스를 올리면 Claude 와 같이 "시간 67% 경과 · 사용 61% · 6%p 여유" 형태로 지금 페이스를 알려줍니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **The timeline marker never appeared on Codex usage bars** — The marker's position (showing where you are within the current window) was always computed against a 5-hour / 7-day window. Codex uses a weekly window on some accounts, where that math goes negative and clamps to zero — pinning the marker to the far-left edge, out of sight. It now uses the window length the API actually reports.

### Improved
- **Pace tooltip on Codex bars** — Hovering a Codex bar now shows the same pace readout as Claude: "Time 67% · Used 61% · 6pp behind".
<!-- /en -->

## [1.36.1] - 2026-08-04

<!-- ko -->
### 수정
- **오늘의 토큰이 실제 사용량보다 훨씬 적게 집계되던 문제** — 세션 파일이 계속 기록되는 동안에는 "직전 조회 이후 새로 쓰인 부분"만 더해 오늘 총량으로 표시하고, 그 값을 그대로 히스토리에 덮어썼습니다. 작업 중일수록 어긋나서 실측에서는 캐시 읽기가 실제의 1/3 수준까지 떨어졌고, 7일 추이 차트와 비용 추정치도 함께 낮게 나왔습니다. 이제 오늘 기록이 있을 수 있는 파일만 골라 매번 전체를 다시 읽어, 몇 번을 새로고침해도 같은 총량이 나옵니다. 시간대별 차트가 경로에 따라 캐시 토큰을 빼먹던 문제도 함께 고쳤습니다.
- **로그인이 풀렸는데 "API 응답 대기 중"으로만 표시되던 문제** — Claude Code 가 로그아웃하면서 액세스 토큰을 빈 값으로 남기는 경우가 있는데, 앱이 이를 토큰이 있는 것으로 보고 인증 없이 요청을 보냈습니다. 그 응답(429)을 일시적 제한으로 오해해 "잠시 후 자동 재시도" 안내만 반복하며 사용량이 0% 로 고정됐습니다. 이제 빈 토큰을 로그인 필요 상태로 인식하고 로그인 안내를 표시합니다.
- **할당량을 못 받아온 상태를 "0% 사용 · 잔량 100%"로 표시하던 문제** — 한 번도 조회에 성공하지 못한 상태에서 여유가 가득한 것처럼 보였습니다. 이제 조회 전에는 "—"와 안내 문구로 구분해 표시합니다.

### 개선
- 트랜스크립트 스캔을 백그라운드 스레드로 옮겨, 새로고침 중 창이 잠깐 멈추던 현상을 없앴습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Today's token count was far lower than actual usage** — While a session file was still being written, the app summed only the bytes added since the previous scan, showed that as the day's total, and overwrote history with it. The more you worked, the further it drifted — measured on real data, cache-read tokens fell to about a third of actual, dragging the 7-day chart and cost estimate down with them. It now re-reads every file that could hold today's entries in full, so repeated refreshes produce the same total. The hourly chart, which dropped cache tokens on one of the two code paths, is consistent again.
- **A signed-out state showed only "waiting for API response"** — Claude Code can leave the access token as an empty value when signing out. The app treated that as a real token, sent an unauthenticated request, and read the resulting 429 as a temporary limit — so it kept showing "retrying automatically" with usage stuck at 0%. An empty token is now recognized as signed-out and shows sign-in guidance.
- **An unfetched quota was shown as "0% used · 100% remaining"** — Before any successful fetch, the app looked like it had full headroom. It now shows "—" with an explanatory line until a quota is actually retrieved.

### Improved
- Transcript scanning moved off the UI thread, removing a brief freeze during refresh.
<!-- /en -->

## [1.36.0] - 2026-07-23

<!-- ko -->
### 추가
- **자동 업데이트 켜기/끄기와 대기 시간 설정** — 설정 → 일반 탭에 "새 버전을 자동으로 설치" 토글과 대기 시간 슬라이더(10~300초, 기본 60초)를 추가했습니다. 끄면 업데이트 창은 그대로 뜨되 카운트다운 없이 직접 실행해야 합니다. 자동 설치를 끄면 대기 시간 슬라이더도 함께 비활성화됩니다.

### 개선
- **설정 일반 탭 스크롤** — 항목이 늘어 창 높이 상한을 넘기면서 마지막 항목이 잘리던 문제를 막기 위해, 일반 탭에 스크롤을 추가했습니다.
<!-- /ko -->

<!-- en -->
### Added
- **Auto-update toggle and wait-time setting** — Settings → General now has an "Install new versions automatically" toggle and a wait-time slider (10–300s, default 60s). With it off, the update window still appears but installs only when you press the button yourself. Turning it off also disables the wait-time slider.

### Improved
- **Scrolling in the General settings tab** — As settings piled up, the tab exceeded the window height cap and silently clipped the last item. The tab now scrolls.
<!-- /en -->

## [1.35.2] - 2026-07-23

<!-- ko -->
### 보안
- **무결성 검증이 불가능한 릴리스는 자동 설치하지 않습니다** — 릴리스에 SHA256 체크섬 파일이 첨부되지 않은 경우, 지금까지는 검증을 조용히 건너뛰고 그대로 설치했습니다. 이제 이런 릴리스에서는 카운트다운 자동 설치를 하지 않고 업데이트 창에 안내를 표시하며, 사용자가 직접 "지금 업데이트"를 누른 경우에만 진행합니다. 다운로드도 시작하기 전에 판단하므로 불필요한 내려받기가 없습니다.
<!-- /ko -->

<!-- en -->
### Security
- **Releases that can't be verified are never installed unattended** — When a release ships without a SHA256 checksum file, the app used to skip verification silently and install anyway. Such releases now skip the countdown auto-install and show a notice in the update window; the install proceeds only if you press "Update Now" yourself. The check happens before the download starts, so nothing is fetched needlessly.
<!-- /en -->

## [1.35.1] - 2026-07-23

<!-- ko -->
### 수정
- **업데이트 교체에 실패하면 앱이 사라지던 문제** — 교체 스크립트가 기존 실행 파일을 먼저 삭제한 뒤 새 파일을 옮기는 구조여서, 삭제만 성공하고 이동이 실패하면(파일 잠금·권한 문제 등) 앱이 통째로 없어졌습니다. 이제 기존 파일을 백업으로 물러 두고 교체하며, 실패하면 원래 버전을 제자리로 되돌려 다시 실행합니다.
- **업데이트 실행기를 시작하지 못해도 앱이 종료되던 문제** — 교체 스크립트를 띄우지도 못한 상태에서 앱을 종료해 아무것도 남지 않던 동작을 고쳐, 오류를 알리고 앱을 그대로 유지합니다.

### 개선
- 교체 스크립트가 종료할 프로세스 이름을 대상 실행 파일에서 유도하도록 정리해, 다른 이름으로 설치된 경우에도 올바르게 동작합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **The app could disappear when the update swap failed** — The swap script deleted the existing executable before moving the new one into place, so a successful delete followed by a failed move (file lock, permissions) left no app at all. It now moves the original aside as a backup and, on any failure, restores it and relaunches the previous version.
- **The app no longer exits when the installer fails to start** — It used to shut down even when the swap script never launched, leaving nothing running. It now surfaces the error and keeps running.

### Improved
- The swap script derives the process name to stop from the target executable instead of hardcoding it.
<!-- /en -->

## [1.35.0] - 2026-07-23

<!-- ko -->
### 추가
- **업데이트 모달 자동 적용 카운트다운** — 업데이트 창이 뜨면 60초 카운트다운이 시작되고, 그 안에 아무것도 누르지 않으면 자동으로 설치가 진행됩니다. 카운트다운 중 "이번 버전 건너뛰기"나 "지금 업데이트"를 누르면 즉시 처리되고, 창을 닫으면 자동 적용이 취소됩니다.

### 개선
- **앱 시작 시 업데이트 확인 신뢰성 개선** — 사용량 조회가 끝나기를 기다리지 않고 시작 직후 독립적으로 확인하며, 부팅 직후 네트워크가 아직 준비되지 않아 실패하면 15초·1분·3분 간격으로 재시도합니다. 이전에는 첫 시도가 실패하면 다음 확인이 24시간 뒤였습니다.
- **확인 결과가 화면에 보이도록 수정** — 새 버전을 찾아도 아무 표시가 없던 문제를 고쳐, 시작 시 새 버전을 찾으면 업데이트 창을 바로 띄우고 백그라운드 확인 결과는 푸터에 표시합니다. 좌하단 버전에 마우스를 올리면 마지막 확인 시각과 결과를 볼 수 있습니다.
- **자동 업데이트 반복 방지** — 자동 적용이 끝내 반영되지 않은 버전은 다음 실행에서 자동 재시도 대신 직접 실행하도록 안내해, 다운로드와 재시작이 반복되는 상황을 막습니다.
- **업데이트 창을 잠시 치워둘 수 있도록 개선** — 업데이트 창의 항상 위 고정을 해제하고 최소화 버튼(—)을 추가해, 다른 작업을 하는 동안 창이 계속 위에 떠 있지 않습니다. 최소화해도 카운트다운은 계속 흐르므로, 자동 적용을 멈추려면 창을 닫거나 "이번 버전 건너뛰기"를 누르면 됩니다.
<!-- /ko -->

<!-- en -->
### Added
- **Auto-apply countdown in the update dialog** — When the update window opens, a 60-second countdown starts and the update installs automatically if you do nothing. Pressing "Skip This Version" or "Update Now" during the countdown takes effect immediately, and closing the window cancels the auto-apply.

### Improved
- **More reliable update check at startup** — The check now runs independently instead of waiting for the usage refresh to finish, and retries after 15s, 1m, and 3m when it fails because the network isn't up yet right after boot. Previously a single failed attempt meant no further check for 24 hours.
- **Check results are actually visible** — Finding a new version used to leave no trace on screen. The startup check now opens the update window directly, background checks show the result in the footer, and hovering the version in the bottom-left shows the last check time and outcome.
- **Guard against repeated auto-updates** — If an automatic update never takes effect, the next launch asks you to run it manually instead of retrying automatically, preventing a download-and-restart loop.
- **The update window can be set aside** — It no longer stays pinned above every other window, and a minimize button (—) was added. The countdown keeps running while minimized, so close the window or press "Skip This Version" to stop the auto-apply.
<!-- /en -->

## [1.34.12] - 2026-07-15

<!-- ko -->
### 개선
- **릴리스 빌드 워크플로우를 Node 24 런타임으로 갱신** — GitHub Actions의 Node 20 지원 종료 경고를 제거하기 위해 릴리스 자동화에 쓰는 액션(checkout·setup-dotnet·upload/download-artifact·gh-release)을 Node 24 버전으로 올렸습니다. 앱 동작에는 변화가 없습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Updated the release workflow to Node 24 runtimes** — Bumped the GitHub Actions used by release automation (checkout, setup-dotnet, upload/download-artifact, gh-release) to their Node 24 versions to clear the Node 20 deprecation warnings. No change to app behavior.
<!-- /en -->

## [1.34.11] - 2026-07-15

<!-- ko -->
### 수정
- **Codex 사용량 창 라벨이 실제 기간과 어긋나던 문제 수정** — ChatGPT가 rate_limits 구조를 바꿔 주간(7일) 창을 `primary`로 내려보내면서, 트레이가 이를 "단기 윈도우"로 잘못 표시하던 문제를 고쳤습니다. 이제 `window_minutes`를 읽어 창 길이에 맞는 라벨("주간 윈도우"·"5시간 윈도우" 등)을 표시하고, 여러 창이 오면 짧은 창부터 순서대로 배치합니다. 창이 하나뿐이면 두 번째 게이지는 숨깁니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Fixed Codex usage window labels not matching the actual period** — After ChatGPT changed its rate_limits to send the weekly (7-day) window as `primary`, the tray mislabeled it as "Short window". The tray now reads `window_minutes` and shows a length-accurate label ("Weekly window", "5-hour window", etc.), ordering multiple windows shortest-first and hiding the second gauge when only one window is present.
<!-- /en -->

## [1.34.10] - 2026-07-15

<!-- ko -->
### 수정
- **Codex 토큰 집계가 "—"로 멈추던 문제 수정** — ChatGPT 사용량 응답에서 장기 윈도우(`rate_limits.secondary`)가 `null`로 바뀌면서 로컬 세션 로그 파싱이 예외로 중단돼, 입력·출력·캐시 토큰이 모두 "—"로 표시되던 버그를 고쳤습니다. null 윈도우를 안전하게 처리하도록 파서를 보강해 토큰 집계가 정상 복구됩니다.

### 개선
- **Codex 사용량 조회 상태 표시 추가** — 토큰 데이터를 아직 불러오는 중이면 "불러오는 중", 오늘 사용 기록이 없으면 "오늘 사용 없음"으로 구분해 안내하고, 데이터가 없을 때 "—" 타일 4개 대신 안내 문구를 표시합니다. 토큰 4타일 노출 기준도 퍼센트가 아닌 실제 토큰 유무로 통일했습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Fixed Codex token totals getting stuck at "—"** — When ChatGPT's usage response began returning the long window (`rate_limits.secondary`) as `null`, the local session-log parser threw and skipped every token line, showing input/output/cache tokens as "—". The parser now handles the null window safely and token totals are restored.

### Improved
- **Added Codex usage loading / empty states** — The token area now shows "Loading…" while data is still being read and "No usage today" when there is none, instead of a row of four "—" tiles. Tile visibility is now driven by actual token data rather than the usage percentage.
<!-- /en -->

## [1.34.9] - 2026-07-13

<!-- ko -->
### 개선
- **고정 사용량 미니 스트립을 더 평평하게 다듬고 가장자리 스냅을 추가** — 고정 사용량 모드에서 바깥 카드 테두리와 그림자를 제거하고, 에이전트 이름·게이지·퍼센트만 남긴 얇은 스트립으로 정리했습니다. 드래그하면 화면 가장자리에 자석처럼 붙고, 투명도 설정은 그대로 유지됩니다. #85
<!-- /ko -->

<!-- en -->
### Improved
- **Flattened the pinned usage strip and added edge snapping** — The pinned usage mode now drops the outer card frame and shadow, leaving only the agent name, gauge, and percentage in a thin strip. Dragging now snaps the strip to the nearest screen edge, and the opacity setting still applies. #85
<!-- /en -->

## [1.34.8] - 2026-07-13

<!-- ko -->
### 수정
- **설정창 투명도 슬라이더 초기화 예외 수정** — 미니 패널 투명도 슬라이더가 XAML 초기화 중 값을 밀어 넣으면서 `SettingsWindow` 생성 시 `NullReferenceException` 이 나던 회귀를 수정했습니다. 슬라이더는 이제 초기화가 끝난 뒤에만 저장 로직을 타며, 설정 창이 예외 없이 열립니다. #83

### 개선
- **고정 사용량 미니 스트립을 더 납작하게 정리** — 고정된 팝업 모드를 위아래로 길게 쌓지 않고, 시계 위에 얹히는 낮은 스트립 형태로 다시 다듬었습니다. 이제 헤더와 푸터는 접히고, 에이전트 이름과 게이지만 보이며 설정은 트레이 메뉴에서 열 수 있습니다. 투명도 설정은 그대로 유지됩니다. #84
<!-- /ko -->

<!-- en -->
### Fixed
- **Fixed the settings-window opacity slider initialization crash** — The mini panel opacity slider was triggering a `NullReferenceException` while the XAML was still initializing. The slider now waits until initialization finishes before saving, so the settings window opens normally again. #83

### Improved
- **Refined the pinned usage view into a flatter mini strip** — The pinned popup mode no longer stacks into a tall panel. It now appears as a low strip above the taskbar clock with only agent names and gauges visible. The header and footer collapse, Settings moved to the tray menu, and opacity control remains available. #84
<!-- /en -->

## [1.34.7] - 2026-07-13

<!-- ko -->
### 개선
- **사용량 전용 미니 패널과 투명도 설정 추가** — 팝업에서 날씨, 히스토리 차트, 푸터를 덜어낸 반투명 미니 패널 모드를 추가하고, 미니 패널 투명도는 설정에서 50%~100% 범위로 조절할 수 있게 했습니다. #82
<!-- /ko -->

<!-- en -->
### Improved
- **Added a usage-only mini panel and opacity control** — The popup now has a translucent mini panel mode that strips out weather, history charts, and the footer, and the mini panel opacity can be adjusted from 50% to 100% in settings. #82
<!-- /en -->

## [1.34.6] - 2026-07-13

<!-- ko -->
### 개선
- **작업표시줄 위 팝업 고정 옵션 추가** — 포커스를 잃어도 닫히지 않는 반투명 팝업 모드를 설정에서 켜고 끌 수 있게 했습니다. 기본 트레이 클릭/자동 숨김 동작은 그대로 유지됩니다. #81
<!-- /ko -->

<!-- en -->
### Improved
- **Added an optional sticky popup mode above the taskbar** — You can now turn on a slightly translucent popup mode that stays open even after focus is lost. The default tray-click-and-hide behavior remains unchanged. #81
<!-- /en -->

## [1.34.5] - 2026-07-13

<!-- ko -->
### 개선
- **Codex 사용량 게이지에 그라데이션 적용** — Codex의 단기/장기 사용량 막대를 Claude와 같은 톤의 그라데이션으로 바꿔, 다중 에이전트 팝업에서 색감이 덜 평면적으로 보이도록 정리했습니다. 기능 동작은 그대로고 시각 스타일만 개선했습니다. #80
<!-- /ko -->

<!-- en -->
### Improved
- **Added gradient styling to the Codex usage gauges** — The Codex short/long usage bars now use a Claude-like gradient so the multi-agent popup feels more visually consistent and less flat. Behavior is unchanged; this is a visual-only refinement. #80
<!-- /en -->

## [1.34.4] - 2026-07-11

<!-- ko -->
### 개선
- **Claude 외 Codex 사용량 게이지에 시간선 오버레이 추가** — Claude에만 있던 시간선 마커를 Codex의 단기/장기 사용량 막대에도 적용해, 현재 시점을 진행 막대 위에서 바로 확인할 수 있게 했습니다. Codex도 Claude와 같은 5시간/7일 리셋 구조를 쓰므로 동일한 시간선 표현을 공유합니다. #79
<!-- /ko -->

<!-- en -->
### Improved
- **Added a time-marker overlay to the Codex usage gauges** — The time marker that previously existed only on Claude is now shown on Codex's short/long usage bars too, so the current moment is visible directly on the progress bars. Codex uses the same 5-hour/7-day reset structure as Claude, so it now shares the same time-line treatment. #79
<!-- /en -->

## [1.34.3] - 2026-07-09

<!-- ko -->
### 추가
- **멀티 PC 사용량 스냅샷 동기화 추가** — 설정의 `다중 PC 동기화`에서 공유 폴더를 지정하면 각 PC가 민감정보 없는 일일 표시용 스냅샷만 저장하고, 오늘 사용량은 장치별 최신 스냅샷을 합산해 표시합니다. Claude API가 한 PC에서 실패하거나 쿨다운 중이어도 다른 PC의 신선한 quota 스냅샷을 읽어 5시간/7일 사용량 표시를 유지합니다. 원본 로그, OAuth 토큰, 자격증명은 동기화하지 않으며 계정 식별자는 해시로만 저장합니다. #78
<!-- /ko -->

<!-- en -->
### Added
- **Added multi-PC usage snapshot sync** — A new `Multi-PC Sync` settings section lets you choose a shared folder. Each PC writes only non-sensitive daily display snapshots, and today's local usage is shown by merging the latest snapshot per device. If the Claude API fails or is in cooldown on one PC, the tray can still show a fresh quota snapshot from another PC for the 5-hour/7-day windows. Raw logs, OAuth tokens, and credentials are never synced, and account identity is stored only as a hash. #78
<!-- /en -->

## [1.34.2] - 2026-07-09

<!-- ko -->
### 수정
- **7일·추가 크레딧 사용량 막대 두께 정상화** — 7일 윈도우 막대와 추가 크레딧(Extra Usage) 막대가 5시간 막대보다 얇게 잘려 보이던 문제를 수정했습니다. 막대를 담는 행이 고정 높이(6px)로 묶여 있어, 8px로 그려지는 시간 마커 오버레이(7일)와 4px 막대+여백(추가 크레딧)이 아래쪽에서 잘린 것이 원인입니다. 두 행을 5시간 막대와 동일하게 `Auto` 높이로 바꿔 같은 두께로 표시되도록 했습니다. v1.34.0에서 시간 마커 오버레이를 추가할 때 5시간 행만 갱신되고 7일 행에 예전 고정 높이가 남아 있던 회귀입니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Normalized the 7-day and extra-credit usage bar thickness** — Fixed the 7-day window bar and the extra-credit (Extra Usage) bar rendering thinner (clipped) than the 5-hour bar. The row hosting each bar was pinned to a fixed 6px height, clipping the 8px time-marker overlay (7-day) and the 4px bar-plus-margin (extra credit) at the bottom. Both rows now use `Auto` height like the 5-hour bar, so they render at the same thickness. This was a regression from v1.34.0, where adding the time-marker overlay updated only the 5-hour row and left the old fixed height on the 7-day row.
<!-- /en -->

## [1.34.1] - 2026-07-08

<!-- ko -->
### 수정
- **시간 진행률 마커 — 윈도우 초반 과장 완화** — 리셋 직후처럼 경과 시간이 아주 짧을 때(5시간 윈도우 5분 미만, 7일 윈도우 2시간 미만) 사용량이 시간을 크게 앞지른 것처럼 막대가 대부분 주황으로 보이던 문제를 완화했습니다. 이 구간에서는 소진 예측과 동일한 하한을 적용해 페이스 판정(초과색·빠름/여유 문구)을 유보하고, 툴팁에 "페이스 측정 중"으로 표시합니다. 함께 Claude 뷰모델의 미사용 새로고침 로직(중복 죽은 코드)을 제거해 내부를 정리했습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Time-progress marker — less exaggeration early in the window** — When very little time has elapsed (under 5 min in the 5-hour window, under 2 h in the 7-day window), the bar no longer looks mostly amber as if usage had raced far ahead of elapsed time. In that range it applies the same lower bound as the depletion estimate, holding off the pace verdict (amber overflow / ahead-behind wording) and showing "measuring pace" in the tooltip. Also removed the unused refresh logic in the Claude view model (dead duplicate) as cleanup.
<!-- /en -->

## [1.34.0] - 2026-07-08

<!-- ko -->
### 추가
- **5시간·7일 사용량 막대에 시간 진행률 마커 오버레이 추가** — 사용량 막대 위에 현재 시각이 윈도우의 어디쯤인지를 흰 세로선(마커)으로 표시하고, 사용 속도가 시간 경과를 앞지른 구간만 주황으로 칠해 "지금 페이스가 빠른지 여유로운지"를 육안으로 바로 알 수 있게 했습니다. 시간 진행률은 리셋 시각에서 역산(경과 = 1 − 남은시간 / 윈도우 길이)하며 기존 1초 카운트다운 타이머에 얹어 부드럽게 갱신됩니다. 마커에 마우스를 올리면 `시간 31% 경과 · 사용 41% · 10%p 빠름`처럼 시간 경과 대비 사용량 페이스 요약(4개 언어)이 표시됩니다.
<!-- /ko -->

<!-- en -->
### Added
- **Time-progress marker overlay on the 5-hour and 7-day usage bars** — A white vertical marker now shows where the current moment sits within each window, and only the portion where usage has outpaced elapsed time is filled amber — so you can tell at a glance whether you're burning fast or have room to spare. Time progress is derived from the reset time (elapsed = 1 − remaining / window length) and updates smoothly on the existing 1-second countdown timer. Hovering the marker shows a pace summary in all four languages, e.g. `Time 31% · Used 41% · 10pp ahead`.
<!-- /en -->

## [1.33.8] - 2026-07-03

<!-- ko -->
### 수정
- **업데이트 모달이 열리지 않던 문제 수정 + 상단 배너 제거** — 상단 초록색 "새 버전 업데이트" 배너를 제거하고, 업데이트는 좌하단 버전 클릭 → 모달로만 안내하도록 단순화했습니다. 모달이 뜨지 않던 근본 원인도 수정했습니다: `UpdateDialog` 를 (항상 숨는) `Topmost` 팝업의 `Owner` 로 지정하던 탓에, 팝업이 포커스를 잃고 숨는 순간 소유된 모달까지 함께 숨겨져 `ShowDialog()` 가 무한 대기하고 이후 클릭이 전부 무반응이 되던 문제였습니다. 이제 모달은 Owner 없이 `Topmost` + 화면 중앙으로 독립 표시되며, 백그라운드 업데이트 확인은 UI 를 가로채지 않고 캐시만 갱신합니다. 모달 표시 실패 시 원인을 조용히 삼키지 않고 버전 옆에 노출합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Fixed the update modal not opening + removed the top banner** — Removed the green "new version" banner at the top; updates now surface only via the bottom-left version click → modal. Also fixed the underlying reason the modal never appeared: the `UpdateDialog` was set as the `Owner` of the always-`Topmost` popup, so the instant the popup lost focus and hid itself, the owned modal was hidden with it — leaving `ShowDialog()` blocked forever and every later click unresponsive. The modal is now shown ownerless as a `Topmost`, screen-centered window, and background update checks only refresh the cache instead of hijacking the UI. Modal display failures are surfaced next to the version instead of being silently swallowed.
<!-- /en -->

## [1.33.7] - 2026-07-03

<!-- ko -->
### 수정
- **"No access token found" 오류 안내 개선** — 액세스 토큰을 찾지 못했을 때 막연한 원문 대신, 터미널에서 Claude Code에 로그인(`claude` → `/login`)한 뒤 앱을 재시작하라는 구체적 안내를 4개 언어로 표시하도록 했습니다. 내부 sentinel 문자열을 상수화(`UsageApiService.NoTokenError`)해 매직 스트링을 제거하고, 사용자가 원문을 보지 않도록 회귀 방지 테스트를 추가했습니다. 새 PC/데스크톱 앱만 사용해 CLI 로그인 이력이 없어 `claudeAiOauth` 블록이 없던 환경에서 원인을 즉시 파악할 수 있습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Clearer "No access token found" guidance** — When no access token is available, the tray now shows an actionable, localized message (in all four languages) telling you to log in to Claude Code in a terminal (`claude` → `/login`) and restart the app, instead of the raw internal string. The internal sentinel is now a constant (`UsageApiService.NoTokenError`) to remove the magic string, with a regression test ensuring the raw text is never surfaced. This makes the cause obvious on new-PC / desktop-app-only setups that never ran the CLI login and therefore lacked the `claudeAiOauth` block.
<!-- /en -->

## [1.33.6] - 2026-06-19

<!-- ko -->
### 수정
- **업데이트 배너 문구 정리** — 상단 업데이트 배너에서 다시 표시되던 "클릭하여 설치" 안내를 제거했습니다. 배너 클릭 및 왼쪽 하단 버전 클릭 동작은 유지하며, 4개 언어 문구가 간결한 업데이트 알림만 표시하도록 회귀 방지 테스트를 추가했습니다. #77
<!-- /ko -->

<!-- en -->
### Fixed
- **Cleaned up the update banner copy** — Removed the reintroduced "click to install" prompt from the top update banner. The banner and bottom-left version click behavior are unchanged, and a regression test now keeps all four localized update messages concise. #77
<!-- /en -->

## [1.33.5] - 2026-06-19

<!-- ko -->
### 수정
- **버전 클릭 업데이트 모달 동작 복구** — 왼쪽 하단 버전 텍스트 또는 업데이트 배너 클릭 시 이미 확인된 업데이트 정보를 재사용해 `UpdateDialog` 모달을 바로 열도록 했습니다. 배너 클릭이 GitHub 재조회에 의존하던 흐름을 분리하고, 모달 중복 표시를 방지해 다운로드 진행 경로를 안정화했습니다. #76
<!-- /ko -->

<!-- en -->
### Fixed
- **Restored version-click update modal behavior** — Clicking the bottom-left version text or the update banner now reuses the already detected update info and opens the `UpdateDialog` modal directly. The banner no longer depends on a second GitHub check, and duplicate update dialogs are prevented for a steadier download flow. #76
<!-- /en -->

## [1.33.4] - 2026-06-19

<!-- ko -->
### 수정
- **안정성 회귀 복구** — v1.33.0 이후 느슨해진 `PropertyChanged` 처리로 트레이 아이콘이 무관한 상태 변경에도 과도하게 재생성되던 문제를 수정했습니다. `ClaudeViewModel` 구독을 복구하고 상태 메뉴/아이콘 갱신 대상을 필요한 속성으로 제한했습니다. #75
- **Extra Usage 표시 복구** — 화면은 `ClaudeVm.*` 바인딩을 사용하지만 실제 갱신은 `MainViewModel` 필드에만 쓰이던 불일치를 정리해 Extra Credits/Extra Usage 표시가 다시 동기화되도록 했습니다. #75
- **WPF 통합 테스트 hang 수정** — 테스트 전용 STA Dispatcher 호스트를 추가해 `RefreshAsync_SetsLastUpdatedLabel` 테스트가 메시지 펌프 없이 멈추지 않도록 했고, 릴리즈 워크플로에 `dotnet test` 게이트를 추가했습니다. #75
<!-- /ko -->

<!-- en -->
### Fixed
- **Stability regression recovery** — Restored focused `PropertyChanged` handling after v1.33.0 so unrelated state changes no longer redraw the tray icon excessively. `ClaudeViewModel` subscriptions are restored and menu/icon refreshes are limited to relevant properties. #75
- **Extra Usage display restored** — Fixed the mismatch where the UI read `ClaudeVm.*` bindings while live refresh still wrote only to `MainViewModel` fields, keeping Extra Credits/Extra Usage synchronized again. #75
- **WPF integration test hang fixed** — Added a dedicated STA Dispatcher host for WPF tests so `RefreshAsync_SetsLastUpdatedLabel` no longer hangs without a message pump, and added a `dotnet test` gate to the release workflow. #75
<!-- /en -->

## [1.33.3] - 2026-06-18

<!-- ko -->
### 수정
- **트레이 아이콘 갱신 중 GDI+ 예외로 전역 오류창이 표시되는 문제 수정** — `Bitmap.GetHicon()`으로 생성한 네이티브 HICON을 `Icon.Clone()` 후 `DestroyIcon()`으로 즉시 해제해 GDI 리소스 누수를 막았습니다. 아이콘 재그리기가 일시적으로 실패하더라도 기존 아이콘을 유지하고 앱이 크래시되지 않도록 보호했습니다. #74
<!-- /ko -->

<!-- en -->
### Fixed
- **Global error dialog shown after a GDI+ tray icon redraw failure** — Native HICON handles created by `Bitmap.GetHicon()` are now cloned and immediately released with `DestroyIcon()` to prevent GDI resource leaks. If tray icon redraw fails transiently, the app keeps the previous icon instead of crashing. #74
<!-- /en -->

## [1.33.2] - 2026-06-17

<!-- ko -->
### 수정
- **업데이트 모달 팝업(UpdateDialog)이 표시되지 않고 배너만 노출되는 문제 수정** — `ShowUpdateDialog()`에서 `dialog.Show()` + `Activate()` 대신 `ShowDialog()`로 전환하여 모달 동작을 보장하고, 현재 표시 중인 Topmost Window를 Owner로 지정하여 Z-order 충돌을 해결했습니다. 다이얼로그 생성/표시 실패 시에도 앱이 크래시되지 않고 배너가 fallback으로 동작합니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Update modal popup (UpdateDialog) not shown — only banner appeared** — Changed `ShowUpdateDialog()` from `dialog.Show()` + `Activate()` to `ShowDialog()` to guarantee modal behavior, and set the active Topmost Window as Owner to resolve Z-order conflicts. Added error handling so the app doesn't crash on dialog show failure — the banner serves as a graceful fallback.
<!-- /en -->

## [1.33.1] - 2026-06-17

<!-- ko -->
### 수정
- **예상치 못한 API 에러 메시지(raw 응답 본문)가 팝업에 그대로 노출되는 문제 수정** — "Cannot read image.png (this model does not support image input)" 같은 현상이 일시적인 API 게이트웨이 오류로 인해 발생할 수 있음을 식별하여, `ParseFriendlyError`가 `error.type`을 기준으로 permission_error/모델 비호환 오류를 자동 감지하고 사용자 친화적 메시지로 대체합니다. JSON 에러 본문 파싱을 Release 빌드에서도 수행하도록 개선했습니다.
### 개선
- **`ApiPermissionDenied` 다국어 문자열 추가** — 권한/모델 비호환 API 오류 발생 시 표시할 공통 메시지 (ko/zh/ja/en).
<!-- /ko -->

<!-- en -->
### Fixed
- **Raw API error body exposed in popup for unexpected errors** — Detected a class of transient API gateway errors (e.g., "Cannot read image.png (this model does not support image input)") that leaked into `ClaudeVm.ErrorMessage`. `ParseFriendlyError` now inspects `error.type` to detect permission_error / model-capability errors and substitutes a user-friendly message. JSON error-body parsing is now enabled in Release builds too.
### Improved
- **Added `ApiPermissionDenied` localized string** — Common message shown for permission/model-capability API errors (ko/zh/ja/en).
<!-- /en -->

## [1.33.0] - 2026-06-17

<!-- ko -->
### 개선
- **MainViewModel→ClaudeViewModel 완전 위임** — MainViewModel에 남아있던 Claude 관련 [ObservableProperty] 14개를 제거하고 ClaudeViewModel으로 완전 이관. SyncClaudeVm() 브릿지(53줄) 삭제, XAML 바인딩을 ClaudeVm.*로 변경. Claude 데이터의 단일 소유자가 ClaudeViewModel로 확정됨.
<!-- /ko -->

<!-- en -->
### Improved
- **Full delegation from MainViewModel to ClaudeViewModel** — Removed 14 Claude-related [ObservableProperty] from MainViewModel, fully delegating to ClaudeViewModel. Deleted SyncClaudeVm() bridge (53 lines), changed XAML bindings to ClaudeVm.* prefix. ClaudeViewModel is now the single source of truth for Claude data.
<!-- /en -->

## [1.32.5] - 2026-06-17

<!-- ko -->
### 변경
- **조기소진 푸시 알림 우선순위 하향** — 열심히 타이핑 중일 때 핸드폰이 계속 울리지 않도록 진동/소리 없는 조용한 알림(priority 2, low)로 변경했습니다.
<!-- /ko -->

<!-- en -->
### Changed
- **Lowered early exhaustion push notification priority** — Changed to silent notification (priority 2, low) with no vibration/sound so the phone doesn't keep buzzing during active typing.
<!-- /en -->

## [1.32.4] - 2026-06-04

<!-- ko -->
### 수정
- **날씨 ntfy 알림 클릭 URL 404 수정** — meteoblue URL 구조 변경으로 `/widget/interactive/` 경로가 404를 반환하여 `/week/` 경로로 변경했습니다.
### 개선
- **테마 적용 다이얼로그로 교체** — 업데이트 체크 실패 시 표시되던 기본 Windows MessageBox를 앱 테마를 따르는 `DarkMessageBox`로 교체했습니다.
- **업데이트 오류 인라인 표시** — 업데이트 다운로드 실패 시 별도 MessageBox 대신 UpdateDialog 내부에 에러 메시지를 표시하도록 변경했습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Fixed weather ntfy click URL 404** — Meteoblue URL structure changed, causing `/widget/interactive/` paths to return 404; changed to `/week/` path.
### Improved
- **Themed dialog for update prompts** — Replaced the default Windows MessageBox with a `DarkMessageBox` that matches the app's theme for update check failure prompts.
- **Inline update error display** — Update download errors are now shown inline within the UpdateDialog instead of a separate MessageBox.
<!-- /en -->

## [1.32.3] - 2026-06-02

<!-- ko -->
### 수정
- **조기 소진 알림 오발송 수정** — 소진 속도가 빨라졌을 때만 푸시 알림을 받도록 했으나, 새 5시간 주기 시작 시 속도 변화와 무관하게 알림이 발송되던 버그를 수정했습니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Fixed early exhaustion false notification** — Push notifications were intended to fire only when the usage rate accelerated, but a bug caused them to fire on every new 5-hour cycle start regardless of rate change. Removed the unconditional new-cycle trigger.
<!-- /en -->

## [1.32.2] - 2026-06-01

<!-- ko -->
### 개선
- **업데이트 체크 실패 시 Releases 페이지 안내** — GitHub API 오류(RateLimit, Timeout, API Error)로 버전 확인이 불가능할 때, Releases 페이지에서 직접 다운로드할 수 있도록 MessageBox로 열람 여부를 묻는 UX를 추가했습니다. 네트워크 오류는 제외(웹페이지도 못 열기 때문).
<!-- /ko -->

<!-- en -->
### Improved
- **Releases page prompt on update check failure** — When version check fails due to GitHub API errors (RateLimit, Timeout, API Error), a MessageBox now asks whether to open the Releases page for manual download. Network errors are excluded (the web page wouldn't load either).
<!-- /en -->

## [1.32.1] - 2026-06-01

<!-- ko -->
### 개선
- **ntfy 푸시 메시지에 PC 이름 표시** — 모든 ntfy 푸시 알림 본문 하단에 발송 PC 이름(`Environment.MachineName`)을 자동으로 추가하여 여러 PC에서 동일 토픽을 사용할 때 어느 PC의 알림인지 식별할 수 있습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **PC name in ntfy push messages** — Automatically appends the sending PC name (`Environment.MachineName`) to the body of every ntfy push notification, so users can identify which PC sent the alert when using the same topic across multiple machines.
<!-- /en -->

## [1.32.0] - 2026-05-29

<!-- ko -->
### 개선
- **MainViewModel God Object 리팩토링** — 2,100+ 라인의 MainViewModel을 6개 공급자별 ViewModel(Claude, Codex, Gemini, OpenCode, Antigravity, Weather)로 분해하여 단일 책임 원칙(SRP)을 준수하고 유지보수성을 대폭 개선했습니다.
- **계산 로직 분리** — `UsageCalculator` 클래스로 핵심 계산 로직(조기 소진 예측, 비용 산정, 리셋 시간 포맷 등)을 추출하여 단위 테스트 가능한 순수 함수로 분리했습니다.
- **단위 테스트 강화** — 111개 단위 테스트 추가(UsageCalculator 31개, ViewModel 내부 로직 29개, 기존 51개 포함).
- **통합 테스트 기반** — `MainViewModelIntegrationTests` 8개 통합 테스트 추가로 전체 VM 오케스트레이션 검증 기반 마련.
<!-- /ko -->

<!-- en -->
### Improved
- **MainViewModel God Object Refactoring** — Decomposed the 2,100+ line MainViewModel into 6 provider-specific ViewModels (Claude, Codex, Gemini, OpenCode, Antigravity, Weather), following the Single Responsibility Principle and greatly improving maintainability.
- **Calculation Logic Extraction** — Extracted core calculation logic (depletion prediction, cost estimation, reset time formatting, etc.) into the `UsageCalculator` class as testable pure functions.
- **Unit Test Coverage** — Added 111 unit tests (31 for UsageCalculator, 29 for ViewModel internals, plus 51 existing).
- **Integration Test Foundation** — Added 8 integration tests (`MainViewModelIntegrationTests`) to validate full VM orchestration.
<!-- /en -->

## [1.31.6] - 2026-05-29

<!-- ko -->
### 개선
- **조기 소진 예상 알림 반복 발송 방지** — 같은 5시간 주기 내에서 사용량이 줄어 예상 소진 시각이 늦춰져도 계속 푸시 알림이 반복되던 문제를 수정했습니다. 이제 예상 소진 시각이 **이전보다 당겨졌을 때만** 알림을 발송하며, 늦춰지거나 동일한 경우에는 알림을 보내지 않습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Prevent Repeated Early-Exhaustion Alerts** — Fixed an issue where push notifications kept firing repeatedly within the same 5-hour cycle even as the estimated depletion time shifted later due to reduced usage. Now alerts are sent **only when the estimated depletion time moves earlier**; no notification is sent if the estimate stays the same or moves later.
<!-- /en -->

## [1.31.5] - 2026-05-28

<!-- ko -->
### 개선
- **API 할당량 에이전트 행 간격 일관성 확보** — OpenCode 섹션의 바텀 마진이 다른 에이전트 섹션(Claude, Codex, Antigravity)의 바텀 마진(`8px`)과 달리 `0px`로 되어 있던 간격 불일치를 수정하였습니다. OpenCode 섹션의 마진을 `0,0,0,8`로 통일함으로써 모든 에이전트 행이 시각적으로 대칭을 이루어 훨씬 균형 있고 정돈된 레이아웃을 구축했습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Consistent Vertical Spacing for Agent Rows** — Corrected a margin inconsistency where the OpenCode section's bottom margin was set to `0px` instead of `8px` like the other agent sections (Claude, Codex, Antigravity). Unifying this margin to `8px` ensures perfectly symmetrical spacing, yielding a more polished and visually balanced quota layout.
<!-- /en -->

## [1.31.4] - 2026-05-28

<!-- ko -->
### 개선
- **Antigravity 활성 모델 리스트 프리미엄 3단 레이아웃 개편** — 모델명 텍스트(`DisplayName`)와 사용률 라벨(`UsageLabel`)이 가로 영역이 좁아 겹치거나 잘리던 문제를 해결하기 위해, 개별 아이템 템플릿을 **1행: 모델명 & 사용률, 2행: ProgressBar, 3행: 초기화 정보**의 3단 가로 확장 구조로 개편했습니다. 모델명이 100% 한눈에 노출되며, 말줄임표 안전 장치 탑재 및 우측 하단 은은한 초기화 정보 배치로 극상의 가독성과 프리미엄 심미성을 구축했습니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Premium 3-row Layout for Antigravity Model Items** — Resolves text overlaps and clipped model names by transitioning individual item templates into a **3-tier layout: Row 1 for Model Name & Usage Label, Row 2 for ProgressBar, and Row 3 for Reset Time**. This layout ensures model names are fully visible with safe text trimming, delivering maximized legibility and a highly polished UI.
<!-- /en -->

## [1.31.3] - 2026-05-28

<!-- ko -->
### 개선
- **팝업 레이아웃의 근본적 스크롤 마이그레이션 (Grid + ScrollViewer)** — 여러 공급자를 활성화하거나 모델이 많을 때 화면 가용 영역(WorkArea)을 넘어 하단이 잘리는 현상을 구조적으로 해결하기 위해 팝업 전체 레이아웃을 **고정 헤더 + 스크롤 메인 + 고정 푸터**의 3단 Grid 아키텍처로 개편했습니다. 윈도우 최대 높이에 도달하면 얇고 미려한 다크 스크롤바가 자동 활성화되어 어떤 해상도에서도 화면 잘림 현상이 원천 차단됩니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Structural Scroll Migration (Grid + ScrollViewer)** — Redesigned the main popup structure into a 3-tier Grid layout featuring a **fixed header, scrollable main content, and fixed footer**. When the dynamic height hits the WorkArea's limit, a sleek dark main ScrollViewer automatically takes over, structurally preventing screen overflow and ensuring usability on all monitor resolutions.
<!-- /en -->

## [1.31.2] - 2026-05-28

<!-- ko -->
### 개선
- **Antigravity 모델 다수 노출 시 화면 잘림 방지** — 사용률이 0% 초과인 활성 모델이 다수 존재할 때 메인 사용량 팝업 창이 너무 길어져 화면 경계 밖으로 잘리는 문제를 해결하기 위해 모델 목록을 MaxHeight="180" 제한이 적용된 ScrollViewer로 래핑하여 쾌적한 스크롤 조회를 제공함.
<!-- /ko -->

<!-- en -->
### Improved
- **Prevent screen overflow for long Antigravity model lists** — Resolves popup height overflow by wrapping the model list in a ScrollViewer capped at MaxHeight="180" with vertical scrolling, ensuring the popup fits comfortably on all screens when many active models are shown.
<!-- /en -->

## [1.31.1] - 2026-05-28

<!-- ko -->
### 개선
- **Antigravity 모델 목록 필터링 및 아코디언 접기 기능 추가** — Antigravity 모델 목록 중 사용률이 0% 초과인 모델만 노출하도록 필터링하여 불필요한 공백을 줄이고, 다른 공급자들과 마찬가지로 접었다 펼 수 있는 아코디언 접기 및 브랜드 전체 사용 비율 % 노출을 지원하여 UI 가독성과 일관성을 극대화함.
<!-- /ko -->

<!-- en -->
### Improved
- **Antigravity model list filtering and accordion collapse** — Filters out models with 0% usage to reduce layout clutter, and introduces interactive accordion folding and aggregate brand usage percentage just like other providers to maximize UI readability and consistency.
<!-- /en -->

## [1.31.0] - 2026-05-28

<!-- ko -->
### 추가
- **Antigravity 모델별 쿼터 패널** — Google Antigravity IDE(2.0)의 모델별 사용량과 리셋 시간을 트레이 팝업에 표시. Windows Credential Manager의 OAuth 토큰을 활용해 `daily-cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota`를 1시간마다 자동으로 갱신하며, Gemini 2.5/3.x, Claude Sonnet/Opus 4.6, GPT-OSS 120B 등 모델별 잔여 비율과 다음 리셋 시각을 progress bar 로 렌더. Antigravity 미설치/미로그인 PC에서는 섹션이 자동으로 숨겨짐.
<!-- /ko -->

<!-- en -->
### Added
- **Antigravity per-model quota panel** — Surfaces Google Antigravity IDE (2.0) per-model usage and reset times in the tray popup. Reads the OAuth token from Windows Credential Manager and polls `daily-cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota` (hourly auto-refresh), rendering per-model remaining fractions and next reset times as progress bars for Gemini 2.5/3.x, Claude Sonnet/Opus 4.6, GPT-OSS 120B, and more. The section auto-hides on PCs where Antigravity is not installed or signed in.
<!-- /en -->

## [1.30.7] - 2026-05-23

<!-- ko -->
### 변경
- **ntfy 알림 우선순위 분배** — 모든 알림이 우선순위 4(고정)로 발송되던 것을 알림 종류와 임계값에 따라 1~5 단계로 분산. 100% 소진은 urgent(5)로 알림음 반복, 50%는 low(2)로 조용히 표시되며, Rate Limit과 Quota Reset은 low(2), 조기 소진은 high(4)로 설정. 날씨 특보는 기존 우선순위 유지.
<!-- /ko -->

<!-- en -->
### Changed
- **ntfy notification priority differentiation** — All notifications previously sent at fixed priority 4 are now differentiated by type and threshold across 1–5 levels. 100% exhaustion triggers urgent(5) with repeated sound, 50% is low(2) and collapsed, Rate Limit and Quota Reset are low(2), early exhaustion is high(4). Weather alerts retain their existing priority.
<!-- /en -->

## [1.30.6] - 2026-05-23

<!-- ko -->
### 수정
- **테스트 빌드 및 코드 안정성 개선** — xUnit1031 규칙 위반으로 인한 테스트 프로젝트 빌드 실패 수정, 트레이 아이콘 GDI+ Bitmap 누수 해결, SHA256 업데이트 검증 실패 시 엄격하게 예외 발생, 날씨 알림 처리 중 예외를 무단 삼키던 빈 catch 블록 제거, 루트 폴더 미사용 오류 덤프 파일 정리.
<!-- /ko -->

<!-- en -->
### Fixed
- **Test build and code stability improvements** — Fixed test project build failure caused by xUnit1031 rule violations, resolved tray icon GDI+ Bitmap leak, made SHA256 update verification throw on failure, removed empty catch blocks that silently swallowed weather alert exceptions, cleaned up unused error dump file in root folder.
<!-- /en -->

## [1.30.5] - 2026-05-21

<!-- ko -->
### 개선
- **작은 화면에서 팝업 자동 접기** — work area 높이가 800px 이하인 작은 화면(노트북 등)에서 사용량 팝업을 열 때 모든 에이전트 섹션을 접은 상태로 시작. Claude 상세를 펼치면 팝업이 찌그러지는 문제를 방지하며, 사용자가 원하는 공급자를 클릭하여 직접 펼칠 수 있음.
<!-- /ko -->

<!-- en -->
### Improved
- **Auto-collapse popup on small screens** — When the work area height is 800px or less (laptops etc.), the usage popup now opens with all agent sections collapsed. This prevents the popup from becoming cramped when Claude's detail section is expanded. Users can tap any provider row to expand it manually.
<!-- /en -->

## [1.30.4] - 2026-05-21

<!-- ko -->
### 추가
- **조기 소진 푸시 알림** — 토큰 소진 속도가 예상보다 빨라 5시간 윈도우 하단에 조기 소진 라벨이 표시될 때, Windows 알림 및 ntfy 푸시 알림을 발송. 예상 소진 시각과 원래 초기화 시간을 비교하여 알려주며, 동일 윈도우 내에서는 중복 발송되지 않음.
<!-- /ko -->

<!-- en -->
### Added
- **Early exhaustion push notification** — When token consumption outpaces the 5-hour window and triggers the early exhaustion label, a Windows toast and ntfy push notification is now sent. The alert includes the predicted exhaustion time and original reset time for comparison. Duplicate alerts within the same window are suppressed.
<!-- /en -->

## [1.30.3] - 2026-05-20

<!-- ko -->
### 개선
- **초기화 시간 10분 미만 시 초 단위 카운트다운 표시** — 리셋 시간이 10분 미만 남았을 때 "9m 59s"와 같이 초 단위로 정밀하게 표시하여 사용자가 정확한 초기화 시점을 파악할 수 있도록 개선. API 요청은 기존 2분 주기를 유지하면서 화면 표시만 1초마다 갱신.
<!-- /ko -->

<!-- en -->
### Improved
- **Seconds-precision countdown when reset time is under 10 minutes** — When the quota reset time is less than 10 minutes away, the display now shows precise second-level countdown (e.g., "9m 59s") instead of rounding to minutes. The API polling interval remains unchanged at 2 minutes; only the display updates every second.
<!-- /en -->

## [1.30.2] - 2026-05-20

<!-- ko -->
### 개선
- **에이전트 아코디언 모두 접기 허용** — 메인 팝업에서 에이전트별 상세 사용량을 아코디언 방식으로 표시할 때, 기존에는 반드시 하나 이상 펼쳐져 있어야 했던 제약을 제거. 이제 펼쳐진 섹션을 다시 클릭하면 접히며, 모든 섹션을 동시에 접을 수 있음.
<!-- /ko -->

<!-- en -->
### Improved
- **Allow collapsing all agent accordion sections** — Removed the constraint that required at least one agent detail section to remain expanded in the main popup. Clicking an already-expanded section now collapses it, allowing all sections to be collapsed simultaneously.
<!-- /en -->

## [1.30.1] - 2026-05-19

<!-- ko -->
### 수정
- **알림 본문 에이전트명 중복 제거** — Codex 등 타 에이전트 사용량 알림 본문에 `[Codex] Codex`처럼 에이전트명이 중복 표시되던 버그 수정. 이제 `[Codex] 사용량` 형식으로 간결하게 표시됨.
<!-- /ko -->

<!-- en -->
### Fixed
- **Removed duplicated agent name in notification body** — Fixed a bug where the agent name appeared twice (e.g. `[Codex] Codex`) in usage alerts for non-Claude providers. Now displays as `[Codex] Usage`.
<!-- /en -->

## [1.30.0] - 2026-05-19

<!-- ko -->
### 변경
- **알림 본문에 에이전트 이름 표시** — 사용량 알림 본문에 에이전트 이름(Claude, Codex, Gemini CLI 등)이 `[Claude]`, `[Codex]` 형식으로 표시되어 어떤 에이전트의 알림인지 즉시 구분 가능.
<!-- /ko -->

<!-- en -->
### Changed
- **Agent name in notification body** — Usage alert body now displays the agent name (Claude, Codex, Gemini CLI, etc.) in `[Claude]`, `[Codex]` format for immediate identification.
<!-- /en -->

## [1.29.9] - 2026-05-18

<!-- ko -->
### 변경
- **알림 제목을 다중 에이전트 환경에 맞게 일반화** — 사용량 알림(Windows balloon · ntfy) 제목을 "Claude 사용량 알림" → "에이전트 사용량 알림"으로 변경. Claude 외에 Codex/Gemini CLI 등 다른 에이전트 임계값 알림도 모두 같은 제목으로 발송되므로 더 정확한 표현으로 통일.
- **에이전트 이름 표기 정상화** — Codex/Gemini CLI 임계값 알림 본문에서 공급자 이름이 내부 키 그대로(`codex`, `gemini-cli`) 소문자로 출력되던 문제를 수정. 이제 본문에 "Codex가 90%에 도달했습니다", "Gemini CLI가 90%에 도달했습니다"처럼 사람이 읽기 좋은 표기로 표시됨.
- **다국어 적용** — 알림 제목과 ntfy 안내 문구를 ko/zh/ja/en 모두 동일한 흐름으로 갱신.
<!-- /ko -->

<!-- en -->
### Changed
- **Generalized notification title for the multi-agent setup** — Usage alert title (Windows balloon · ntfy) renamed from "Claude Usage Alert" → "Agent Usage Alert". Threshold alerts now fire for Codex/Gemini CLI as well, so the title is updated to cover all monitored agents.
- **Proper-cased agent names in alert body** — Threshold alerts for Codex/Gemini CLI previously printed the internal kind string (`codex`, `gemini-cli`) verbatim. The body now reads "Codex reached 90%" / "Gemini CLI reached 90%" instead.
- **Localized everywhere** — Updated the notification title and ntfy onboarding copy across ko/zh/ja/en.
<!-- /en -->

## [1.29.8] - 2026-05-15

<!-- ko -->
### 추가
- **날씨 알림 테스트 버튼** — 설정 > 날씨 탭에 "날씨 알림 테스트" 버튼 추가. 현재 날씨 데이터로 알림을 즉시 전송하여 ntfy 연동 상태를 ping처럼 확인 가능.
- **ntfy 날씨 알림 상세화** — 기존에 ntfy 메시지가 "오늘의 날씨" 제목만 전달하던 문제를 수정. 이제 최저/최고 기온, 현재 기온, 체감 온도, 강수확률이 모두 포함된 상세 메시지가 전송됨.
- **ntfy 날씨 알림 클릭 링크** — 날씨 알림(일간 예보, 기상 상태 경고, 공식 경보) 클릭 시 Meteoblue 날씨 상세 페이지로 이동.
- **다국어 문자열 4종 추가** — `WeatherDailyTemp`, `WeatherCurrentTemp`, `WeatherFeelsLike`, `TestWeatherNotification`/`TestWeatherHint`/`TestWeatherNoLocation`/`TestWeatherNoData` (ko/zh/ja/en).

### 수정
- **기상 상태 경고 ntfy 메시지 누락** — 비/폭염/한파/풍 경고 시 ntfy에 경고 제목만 전송되던 문제를 수정. 실제 경고 내용이 메시지 본문에 포함됨.
<!-- /ko -->

<!-- en -->
### Added
- **Weather alert test button** — Added "Test weather alert" button in Settings > Weather tab. Sends an immediate notification with current weather data, useful for verifying ntfy integration like a ping.
- **Detailed ntfy weather notifications** — Fixed issue where ntfy messages only contained the "Today's Weather" title. Now includes daily low/high, current temperature, feels-like temperature, and precipitation probability.
- **Clickable weather links in ntfy** — Weather notifications (daily forecast, condition alerts, official warnings) now include a click URL that opens the Meteoblue weather detail page.
- **4 new localization strings** — `WeatherDailyTemp`, `WeatherCurrentTemp`, `WeatherFeelsLike`, `TestWeatherNotification`/`TestWeatherHint`/`TestWeatherNoLocation`/`TestWeatherNoData` (ko/zh/ja/en).

### Fixed
- **Condition alert ntfy message missing detail** — Rain/heat/cold/wind warnings only sent the alert title to ntfy instead of the actual warning content. Now the full message body is included.
<!-- /en -->

## [1.29.7] - 2026-05-15

<!-- ko -->
### 수정
- **7일 사용추이 차트가 카드 밖으로 삐져나오는 문제** — 팝업이 숨겨진 상태에서 백그라운드 새로고침이 차트 갱신을 트리거하면 `Canvas.ActualWidth`가 아직 0이어서 코드가 폴백 폭(288px)으로 막대/날짜 라벨을 그렸습니다. 실제 카드 내부 가용 폭은 약 252px이라 약 36px 만큼 카드 오른쪽 경계 밖으로 콘텐츠가 튀어나왔습니다(WPF `Canvas`는 기본적으로 자식을 클리핑하지 않음).
  - `HistoryCanvas`에 `ClipToBounds="True"` 적용해 어떤 상황에서도 카드 안에 머물도록 안전망 추가.
  - `Canvas.SizeChanged`에서 차트를 재렌더링해 폭이 잡힐 때 정확한 크기로 다시 그림.
  - `ActualWidth`가 너무 작으면 즉시 그리지 않고 SizeChanged를 기다리도록 변경(잘못된 크기로 자식을 만들지 않음).
<!-- /ko -->

<!-- en -->
### Fixed
- **7-day usage chart overflowing the card** — When a background refresh triggered chart redraw while the popup was hidden, `Canvas.ActualWidth` was still 0, so the code used the 288px fallback to lay out bars and date labels. The actual content width inside the card is ~252px, so bars and labels spilled ~36px past the right edge (WPF `Canvas` does not clip children by default).
  - Added `ClipToBounds="True"` on `HistoryCanvas` as a safety net.
  - Re-render the chart on `Canvas.SizeChanged` so it draws at the correct width once layout settles.
  - Skip drawing when `ActualWidth` is too small instead of seeding children at a wrong size — `SizeChanged` will trigger the real draw.
<!-- /en -->

## [1.29.6] - 2026-05-14

<!-- ko -->
### 수정
- **날씨 아이콘 여전히 안 보이던 문제** — WPF는 Segoe UI Emoji의 컬러 글리프(COLR)를 렌더링하지 못해 검은 윤곽선만 그려졌고, 어두운 배경(`#2D2F45`) 위에서 사실상 보이지 않았습니다. 또한 🌧/🌫/🌤 등 high-plane(U+1F3xx) 이모지는 폰트 fallback이 안 잡혔습니다.
  - 아이콘 폰트를 `Segoe UI Symbol`(모노크롬 BMP 심볼 폰트)로 교체하고 밝은 색상(`#F1F5F9`) 지정.
  - 아이콘 문자를 BMP 범위(U+2600–U+26FF, U+2744)로 정리: ☀ / ⛅ / ☁ / ☂ / ☔ / ❄ / ⚡.
<!-- /ko -->

<!-- en -->
### Fixed
- **Weather icon still invisible** — WPF doesn't render Segoe UI Emoji's COLR color glyphs, so the icon was drawn as black outlines that were nearly invisible on the dark `#2D2F45` background. High-plane emoji (🌧/🌫/🌤, U+1F3xx) also failed font fallback.
  - Switched the icon TextBlock to `Segoe UI Symbol` (monochrome BMP symbol font) with an explicit light Foreground.
  - Collapsed icon set to BMP-only glyphs (U+2600–U+26FF, U+2744): ☀ / ⛅ / ☁ / ☂ / ☔ / ❄ / ⚡.
<!-- /en -->

## [1.29.5] - 2026-05-14

<!-- ko -->
### 개선
- **현재 위치 도시 수준으로 표시** — 📍 버튼으로 현재 위치를 지정할 때 Nominatim의 structured `address` 필드에서 city/town/county 우선으로 추출해, "헬로소프트 ..., 294-7, 하안로, ..., 광명시, 경기도, ..., 대한민국" 같은 긴 전체 주소 대신 "광명시"만 저장합니다.
- **기존 긴 주소도 도시명만 표시** — 이전 버전에서 저장된 긴 주소도 팝업·트레이에서는 시/구/군 접미어를 자동 인식하여 도시 부분만 노출합니다. 설정 화면에서 📍를 다시 누르면 저장값 자체도 짧게 갱신됩니다.
<!-- /ko -->

<!-- en -->
### Improved
- **Current location shown at city level** — When 📍 picks the current location, we now read Nominatim's structured `address` (city → town → county → state) instead of the full `display_name`, so the saved location is "Seoul" rather than the full street/building/postcode chain.
- **Existing long addresses are trimmed for display** — Locations stored as the full display_name in earlier versions are now displayed at city granularity in the popup and tray tooltip (suffixes like 시/구/군/City/Town/市/区 are recognized). Re-tapping 📍 in settings updates the saved value itself.
<!-- /en -->

## [1.29.4] - 2026-05-14

<!-- ko -->
### 수정
- **날씨 아이콘 두부글자 렌더링** — 기본 폰트(Segoe UI)에 이모지 글리프가 없어 빈 사각형(□)으로 표시되던 문제 수정. 아이콘 TextBlock에 `FontFamily="Segoe UI Emoji"`를 지정하여 ☀ 🌧 ⛅ 등이 정상 렌더링됩니다.
<!-- /ko -->

<!-- en -->
### Fixed
- **Weather icon rendered as tofu** — The default font (Segoe UI) lacks the emoji glyphs, so the popup weather card showed an empty square (□). Applied `FontFamily="Segoe UI Emoji"` to the icon TextBlock so ☀ 🌧 ⛅ etc. render correctly.
<!-- /en -->

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
