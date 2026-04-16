# Claude Usage Tray Windows — Agent Notes

Windows 시스템 트레이 기반 Claude AI 사용량 모니터링 앱.

---

## 프로젝트 기본 정보

| 항목 | 내용 |
|------|------|
| **저장소** | `https://github.com/jeiel85/claude-usage-tray-windows` |
| **언어** | C# (WPF, .NET 9) |
| **Target Framework** | `net9.0-windows10.0.17763.0` |
| **주요 NuGet** | `CommunityToolkit.Mvvm 8.4.2`, `Microsoft.Extensions.Http 10.0.5`, `Microsoft.Toolkit.Uwp.Notifications 7.1.3` |
| **릴리즈 형식** | self-contained (기본) + framework-dependent (선택) |
| **현재 버전** | `v1.15.21` (릴리즈 시 csproj의 `<Version>` 참조) |

---

## 빌드

### 로컬 빌드

```powershell
# Release 빌드
dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo

# 빌드 + 실행 (기존 프로세스 자동 종료)
# 프로젝트 루트의 build.bat 실행
.\build.bat

# self-contained exe만 생성
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true

# framework-dependent exe만 생성
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj `
  -c Release `
  --self-contained false `
  -p:PublishSingleFile=true
```

### 빌드 출력 경로

| 형식 | 로컬 경로 | GitHub Actions 경로 |
|------|-----------|---------------------|
| **self-contained exe** | `ClaudeUsageTray\bin\sc\ClaudeUsageTray.exe` | `ClaudeUsageTray/bin/sc/ClaudeUsageTray.exe` |
| **framework-dependent exe** | `ClaudeUsageTray\bin\fd\ClaudeUsageTray.exe` | `ClaudeUsageTray/bin/fd/ClaudeUsageTray-framework-dependent.exe` |
| **SHA256 (sc)** | `SHA256.txt` | `SHA256.txt` |
| **SHA256 (fd)** | `SHA256-framework-dependent.txt` | `SHA256-framework-dependent.txt` |

> ⚠️ **csproj 경로 주의**: `dotnet publish` 명령어에서 csproj 경로는 **프로젝트 루트 기준** (`ClaudeUsageTray/ClaudeUsageTray.csproj`). GitHub Actions의 작업 디렉토리는 레포 루트이므로 `-p:PublishDir=bin/sc`는 `ClaudeUsageTray/bin/sc/`에 출력됨.

### 릴리즈 워크플로우 (GitHub Actions)

- **트리거**: `v*` 태그 푸시 시 자동 실행
- **위치**: `.github/workflows/release.yml`
- **산출물**: GitHub Release에 아래 파일 게시

| 파일 | 설명 |
|------|------|
| `ClaudeUsageTray-sc.exe` | self-contained exe (.NET 설치 없이 실행 가능, 권장) |
| `ClaudeUsageTray-sc.sha256` | self-contained exe의 SHA256 해시 |
| `ClaudeUsageTray-fd.exe` | framework-dependent exe (dotnet 9 설치 PC용) |
| `ClaudeUsageTray-fd.sha256` | framework-dependent exe의 SHA256 해시 |

> ⚠️ **릴리즈 워크플로우 수정 시 주의사항**:
> - self-contained와 framework-dependent 빌드는 `-p:PublishDir`로 **서로 다른 경로**에 출력해야 파일이 덮어씌워지지 않음
> - GitHub Release에서 같은 이름의 파일은 **같은 애셋**으로 인식되므로, 두 exe 모두 리네임 후 게시 (`ClaudeUsageTray-sc.exe`, `ClaudeUsageTray-fd.exe`)
> - SHA256 해시 생성 단계의 경로와 게시 단계의 경로가 **정확히 일치**해야 함
> - `UpdateService.cs`는 `-sc.exe` / `-sc.sha256`를 명시적으로 검색 — 자동 업데이트는 항상 sc 빌드를 받음

---

## 릴리즈 배포

### v* 태그 방식 (권장 — 자동화)

```powershell
# 1. CHANGELOG.md에 새 버전 섹션 추가 (이미 되어있어야 함)
# 2. csproj 버전 + CHANGELOG 변경
# 3. git add + commit + tag + push
git add -A
git commit -m "release: vX.Y.Z"
git tag vX.Y.Z
git push origin master --tags
```

### release.bat (로컬 자동화)

```powershell
# 프로젝트 루트에서 실행
.\release.bat
```

버전 선택 (patch/minor/major/직접입력) → csproj 업데이트 → publish → git commit/tag/push → GitHub Release 자동 생성.

### 주의: csproj 버전 vs 태그 버전

- **csproj `<Version>`**: 현재 앱에 표시되는 버전 (예: `1.15.8`)
- **Git 태그**: `v` prefix 필요 (예: `v1.15.8`)
- GitHub Actions는 태그에서 버전을 파싱하여 exe에 삽입 (`-p:Version=`)

---

## 프로젝트 구조

```
ClaudeUsageTray/
├── App.xaml[.cs]          # 앱 진입점 — 트레이 아이콘, 중복실행방지(Mutex), 예외핸들러
├── Converters/
│   ├── InverseBooleanToVisibilityConverter.cs  #Inverse bool → Visibility 컨버터
│   └── StringToVisibilityConverter.cs      # 빈 문자열 → Collapsed 컨버터
├── Models/
│   ├── Credentials.cs     # OAuth 인증 모델 (AccessToken, RefreshToken, ExpiresAt)
│   ├── UsageData.cs      # API 응답 모델
│   └── NotificationSettings.cs  # 설정 직렬화 모델
├── Services/
│   ├── AppConstants.cs         # 매직 넘버 중앙化管理 (폴링 간격, 타임아웃 등)
│   ├── CredentialService.cs     # OAuth 토큰 갱신 + FileSystemWatcher로 계정 전환 감지
│   ├── UsageApiService.cs       # api.anthropic.com/api/oauth/usage 호출
│   ├── SessionMonitor.cs         # ~/.claude/projects/*.jsonl 파싱 → 토큰 집계
│   ├── NotificationService.cs   # Windows 알림 + ntfy.sh 푸시
│   ├── SettingsService.cs       # 설정 저장/불러오기
│   ├── UpdateService.cs         # GitHub Releases → SHA256 검증 → 배치 스크립트 업데이트
│   ├── HistoryService.cs        # 7일 이력 저장 (orgUuid별 분리) + CSV 내보내기
│   └── LocalizationService.cs   # 한국어/영어/중국어/일본어 — 모든 문자열
├── ViewModels/
│   └── MainViewModel.cs         # 전체 비즈니스 로직
└── Views/
    ├── UsagePopup.xaml[.cs]    # 메인 팝업 — 차트, 토글, 드래그, 키보드 단축키
    ├── SettingsWindow.xaml[.cs] # 설정 모달
    └── UpdateDialog.xaml[.cs]   # 업데이트 대화상자

