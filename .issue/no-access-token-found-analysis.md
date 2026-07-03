# "No access token found" 오류 분석 (해결)

작성일: 2026-07-01 (갱신: 2026-07-03)
상태: 원인 규명 완료 / 코드 수정 반영 / 사용자 조치 안내

## 증상
- 트레이 앱 Claude 섹션에 `API 오류: No access token found` 표시
- 5시간/7일 윈도우 모두 0%
- 반면 "오늘의 토큰"(입력/출력/캐시), "36개 세션" 숫자는 정상 표시됨
- Claude Code 자체는 정상 동작, 로그인 상태도 유지 중

## 결론 (요약)
계정 정지(flagged)로 인한 오류가 **아님**.
`No access token found`는 네트워크 요청 전에 **로컬에서 토큰 파일을 못 읽어** 앱이 자체적으로 세팅하는 메시지.

## 코드 경로
- `ClaudeUsageTray/Services/UsageApiService.cs:25-30`
  - `GetValidAccessTokenAsync()`가 `null`이면 즉시 `LastError = "No access token found"` 후 return (HTTP 호출 자체를 안 함)
- `ClaudeUsageTray/Services/CredentialService.cs:104-110`
  - `Load()` 후 `cred?.ClaudeAiOauth is not { } oauth` 이면 `null` 반환
  - 즉 `~/.claude/.credentials.json` 에 `claudeAiOauth` 블록이 없으면 토큰 없음 처리
- 참고: 계정 정지/권한 문제였다면 `UsageApiService.cs:63` 의 `HTTP 401/403 ...` 분기로 떴을 것. 이번 건은 그 분기가 아님.

## 진단 근거 (사용자 PC, 2026-07-01 확인)
- `~/.claude/.credentials.json` **존재하지만** top-level 키가 `['mcpOAuth']` 뿐 → `claudeAiOauth` 블록 **MISSING**
  - `mcpOAuth` 안에는 plugin:engineering:* (notion/linear/atlassian/slack/github/pagerduty/datadog/asana) MCP 토큰만 있음
- `.claude.json` 에는 `oauthAccount` 정상 존재 → **로그인 상태 유지 중**
  - emailAddress: okpos.cs1.2@gmail.com, seatTier/billingType 등 구독 메타데이터 정상
- `ANTHROPIC_API_KEY` 미설정, `.claude.json`에 `primaryApiKey` 없음
- Windows 자격증명 관리자(cmdkey/vaultcmd)에 claude/anthropic 항목 **없음** (gemini:antigravity 항목만 존재)
- `.claude/` 하위에서 `claudeAiOauth` 문자열 포함 파일은 이번 세션 JSONL 로그뿐 (실제 자격증명 아님)

→ **로그인·계정은 정상인데, 액세스 토큰만 트레이 앱이 읽는 위치(`.credentials.json`의 `claudeAiOauth`)에서 사라진 상태.**

## "flagged 이슈"와의 연관성
- 저장소가 비공개라 GitHub 이슈 목록 직접 확인 불가(WebFetch 404). 어떤 이슈인지는 미확정.
- 계정 flagged/정지 관련 이슈라면 → **무관** (계정 정상, 순수 로컬 파일 문제)
- Claude Code 자격증명 저장 방식/위치 변경 관련 이슈라면 → **일치**
  - 최신 Claude Code가 로그인 토큰을 평문 `.credentials.json`에서 OS 보안 저장소로 이전했거나,
    MCP OAuth 기록 과정에서 파일을 덮어써 `claudeAiOauth`가 유실되는 케이스로 추정

## 최종 결론 (2026-07-03, 새 PC에서 재확인)
새 PC엔 애초에 **CLI 로그인을 한 적이 없어** `claudeAiOauth` 블록이 파일에 쓰인 적이 없다.
데스크톱 앱만 사용 → 계정 토큰은 앱 자체 저장소에 있고 `.credentials.json`엔 `mcpOAuth`만 기록됨.
Windows 자격증명 관리자에도 Claude 항목 없음. 순수 로컬 자격증명 누락 (계정 정상, flagged 아님).

### 검토했으나 폐기한 수정안: `CLAUDE_CODE_OAUTH_TOKEN` 환경변수 폴백
`claude setup-token` 토큰은 **inference 전용 스코프**라 usage API가 요구하는 `user:profile` 스코프가 없다.
→ 토큰이 있어도 `GET /api/oauth/usage`가 `403 permission_error: OAuth token does not meet scope requirement user:profile`.
Claude Code 자체 `/usage` 도 이 토큰으론 실패. 따라서 환경변수 폴백은 **무효** → 채택하지 않음.

## 반영한 코드 수정 (UX 개선)
막연한 원문 `No access token found` 대신 구체적 로그인 안내를 표시하도록 변경:
- `UsageApiService.cs`: 내부 sentinel 상수 `NoTokenError` 도입 (매직 스트링 제거)
- `LocalizationService.cs`: 죽어있던 `Loc.NoToken` 을 실행 가능한 안내문으로 개선 (ko/zh/ja/en)
- `MainViewModel.cs` / `ClaudeViewModel.cs`: no-token 분기에서 `Loc.NoToken` 라우팅
- `LocalizationServiceTests.cs`: 사용자가 원문 sentinel 을 보지 않고 언어별 안내가 나오는지 검증
- 주의: 이 저장소엔 .NET **SDK 미설치**(런타임만) 라 로컬 빌드/테스트 미실행 — 수기 검토로 컴파일 정합성만 확인.

## 사용자 조치 (실제 해결 = 토큰 재생성)
토큰이 물리적으로 없으므로 코드만으론 해결 불가. CLI 로그인으로 `claudeAiOauth`(=`user:profile` 스코프 포함)를 파일에 다시 써야 한다:
1. `npm install -g @anthropic-ai/claude-code`
2. `claude` 실행 → `/login` → 브라우저 OAuth 완료
3. 확인: `Get-Content "$env:USERPROFILE\.claude\.credentials.json" | ConvertFrom-Json | Select claudeAiOauth`
4. 트레이 앱 완전 종료 후 재실행
- 만약 3에서 `claudeAiOauth`가 안 생기면 → 현재 Windows CLI가 토큰을 OS 자격증명 저장소에 저장한 것.
  이 경우 `CredentialService`가 Windows 자격증명 관리자를 읽도록 확장하는 후속 작업 필요 (AntigravityUsageMonitor 의 CredRead 패턴 재사용).
