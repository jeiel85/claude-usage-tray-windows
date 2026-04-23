# Claude Usage Tray Windows — Agent Rules

이 문서는 에이전트가 프로젝트에서 작업할 때 준수해야 할 **핵심 가이드라인**을 담고 있습니다.

---

## ⚠️ 작업 전 필수 확인 사항

```powershell
# 최신 소스 동기화 및 상태 확인
git fetch origin && git pull origin master && git status
```

---

## 📋 핵심 개발 규칙

### 0. 이슈 기반 개발
- **모든 개발/수정은 반드시 `README.md`의 이슈 섹션에 먼저 등록 후 시작한다.**
- 이슈 등록 시 분류(버그, 기능, 개선)와 목표를 명시한다.

### 1. 하드코딩 금지
- 사용자 경로는 항상 `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` 등을 사용하여 동적으로 생성한다.

### 2. 보안 및 무결성
- 업데이트 파일 다운로드 후 반드시 SHA256 해시를 검증한다 (`UpdateService.cs`).
- ntfy 토픽은 20자 이상, 허용 문자(`^[a-z0-9_\-@.]+$`)만 사용하도록 검증한다.

### 3. 다국어 지원 (Localization)
- 모든 새 문자열은 `LocalizationService.cs`에 4개 언어(ko, zh, ja, en)를 모두 추가한다.
- `Loc.Name` 패턴을 유지한다.

### 4. 품질 및 안정성
- 사용하지 않는 변수/경고를 방치하지 않는다.
- `IDisposable` 리소스는 반드시 정리한다.
- 파일 접근 시 `FileShare.ReadWrite`를 사용하여 충돌을 방지한다.

---

## 🔄 레슨 런 (Lessons Learned — 반복 실수 방지)

### 빌드 및 배포
- **WPF 트리밍 제한**: WPF/WinForms 앱에서 `PublishTrimmed=true` 옵션은 리소스 로딩 오류 및 빌드 실패를 유발하므로 사용하지 않는다.
- **ReadyToRun 주의**: `PublishReadyToRun=true`는 시작 속도를 개선하지만, 특정 환경(런타임 버전 미세 차이 등)에서 실행 즉시 크래시를 유발할 수 있다. 앱 실행 불가 보고 시 최우선 제거 대상이다.
- **단일 파일 배포 확인**: `PublishSingleFile=true` 설정 시, `SelfContained=false`인 경우 반드시 사용자의 PC에 해당 버전의 .NET Runtime이 설치되어 있어야 한다. (미설치 시 아무 반응 없이 종료됨)
- **릴리즈 자산 검증**: GitHub 릴리즈 생성 후 반드시 파일 크기(최소 수 MB 이상)와 릴리즈 노트가 정상적으로 포함되었는지 확인한다.

### GitHub Actions (CI/CD)
- **릴리즈 노트 추출**: `awk` 보다는 `sed`와 정규표현식을 조합하여 `CHANGELOG.md`의 특정 버전 섹션(예: `## [1.x.x]`)을 추출하는 것이 버전 형식 변화에 더 강인하다.
- **태그 재발행**: 빌드 오류로 태그를 다시 달아야 할 경우, 로컬과 원격의 태그를 모두 삭제(`git tag -d`)한 후 다시 푸시해야 한다.

### 안정성 가이드
- **최적화 옵션 최소화**: `ReadyToRun`이나 `Trimming`은 빌드 서버와 사용자 환경의 미세한 차이(런타임 패치 버전 등)로 인해 실행 불가 문제를 일으킬 확률이 높으므로, 데스크톱 WPF 앱에서는 가급적 사용을 피한다.

### 자동 업데이트
- **PowerShell 전환**: 배치 스크립트보다 PowerShell이 프로세스 종료/교체 안정성이 높다.
- **한글 경로**: 배치 스크립트 사용 시 UTF-8 BOM이 없으면 한글 경로에서 실패한다.

### 인증 및 계정
- **Atomic Write 감지**: Claude 앱은 파일 쓰기 시 임시 파일 생성 후 Rename하므로 `Renamed` 이벤트 감지가 필수다.
- **Rate Limit 상태**: 계정 전환 시 이전 계정의 `Retry-After` 상태를 반드시 초기화해야 한다.

---

## 📜 최근 변경 이력 요약
*상세 내용은 [CHANGELOG.md](./CHANGELOG.md) 참조*

- **v1.15.36**: 업데이터 용량 최적화, 메인 앱 RTR 적용, 문서 통합.
- **v1.15.35**: fd 빌드 실행 불가 버그 수정.
- **v1.15.34**: 업데이트 스크립트 PowerShell 전환.