.github/workflows/release.yml     # GitHub Actions 빌드/릴리즈 자동화
CHANGELOG.md                    # 버전별 변경 이력 (한국어+영어+중국어+일본어)
build.bat                       # 빌드 + 프로세스 종료 + 실행
release.bat                     # 버전 선택 → csproj → publish → git push → GitHub Release
```

---

## 주요 데이터 경로

| 데이터 | 경로 |
|--------|------|
| **OAuth 토큰** | `%USERPROFILE%\.claude\.credentials.json` (Claude Code가 관리) |
| **세션 로그** | `%USERPROFILE%\.claude\projects\**\*.jsonl` |
| **앱 설정** | `%USERPROFILE%\.claude\claude-usage-tray.json` |
| **사용량 이력** | `%USERPROFILE%\.claude\claude-usage-tray-history.json` (또는 `claude-usage-tray-history-{orgUuid 앞8자리}.json`) |

---

## 개발 시 중요 규칙

### 1. 하드코딩 금지 — 경로는 항상 동적으로

```csharp
// ✅ 올바른 예
Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")

// ❌ 금지 — 사용자 환경에 따라 경로가 다름
Path.Combine("C:\\Users\\jeiel", ".claude")
```

### 2. SHA256 검증 — 업데이트 관련 코드 수정 시

`UpdateService.cs`의 `ApplyUpdateAsync`는 반드시:
- exe 다운로드 후 SHA256 검증 수행
- sha256 파일 없으면 (예전 버전 호환) 기존대로 설치 진행
- 불일치 시 `InvalidOperationException` 발생 → `UpdateDialog`에서 빨간색 에러 표시

### 3. ntfy 토픽 검증 — 설정 관련 코드 수정 시

`SettingsWindow.xaml.cs`의 `ValidateAndSaveNtfyTopic` 규칙:
- 빈 문자열: 허용 (ntfy 미사용)
- 길이: 20자 이상 필수
- 문자: `^[a-z0-9_\-@.]+$` 만 허용
- 위반 시 저장 차단 + 경고 표시

### 4. LocalizationService — 문자열 추가 시

- 모든 새 문자열은 4개 언어 모두 추가 필수 (ko, zh, ja, en)
- 패턴: `public static string Name => Lang switch { ... }`
- `Loc.`으로 ViewModel과 View에서 접근

### 5. 빌드 경고 방지

```csharp
// ❌ 미사용 변수 — 컴파일러 경고 발생
const double labelH = 14;

