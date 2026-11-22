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
- 자동 업데이트는 업데이트 서명(`ClaudeUsageTray.exe.sig`, `UpdateSignature.cs`)이 맞는 exe 만 설치한다. 검증 없이 설치하는 경로를 만들지 않는다(`docs/DECISIONS.md` D-1). 서명 개인키는 메인 체크아웃 `.keystore/update-signing.pem`(+ `update-signing.properties`)에만 있고 절대 커밋·출력하지 않는다.
- ntfy 토픽은 20자 이상, 허용 문자(`^[a-z0-9_\-@.]+$`)만 사용하도록 검증한다.

### 3. 다국어 지원 (Localization)
- 모든 새 문자열은 `LocalizationService.cs`에 4개 언어(ko, zh, ja, en)를 모두 추가한다.
- `Loc.Name` 패턴을 유지한다.

### 3-1. 화면 디자인
- 팝업·설정 창에 공급자 섹션·게이지·안내 줄·입력 칸을 넣거나 고칠 때는 **먼저 [`docs/UI_GUIDELINES.md`](docs/UI_GUIDELINES.md) 를 읽고 그 규칙(색·막대 높이·주 게이지/목록 행·안내 줄·설정 탭 스크롤)을 따른다.** 새 패턴이 필요하면 같은 PR 에서 문서를 고친다.
- 화면을 바꾸면 `UiSnapshotTests` 로 렌더해 눈으로 확인한다(문서 7절).

### 4. 품질 및 안정성
- 사용하지 않는 변수/경고를 방치하지 않는다.
- `IDisposable` 리소스는 반드시 정리한다.
- 파일 접근 시 `FileShare.ReadWrite`를 사용하여 충돌을 방지한다.

### 5. 즉시 배포 원칙 (CD 우선)
- 주요 기능 추가나 버그 수정이 완료되어 `master` 브랜치에 푸시된 경우, **반드시 즉시 버전을 올리고(bump) 태그를 생성하여 푸시**한다.
- 이는 사용자가 수동 빌드 없이 GitHub Actions를 통해 즉시 최신 버전을 내려받을 수 있도록 하기 위함이다.
- 태그 생성 전 `csproj`의 버전과 `CHANGELOG.md`가 최신화되었는지 재확인한다.
- 릴리즈 태그/푸시 전에 가능한 경우 로컬에서 실제 실행 검증을 먼저 수행한다. 이 규칙은 CI 릴리즈 확인을 대체하지 않으며, 로컬 빌드나 실행이 환경상 불가한 경우에만 예외적으로 생략할 수 있고 그 사유를 기록한다.

### 6. 작업 완료 후 자동 릴리즈 사이클
- **모든 코드 수정(버그 수정, 기능 추가, 개선) 완료 후 다음 작업을 자동으로 수행한다:**
  1. `dotnet build`로 빌드 검증 (0 warning, 0 error 필수). 가능한 경우 실제 앱 실행으로 릴리즈 경로를 먼저 검증한다. 로컬 빌드/실행이 환경상 불가한 경우에만 이 단계는 예외적으로 건너뛸 수 있으며, 그 사유를 기록한다.
  2. 버전 bump (`csproj` `<Version>` 및 `<AssemblyVersion>` +1)
  3. `CHANGELOG.md` 최상단에 신규 버전 섹션 추가 (변경 내역 요약, ko/en 이중 언어)
  4. 커밋 → 태그(`v{x.y.z}`) → `git push origin master && git push origin v{x.y.z}`
  5. GitHub Release 생성 확인 (`gh release view v{x.y.z}` — exe 자산 첨부 여부까지 검증)
  6. **업데이트 서명**: `pwsh tools/sign-release.ps1 v{x.y.z}` → `gh release view` 에 `ClaudeUsageTray.exe.sig` 가 있는지 확인. 서명이 없으면 v1.67.3 이상 앱은 그 버전을 받지 않는다(조용히 "최신").
- **사용자가 "새 버전 만들기" 같은 명시적 요청을 하지 않아도 위 사이클을 자동 실행한다.**
- 단, 사용자가 "아직 릴리즈 하지 마" 또는 "커밋만 해" 등 명시적으로 보류 의사를 밝힌 경우에는 릴리즈를 생성하지 않는다.

---

## 🧠 판단 및 의사결정 원칙

### 6. 사용자 요청에 대한 냉정한 검토
- **사용자의 제안이나 수정 요청을 무조건 수용하지 않는다.**
- 모든 요청은 **팩트(fact)**, **보편적이고 객관적인 상식**, **프로젝트 기술 부채 및 유지보수성**을 기준으로 냉정하게 검토한다.
- "~하면 좋을 것 같습니다", "~해 주세요" 등의 요청에도, 실제로 프로젝트에 도움이 되는지, 부작용은 없는지, 더 나은 대안은 없는지를 먼저 판단한다.
- 잘못되었거나 비효율적인 요청, 프로젝트 방향과 맞지 않는 요청에 대해서는 **정중하지만 단호하게 반대 의견을 제시**한다.
- "네, 맞습니다", "좋습니다" 등의 무조건적 긍정은 금지한다. 답변 전 반드시 스스로에게 "이것이 정말 맞는가?"를 묻는다.

---

## 🔄 레슨 런 (Lessons Learned — 반복 실수 방지)

### 빌드 및 배포
- **버전 번호 삼위일체 (v1.21.9 교훈)**: 배포 전 다음 세 곳의 버전 번호가 반드시 일치하는지 전수 검사한다. 하나라도 다르면 업데이트 알림 루프에 빠지거나 버전 정보가 불일치하게 된다.
    1.  `ClaudeUsageTray.csproj`: `<Version>` 및 `<AssemblyVersion>`
    2.  `CHANGELOG.md`: 최상단 신규 버전 섹션 헤더 (예: `## [1.21.9]`)
    3.  `git tag`: 실제 푸시하는 태그명 (예: `v1.21.9`)
