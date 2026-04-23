# Claude Usage Tray Windows — Agent Notes

Windows 시스템 트레이 기반 Claude AI 사용량 모니터링 앱.

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

## 🔄 프로젝트 진행 중 겪은 시행착오 (Lessons Learned)

> 이 섹션은 개발 과정에서 실제로 발생한 문제들과 해결책을 기록합니다.

### 빌드 관련

#### 1. framework-dependent 빌드 exe 실행 안 되는 문제
- **현상**: `PublishSingleFile=true` 누락으로 DLL 없이 런처 스텁만 배포됨
- **해결**: csproj에 `<PublishSingleFile>true</PublishSingleFile>` 추가
- **관련 버전**: v1.15.35

#### 2. fd 빌드 크기 문제 (178MB → 25MB → ~3-5MB)
- **원인1**: sc 빌드의 `-r win-x64` RID가 fd 빌드에 전파됨
  - **해결**: fd 빌드에 `-r win-x64` 명시적 지정
- **원인2**: `dotnet clean` 미실행으로 sc 캐시 재사용
  - **해결**: sc 빌드 후 `dotnet clean` 실행
- **원인3**: TFM `net9.0-windows`(버전 미지정)이 WPF 런타임 포함
  - **해결**: `net9.0-windows10.0.17763.0` 사용
- **원인4**: `Microsoft.Windows.SDK.NET.dll`(23 MB) 포함
  - **해결**: WinRT 알림 → balloon tip 변경, TFM을 `net9.0-windows`로/downgrade
- **관련 버전**: v1.15.12~15

#### 3. sc/fd 빌드 환경 간섭 문제
- **현상**: 동일 job 내에서 sc 빌드 후 fd 빌드 시 `--self-contained false` 무시
- **해결**: sc와 fd를 각각 별도 job으로 분리 (완전한 환경 격리)
- **관련 버전**: v1.15.16

---

### 자동 업데이트 관련

#### 4. 한글 경로 처리 문제
- **현상**: 사용자 폴더 경로에 한글이 포함된 환경에서 배치 스크립트 파일 복사 실패
- **해결**:
  1. 배치 스크립트 UTF-8 BOM 추가
  2. `copy` → `robocopy` 명령 변경
- **관련 버전**: v1.15.19

#### 5. SHA256 검증이 항상 스킵되는 문제
- **원인**: 다운로드 스트림이 `FileShare.None`으로 열려 있어 검증 코드가 파일을 읽지 못함
- **해결**: 검증 전 스트림 Close
- **관련 버전**: v1.15.11

#### 6. SHA256.txt 파일 인식 문제
- **현상**: `.sha256` 확장자만 인식하고 `.txt`는 무시
- **해결**: `.sha256`, `.txt` 확장자 모두 인식하도록 수정
- **관련 버전**: v1.15.29

#### 7. 파일명 변경으로 인한 구버전 호환성 문제
- **현상**: v1.15.17에서 파일명을 `ClaudeUsageTray-sc.exe` → `ClaudeUsageTray.exe`로 변경
- **문제**: 구버전이 `-sc.exe`Suffix를 찾아 실패
- **해결**:新旧 파일명 모두 정상 인식하도록 호환성 로직 추가
- **관련 버전**: v1.15.18

#### 8. TEMP 경로 공백 문제
- **현상**: 사용자명에 공백이 있을 때 업데이트 후 파일이 교체되지 않음
- **해결**: `cmd.exe /c` 경로 인용 방식 수정 (double-double-quoting)
- **관련 버전**: v1.15.11

#### 9. race condition - 프로세스 종료 대기 문제
- **현상**: 업데이트 시 기존 프로세스가 완전히 종료되기 전에 exe 복사 시도
- **해결**: 프로세스 종료 대기 로직 추가
- **관련 버전**: v1.15.24

#### 10. SmartScreen 경고
- **현상**: Updater.exe 실행 시 "게시자를 확인할 수 없습니다" 보안 경고
- **해결**: 배치 스크립트를 통해 실행하여 경고 최소화
- **관련 버전**: v1.15.32

#### 11. SHA256 파일 경로 불일치
- **현상**: 빌드 출력 경로와 SHA256 생성 경로가 맞지 않음
- **해결**: 경로一致性 확인
- **관련 버전**: v1.15.7

---

### 파일 처리 관련

#### 12. Claude 앱과 파일 잠금 충돌
- **현상**: Claude 앱 업데이트 후 "파일이 다른 응용 프로그램에서 사용 중" 오류
- **원인**: `SessionMonitor`가 `.jsonl` 파일을 `FileShare.Read`로 열어 Claude 쓰기 차단
- **해결**: `FileShare.ReadWrite`로 변경하여 동시 접근 허용
- **관련 버전**: v1.15.9

---

### 인증/계정 관련

#### 13. 토큰 갱신 시 계정 전환 오감지
- **현상**: 앱이 `credentials.json`에 토큰을 쓸 때 FileSystemWatcher가 계정 전환으로 감지
- **해결**: `_isSelfWriting` 플래그로 self-trigger 방지
- **관련 버전**: v1.15.3

