# Claude Usage Tray (Windows)

Windows 시스템 트레이에서 Claude AI 사용량을 실시간으로 모니터링하는 앱입니다.

> **[claude-usage-mini](https://github.com/jeremy-prt/claude-usage-mini) by [@jeremy-prt](https://github.com/jeremy-prt) 에서 영감을 받았습니다**
> 원본 프로젝트는 Swift/SwiftUI로 제작된 macOS 메뉴바 앱입니다.
> 이 프로젝트는 WPF 기반의 Windows 포팅 버전입니다.
> 아이디어와 영감을 준 Jeremy Prat, 그리고 [claude-usage-bar](https://github.com/Krystian-key/claude-usage-bar)의 Krystian에게 감사드립니다.

---

## 주요 기능

- **시스템 트레이 아이콘** — 사용량에 따라 색상이 변하는 실시간 인디케이터 (보라 → 주황 → 빨강)
- **5시간 & 7일 API 쿼터** — Anthropic OAuth 사용량 API에서 실시간 진행 바 표시
- **오늘의 토큰 통계** — 로컬 세션 파일에서 입력/출력/캐시 읽기/쓰기 토큰 합산
- **레이트 리밋 감지** — 속도 제한이 걸렸을 때 경고 및 리셋 시간 표시
- **자동 새로고침** — 30초마다 자동 업데이트
- **다크 UI** — 모던 다크 테마 팝업, 둥근 모서리와 부드러운 애니메이션
- **별도 로그인 불필요** — Claude Code에 이미 저장된 OAuth 토큰을 재사용

## 스크린샷

> *준비 중*

## 요구 사항

- Windows 10 이상
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Claude Code](https://claude.ai/code) 설치 및 로그인 상태
  (앱이 `~/.claude/.credentials.json`에서 인증 정보를 읽어옵니다)

## 시작하기

### 소스에서 실행

```bash
git clone https://github.com/YOUR_USERNAME/claude-usage-tray-windows
cd claude-usage-tray-windows/ClaudeUsageTray
dotnet run
```

### 릴리즈 빌드

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## 작동 원리

### 인증

OAuth 플로우를 새로 구현하는 대신, Claude Code가 이미 저장해 둔 액세스 토큰을 재사용합니다:

```
%USERPROFILE%\.claude\.credentials.json
```

### API 사용량

Bearer 토큰으로 `https://api.anthropic.com/api/oauth/usage` 를 호출하여 다음 정보를 가져옵니다:
- 5시간 롤링 윈도우 사용량 및 쿼터
- 7일 롤링 윈도우 사용량 및 쿼터

### 로컬 세션 데이터

`%USERPROFILE%\.claude\projects\**\*.jsonl` 파일을 스캔하여 Claude Code 세션 파일에서 오늘의 토큰 사용량을 직접 집계합니다 — 추가 API 호출 없이 동작합니다.

## 기술 스택

| 구성 요소 | 기술 |
|-----------|------|
| UI 프레임워크 | WPF (.NET 9) |
| 시스템 트레이 | System.Windows.Forms.NotifyIcon |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| HTTP | System.Net.Http |
| JSON | System.Text.Json |

## 프로젝트 구조

```
ClaudeUsageTray/
├── Models/
│   ├── Credentials.cs      # OAuth 인증 정보 모델
│   └── UsageData.cs        # API 응답 + 세션 통계 모델
├── Services/
│   ├── CredentialService.cs  # ~/.claude/.credentials.json 읽기
│   ├── UsageApiService.cs    # Anthropic 사용량 API 호출
│   └── SessionMonitor.cs     # 로컬 .jsonl 세션 파일 파싱
├── ViewModels/
│   └── MainViewModel.cs      # 데이터 바인딩 + 새로고침 로직
├── Views/
│   └── UsagePopup.xaml       # 다크 테마 팝업 UI
└── App.xaml.cs               # 트레이 아이콘 설정 + 앱 수명 주기
```

## macOS 원본과의 차이점

| 항목 | macOS (claude-usage-mini) | Windows (이 프로젝트) |
|------|--------------------------|----------------------|
| 언어 | Swift 6.2 + SwiftUI | C# 13 + WPF |
| 플랫폼 | macOS 26+ | Windows 10+ |
| UI 위치 | 메뉴바 | 시스템 트레이 |
| 인증 | 자체 OAuth PKCE 플로우 | Claude Code 토큰 재사용 |
| 아이콘 스타일 | 메뉴바 애니메이션 바 | 색상 코드 트레이 아이콘 |

## 기여하기

PR은 언제든 환영합니다! 개선 아이디어:

- [ ] 액세스 토큰 만료 시 자동 갱신
- [ ] 새로고침 간격 설정 (설정 패널)
- [ ] Windows 시작 시 자동 실행 옵션
- [ ] 레이트 리밋 임박 시 토스트 알림
- [ ] 팝업에서 모델별 사용량 상세 표시

## 라이선스

MIT License

---

*유용하게 사용하셨다면 원본 프로젝트 [claude-usage-mini](https://github.com/jeremy-prt/claude-usage-mini)에도 ⭐ 부탁드립니다.*