- **버전업 = GitHub Release 확인까지가 배포 완료**: 버전 번호를 올리고 태그 push까지만 하면 절반이다. GitHub Release가 실제로 생성되고 `ClaudeUsageTray.exe` 자산이 첨부돼야 배포가 끝난 것이다. 버전업 시 반드시 아래 순서를 모두 완료한다:
  ```bash
  # 1. 버전 bump: csproj + CHANGELOG 수정 후 커밋
  # 2. 태그 생성 및 push (CI가 GitHub Release 자동 생성)
  git tag v{버전}
  git push origin master && git push origin v{버전}
  # 3. GitHub Release 생성 확인 — exe 자산 첨부 여부 필수 검증
  gh release view v{버전}
  # 4. 업데이트 서명(배포 PC 에서) — ClaudeUsageTray.exe.sig 가 붙어야 배포 완료
  pwsh tools/sign-release.ps1 v{버전}
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

- **v1.72.12**: 업데이트 진단 로그 인코딩 — 교체 스크립트(PowerShell 5.1 `Out-File`)가 `claude_update_debug.log` 를 UTF-16 LE(BOM ff fe)로 새로 만드므로 `UpdateService.AppendDebugLog` 는 앞머리 BOM 을 보고 같은 인코딩으로 붙인다(새 파일은 UTF-8 BOM). 이 파일에 쓰는 코드를 추가할 때는 이 함수를 쓴다.
- **v1.72.11**: 하드닝 5차(#209 #113 #274 #89 #268) — `CodexUsageMonitor.ConversationInitCache` 가 `conversation/init` 본문을 계정(계정 ID, 없으면 토큰)마다 10분 들고 있다(실패 null 은 안 들고, 본문을 다시 해석하므로 지난 창은 버려짐). `UsageSyncService.WriteSnapshot` 은 (폴더·계정·provider·날짜)마다 하루 한 번 `PruneExpiredSnapshots` — `SnapshotRetentionDays`(14)보다 오래된 yyyy-MM-dd 폴더의 *.json·*.tmp 를 다른 PC 것까지 지우고 빈 폴더 삭제(읽는 쪽은 오늘·어제만). PC 이름이 바뀌면 `ApplyMobileSettings` 가 `MobileUsageFeed.Touch()`·`PeerSyncHub.Touch()` 로 열린 연결에 바로 보낸다. 서버 `Stop()` 뒤 실시간·동기화 소켓은 0.5초 안에 닫힘(회귀 테스트 `Stop_ClosesOpenLiveSocketsRightAway`·`StoppingTheHostServerDropsTheSyncConnectionRightAway`; #274 의 25초는 재현 안 됨 — 클라이언트 재연결 백오프 2·5·15초로 추정). 업데이트 확인 결과는 `UpdateService.DebugLogPath`(`%TEMP%\claude_update_debug.log`, 512KB 넘으면 새로)에 한 줄.
- **v1.72.10**: 하드닝 4차(#99 #89 #141 #145 #268 #213 #283 #288) — `UsageApiService` 는 같은 토큰의 401·5xx 가 이어지면 요청을 보내지 않고 `LastError` 를 그대로 둔다(1회차 0, 이후 4·8·16·30분; 토큰 변경·성공·`ResetFailureBackoff()`(자격 파일 변경·계정 전환) 때 풀림). 429·403 쿨다운(`LastRetryAfterSeconds` → `_apiRetryAfter`, 화면 "잠시 제한" 안내)과는 따로라 401 이 쿨다운 안내로 바뀌지 않는다. 테스트는 internal 생성자(토큰 공급자·`HttpClient`·시계). 업데이트 대화상자 오류는 `UpdateService.DescribeInstallFailure`(서비스가 낸 현재 언어 문구는 그대로, 그 밖은 `Loc.UpdateDownloadFailed`). 다른 PC 출처 문구는 재료(`_claudeQuotaOrigin`·`_codexQuotaOrigin`)를 들고 `RefreshSyncOriginLabels` 가 언어 변경 때 다시 만든다(화면 문구가 마지막으로 만든 것과 같을 때만). 세션 제목은 이름에 `-`·`_` 가 든 래퍼 태그만 거른다. `CodexUsageMonitor.ReadAuthInfo` 가 auth.json 을 경로별로 캐시(길이·수정 시각, 2초 안에 쓴 파일은 다시 읽음). `LocCollectionRuleTests` — `Loc.` 을 쓰는 테스트 클래스는 `[Collection("WpfTests")]` 필수(원문 검사). release.yml 은 github APK 의 `UPDATE_PACKAGES_WITHOUT_USER_ACTION`·`UpdateInstallReceiver` 를 확인하고, `mobile/app/build.gradle.kts` 는 부·수 번호가 100 이상이면 빌드를 멈춘다.
- **v1.72.9**: 휴대폰 매니페스트(#288) — `PairScanActivity` 의 세로 고정이 암시하는 `android.hardware.screen.portrait` 를 `required="false"` 로 선언(Play 사전 검사 "필수 하드웨어 기능" 해소). `release.yml` 의 'Verify APK package and channel' 이 badging 에 필수 `android.hardware.screen.*` 가 있으면 실패 — 화면을 방향 고정할 때는 같은 선언을 둔다.
- **v1.72.8**: 테스트만(#157) — `MainViewModelIntegrationTests.RefreshAsync_AppliesOfficialOpenCodeQuotaFromAnotherDevice` 가 동기화 폴더 → `RefreshAsync` → `OpenCodeVm` 값까지 본다(`CreateViewModel(usageSyncOverride)` 로 동기화 서비스를 주입). 앱 동작은 v1.72.7 과 같다.
- **v1.72.7**: 버그(#331 #332 #333) — `SettingsService` 는 정적 잠금 하나 아래에서 `.tmp` 에 쓰고 `File.Replace`(직전본 `.bak`), 읽기 실패면 깨진 파일을 `.corrupt-<시각>` 으로 옮기고 `.bak` 에서 읽는다. 일부 값만 바꾸는 곳은 `Load`→`Save` 대신 `Update(s => …; return s 또는 null)` 를 쓴다(읽기·쓰기를 한 잠금 안에서 — 다른 스레드의 저장을 덮어쓰지 않게). `SessionMonitor` 는 세션을 sessionId 로 묶는다: 서브에이전트 기록(`isSidechain`)은 부모 세션에 합치고 제목·폴더·브랜치는 부모 쪽 우선, 토큰은 모두 더함. 설정 창에서 WinForms 대화상자는 `WithNativeDialog(owner => dialog.ShowDialog(owner))` 로 띄운다(주인 지정 + 떠 있는 동안 `Deactivated` 로 숨지 않음). `Loc` 문구를 비교하는 테스트는 `[Collection("WpfTests")]` 에 둔다.
- **v1.72.6**: 하드닝 3차(#320 #299 #310 #237) — 피드 `errorKind` `MobileProtocol.AuthFailedErrorKind`("auth_failed")는 사용자가 PC 에서 키·로그인을 다시 넣어야 풀리는 상태: `MobileUsageFeed.Publish` 가 직전 쿼터를 잇지 않고, 휴대폰은 `AccountAggregator.authFailedOn` 으로 카드에 PC 이름과 안내를 보이며 `hasUsage` 도 참(값 없어도 카드 유지). 지금은 DeepSeek(`MainViewModel.HmsMobileErrorKind`)만 보낸다. 휴대폰 PC 탭은 `PcVersions.isOutdated(appVersion)`(`RECOMMENDED_MIN` 1.68.0 — PC 동작이 휴대폰에 영향을 주는 변경이 또 생기면 올린다)로 업데이트 안내. `HmsUsageService` 는 키 변경·삭제 때 옛 쿠키로 `POST /api/logout`(본문 `{}`, 최선 노력; 앱 종료 때는 안 함). `docs/mobile-protocol/usage-snapshot.served.v1.json` 은 `MobileServedSnapshotTests` 가 페어링한 서버의 `/api/v1/live` 첫 프레임으로 쓰는 픽스처(ts·pcId·appVersion 만 고정) — 휴대폰 `ProtocolTest.served_live_frame_becomes_account_cards` 가 카드로 묶는다; 새 공급자를 피드에 실으면 여기에 넣는다. 팝업 시간선 마커는 `views:TimelineMarker`(`Percent`·`Visibility`) — 열 두 개 Grid 로 손수 짜지 않는다(`PopupGaugeBindingRuleTests` 가 막대 틀마다 하나를 검사), `PercentToStarGridLengthConverter` 는 지웠다.
- **v1.72.5**: 하드닝 2차(#325 #314 #299) — 목록(`ItemsControl`) 행 막대는 `views:GaugeAnimation.Key="{Binding AnimationKey, StringFormat='<목록>:{0}'}"` 로 이름을 준다(`QuotaGaugeRow.AnimationKey` = 한도 키|창 분; 앞말은 목록마다 다르게 — `PopupGaugeBindingRuleTests` 가 검사). 이름별 기억은 보이는 동안 정해진 값이다(숨을 때 기록하면 옛 행의 숨음 알림이 새 행보다 늦게 와 못 이어 받음, 템플릿 안에서는 값이 이름보다 먼저 묶임 — 실측). `CodexUsageMonitor.GetTodaySnapshot` 은 파일별 `FileScan` 캐시(핸들에서 읽은 길이·수정 시각·오늘이 같으면 재사용)를 파일 순서대로 합친다 — 합치는 규칙을 바꾸면 `CodexLogScanCacheTests` 의 "전부 다시 읽은 것과 같음" 이 지켜야 한다. `CodexViewModel`·`OpenCodeViewModel` 은 internal 생성자로 스냅샷 읽기를 받는다(`RefreshUiResponsivenessTests` 가 1.5초 읽기 중 UI 틈 0.7초 미만을 잰다). 휴대폰 업데이트 알림 채널은 `UpdateChannels`(main): `cut.app.update.quiet`(LOW) · `cut.app.update.action`(DEFAULT, github 설치 확인만), 옛 `cut.app.update` 는 지우고 꺼져 있었으면 새 채널도 끈 채로 만든다; 앱 시작에 `AppUpdates.ensureChannels`.
- **v1.72.4**: 하드닝 대응(#325 #314 #310 #299 #237) — `MainViewModel.RefreshAsync` 는 `CoalescedRefresh` 로 겹친 요청을 합친다(진행 중 요청은 끝난 뒤 한 번 더, 반환은 그 마지막 실행 뒤). 본문은 `RefreshOnceAsync`. `HmsUsageService` 는 실패가 이어지면 1·2·4·8·16·30분 쉬고(429 `Retry-After` 가 더 길면 그만큼, 상한 1시간), 성공·키 변경 때 초기화 — 시계는 internal 생성자로 주입. `ShowWeatherAlert` 의 `priority` 는 필수 인수(기본값 없음), `WeatherAlertService` 는 발송 기록 경로를 생성자로 받는다(테스트는 임시 폴더). 언어 변경 때 `RefreshProviderNotes` 가 Codex·OpenCode 하단 안내를 다시 만든다. 팝업 `ProgressBar` 는 `PopupGaugeBindingRuleTests` 가 XAML 원문에서 `GaugeAnimation.Value` 묶음을 검사한다. 환경 변수가 있어야 하는 실물 테스트는 `[RequiresEnvironmentFact("VAR")]`(없으면 Skip). OpenCode 할당량 행 이름 `OpenCode{Rolling,Weekly,Monthly}{Label,Percent,Reset}` 은 `WindowRowLayoutTests` 가 쓴다.
- **v1.72.3**: 휴대폰 빈 계정 카드 숨김(#326) — `HomeScreen` 계정 탭은 `AccountAggregator.hasUsage`(게이지·오늘 토큰·추가 크레딧 중 하나라도 있음)인 카드만 그린다. `aggregate` 자체는 모든 카드를 돌려주므로 다른 곳에서 쓸 때도 같은 필터를 건다. 0% 게이지는 보인다(초기화 직후 정보). 디버그 미리보기 `--ez unread true` 가 할당량 없는 OpenCode 를 넣어 숨김을 확인한다.
- **v1.72.2**: 게이지 차오름(#322) — 팝업 막대는 `Value="{Binding X}"` 대신 `views:GaugeAnimation.Value="{Binding X}"` 로 묶는다(`Views/GaugeAnimation.cs`). 보이는 동안 바뀌면 0.6초 CubicEase 로 움직이고, 숨은 동안 바뀌면 바로 적용한 뒤 다시 보일 때 마지막으로 보였던 값에서 움직인다. 처음 값·한 번도 보인 적 없는 막대·`SystemParameters.ClientAreaAnimation` 꺼짐은 즉시. `Value` 에는 바인딩이 없으므로 로컬 값으로 넣는다 — `SetCurrentValue` 로 넣은 값은 애니메이션이 붙으면 기준값에서 사라진다(실측). 테스트에서 애니메이션 여부는 `HasAnimatedProperties`(`GetValueSource().IsAnimated` 는 첫 틱 전 false).
- **v1.72.0**: 휴대폰에 DeepSeek(#318) — 피드 provider `HmsUsageService.MobileProvider`("100milionseek", 서비스 이름 그대로: 예전 앱도 카드 이름으로 알아보고 공식 DeepSeek API 와 겹치지 않음). `MainViewModel.RefreshHmsAsync` → `PublishHmsMobile`: 계정 키 = API 키 지문(`HmsReadResult.KeyId`), 쿼터는 `HmsViewModel.CreateMobileQuota`(성공일 때만, short 창 1440분) — 실패면 쿼터 없이 보내 피드가 같은 키의 직전 값을 잇는다. 피드 정리 `MobileFeedProviders(settings)` = 표시 공급자 + 키가 있으면 DeepSeek. 휴대폰은 `Theme.providerName`·`forProvider`·`AccountAggregator.PROVIDER_ORDER` 세 곳만(게이지는 공통 short 경로). 디버그 미리보기 `DesignPreviewActivity … --ez deepseek true` 가 PC 와 같은 JSON 을 `ProviderUsage.parse` 로 읽어 카드를 넣는다.
- **v1.71.1**: UI 규칙 문서화(#316) — `docs/UI_GUIDELINES.md`. DeepSeek 게이지가 `ProgressBar` 에 `Height="8"` 을 직접 줘 두 배 두꺼웠고(막대 높이는 스타일 값 4만 쓴다), 섹션 주 게이지인데 목록 행 규칙(말줄임)이라 이름이 잘렸다 → 주 게이지 규칙(이름 Auto 열, 초기화만 줄바꿈), `WindowRowLayoutTests.DeepSeekGaugeRow_*` 가 4개 언어 배치와 막대 높이를 본다(템플릿 안 행은 시각 트리에서 찾음). 설정 탭 내용은 모두 `ScrollViewer` 안(`SettingsHmsKeySaveTests.EveryTab_ScrollsInsteadOfClipping`) — 트레이·알림 탭에 없어 키 칸이 들어가자 아래가 잘렸다.
- **v1.71.0**: Claude 계정 별명 PC 간 동기화(#313) — 별명마다 바꾼 시각을 둔다(`AccountAliasEntry`, 지운 것은 Alias "" 기록; 설정은 v1.70.0 모양의 `ClaudeAccountAliases` + `ClaudeAccountAliasUpdatedAt`). `UsageSyncSnapshot.AccountAlias`(선택 필드 `accountAlias: {keyHash, alias, updatedAtUtc}`, 스키마 버전 그대로)를 `TrySyncClaudeUsage` 가 싣고(`ClaudeSyncAlias` — 기록이 없으면 null, "없음" 으로 남의 별명을 지우지 않게), 모은 스냅샷에서 `AccountAlias.PickSynced`(자기 별명 키 해시 `ClaudeAliasKeyHash` 와 같고 자기 기기가 아닌 것) → `Wins`(더 최근 시각, 같으면 문자열 순서로 모든 PC 가 같은 값) 면 UI 스레드에서 적용·저장(`AdoptSyncedClaudeAlias`). 스냅샷 계정 해시는 조직 단위라 키 해시로 팀 조직의 다른 사람을 거른다. 따른 별명은 다음 갱신 스냅샷부터 다시 나간다.
- **v1.70.1**: 새로고침 UI 정지·DeepSeek 분리(#311) — `CodexViewModel`·`OpenCodeViewModel` 은 스냅샷을 `Task.Run` 으로 읽는다: `await` 뒤 이어짐은 UI 스레드로 돌아오므로 API 응답 뒤의 동기 로그 스캔(이 PC 11~17초)이 화면을 붙잡았다(새로고침 한 번에 UI 최대 정지 14.2초 → 0.1초, DispatcherTimer 간격으로 측정). 새 공급자 읽기도 무거우면 같은 방식으로. DeepSeek 은 `HmsViewModel`(`MainViewModel.HmsVm`) 독립 섹션 — 펼침 키 `MainViewModel.HmsFocusKey`("hms", `UsageProviderKind` 아님: 트레이 기준·표시 목록에 넣지 않음, 키가 있을 때만 유효). 섹션 표시는 `HmsVm.HasContent`(게이지 또는 안내). 설정 키 칸은 [저장] 버튼·Enter 로만 저장한다.
- **v1.70.0**: Claude 계정 별명(#307) — 설정 `ClaudeAccountAliases`(키 = 휴대폰 계정 키 `orgUuid|accountUuid`, `BuildClaudeMobileAccountKey`)에 계정마다 따로 저장, `AccountAlias`(20자, `PcName.Clean` 규칙)로 정리. 있으면 팝업 배지·휴대폰 피드 `accountLabel`(`ClaudeMobileAccountLabel`)·앱 푸시 `accountLabel` 에 이름 대신 쓰고, `AppPushSender.Build` 가 `PushAccount.Alias` 로 제목 앞에 `[별명] ` 을 붙인다(`AlertText.WithAccountAlias`) — Android 12+ 헤드업·접힌 알림은 subText(계정 이름)를 안 보여 줘서. 휴대폰 앱 변경 없음. 평문 ntfy·Windows 풍선에는 넣지 않는다. 설정 창 칸은 LostFocus·Enter 와 창이 숨을 때(`IsVisibleChanged` — 바깥 클릭으로 숨으면 LostFocus 가 오지 않음) 저장한다. 다른 PC 로 동기화되지 않는다(PC 마다 설정).
- **v1.69.0**: 100milionseek(DeepSeek 무료 API) 일일 한도(#306) — `HmsUsageService` 가 대시보드 내부 API 를 부른다: `POST /api/login`(`{"api_key"}`) → `hm_session` 쿠키 → `GET /api/me` 의 `today`(`used`·`limit`·`reset_at`). `/api/me` 는 Bearer 를 받지 않는다. 쿠키는 메모리에 직접 들고(`UseCookies=false`) 401 일 때만 재로그인, `invalid_api_key` 로 거절된 키는 바뀔 때까지 다시 두드리지 않는다. 키는 `HmsApiKeyProtected`(DPAPI, `MobileSecret`) — 설정 > 트레이. 게이지는 `OpenCodeViewModel.HmsRows`(OpenCode 상세, 키 없으면 비어 숨음). 임시 서비스라 휴대폰·알림·동기화에는 넣지 않았다 — 걷어낼 때는 이 서비스·설정 한 칸·XAML 두 곳만 지운다. 실물 확인: `HMS_API_KEY` 환경 변수로 `HmsUsageLiveTests`(Network).
- **v1.68.2**: 휴대폰 Antigravity 표시 규칙(#304) — 휴대폰은 받은 창을 모두 그리므로 PC 가 거른 값을 보낸다: `MainViewModel.PublishAntigravityMobile` → `CreateAntigravityMobileQuotaSnapshot`(팝업과 같은 `AntigravityViewModel.IsDisplayedBucket`). 공유 폴더·PC 동기화는 `CreateAntigravityQuotaSnapshot` 원본(받는 PC 가 거름). 남는 창이 없어도 HasData 유지 — 쿼터 없이 보내면 `MobileUsageFeed.Publish` 가 직전 쿼터를 이어 붙인다.
- **v1.68.1**: 연속 배포 시 두 번 업데이트(#300) — 확인 결과(`_availableUpdate`, `UpdateInfo` 한 객체)는 시작 시·24시간마다만 새로 고치므로 설치에 그대로 쓰지 않는다. 대화상자는 자기가 받은 `UpdateInfo` 에서 출발하고, '지금 업데이트'·카운트다운 만료 때 `UpdateService.ResolveInstallTargetAsync` 로 다시 확인해 더 새 서명 릴리스면 그걸 받는다(`UpdateDialog.ChangeVersion` 으로 제목·노트 교체, 실패하면 찾아 둔 것으로). 자동 시도 표식은 실제로 받는 버전으로 남긴다. 버전 클릭(`ManualCheckForUpdateAsync`)은 캐시 없이 확인하고, 실패할 때만 찾아 둔 대화상자를 연다. 호출부 없던 `StartUpdate`·`ApplyUpdate` 명령은 지웠다.
- **v1.68.0**: 휴대폰 알림 중요도 분리(#297) — 휴대폰은 종류별 채널(`cut.<category>`) 대신 `cut.alert.urgent`(HIGH, 소리·진동)·`cut.alert.quiet`(LOW) 두 채널, 고르는 기준은 메시지 `priority` 뿐(`NotificationGroups.isUrgent`: `warning`·`critical`). 채널 중요도는 만든 뒤 앱이 못 바꾸므로 옛 채널은 `PushNotifier.ensureChannels`(앱 시작·알림마다)가 지운다 — 새 채널 id 는 `cut.` 뒤에 점을 넣어 옛 패턴(`isLegacyChannel`)과 겹치지 않게 한다. 종류별 끄기는 앱 '받을 알림'(PC 가 건너뜀). 그래서 "울리는 알림" 은 PC 가 정한다: 사용량 90·100%, 레이트 리밋(4), 공식 기상 특보(`WeatherPriority.Official`)만 4 이상 — 새 알림을 추가할 때 우선순위를 이 기준으로 고른다(`AlertPriorityTests`).
- **v1.67.1**: 상대가 지운 페어링 철회(#285) — `DELETE /api/v1/pair`(Bearer, 토큰의 기기 자신만 `Revoke`). 휴대폰 `MainActivity.removePc` → `UsageApi.forget`(405·404 옛 PC면 `DELETE /api/v1/push` 로 대신), PC 동기화 `PeerSyncClient.Remove` 도 상대 PC 에 보낸다(최선 노력, 목록은 바로 지움). 안 보내면 상대 목록에 옛 항목이 남아 다시 연결 시 같은 기기가 두 번 보였다(옛 항목은 `SetPushSubscription` 의 같은 엔드포인트 정리에 안 걸림: 푸시 해제로 비었거나 재설치로 주소가 다름). 이름·기종으로 자동 정리는 하지 않는다(같은 기종·이름의 다른 휴대폰). 휴대폰 단위 테스트는 `mockwebserver`(`ForgetPcTest`).
- **v1.67.3**: 업데이트 서명(#290, `docs/DECISIONS.md` D-1) — `UpdateService.SelectUpdate` 는 exe 와 `ClaudeUsageTray.exe.sig` 가 모두 있는 가장 새 릴리스만 고르고, `DownloadAndPrepareUpdateAsync(url, sigUrl, version, …)` 는 `UpdateSignature.Verify`(ECDSA P-256, 메시지 `claude-usage-tray-update/v1
<x.y.z>
<sha256>
`)가 틀리면 파일을 지우고 거부한다. `allowUnverified`·`canVerify`·`UpdateNoChecksumWarning` 은 없어졌다. 서명은 태그 릴리스 뒤 `tools/sign-release.ps1`. 실물 확인: `UpdateSignatureLiveReleaseTests`(Network). `SHA256.txt` 는 v1.67.2 이하 앱을 위해 계속 올린다.
- **v1.67.2**: 보안 하드닝(#290) — 인증 없이 열린 휴대폰 API 는 `POST /api/v1/pair` 뿐(`/api/v1/info` 도 Bearer). 페어링 실패는 QR 이 떠 있을 때만 세고 `Begin()` 이 초기화(Cloudflare 경로로 누구나 `/pair` 를 부를 수 있음). 동시 연결 슬롯은 `PathKind` 마다 32. `release.yml` 의 서드파티 Action 은 커밋 SHA 로 고정 — 올릴 때 새 태그의 커밋을 확인해 바꾼다. 저장소에 `v*` 태그 ruleset(생성·이동·삭제는 관리자만, 관리자 우회) — 태그 재발행도 소유자 계정으로만 된다. `ClaudeUsageTray.Updater` 프로젝트는 삭제됨(업데이트는 `UpdateService` 의 PowerShell 스왑만).
- **v1.67.0**: Play 빌드 새 버전 알림(#284, 도란 방식) — `playImplementation` `com.google.android.play:app-update:2.1.0` 으로 휴대폰의 Play 앱에 더 높은 versionCode 가 있는지 묻고(`StoreUpdates.check`, 15초 제한, Play 에서 받지 않은 설치본은 `NotFromPlay`), 홈 배너·설정 카드('Google Play' 버튼)·배경 잡 알림(버전당 1회, `StoreUpdateJobService` 잡 ID 787076, 12시간)으로 알린 뒤 Play 스토어(`market://`, 없으면 웹)로 보낸다. 설치·다운로드 코드는 여전히 github 에만. `AppUpdates.IN_APP` 은 없어졌고(두 빌드 모두 설정 '업데이트' 구역), 채널 전용 라이브러리 라이선스는 `AppUpdates.THIRD_PARTY`. 사이드로드 디버그로 확인: `am start … MainActivity --ei cut.fakeStoreUpdate 16700`(설치 버전보다 높게, `-Pcut.version=`).
- **v1.66.0**: 휴대폰 앱 백그라운드 자동 설치(#281, github flavor) — 배경 잡(`UpdateCheckJobService.runCheck`)이 새 버전을 찾으면 `UpdateAutoInstaller` 가 받기(SHA-256) → 같은 패키지·더 높은 versionCode·같은 서명 키 확인 → 앱이 안 보일 때 `PackageInstaller` 세션 커밋(31+ `USER_ACTION_NOT_REQUIRED`, 권한 `UPDATE_PACKAGES_WITHOUT_USER_ACTION`; 자기 자신 업데이트라 첫 회도 조용함, 단 '알 수 없는 앱 설치' 미허용이면 확인 필요). 결과는 `UpdateInstallReceiver`: PENDING_USER_ACTION → 확인 인텐트 알림, ABORTED 는 무시(새 세션이 옛 세션을 버릴 때 오는 콜백이 진행 중 설치 기록을 지우던 경쟁), 그 밖 실패 → 평소 알림. 교체 뒤 `MY_PACKAGE_REPLACED` 로 "업데이트했습니다" 한 번. 버전당 자동 시도 한 번(`UpdateDecision.shouldAutoInstall`). 사용자가 누르는 수동 설치는 그대로 `ACTION_INSTALL_PACKAGE`. 시험: 잡 ID 786984 → `adb shell cmd jobscheduler run -f com.jeiel85.aiusage 786984`(앱을 한 번 열고 홈으로).
- **v1.65.3**: 휴대폰 앱 `compileSdk`·`targetSdk` 36(#278) — Play 가 새 업로드에 API 36 이상을 요구(검토 전송 미리보기에서 오류로 막힘). AGP 8.7.3 은 그대로라 `mobile/gradle.properties` 의 `android.suppressUnsupportedCompileSdk=36`; AGP 를 올리면 이 줄을 지운다. Android 16 에서는 큰 화면(sw600dp+)에서 `screenOrientation` 이 무시되고, 뒤로가기는 Compose `BackHandler` 만 쓴다(`onBackPressed` 금지 — 예측형 뒤로가기에서 호출되지 않음).
- **v1.65.2**: 하드닝 대응 — 실제 외부 API 를 부르는 테스트는 클래스에 `[Trait("Category", "Network")]` 를 붙인다(지금은 `WeatherLiveApiTests`). 릴리스 워크플로는 `Category!=Network` 로 게이트하고 Network 는 `continue-on-error` 단계로 따로 돈다(`Integration` 은 로컬 UI·스크립트 테스트라 게이트에 남는다). 릴리스 APK 는 badging 으로 패키지명 `com.jeiel85.aiusage`·`REQUEST_INSTALL_PACKAGES`(github flavor)를 검사한다. 앱 푸시 `accountLabel` 은 `PcName.Clean(…, 64)`.
- **v1.65.1**: Claude 계정 키 폴백(#275) — 최근 Claude Code 는 `.credentials.json` 에 `organizationUuid` 를 쓰지 않아 계정 키가 늘 null(휴대폰 device 범위·동기화 `claude:default` 한 그룹)이었다. `MainViewModel.CurrentClaudeOrgUuid()`(= `ResolveClaudeOrgUuid`: 자격 파일 값 → 없으면 자격 파일이 있을 때만 `~/.claude.json` `oauthAccount.organizationUuid`)를 모든 Claude 계정 판별에 쓴다 — `_credentials.GetOrganizationUuid()` 를 직접 부르지 않는다. `HistoryService` 는 계정 scope 파일이 하나도 없는 PC 에서 처음 계정 scope 를 열 때 계정 없는 scope 기록을 복사해 이어 받는다.
- **v1.65.0**: PC 이름(#271) — 설정 `PcAlias` → `PcName.Current`(비면 `Environment.MachineName`)를 휴대폰 서버·앱 푸시·PC 동기화 프레임·공유 폴더 스냅샷·ntfy 가 보낼 때마다 읽는다(구성 요소는 `Func<string>` 로 받음, 테스트는 고정 이름 주입). 받는 쪽: 다른 PC 는 `fromPcName` 으로 `SyncPeerStore.UpdateName`/`MobileDeviceStore.RenamePc`, 휴대폰은 스냅샷 `pcName` 으로 `PcStore.updateName`. 휴대폰 별칭은 `PcEntry.alias`(→ `displayName`, PC 탭 ⋮ '이름 바꾸기'), 푸시는 `PushReceiver` 가 `pcName` 을 `displayName` 으로 바꿔 표시·기록. 정리 규칙은 `PcName.Clean`·`PcNames.clean` 이 같다(한 줄, 32자).
- **v1.64.0**: 휴대폰 게이지 시간선(#269) — `Gauge.timeProgress(nowMs)`(PC `UsageCalculator.TimeProgress` 와 같은 식, 창 길이를 모르거나 창 밖이면 null)로 `HomeScreen.UsageBar` 가 막대 위아래로 3dp 넘치는 흰 마커를 그린다. 창 길이: 기본·추가 한도는 `windowMinutes`, OpenCode 는 `Quota.parse` 가 롤링 300·주간 10080·월간(초기화 − 1개월), Antigravity 는 `window` 또는 `modelId` 꼬리("weekly"·"5h"). 프로토콜 변경 없음.
- **v1.63.1**: Codex 계정 이름(#266) — `CodexUsageMonitor.GetCurrentAccountName()` 이 `~/.codex/auth.json` id_token 의 `name` 을 읽어(이메일 꼴이면 null, 64자 제한, `email` 클레임은 읽지 않음) 피드 `accountLabel`·앱 푸시 `accountLabel` 에 싣는다.
- **v1.63.0**: 샘플 데이터 둘러보기(#261) — 빈 홈의 버튼 → `HomeScreen(sample = true)` 에 `domain/SampleData`(디버그 `DesignPreviewActivity` 와 공용, 실제 이름·토큰 없음). 상태는 `MainActivity` 의 `rememberSaveable`, 실제 PC가 생기면 자동 종료. Play 등록 자료(아이콘·그래픽·스크린샷·문구·선언 초안)는 `mobile/store/`, 개인정보처리방침은 `docs/privacy/`(GitHub Pages).
- **v1.62.0**: 휴대폰 알림 계정 표시·문구 개선(#262) — 앱 푸시 `PushMessage` 에 선택 필드 `provider`·`accountHash`·`accountLabel`(피드와 같은 값, `PushAccount` → `AppPushSender.Build` 가 `MobileAccountHash.For` 로 해시)을 싣고 휴대폰은 `PushMessage.source`(계정 이름, 없으면 PC 이름)를 보조 문구·알림센터에, 서비스 색을 강조색에 쓴다. ntfy 토픽에는 계정을 싣지 않는다. 문구는 `AlertText`(NotificationService.cs) — 제목은 서비스·퍼센트를 앞에(Android 16 접힌 알림이 제목 끝부터 자름), 본문은 절대 초기화 시각. 디버그 `DesignPreviewActivity -e screen notify` 로 실제 알림을 띄워 볼 수 있다.
- **v1.61.1**: Play 업로드 키(#256) — `play` flavor 는 `CUT_PLAY_*`(로컬 `mobile/play-signing.properties`, 키 `mobile/.keystore/cut-play-upload.jks`, git 제외)로 서명. 릴리스 워크플로 `build-aab` 가 서명된 AAB 를 산출물 `play-bundle` 로 남긴다(GitHub Release 에는 안 붙임, Play 업로드는 수동). 인증서 지문은 `docs/MOBILE_UPDATES.md` "Play 업로드 키".
- **v1.61.0**: 휴대폰 앱 배포 채널 분리(#257) — product flavor `github`/`play`. 앱 내 업데이트(`update/`·`REQUEST_INSTALL_PACKAGES`·`RECEIVE_BOOT_COMPLETED`·FileProvider·`update_*` 문구)는 `src/github/`에만, 공용 코드는 `update/AppUpdates`(play 쪽은 빈 구현)만 부른다. Gradle 작업명에 flavor가 붙는다: `assembleGithubDebug`·`testGithubDebugUnitTest`·`lintPlayDebug`·`bundlePlayRelease`. `cut` 키는 github 릴리스에만, Play는 별도 업로드 키(미발급, #256). `docs/MOBILE_UPDATES.md` "배포 채널".
- **v1.60.0**: 휴대폰 앱 패키지명 변경(#254) — `applicationId` = `com.jeiel85.aiusage`(Kotlin 패키지·`namespace`는 `io.github.jeiel85.claudeusage` 유지, 그래서 `adb am start -n com.jeiel85.aiusage/io.github.jeiel85.claudeusage.<Activity>`). 이전 앱이 남아 있으면 홈에 삭제 안내(`ui/LegacyAppNotice.kt`, `ACTION_DELETE` 는 API 28+ 에서 `REQUEST_DELETE_PACKAGES` 없으면 조용히 무시된다). 이전 앱에서 데이터는 옮겨지지 않는다. `docs/MOBILE_UPDATES.md` "패키지명 변경".
- **v1.59.2**: 페어링 QR 인식 개선(#251) — `Views/PairingQr.cs` 가 칸을 물리 픽셀 정수배로 그리고 비트맵 DPI = 96×배율로 1:1 표시(창 폭 470, QR 최대 360 DIP), 폰은 `ui/PairScanActivity`(zxing CaptureActivity 상속, 연속 초점·중앙 측광 — 설정은 `super.onCreate` 뒤에 바꿔야 인텐트가 덮어쓰지 않는다). TRY_HARDER 는 시뮬레이션에서 효과가 없어 넣지 않았다.
- **v1.59.1**: 업데이트 확인·설치 일관화(#247) — `UpdateSurface.offered` 를 홈 배너와 설정 카드가 공유, 설정 '지금 확인'이 새 버전을 찾으면 창을 바로 연다. PC ⋮ 메뉴는 `Palette.Raised`·테두리·그림자(#248, 메뉴 바탕이 카드와 같아 묻히던 회귀).
- **v1.59.0**: 휴대폰 앱 디자인 개선(#244) — 공용 컴포넌트 `ui/Components.kt`(`Card`·`Chip`·`Monogram`·`SegmentedTabs`·`SectionHeader`·`AppSwitch`), 게이지 2줄, PC 삭제는 ⋮ 메뉴. 디버그 전용 `DesignPreviewActivity`(src/debug)로 페어링 없이 샘플 화면 캡처: `adb shell am start -n com.jeiel85.aiusage/io.github.jeiel85.claudeusage.DesignPreviewActivity -e screen accounts|pcs|history|settings|pair|empty`.
- **v1.58.2**: ntfy 중복 구독 수정(#242) — `UnifiedPush.register()`는 이전 토큰을 먼저 UNREGISTER, `PushReceiver`는 현재가 아닌 토큰의 NEW_ENDPOINT/MESSAGE를 받으면 설치된 배포자 모두에 그 토큰 UNREGISTER(`UnifiedPush.staleToken`). 재설치로 앱 데이터가 지워져도 ntfy 구독은 남기 때문.
- **v1.58.1**: 휴대폰 앱 정식 서명(#240) — Actions 시크릿 `CUT_RELEASE_*` 4개 등록, 이 릴리스부터 정식 서명 APK + `update.json`(앱 내 자동 업데이트 활성). 로컬 서명 빌드는 `mobile/release-signing.properties`(git 제외)를 그대로 읽는다. 인증서 SHA-256은 `docs/MOBILE_UPDATES.md`.
- **v1.58.0**: 휴대폰 앱 알림센터(#235) — `PushReceiver`가 뮤트·중복 검사를 통과한 푸시를 `NotificationHistory`(SharedPreferences `cut_push_history`, 30일·200건, 알림 권한 꺼져도 기록)에 남기고, 홈 종 아이콘(안 읽은 개수 = `receivedAtMs > lastSeen`) → `NotificationCenterScreen`.
- **v1.57.1**: 팝업 창 라벨·퍼센트 겹침 수정(#234) — 라벨 Auto 열, 초기화 문구는 10pt·폭 부족 시 오른쪽 정렬 줄바꿈(말줄임 금지), 절대 시각은 NBSP로 묶음. 회귀 테스트 `WindowRowLayoutTests`(실제 팝업 배치, 4개 언어).
- **v1.56.0**: 휴대폰 앱 설정 화면(#230) — 홈 톱니바퀴 → 알림('알림' 탭에서 옮김, 홈은 계정·PC 2탭)·화면(`AppPrefs`: 초기화 시각 표시, 화면 켜 두기, Android 13+ 앱 언어 `locales_config`)·업데이트·정보.
- **v1.55.0**: 휴대폰 앱 자동 업데이트(#228) — Homebase `update/` 이식(하루 1회 `update.json`, 배너·알림 1회, SHA-256 대조 후 `ACTION_INSTALL_PACKAGE`). `update.json`은 정식 서명 릴리스에만 붙인다(디버그 키는 빌드마다 달라 업데이트 설치 불가). `docs/MOBILE_UPDATES.md`.
- **v1.54.0**: Tailscale 없이 밖에서 연결(#225) — `cloudflared` 하위 프로세스(Quick Tunnel/이름 있는 터널), 루프백 평문 리스너, 서명된 경로 갱신 푸시(HMAC, 키 = 기기 토큰 해시). `CloudflareTunnel.cs`·`EndpointAnnouncer.cs`.
- **v1.53.0**: 휴대폰(Android) 앱 연동(#220) — QR 페어링, LAN(TLS 핀)·Tailscale 직결, 계정 해시로 여러 PC를 계정 단위로 묶어 실시간 표시. 앱은 `mobile/`, 프로토콜은 `docs/MOBILE_PROTOCOL.md`.
- **v1.21.8**: 일일 목표 설정 제거 및 자동화(7일 최대치 기반), 설정 화면 정리.
- **v1.21.7**: 설정 화면 내 자동 모드 안내 문구 간소화(개행 방지 최적화).
- **v1.17.5**: Codex 초기화 시간 폴백 표시 (로그에 없을 시 최초 활동 기준).
- **v1.17.4**: 완료된 GitHub 이슈 정리 (#57, #58), 소진 예측 문구(추세대로면~) 표시 조건 개선 (100% 소진 시 숨김).
- **v1.17.3**: "Extra Credits" 문구 다국어 지원 추가 (한국어, 중국어, 일본어, 영어), 프로젝트 및 문서 버전 동기화.
- **v1.17.2**: 사용량 팝업 가독성 개선 (폰트 크기 증대 및 시인성 강화), 프로젝트 및 문서 버전 동기화.
- **v1.17.1**: 팝업 보조 텍스트 크기 원복, 설정창 초기 로딩 가드 추가.
- **v1.17.0**: Codex/Gemini 사용량 바 미표시 버그 수정, 새로고침 버튼 미동작 수정, 앱 제목 일반화 및 레이아웃 재구성.
- **v1.16.2**: Gemini CLI 토큰 파싱 구현(실시간 % 계산), 추가 사용량(Extra Usage) 위치를 Claude 섹션 내부로 조정, 트레이 게이지 기준 명시.
