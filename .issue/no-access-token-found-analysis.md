# "No access token found" 오류 분석 (진행중)

작성일: 2026-07-01
상태: 원인 규명 완료 / 수정 방향 미결정

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

## 다음 단계 (미결)
1. `claude` 재로그인(`/login`) 후 `~/.claude/.credentials.json`에 `claudeAiOauth`가 다시 생기는지 확인
   - 생기면 → 앱 정상화, 단순 유실이었음
2. 재로그인해도 `claudeAiOauth`가 안 돌아오면 → Claude Code가 토큰을 다른 저장소로 이전한 것
   - 앱이 새 저장 위치(예: Windows 자격증명 관리자 / DPAPI)를 읽도록 `CredentialService` 수정 필요
   - 이 경우가 "그 GitHub 이슈"의 본질일 가능성 높음
3. 사용자에게 "flagged 이슈"가 (a) 계정 정지 (b) 자격증명 저장 변경 중 무엇인지 확인 필요
