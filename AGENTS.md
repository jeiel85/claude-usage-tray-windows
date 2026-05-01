# Claude Usage Tray Windows — Agent Rules (AGENTS.md)

이 문서는 에이전트가 이 프로젝트에서 작업할 때 준수해야 할 **핵심 가이드라인 및 통합 지침**을 담고 있습니다.

---

## ⚠️ 작업 전 필수 확인 사항

### 0. Source Sync First
세션을 시작하거나 첫 요청을 받았을 때, 항상 로컬 소스 코드가 최신인지 확인하고 동기화합니다.
```powershell
git fetch origin && git pull origin master && git status
```

---

## 🚀 개발 및 배포 원칙

### 1. 로컬 빌드 및 실행 금지
이 프로젝트는 **GitHub Actions(CI/CD)를 통해 빌드 및 배포**됩니다.
- **로컬 빌드/실행 금지**: `dotnet build`, `dotnet run` 등을 로컬에서 실행하지 않습니다.
- **검증 방식**: 정적 분석, 단위 테스트(`ClaudeUsageTray.Tests`), 그리고 GitHub Actions 빌드 결과로 검증합니다.
- **릴리즈**: `csproj` 버전 수정 후 태그(`v*`)를 푸시하여 CI가 처리하도록 합니다.

### 2. 즉시 배포 원칙 (CD 우선)
주요 기능 추가나 버그 수정이 완료되어 `master`에 푸시된 경우, **반드시 즉시 버전을 올리고(bump) 태그를 생성하여 푸시**합니다. 사용자가 GitHub Actions를 통해 즉시 최신 버전을 내려받을 수 있도록 하기 위함입니다.

---

## 📋 핵심 개발 규칙

### 1. 이슈 기반 개발
- 모든 작업은 `README.md`의 이슈 섹션 및 GitHub Issue에 등록 후 시작합니다.
- 이슈 등록 시 분류(버그, 기능, 개선)와 목표를 명시합니다.

### 2. 하드코딩 금지
- 사용자 경로는 항상 `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` 등을 사용하여 동적으로 생성합니다.

### 3. 보안 및 무결성
- 업데이트 파일 다운로드 후 반드시 SHA256 해시를 검증합니다 (`UpdateService.cs`).
- ntfy 토픽은 20자 이상, 허용 문자(`^[a-z0-9_\-@.]+$`)만 사용하도록 검증합니다.

### 4. 다국어 지원 (Localization)
- 모든 새 문자열은 `LocalizationService.cs`에 4개 언어(ko, zh, ja, en)를 모두 추가합니다. (`Loc.Name` 패턴 유지)

### 5. 품질 및 안정성
- 사용하지 않는 변수/경고를 방치하지 않으며, `IDisposable` 리소스는 반드시 정리합니다.
- 파일 접근 시 `FileShare.ReadWrite`를 사용하여 충돌을 방지합니다.

---

## 🔄 레슨 런 (Lessons Learned)

### 빌드 및 배포
- **버전업 완료의 정의**: `csproj` 버전 bump + `CHANGELOG.md` 수정 + 태그 push + **GitHub Release 자산(exe) 첨부 확인**까지가 완료입니다.
- **릴리즈 트리거**: CI는 브랜치 push가 아닌 **`v*` 형식의 태그 push**에만 반응합니다.
- **WPF 제약**: `PublishTrimmed=true`는 사용하지 않습니다. `PublishReadyToRun=true`는 실행 불가 문제 발생 시 최우선 제거 대상입니다.
- **단일 파일 배포**: `SelfContained=false`인 경우 사용자 환경에 .NET Runtime 설치가 필수임을 유지합니다.

### GitHub Actions & 도구
- **릴리즈 노트 추출**: `sed`와 정규표현식을 사용하여 `CHANGELOG.md`에서 버전을 추출합니다.
- **GitHub Issue**: 본문 개행 보존을 위해 PowerShell here-string(`@' ... '@`) 또는 `--body-file`을 사용합니다.

### 자동 업데이트 및 인증
- **PowerShell**: 프로세스 교체 안정성을 위해 PowerShell 스크립트를 우선합니다.
- **Atomic Write**: Claude 앱의 파일 쓰기는 `Renamed` 이벤트 감지가 필수입니다.
- **Rate Limit**: 계정 전환 시 이전 계정의 `Retry-After` 상태를 반드시 초기화합니다.

---

## 📜 최근 변경 이력 요약
*상세 내용은 [CHANGELOG.md](./CHANGELOG.md) 참조*

- **v1.21.10**: 트레이 표시 '자동' 모드 로직 개선 (사용량 있는 공급자 동적 우선순위 표시), 설정 안내 실시간 반영, GEMINI.md 지침 통합.
- **v1.21.8**: 일일 목표 설정 제거 및 자동화(7일 최대치 기반), 설정 화면 정리.
- **v1.21.7**: 설정 화면 내 자동 모드 안내 문구 간소화(개행 방지 최적화).