#### 14. 로그아웃/재로그인 시 계정 전환 미감지
- **현상**: FileSystemWatcher가 `Changed` 이벤트만 감지
- **문제**: 파일 삭제(로그아웃)/생성(재로그인) 시 Created, Deleted 이벤트 감지 안 됨
- **해결**: `Created`, `Deleted` 이벤트 및 `FileName` NotifyFilter 추가
- **관련 버전**: v1.15.2

#### 15. 계정 전환 시 rate-limit 상태 미초기화
- **현상**: 이전 계정의 rate-limit 대기가 새 계정 API 조회 차단
- **해결**: 계정 전환 시 대기 상태 초기화
- **관련 버전**: v1.15.3

---

### UI/UX 관련

#### 16. NullReferenceException on startup
- **현상**: 앱 시작 시 타이머 초기화 순서 문제로 크래시
- **해결**: 타이머 초기화 순서 수정
- **관련 버전**: v1.15.28

#### 17. 갱신 주기 입력 방식
- **현상**: 텍스트 입력으로 사용자가 입력하기 번거로움
- **해결**: 슬라이더 UI로 변경 (1~10분)
- **관련 버전**: v1.15.31

---

### 보안 관련

#### 18. ntfy 토픽 검증
- **문제**: ntfy.sh가 topic 이름을 secret으로 사용 (보안 설계 한계)
- **해결**: 20자 이상, 허용 문자(`^[a-z0-9_\-@.]+$`) 제한
- **관련 버전**: v1.15.6

---

### CI/CD 관련

#### 19. exe 파일명 충돌
- **현상**: sc/fd 둘 다 `ClaudeUsageTray.exe`로 게시되어 GitHub가 같은 애셋으로 인식
- **해결**: 게시 전 파일 리네임 (`ClaudeUsageTray-sc.exe`, `ClaudeUsageTray-fd.exe`)
- **관련 버전**: v1.15.8

#### 20. GitHub Actions 태그 파싱 오류
- **현상**: bare bash 변수로 태그 파싱 실패
- **해결**: `github.ref_name` 사용
- **관련 버전**: v1.15.17

#### 21. fd 빌드 크기 (178 MB) 문제
- **원인**: CI 환경에서 `--self-contained false`임에도 `Microsoft.Windows.SDK.NET.dll`(23 MB) 및 WPF 런타임이 번들링됨
- **시도한 해결책들**:
  - TFM `net9.0-windows` 변경 → 실패 (155 MB)
  - `dotnet clean` 추가 → 실패 (178 MB)
  - job 분리 → 실패 (178 MB 재발)
- **최종 해결**: fd 빌드 폐기, sc만 제공
- **관련 버전**: v1.15.12~17

---

## 📋 CI/CD 재발 방지 메모

### GitHub Actions YAML에서 bash 문법 사용 금지

**증상**:릴리즈 제목이 `v${GITHUB_REF_NAME#v}` 문자열 그대로 표시됨

**원인**: `softprops/action-gh-release`의 `name:` 필드는 YAML 값이므로 bash 변수 치환(`${}`)이 동작하지 않음

```yaml
# ❌ 잘못된 방법
name: "v${GITHUB_REF_NAME#v}"

# ✅ 올바른 방법
name: ${{ github.ref_name }}
```

### fd(sc/fd) 빌드 관련

- **절대 로컬에서 확인한 크기가 CI와 다르게 나올 수 있음**
- CI에서 `--self-contained false`임에도 전체 WPF 런타임이 포함됨
- 로컬没问题不代表 CI没问题

---

## 📋 핵심 개발 규칙 (반드시 준수)

---

## 프로젝트 기본 정보

| 항목 | 내용 |
|------|------|
| **저장소** | `https://github.com/jeiel85/claude-usage-tray-windows` |
| **언어** | C# (WPF, .NET 9) |
| **Target Framework** | `net9.0-windows10.0.17763.0` |
| **주요 NuGet** | `CommunityToolkit.Mvvm 8.4.2`, `Microsoft.Extensions.Http 10.0.5`, `Microsoft.Toolkit.Uwp.Notifications 7.1.3` |
| **릴리즈 형식** | framework-dependent (총 ~350KB) |
| **현재 버전** | `v1.15.35` (릴리즈 시 csproj의 `<Version>` 참조) |

### .NET 9 런타임 요구사항

릴리즈 exe 실행 시 **.NET 9 Desktop Runtime**이 설치되어 있어야 합니다.

| 설치 방법 | 설명 |
|----------|------|
| **Microsoft Store** | "NET Desktop Runtime 9" 검색 |
| **직접 다운로드** | https://dotnet.microsoft.com/download/dotnet/9.0 |
| **Windows Update** | 대부분의 Windows 10/11 PC에 자동 설치됨 |

---

## 빌드

### 로컬 빌드

