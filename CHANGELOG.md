# Changelog

모든 주요 변경 사항을 이 파일에 기록합니다.
[Keep a Changelog](https://keepachangelog.com/ko/1.0.0/) 형식을 따릅니다.

---

## [1.11.0] - 2026-03-28

<!-- ko -->
### 개선
- **종료 버튼 시각적 구분 강화** — 종료 버튼을 평소에도 연한 빨간빛으로 표시해 파괴적 액션임을 명확히 구분 (Issue #17)
- **알림 테스트 피드백** — 알림 테스트 버튼 클릭 시 "✓ 전송됨" 결과 표시. ntfy 미설정 시 "ntfy 미설정" 안내 포함 (Issue #20)
<!-- /ko -->

<!-- en -->
### Improved
- **Quit button visual distinction** — Quit button now shows in a subtle red tint at rest, making it clearly distinct as a destructive action (Issue #17)
- **Test notification feedback** — Clicking the test notification button now shows "✓ Sent" result. Shows ntfy status if not configured (Issue #20)
<!-- /en -->

<!-- zh -->
### 改进
- **退出按钮视觉区分增强** — 退出按钮平时显示为淡红色，明确标识其为破坏性操作（Issue #17）
- **通知测试反馈** — 点击通知测试按钮后显示"✓ 已发送"结果，未设置 ntfy 时显示相应提示（Issue #20）
<!-- /zh -->

<!-- ja -->
### 改善
- **終了ボタンの視覚的区別強化** — 終了ボタンを通常時も淡い赤みで表示し、破壊的アクションであることを明確化（Issue #17）
- **通知テストのフィードバック** — 通知テストボタン押下後に「✓ 送信済み」を表示。ntfy 未設定時はその旨を案内（Issue #20）
<!-- /ja -->

---

## [1.10.0] - 2026-03-28

<!-- ko -->
### 추가
- **24시간 자동 업데이트 확인** — 앱이 켜진 상태에서 24시간마다 자동으로 새 버전을 확인 (Issue #19)
- **수동 업데이트 확인** — 푸터의 버전 텍스트(v1.x.x) 클릭 시 즉시 업데이트 체크. 최신 버전이면 "✓ 최신 버전입니다" 표시 후 3초 후 사라짐. 이전에 건너뛴 버전도 수동 체크 시 재표시 (Issue #19)
<!-- /ko -->

<!-- en -->
### Added
- **24-hour auto update check** — Automatically checks for new versions every 24 hours while the app is running (Issue #19)
- **Manual update check** — Click the version label (v1.x.x) in the footer to check immediately. Shows "✓ Already up to date" for 3 seconds if no update found. Skipped versions reappear on manual check (Issue #19)
<!-- /en -->

<!-- zh -->
### 新增
- **24小时自动检查更新** — 应用运行时每24小时自动检查新版本（Issue #19）
- **手动检查更新** — 点击底部版本号（v1.x.x）立即检查。无更新时显示"✓ 已是最新版本"，3秒后消失。手动检查时已跳过的版本也会重新显示（Issue #19）
<!-- /zh -->

<!-- ja -->
### 追加
- **24時間自動アップデート確認** — アプリ起動中、24時間ごとに自動で新バージョンを確認（Issue #19）
- **手動アップデート確認** — フッターのバージョンラベル（v1.x.x）をクリックして即時確認。最新版なら「✓ 最新バージョンです」を3秒表示。手動確認時はスキップ済みバージョンも再表示（Issue #19）
<!-- /ja -->

---

## [1.9.0] - 2026-03-28

<!-- ko -->
### 개선
- **헤더 ✕ 버튼 제거** — 팝업은 이미 클릭 아웃 시 자동으로 닫히므로 헤더의 중복된 닫기 버튼 제거. ↻ 새로고침 버튼만 유지 (Issue #16)
- **종료 버튼 시각적 구분** — 설정(⚙)과 종료 사이에 구분선 추가, 마우스 오버 시 붉은 계열 색상으로 파괴적 액션 명확화 (Issue #17)
- **설정창 닫기 후 메인 팝업 복귀** — 설정창을 닫으면 메인 팝업이 자동으로 다시 표시됨. 설정을 열어도 메인 팝업이 백그라운드에서 유지됨 (Issue #18)
- **알림 테스트 버튼 안내 추가** — "알림 테스트" 버튼이 Windows 토스트와 스마트폰 ntfy 알림을 동시에 테스트함을 버튼 하단에 명시
<!-- /ko -->

<!-- en -->
### Improved
- **Remove header ✕ button** — Popup already closes on click-outside (Deactivated), so the redundant close button in the header has been removed. Only the ↻ refresh button remains (Issue #16)
- **Quit button visual distinction** — Added a separator before the quit button; hover now shows red color to indicate a destructive action (Issue #17)
- **Return to main popup after closing settings** — Closing the settings window automatically brings the main popup back. The main popup also stays visible in the background while settings is open (Issue #18)
- **Test notification hint** — Clarified that the "Test notification" button tests both Windows toast and phone push (ntfy) simultaneously
<!-- /en -->

<!-- zh -->
### 改进
- **移除标题栏 ✕ 按钮** — 弹窗点击外部已自动关闭，标题栏的冗余关闭按钮已移除，仅保留 ↻ 刷新按钮（Issue #16）
- **退出按钮视觉区分** — 设置与退出之间添加分隔线，鼠标悬停时显示红色，明确提示破坏性操作（Issue #17）
- **关闭设置后返回主窗口** — 关闭设置窗口后主弹窗自动重新显示，打开设置时主弹窗保持后台可见（Issue #18）
- **测试通知说明** — 明确标注"测试通知"按钮同时测试 Windows 通知和手机推送（ntfy）
<!-- /zh -->

<!-- ja -->
### 改善
- **ヘッダーの ✕ ボタンを削除** — ポップアップはクリック外で自動的に閉じるため、ヘッダーの重複した閉じるボタンを削除。↻ 更新ボタンのみを残します（Issue #16）
- **終了ボタンの視覚的区別** — 設定と終了の間に区切り線を追加、ホバー時に赤色で破壊的操作を明示（Issue #17）
- **設定を閉じた後にメインポップアップへ戻る** — 設定ウィンドウを閉じると自動的にメインポップアップが再表示。設定中もメインポップアップはバックグラウンドで維持（Issue #18）
- **テスト通知の説明追加** — 「通知テスト」ボタンが Windows トーストとスマホ通知（ntfy）を同時にテストすることをボタン下部に明示
<!-- /ja -->

---

## [1.8.0] - 2026-03-28

<!-- ko -->
### 수정
- **단일 실행 파일 크래시 수정** — GitHub에서 다운로드한 exe 실행 시 `DllNotFoundException`으로 앱이 무음 종료되는 문제 수정. WPF 네이티브 DLL(`PresentationNative_cor3.dll` 등)을 단일 파일에 포함하도록 빌드 방식 변경 (Issue #7)
- **시작 프로그램 경로 수정** — 단일 파일 앱에서 `Assembly.Location`이 빈 문자열을 반환하는 문제 수정, 올바른 실행 파일 경로 사용
<!-- /ko -->

<!-- en -->
### Fixed
- **Single-file exe crash fix** — App was silently crashing with `DllNotFoundException` on launch when downloaded from GitHub. Fixed by bundling WPF native DLLs (`PresentationNative_cor3.dll` etc.) into the single-file exe (Issue #7)
- **Start with Windows path fix** — Fixed `Assembly.Location` returning empty string in single-file apps; now uses the correct executable path
<!-- /en -->

<!-- zh -->
### 修复
- **单文件启动崩溃修复** — 从 GitHub 下载的 exe 启动时因 `DllNotFoundException` 静默崩溃。修复方案：将 WPF 原生 DLL（`PresentationNative_cor3.dll` 等）打包进单文件（Issue #7）
- **开机启动路径修复** — 修复单文件应用中 `Assembly.Location` 返回空字符串的问题，改用正确的可执行文件路径
<!-- /zh -->

<!-- ja -->
### 修正
- **単一ファイル exe クラッシュ修正** — GitHub からダウンロードした exe を起動すると `DllNotFoundException` で無音終了する問題を修正。WPF ネイティブ DLL（`PresentationNative_cor3.dll` など）を単一ファイルに同梱するよう変更（Issue #7）
- **スタートアップパス修正** — 単一ファイルアプリで `Assembly.Location` が空文字を返す問題を修正し、正しい実行ファイルパスを使用するよう変更
<!-- /ja -->

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