// ✅ 제거하거나 실제로 사용
```

### 6. WPF 리소스 정리

```csharp
// Icon 등 GDI 리소스 사용 시 Dispose 필수
var oldIcon = _trayIcon.Icon;
_trayIcon.Icon = newIcon;
oldIcon?.Dispose();
```

---

## CI/CD 보안 고려사항

### SHA256 해시 검증 흐름

```
GitHub Actions (릴리즈 시):
  빌드 → SHA256.txt 생성 → exe + SHA256.txt 게시

앱 (업데이트 시):
  CheckForUpdateAsync()
    → GitHub API assets에서 .exe + .sha256 URL 추출
  ApplyUpdateAsync(downloadUrl, sha256Url)
    → exe 다운로드 → SHA256.txt 다운로드 → 해시 비교
      - 불일치 → InvalidOperationException → 빨간색 에러
      - sha256 파일 없음 → 기존대로 설치 (예전 버전 호환)
```

### ntfy 보안 설계 한계

ntfy.sh는 topic 이름을 secret으로 사용하는 구조. topic을 아는 누구나 subscribe + send 가능.
- **입력 검증**: 20자 이상, 허용 문자 제한 (이미 구현됨)
- **근본 해결**: 자체 호스팅 ntfy 서버 또는 Firebase Cloud Messaging 권장

### 하드코딩된 ClientId

`CredentialService.cs`의 `ClientId`는 Anthropic 공식 OAuth 2.0 Client ID로, 공개 정보입니다.
비밀 토큰은 `credentials.json`에만 있으며 앱이 외부로 전송하지 않습니다.

---

## Git 사용 시 참고

### 최초 설정 (새 환경에서)

```powershell
git config --global --add safe.directory "D:/Project/claude-usage-tray-windows"
```

### GitHub CLI 설치 (릴리즈 자동 생성용)

```powershell
winget install GitHub.cli
gh auth login
```

---

## 자주 묻는 질문

**Q: 빌드가 안 됩니다**
A: .NET 9 SDK 설치 필요. `winget install Microsoft.DotNet.SDK.9`

**Q: GitHub Actions가 안 돌아갑니다**
A: 저장소가 git 초기화되지 않았거나, 원격 origin이 없거나, 태그가 `v*` 패턴이 아닙니다

**Q: self-contained vs framework-dependent 차이**
A: self-contained는 .NET 런타임 포함 (~77MB), 모든 PC에서 실행 가능. framework-dependent는 .NET 9 설치 PC에서만 실행 (~25MB)

**Q: GitHub Actions 빌드/릴리즈가 실패합니다**
A: 주로 경로 문제입니다. csproj 경로는 **프로젝트 루트 기준**, `-p:PublishDir` 출력은 `ClaudeUsageTray/` 서브디렉토리 기준입니다.

**Q: GitHub Release에 framework-dependent exe가 업로드되지 않습니다**
A: 같은 이름(`ClaudeUsageTray.exe`)의 파일이 있으면 GitHub가 같은 애셋으로 인식합니다. 게시 전에 `ClaudeUsageTray-framework-dependent.exe`로 리네임해야 합니다.

**Q: CHANGELOG 형식**
A: `<!-- ko -->`, `<!-- /ko -->` 블록으로 4개 언어 병기. `release.bat`이 새 섹션 헤더를 자동 생성합니다.
