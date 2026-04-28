# Claude Usage Tray (Windows)

![GitHub release](https://img.shields.io/github/v/release/jeiel85/claude-usage-tray-windows?style=flat-square&color=8B5CF6)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D4?style=flat-square&logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-10B981?style=flat-square)
![Built with Claude](https://img.shields.io/badge/built%20with-Claude%20AI-F59E0B?style=flat-square)

Windows 시스템 트레이에서 Claude, Codex(ChatGPT), Gemini AI 사용량을 실시간으로 통합 모니터링하는 앱입니다.

> **[claude-usage-mini](https://github.com/jeremy-prt/claude-usage-mini) by [@jeremy-prt](https://github.com/jeremy-prt) 에서 영감을 받았습니다**

## 스크린샷

![Claude Usage Tray 스크린샷](docs/screenshot.png)

## 다운로드 (바로 실행)

> **[최신 릴리즈 다운로드 →](https://github.com/jeiel85/claude-usage-tray-windows/releases/latest)**

| 파일 | 크기 | 설명 |
|------|------|------|
| `ClaudeUsageTray.exe` | ~170 KB | Framework-dependent — [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0/runtime) 필요 |
| `ClaudeUsageTray-Updater.exe` | ~170 KB | 자동 업데이트 도구 (메인 앱과 같은 폴더에 위치) |
| **현재 버전** | `v1.17.6` | Latest |

**실행 방법:**
1. 위 링크에서 `ClaudeUsageTray.exe` 다운로드
2. 실행 (Windows Defender 경고 → **추가 정보 → 실행**)
3. 시스템 트레이 아이콘 클릭으로 사용량 확인

## 요구 사항

- Windows 10 이상
- [Claude Code](https://claude.ai/code) 설치 및 로그인 상태
- [**.NET 9.0 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/9.0/runtime) — 미설치 시 앱이 실행되지 않음

---

## 주요 기능

**통합 모니터링 (Multi-Provider Support)**
- **Claude (API)**: 실시간 5시간 / 7일 윈도우 할당량 및 소진 예측
- **Codex (ChatGPT Plan)**: ChatGPT 플랜 사용량 상태 확인
- **Gemini CLI**: Gemini CLI 로그를 통한 실시간 토큰 사용량(%) 및 할당량 추적
- **통합 트레이 툴팁**: 아이콘에 마우스를 올리면 3개 공급자의 상태 요약 즉시 확인
- **동적 트레이 아이콘**: 현재 가장 많이 사용 중인 공급자 또는 설정된 주 공급자 기준 게이지 표시

**상세 분석**
- **오늘의 토큰 통계 (Claude)**: 입력 / 출력 / 캐시 읽기 / 캐시 쓰기 토큰을 색상별로 구분 표시
- **비용 참고치**: 오늘 사용한 토큰량을 USD 달러 비용으로 환산 표시
- **7일/24시간 차트**: 일별 사용 추이 및 시간대별 사용 분포 시각화
- **자동 갱신**: 1~N분 주기로 배경에서 실시간 데이터 동기화 (기본 2분)

**스마트 알림**
- **공급자별 임계값 알림**: Claude, Codex, Gemini 각각 설정된 임계값 도달 시 Windows 알림
- **모바일 푸시**: [ntfy.sh](https://ntfy.sh) 연동으로 스마트폰(iOS/Android) 실시간 푸시 수신

**편의 기능**
- **다국어 지원**: 한국어, 영어, 일본어, 중국어 완벽 지원 (시스템 언어 자동 감지)
- **무설치 단일 파일**: 별도의 설치 과정 없이 `exe` 파일 하나로 실행
- **자동 업데이트**: 새 버전 출시 시 팝업 알림 및 원클릭 업데이트

---

## 작동 원리

### 1. Claude (API)
- **인증**: Claude Code가 로컬에 저장한 OAuth 토큰(`%USERPROFILE%\.claude\.credentials.json`)을 재사용합니다. 별도의 로그인이 필요 없습니다.
- **할당량**: Anthropic 공식 API(`api.anthropic.com/api/oauth/usage`)를 호출하여 5시간/7일 남은 잔여량을 가져옵니다.
- **로컬 통계**: `%USERPROFILE%\.claude\projects\` 내의 `.jsonl` 로그 파일을 스캔하여 오늘 사용한 상세 토큰량과 비용을 집계합니다.

### 2. Gemini CLI
- **로그 분석**: Gemini CLI가 작업 중 생성하는 임시 로그 폴더(`%USERPROFILE%\.gemini\tmp`)를 실시간 감시합니다.
- **실시간 파싱**: 로그 파일 내의 `total_tokens`, `input_tokens`, `output_tokens` 정보를 추출하여 합산합니다.
- **할당량 계산**: 수집된 토큰 데이터를 기반으로 Gemini의 모델별 임계값 대비 현재 사용량(%)을 계산하여 시각화합니다.

### 3. Codex (ChatGPT)
- **상태 추적**: Claude Code의 인증 컨텍스트를 통해 연동된 Codex(ChatGPT) 공급자의 사용량 상태 및 제한 정보를 동기화합니다.

---

## 시작하기

### 바로 실행 (권장)

[Releases 페이지](https://github.com/jeiel85/claude-usage-tray-windows/releases)에서 최신 `ClaudeUsageTray.exe` 다운로드 후 실행하세요.

### 소스에서 빌드

```bash
git clone https://github.com/jeiel85/claude-usage-tray-windows
cd claude-usage-tray-windows
dotnet run --project ClaudeUsageTray
```

## 알림 설정

팝업 하단 **⚙** 버튼으로 설정 창을 열 수 있어요.

### Windows 알림
공급자별로 임계값(기본: 50% / 75% / 90% / 100%) 도달 시 Windows 알림 센터에 알림이 표시됩니다.

### 스마트폰 푸시 알림 (ntfy.sh)
1. iOS / Android에서 **ntfy 앱** 설치
2. 앱에서 고유한 토픽 이름으로 구독 (예: `my-claude-usage-123`)
3. 앱 설정 창의 **ntfy 토픽**란에 동일한 이름 입력

---

## 면책 조항
이 앱은 공식 Anthropic 제품이 아닌 개인 프로젝트입니다. 표시되는 모든 수치는 로컬 로그와 API 응답을 기반으로 한 **참고용**이며, 실제 과금 데이터와는 차이가 있을 수 있습니다. 정확한 사용량은 각 서비스의 공식 대시보드에서 확인하시기 바랍니다.
