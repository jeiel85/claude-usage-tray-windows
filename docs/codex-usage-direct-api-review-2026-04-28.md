# Codex Usage Direct API 검토 (2026-04-28)

## 배경
- 사용자 관측:
  - Codex CLI `/status`: `5h limit: 100% left (resets 20:24)`
  - Claude Usage Tray 앱: Codex `42%` 표시
- 질문:
  - Codex 사용량을 백엔드 API 직접 조회로 맞출 수 있는가?
  - Claude 사용량 조회와 동일한 수준의 리스크 관리가 가능한가?

## 현재 앱 동작 요약
- Claude:
  - 공식 OAuth 토큰을 로컬에서 관리하고(`%USERPROFILE%\\.claude\\.credentials.json`)
  - `https://api.anthropic.com/api/oauth/usage`를 직접 호출
  - 토큰 만료 시 refresh 로직을 앱이 직접 수행
- Codex:
  - `%USERPROFILE%\\.codex\\sessions\\**\\rollout-*.jsonl`의 `token_count.rate_limits`를 읽어 표시
  - 즉, CLI가 남긴 로컬 세션 로그 기반

## 확인된 사실
- 로컬 Codex 세션 로그에 다음 데이터가 실제로 존재:
  - `rate_limits.primary.used_percent`
  - `rate_limits.primary.resets_at`
  - `rate_limits.secondary.used_percent`
  - `rate_limits.secondary.resets_at`
  - `plan_type`
- 이 값은 시점에 따라 CLI `/status`와 일시 불일치 가능:
  - 리셋 직후, 앱이 이전 로그 스냅샷을 잠시 유지할 수 있음

## Direct API 조회 가능성
- 기술적으로는 가능:
  - `%USERPROFILE%\\.codex\\auth.json`에 `access_token/refresh_token` 존재
  - 알려진 사용량 API 경로/구조(`backend-api/codex/usage` 계열)가 커뮤니티/이슈에서 반복적으로 관측됨
- 하지만 운영 안정성은 낮음:
  - 비공식/비고정 계약 가능성(엔드포인트, 필드 변경)
  - refresh token 재사용/무효화 이슈 보고 다수
  - CLI와 앱이 동시에 토큰 갱신 시 인증 충돌 가능성

## Claude 대비 차이점 (핵심)
- Claude는 앱이 소유한 인증 흐름과 공식 endpoint를 사용
- Codex는 CLI 내부 인증 캐시(`.codex/auth.json`)에 의존
- 따라서 "Claude와 같은 방식으로 감안하면 된다"는 결론은 부분적으로만 성립
  - 구현은 가능하나, Codex 쪽이 인증/정책/호환성 리스크가 더 큼

## 권장 전략
1. 기본 경로:
   - 현행처럼 Codex `sessions` 로그 기반 유지
   - `stale`/`재동기화 중` 표시를 강화해 오해 방지
2. 선택 경로(옵션):
   - `Direct API (beta, opt-in)` 모드 제공
   - 첫 단계에서는 `access_token` 읽기 기반 조회만 허용
   - 앱에서 refresh 시도는 금지(401/403 발생 시 즉시 로그 기반 폴백)
3. UI 투명성:
   - 데이터 소스 라벨 명시 (`Local log` / `Direct API beta`)
   - 공식 수치와 차이 가능성 안내

## 구현 시 가드레일
- 토큰/응답 원문 로그 출력 금지
- 예외 메시지에 민감정보 포함 금지
- API 실패 시 지연 없이 폴백
- 스키마 변경 감지 시 기능 자동 비활성화(폴백)

## 결론
- Direct API는 "가능"하지만, "기본값으로 채택"하기에는 리스크가 큼.
- 제품 안정성 기준에서는:
  - 기본: 로그 기반 + 신뢰도 표시
  - 고급 옵션: opt-in Direct API beta
  가 가장 현실적인 타협안.

## 참고 소스
- https://github.com/openai/codex/issues/16323
- https://github.com/openai/codex/issues/15502
- https://github.com/openai/codex/issues/12299
