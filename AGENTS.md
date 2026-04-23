# Claude Usage Tray Windows — Agent Notes

Windows 시스템 트레이 기반 Claude AI 사용량 모니터링 앱.

---

## 📌 현재 이슈 및 진행 현황

### v1.15.36 (진행 중)
| # | 제목 | 분류 | 상태 |
|---|------|------|------|
| [#44] | 업데이터 용량 최적화 (20MB -> 5MB 이하 목표) | 개선 | 완료 (Trimming 적용) |
| [#45] | 메인 앱 실행 속도 최적화 (Startup delay 개선) | 개선 | 완료 (RTR 적용) |
| — | 문서 파편화 정리 (ISSUE_LOG -> AGENTS 통합) | 개선 | 진행 중 |

### 오픈 이슈 (Open)
| # | 제목 | 분류 | 비고 |
|---|------|------|------|
| [#5] | 여러 계정 지원 — 재로그인 후 이전 계정 데이터 표시됨 | 버그 | 조사 필요 |
| [#41] | 설정 창 단축키 (ESC, Alt+F4, Ctrl+W) 지원 | 개선 | |
| [#38] | 기본 사용량 소진 후 추가 사용량을 트레이에 표시 | 개선 | |
| [#37] | 추가 사용량에 대한 알림 | 개선 | |
| [#10] | Discord / Slack 웹훅 알림 지원 | 기능 | 로드맵 |

### 🗺 장기 로드맵 (Roadmap)
- [#43] 빌드 배포 시 설치 파일(.msi/.exe installer)로 배포 방안 검토
- [#42] Microsoft Store 출시 목표
- [#13] 히스토리 보관 기간 확장 및 30일 차트

---

## ⚠️ 작업 전 필수 확인 사항

**매 작업 전에 반드시 실행:**

```powershell
# 1. 최신 소스 동기화
git fetch origin
git pull origin master

# 2. 상태 확인
git status
```

이유: 다른 환경에서 push된 변경사항이 있을 수 있으며, 구버전 소스로 작업하면 충돌이나 불필요한 변경이 발생할 수 있습니다.

---

## 📋 핵심 개발 규칙 (반드시 준수)

### 0. 이슈 기반 개발
- **모든 개발 요청 및 버그 수정은 반드시 GitHub 이슈(또는 `AGENTS.md`의 이슈 섹션)에 먼저 등록한 후 처리를 시작한다.**
- 이슈 등록 시 분류(버그, 기능, 개선 등)와 구체적인 목표를 명시한다.

### 1. 하드코딩 금지 — 경로는 항상 동적으로
```csharp
// ✅ 올바른 예
Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
```

### 2. SHA256 검증 — 업데이트 관련 코드 수정 시
`UpdateService.cs`의 `ApplyUpdateAsync`는 반드시:
- exe 다운로드 후 SHA256 검증 수행
- sha256 파일 없으면 (예전 버전 호환) 기존대로 설치 진행
- 불일치 시 `InvalidOperationException` 발생 → `UpdateDialog`에서 빨간색 에러 표시

### 3. LocalizationService — 문자열 추가 시
- 모든 새 문자열은 4개 언어 모두 추가 필수 (ko, zh, ja, en)
- 패턴: `public static string Name => Lang switch { ... }`
- `Loc.`으로 ViewModel과 View에서 접근

---

## 🔄 프로젝트 진행 중 겪은 시행착오 (Lessons Learned)

> 이 섹션은 개발 과정에서 실제로 발생한 문제들과 해결책을 기록합니다.

### 빌드 및 배포
- **fd 빌드 크기 (178MB 문제)**: CI 환경에서 `--self-contained false`임에도 WPF 런타임이 포함되는 문제. TFM 하향 및 Trimming으로 해결 시도 중.
- **framework-dependent 빌드 exe 실행 안 되는 문제**: `PublishSingleFile=true` 누락 시 DLL 없이 런처 스텁만 배포됨. csproj에 명시적 추가 필요.
- **GitHub Actions YAML bash 문법**: `name: "v${GITHUB_REF_NAME#v}"`는 작동하지 않음. `${{ github.ref_name }}` 사용 필수.

### 자동 업데이트 및 파일 처리
- **한글 경로 처리**: 사용자 폴더명에 한글 포함 시 배치 스크립트 실패. UTF-8 BOM 추가 및 `robocopy` 사용으로 해결.
- **파일 잠금 충돌**: `SessionMonitor`가 `.jsonl` 파일을 `FileShare.Read`로 열어 Claude의 쓰기를 차단하던 문제. `FileShare.ReadWrite`로 변경하여 해결.
- **SHA256 검증 스킵**: 다운로드 스트림이 열려 있어 검증 코드가 파일을 읽지 못함. 검증 전 스트림 Close 필수.

---

## 프로젝트 기본 정보

| 항목 | 내용 |
|------|------|
| **저장소** | `https://github.com/jeiel85/claude-usage-tray-windows` |
| **언어** | C# (WPF, .NET 9) |
| **Target Framework** | `net9.0-windows` |
| **주요 NuGet** | `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Http` |
| **현재 버전** | `v1.15.36` |

---

## 빌드 및 데이터 경로

### 로컬 빌드 명령
```powershell
# 빌드 + 실행 (기존 프로세스 자동 종료)
.\build.bat

# publish (최적화 옵션 포함)
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj -c Release -p:PublishDir=bin/release
```

### 주요 데이터 경로
| 데이터 | 경로 |
|--------|------|
| **OAuth 토큰** | `%USERPROFILE%\.claude\.credentials.json` |
| **세션 로그** | `%USERPROFILE%\.claude\projects\**\*.jsonl` |
| **앱 설정** | `%USERPROFILE%\.claude\claude-usage-tray.json` |
| **사용량 이력** | `%USERPROFILE%\.claude\claude-usage-tray-history.json` |

---

## 자주 묻는 질문 (FAQ)
**Q: 빌드가 안 됩니다**  
A: .NET 9 SDK 설치 필요. `winget install Microsoft.DotNet.SDK.9`

**Q: 릴리즈 exe가 실행 안 됩니다**  
A: .NET 9 Desktop Runtime이 필요합니다.

**Q: 업데이트 시 SmartScreen 경고**  
A: 배치/PowerShell 스크립트를 통해 Updater를 실행하여 경고를 최소화합니다.
