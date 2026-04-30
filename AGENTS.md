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
- **사용자가 제기한 작업 이슈는 `README.md` 등록과 함께 GitHub Issue도 반드시 생성하여 이력을 관리한다.**
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

### 5. 즉시 배포 원칙 (CD 우선)
- 주요 기능 추가나 버그 수정이 완료되어 `master` 브랜치에 푸시된 경우, **반드시 즉시 버전을 올리고(bump) 태그를 생성하여 푸시**한다.
- 이는 사용자가 수동 빌드 없이 GitHub Actions를 통해 즉시 최신 버전을 내려받을 수 있도록 하기 위함이다.
- 태그 생성 전 `csproj`의 버전과 `CHANGELOG.md`가 최신화되었는지 재확인한다.

---

## 🔄 레슨 런 (Lessons Learned — 반복 실수 방지)

### 빌드 및 배포
- **버전업 = GitHub Release 확인까지가 배포 완료**: 버전 번호를 올리고 태그 push까지만 하면 절반이다. GitHub Release가 실제로 생성되고 `ClaudeUsageTray.exe` 자산이 첨부돼야 배포가 끝난 것이다. 버전업 시 반드시 아래 순서를 모두 완료한다:
  ```bash
  # 1. 버전 bump: csproj + CHANGELOG 수정 후 커밋
  # 2. 태그 생성 및 push (CI가 GitHub Release 자동 생성)
  git tag v{버전}
  git push origin master && git push origin v{버전}
  # 3. GitHub Release 생성 확인 — exe 자산 첨부 여부 필수 검증
  gh release view v{버전}
  ```
- **릴리즈는 반드시 태그 push로**: 이 프로젝트의 CI(`release.yml`)는 브랜치 push에 반응하지 않고 **`v*` 형식의 태그 push에만 트리거**된다. 버전 bump 커밋 후 태그를 붙이지 않으면 GitHub Release가 생성되지 않는다.
- **WPF 트리밍 제한**: WPF/WinForms 앱에서 `PublishTrimmed=true` 옵션은 리소스 로딩 오류 및 빌드 실패를 유발하므로 사용하지 않는다.
- **ReadyToRun 주의**: `PublishReadyToRun=true`는 시작 속도를 개선하지만, 특정 환경(런타임 버전 미세 차이 등)에서 실행 즉시 크래시를 유발할 수 있다. 앱 실행 불가 보고 시 최우선 제거 대상이다.
- **단일 파일 배포 확인**: `PublishSingleFile=true` 설정 시, `SelfContained=false`인 경우 반드시 사용자의 PC에 해당 버전의 .NET Runtime이 설치되어 있어야 한다. (미설치 시 아무 반응 없이 종료됨)
- **태그 푸시 후 모니터링**: 릴리즈 태그(`v*`)를 푸시한 후에는 반드시 [GitHub Actions](https://github.com/jeiel85/claude-usage-tray-windows/actions)를 모니터링하여 빌드 및 릴리즈가 성공적으로 완료되는지 확인한다. 빌드 실패 시 즉시 원인을 파악하고 태그 재발행 등의 조치를 취해야 하며, 실제 릴리즈 자산이 생성될 때까지 작업을 종료하지 않는다.
- **릴리즈 자산 검증**: GitHub 릴리즈 생성 후 반드시 파일 크기(최소 수 MB 이상)와 릴리즈 노트가 정상적으로 포함되었는지 확인한다. 특히 단일 실행 파일(`ClaudeUsageTray.exe`)이 정상적으로 업로드되었는지 검증한다.

### GitHub Actions (CI/CD)
- **릴리즈 노트 추출**: `awk` 보다는 `sed`와 정규표현식을 조합하여 `CHANGELOG.md`의 특정 버전 섹션(예: `## [1.x.x]`)을 추출하는 것이 버전 형식 변화에 더 강인하다.
- **태그 재발행**: 빌드 오류로 태그를 다시 달아야 할 경우, 로컬과 원격의 태그를 모두 삭제(`git tag -d`)한 후 다시 푸시해야 한다.
- **GitHub Issue 본문 개행 보존**: `gh issue create/edit --body`에 `\n` 이스케이프 문자열을 직접 넣지 말고, PowerShell here-string(`@' ... '@`) 또는 `--body-file`을 사용해 실제 줄바꿈으로 작성한다.

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

- **v1.21.8**: 일일 목표 설정 제거 및 자동화(7일 최대치 기반), 설정 화면 정리.
- **v1.21.7**: 설정 화면 내 자동 모드 안내 문구 간소화(개행 방지 최적화).
- **v1.17.5**: Codex 초기화 시간 폴백 표시 (로그에 없을 시 최초 활동 기준).
- **v1.17.4**: 완료된 GitHub 이슈 정리 (#57, #58), 소진 예측 문구(추세대로면~) 표시 조건 개선 (100% 소진 시 숨김).
- **v1.17.3**: "Extra Credits" 문구 다국어 지원 추가 (한국어, 중국어, 일본어, 영어), 프로젝트 및 문서 버전 동기화.
- **v1.17.2**: 사용량 팝업 가독성 개선 (폰트 크기 증대 및 시인성 강화), 프로젝트 및 문서 버전 동기화.
- **v1.17.1**: 팝업 보조 텍스트 크기 원복, 설정창 초기 로딩 가드 추가.
- **v1.17.0**: Codex/Gemini 사용량 바 미표시 버그 수정, 새로고침 버튼 미동작 수정, 앱 제목 일반화 및 레이아웃 재구성.
- **v1.16.2**: Gemini CLI 토큰 파싱 구현(실시간 % 계산), 추가 사용량(Extra Usage) 위치를 Claude 섹션 내부로 조정, 트레이 게이지 기준 명시.
