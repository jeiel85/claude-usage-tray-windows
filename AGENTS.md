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
- **WPF 트리밍 제한**: WPF/WinForms 앱에서 `PublishTrimmed=true` 옵션은 리소스 로딩 오류 및 빌드 실패(`NETSDK1175`, `NETSDK1168`)를 유발하므로 사용하지 않는다. (시작 속도 개선을 위해 `ReadyToRun`만 사용 권장)
- **릴리즈 태그 주의**: 태그 생성(`git tag v*`) 전 반드시 모든 수정 사항이 커밋 및 푸시되었는지 확인한다. 태그가 잘못된 커밋에 붙으면 GitHub Actions 빌드 실패의 원인이 된다.
- **CI 환경 격리**: sc 빌드와 fd 빌드는 GitHub Actions에서 별도의 job으로 실행해야 간섭이 없다.

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
