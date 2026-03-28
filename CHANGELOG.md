# Changelog

모든 주요 변경 사항을 이 파일에 기록합니다.
[Keep a Changelog](https://keepachangelog.com/ko/1.0.0/) 형식을 따릅니다.

---

## [1.7.0] - 2026-03-28

<!-- ko -->
### 추가
- **시간대별 사용량 차트** — 차트 섹션에 "7일 / 오늘" 토글 추가. 오늘 탭 선택 시 0시부터 현재 시각까지 시간대별 토큰 사용량 바 차트 표시, 현재 시각 바 보라색 강조 (Issue #15)
<!-- /ko -->

<!-- en -->
### Added
- **Hourly usage chart** — Added "7-Day / Today" toggle to the chart section. The Today tab shows token usage per hour from midnight to the current time, with the current hour highlighted in purple (Issue #15)
<!-- /en -->

<!-- zh -->
### 新增
- **时段用量图表** — 图表区域新增「7天 / 今日」切换。选择今日后显示从0时到当前时刻的每小时用量柱状图，当前时刻以紫色高亮（Issue #15）
<!-- /zh -->

<!-- ja -->
### 追加
- **時間帯別使用量チャート** — チャートセクションに「7日 / 今日」トグルを追加。今日タブでは0時から現在時刻までの時間帯別トークン使用量を表示し、現在時刻のバーを紫色でハイライト（Issue #15）
<!-- /ja -->

---

## [1.6.0] - 2026-03-28

<!-- ko -->
### 추가
- **업데이트 다이얼로그** — 새 버전 감지 시 체인지로그를 확인하고 업데이트 여부를 선택하는 팝업 표시
- **이번 버전 건너뛰기** — 업데이트 다이얼로그에서 특정 버전을 건너뛰면 해당 버전에 대해 다시 알림 없음
- **업데이트 다이얼로그 다국어 지원** — 한국어·영어·중국어·일본어로 현재 언어에 맞는 체인지로그 자동 표시
<!-- /ko -->

<!-- en -->
### Added
- **Update dialog** — When a new version is detected, a popup shows the changelog and lets you choose whether to update
- **Skip this version** — Skipping a version in the update dialog suppresses future notifications for that version
- **Multilingual update dialog** — Changelog is displayed in the user's language (Korean · English · Chinese · Japanese)
<!-- /en -->

<!-- zh -->
### 新增
- **更新对话框** — 检测到新版本时，弹出对话框显示更新日志并询问是否更新
- **跳过此版本** — 在更新对话框中跳过某版本后，不再显示该版本的提醒
- **多语言更新日志** — 根据系统语言自动显示对应的更新日志（韩语·英语·中文·日语）
<!-- /zh -->

<!-- ja -->
### 追加
- **アップデートダイアログ** — 新バージョンを検出したとき、更新内容を確認してアップデートを選択できるポップアップを表示
- **このバージョンをスキップ** — ダイアログでスキップしたバージョンは以降通知されない
- **多言語アップデートログ** — システム言語に合わせた更新内容を自動表示（韓国語·英語·中国語·日本語）
<!-- /ja -->

---

## [1.5.0] - 2026-03-28

<!-- ko -->
### 추가
- **글로벌 예외 핸들러** — 앱 시작 또는 런타임 중 크래시 발생 시 조용히 종료되는 대신 에러 메시지 창 표시, GitHub Issues 신고용 스택트레이스 포함

### 수정
- **Rate limit 배너 오표시 수정** — API가 정상 응답하고 5시간 사용률이 100% 미만이면 이전 rate limit 기록을 초기화하여 리셋 이후에도 경고 배너가 남아있는 문제 해결
<!-- /ko -->

<!-- en -->
### Added
- **Global exception handler** — Instead of silently crashing, the app now shows an error dialog with a stack trace for GitHub Issues reporting

### Fixed
- **Rate limit banner stale display** — Banner now clears automatically when the API responds successfully with usage below 100%
<!-- /en -->

<!-- zh -->
### 新增
- **全局异常处理** — 应用崩溃时不再静默退出，而是显示包含堆栈跟踪的错误对话框，方便在 GitHub Issues 反馈

### 修复
- **限速提示横幅误显示** — API 正常响应且使用率低于 100% 时，横幅现在会自动消失
<!-- /zh -->

<!-- ja -->
### 追加
- **グローバル例外ハンドラー** — クラッシュ時に静かに終了する代わりに、GitHub Issues 報告用のスタックトレース付きエラーダイアログを表示

### 修正
- **レート制限バナーの誤表示** — API が正常に応答し使用率が 100% 未満の場合、バナーが自動的に消えるように修正
<!-- /ja -->

---

## [1.4.0] - 2026-03-27

### 추가
- **OAuth 토큰 자동 갱신** — 만료 시 자동으로 `platform.claude.com/v1/oauth/token` 갱신 후 credentials.json 업데이트 (Issue #1)
- **7일 사용 추이 그래프** — 팝업 하단에 일별 토큰 사용량 바 차트 표시, 오늘 날짜 강조 (Issue #2)
- **닫기(✕) 버튼** — 팝업 헤더에 추가, 클릭 시 팝업만 닫히고 앱은 트레이에 유지 (Issue #3)
- **CSV 내보내기** — 최대 90일 사용 이력을 바탕화면에 CSV로 저장 (Issue #4)

### 개선
- 이력 데이터 로컬 자동 저장 (`~/.claude/claude-usage-tray-history.json`)

---

## [1.3.0] - 2026-03-27

### 추가
- **윈도우 시작 시 자동 실행** — 설정창 토글 한 번으로 레지스트리 등록/해제
- **알림 테스트 버튼** — 설정창에서 Windows 토스트 + ntfy 푸시 알림 즉시 테스트 가능

### 개선
- **API Retry-After 준수** — 429 응답의 `Retry-After` 헤더를 파싱하여 지정된 시간 동안 API 재호출 차단, 불필요한 반복 요청 방지
- **에러 메시지 개선** — Rate limit 시 "API 제한 중 — HH:mm:ss 이후 재시도" 형태로 재시도 가능 시각 표시

---

## [1.2.0] - 2026-03-27

### 추가
- **다음 갱신 카운트다운** — 헤더에 다음 자동 갱신까지 남은 시간을 1초 단위로 실시간 표시

### 개선
- **스마트 에러 UX** — API 조회 실패 시 기존 값을 0%로 초기화하지 않고 마지막 성공 데이터 유지
- **트레이 아이콘 상태 표시** — 데이터 조회 실패 시 트레이 아이콘이 회색 `?`로 변경되어 비정상 상태 직관적으로 전달
- **타임스탬프 개선** — 성공 시 `업데이트 HH:mm:ss`, 실패 시 `⚠ HH:mm:ss` 로 구분 표시
- **폴링 간격 조정** — 30초 → 2분 (API 호출 빈도 제한 준수)

---

## [1.1.0] - 2026-03-27

### 추가
- **자동 업데이트** — 앱 시작 시 GitHub Releases 최신 버전 확인, 팝업 배너에서 원클릭 업데이트
- **Windows 토스트 알림** — 5시간 윈도우 사용량 임계값(50/75/90/100%) 도달 시 알림
- **ntfy.sh 스마트폰 푸시 알림** — iOS·Android에서 실시간 사용량 알림 수신
- **설정 모달 창** — 팝업 외부 공간이 부족할 때 잘리지 않는 별도 모달 창으로 구현
- **다국어 지원** — 한국어·중국어·일본어·영어 (시스템 언어 자동 감지)
- **GitHub Actions 릴리즈 워크플로우** — `v*` 태그 푸시 시 단일 실행 파일 자동 빌드·배포
- **면책 조항** — README 및 설정창에 참고용 도구임을 명시

### 수정
- **API 헤더 수정** — `anthropic-version: 2023-06-01` → `anthropic-beta: oauth-2025-04-20` (429 오류 해결)
- **API 응답 모델 재작성** — 실제 응답 형식(`five_hour`, `seven_day`)에 맞게 전면 수정

---

## [1.0.0] - 2026-03-26

### 최초 릴리즈
- Windows 시스템 트레이 기반 Claude AI 사용량 모니터링
- 5시간 · 7일 API 할당량 진행 바 및 초기화 시간 표시
- 오늘의 토큰 통계 (입력 / 출력 / 캐시 읽기 / 캐시 쓰기)
- 로컬 `.jsonl` 세션 파일 기반 통계 집계
- Claude Code OAuth 토큰 자동 재사용 (별도 로그인 불필요)
- 다크 테마 팝업 UI (WPF .NET 9)