```powershell
# Release 빌드 (개발/테스트용)
dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo

# 빌드 + 실행 (기존 프로세스 자동 종료)
.\build.bat

# framework-dependent exe 생성 (릴리즈용)
dotnet publish ClaudeUsageTray/ClaudeUsageTray.csproj -c Release -p:PublishDir=bin/release

# Updater framework-dependent exe 생성
dotnet publish ClaudeUsageTray.Updater/ClaudeUsageTray.Updater.csproj -c Release -p:PublishDir=bin/updater
```

### 빌드 출력 경로

| 프로젝트 | 로컬 경로 |
|---------|-----------|
| **메인 앱** | `ClaudeUsageTray\bin\Release\net9.0-windows10.0.17763.0\ClaudeUsageTray.exe` |
| **Updater** | `ClaudeUsageTray.Updater\bin\Release\net9.0-windows10.0.17763.0\ClaudeUsageTray-Updater.exe` |
| **릴리즈 publish** | `bin\release\ClaudeUsageTray.exe` |
| **Updater publish** | `bin\updater\ClaudeUsageTray-Updater.exe` |

> ⚠️ **csproj 경로 주의**: `dotnet publish` 명령어에서 csproj 경로는 **프로젝트 루트 기준** (`ClaudeUsageTray/ClaudeUsageTray.csproj`).

### 릴리즈 워크플로우 (GitHub Actions)

- **트리거**: `v*` 태그 푸시 시 자동 실행
- **위치**: `.github/workflows/release.yml`
- **산출물**: GitHub Release에 아래 파일 게시

| 파일 | 설명 | 크기 |
|------|------|------|
| `ClaudeUsageTray.exe` | 메인 앱 (framework-dependent) | ~170KB |
| `ClaudeUsageTray-Updater.exe` | 업데이트 실행기 (framework-dependent) | ~170KB |
| `SHA256.txt` | SHA256 해시 | 66B |

> ⚠️ **릴리즈 워크플로우 수정 시 주의사항**:
> - SHA256 해시 생성 단계의 경로와 게시 단계의 경로가 **정확히 일치**해야 함

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
    ├── UsagePopup.xaml[.cs]    # 메인 팝업 — 차트, 토글, 드래그, 키버드 단축키
    ├── SettingsWindow.xaml[.cs] # 설정 모달
    └── UpdateDialog.xaml[.cs]   # 업데이트 대화상자

ClaudeUsageTray.Updater/            # 업데이트 실행기 프로젝트
├── App.xaml[.cs]
├── MainWindow.xaml[.cs]          # 업데이트 진행률 UI
└── ClaudeUsageTray.Updater.csproj

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

### 0. 이슈 기반 개발
- **모든 개발 요청 및 버그 수정은 반드시 GitHub 이슈(또는 `docs/ISSUE_LOG.md`)에 먼저 등록한 후 처리를 시작한다.**
- 이슈 등록 시 분류(버그, 기능, 개선 등)와 구체적인 목표를 명시한다.

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

### 7. 메모리 누수 방지

- 이벤트 핸들러 구독 해제 필수 (`PropertyChanged`, `Elapsed` 등)
- `IDisposable` 구현 클래스는 `Dispose()`에서 정리
- `App.OnExit()`에서 모든 리소스 정리

---

## CI/CD 보안 고려사항

### SHA256 해시 검증 흐름

```
GitHub Actions (릴리즈 시):
  빌드 → SHA256.txt 생성 → exe + SHA256.txt 게시

앱 (업데이트 시):
  CheckForUpdateAsync()
    → GitHub API assets에서 .exe + SHA256.txt URL 추출
  ApplyUpdateAsync(downloadUrl, sha256Url)
    → 배치 스크립트 실행 → Updater.exe 실행
    → Updater: exe 다운로드 → SHA256.txt 다운로드 → 해시 비교
      - 불일치 → InvalidOperationException → 에러 표시
      - sha256 파일 없음 → 기존대로 설치 (예전 버전 호환)
```

### SmartScreen 우회

Updater.exe 실행 시 SmartScreen 경고 우회를 위해 배치 스크립트를 통해 실행됩니다.

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

**Q: 릴리즈 exe가 실행 안 됩니다**
A: .NET 9 Desktop Runtime이 필요합니다. Microsoft Store에서 "NET Desktop Runtime 9"를 검색하여 설치하세요.

**Q: GitHub Actions 빌드/릴리즈가 실패합니다**
A: 주로 경로 문제입니다. csproj 경로는 **프로젝트 루트 기준**, `-p:PublishDir` 출력은 `ClaudeUsageTray/` 서브디렉토리 기준입니다.

**Q: CHANGELOG 형식**
A: `<!-- ko -->`, `<!-- /ko -->` 블록으로 4개 언어 병기. `release.bat`이 새 섹션 헤더를 자동 생성합니다.

**Q: 업데이트 시 SmartScreen 경고**
A: 배치 스크립트를 통해 Updater를 실행하여 경고를 최소화합니다. 코드 서명 인증서를 구매하면 완전히 제거할 수 있습니다.
