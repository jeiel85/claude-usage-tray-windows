# Changelog

모든 주요 변경 사항을 이 파일에 기록합니다.
[Keep a Changelog](https://keepachangelog.com/ko/1.0.0/) 형식을 따릅니다.

---

## [1.15.22] - 2026-04-16

<!-- ko -->
### 추가
- **갱신 주기 커스터마이징** — 설정 창에서 갱신 주기를 1~N분으로 변경 가능 (기본 2분)

### 수정
- **초기 로딩 상태 표시 개선** — 앱 시작 시 트레이 메뉴에 "Loading..." 상태 표시

### 이슈 닫기
- #41: 설정 창 단축키 (ESC, Alt+F4, Ctrl+W)
- #38: 추가 사용량 트레이 표시
- #37: 추가 사용량 알림
- #33: 앱 초기 로딩 중 스켈레톤/로딩 상태 표시
- #32: 설정값 중앙화 (AppConstants.cs)
- #28: 예외 처리 로깅 추가
- #13: 히스토리 보관 기간 확장 (90일)
- #12: 갱신 주기 커스터마이징
<!-- /ko -->

<!-- en -->
### Added
- **Customizable refresh interval** — Change refresh interval in settings (default 2 minutes)

### Fixed
- **Initial loading state display** — Tray menu shows "Loading..." on app start

### Closed Issues
- #41: Settings window shortcuts (ESC, Alt+F4, Ctrl+W)
- #38: Extra usage tray display
- #37: Extra usage notifications
- #33: App initial loading state display
- #32: Centralized constants (AppConstants.cs)
- #28: Exception handling logging
- #13: History retention period (90 days)
- #12: Refresh interval customization
<!-- /en -->

<!-- zh -->
### 新增
- **可自定义刷新间隔** — 在设置中更改刷新间隔（默认2分钟）

### 修复
- **初始加载状态显示** — 应用启动时托盘菜单显示"Loading..."

### 关闭的问题
- #41: 设置窗口快捷键 (ESC, Alt+F4, Ctrl+W)
- #38: 额外用量托盘显示
- #37: 额外用量通知
- #33: 应用初始加载状态显示
- #32: 集中常量 (AppConstants.cs)
- #28: 异常处理日志
- #13: 历史保留期（90天）
- #12: 刷新间隔自定义
<!-- /zh -->

<!-- ja -->
### 追加
- **更新間隔のカスタマイズ** — 設定で更新間隔を変更可能（デフォルト2分）

### 修正
- **初期ローディング状态的表示** — アプリ起動時にトレイメニューに"Loading..."を表示

### クローズした 이슈
- #41: 設定ウィンドウショートカット (ESC, Alt+F4, Ctrl+W)
- #38: 追加使用量のトレイ表示
- #37: 追加使用量通知
- #33: アプリの初期ローディング状态的表示
- #32: 定数の中央集管理 (AppConstants.cs)
- #28: 例外処理ロギング
- #13: 履歴保持期間（90日）
- #12: 更新間隔のカスタマイズ
<!-- /ja -->

---

## [1.15.21] - 2026-04-16

<!-- ko -->
### 추가
- **기본 사용량 소진 후 추가 사용량 모니터링** — 5시간 윈도우 100% 소진 시 자동으로 추가 사용량으로 모니터링 대상 전환
- **추가 사용량 알림** — 추가 사용량도 기본 임계값(50/75/90/100%) 도달 시 알림

### 수정
- **경고 제거** — 사용하지 않는 변수 경고 제거 (GC.KeepAlive 추가)
<!-- /ko -->

<!-- en -->
### Added
- **Extra usage monitoring after base quota exhaustion** — Automatically switches to extra usage monitoring when 5-hour window reaches 100%
- **Extra usage notifications** — Notifications for extra usage at the same thresholds (50/75/90/100%)

### Fixed
- **Warning cleanup** — Removed unused variable warnings (added GC.KeepAlive)
<!-- /en -->

<!-- zh -->
### 新增
- **基本用量耗尽后监控额外用量** — 5小时窗口达到100%时自动切换到额外用量监控
- **额外用量通知** — 额外用量达到相同阈值(50/75/90/100%)时发送通知

### 修复
- **警告清理** — 移除未使用变量警告(添加GC.KeepAlive)
<!-- /zh -->

<!-- ja -->
### 追加
- **基本使用量枯渇後の追加使用量監視** — 5時間ウィンドウが100%に達すると自動的に追加使用量への監視を切り替え
- **追加使用量通知** — 追加使用量も閾値(50/75/90/100%)到達時に通知

### 修正
- **警告クリーンアップ** — 未使用変数の警告を削除(GC.KeepAliveを追加)
<!-- /ja -->

---

## [1.15.20] - 2026-04-16

<!-- ko -->
### 추가
- **설정 창 단축키 지원** — 설정 창에서도 ESC, Alt+F4, Ctrl+W로 닫기 가능

### 수정
- **예외 처리 로깅 추가** — DEBUG 모드에서 빈 catch 블록에 예외 메시지 로깅 추가
- **설정값 중앙화** — AppConstants.cs 생성하여 매직 넘버 상수化管理
  - 폴링 간격, 타임아웃, 알림 임계값, 히스토리 보관 기간 등
<!-- /ko -->

<!-- en -->
### Added
- **Settings window shortcuts** — ESC, Alt+F4, Ctrl+W now close the settings window

### Fixed
- **Exception handling logging** — Added DEBUG-mode logging to empty catch blocks
- **Centralized constants** — Created AppConstants.cs to manage magic numbers
  - Polling intervals, timeouts, notification thresholds, history retention, etc.
<!-- /en -->

<!-- zh -->
### 新增
- **设置窗口快捷键** — 设置窗口也支持 ESC、Alt+F4、Ctrl+W 关闭

### 修复
- **异常处理日志** — 在 DEBUG 模式下为空 catch 块添加了异常消息日志
- **集中常量** — 创建 AppConstants.cs 管理魔数
  - 轮询间隔、超时、通知阈值、历史保留期等
<!-- /zh -->

<!-- ja -->
### 追加
- **設定ウィンドウショートカット** — 設定ウィンドウでも ESC、Alt+F4、Ctrl+W で閉じることに対応

### 修正
- **例外処理ロギング追加** — DEBUG モードで空の catch ブロックに例外メッセージロギングを追加
- **定数の中央集管理** — AppConstants.cs を作成してマジックナンバーを管理
  - ポーリング間隔、タイムアウト通知閾値、履歴保持期間など
<!-- /ja -->

---

## [1.15.19] - 2026-04-16

<!-- ko -->
### 수정
- **자동 업데이트 한글 경로 처리 개선** — 사용자 폴더 경로에 한글이 포함된 환경에서 배치 스크립트 실행 시 파일 복사 실패하던 문제 수정
- UTF-8 BOM 추가 + robocopy로 변경하여 한글 경로 정상 처리
<!-- /ko -->

<!-- en -->
### Fixed
- **Auto-update Korean path handling improved** — Fixed batch script failing to copy files when user folder path contains Korean characters
- Added UTF-8 BOM and changed to robocopy for proper Korean path handling
<!-- /en -->

<!-- zh -->
### 修复
- **自动更新韩文路径处理改进** — 修复了用户文件夹路径包含韩文时批处理脚本无法复制文件的问题
- 添加了 UTF-8 BOM 并改用 robocopy 以正确处理韩文路径
<!-- /zh -->

<!-- ja -->
### 修正
- **自動更新の日本語パス処理を改善** — ユーザーフォルダー경로에 한글이 포함된 환경에서 배치 스크립트가 파일을 복사하지 못하던 문제를修正
- UTF-8 BOMを追加し、robocopyに変更して한글 경로를正しく処理
<!-- /ja -->

---

## [1.15.18] - 2026-04-16

<!-- ko -->
### 수정
- **자동 업데이트 파일명 호환성 수정** — v1.15.17에서 파일명을 `ClaudeUsageTray.exe`로 단순화했지만, 구버전 앱이 여전히 `-sc.exe`Suffix를 찾아 실패하던 버그 수정
- 이제 새、旧 파일명 모두 정상 인식
<!-- /ko -->

<!-- en -->
### Fixed
- **Auto-update filename compatibility fixed** — v1.15.17 simplified filename to `ClaudeUsageTray.exe`, but older app versions were still looking for `-sc.exe` suffix and failing
- Now correctly recognizes both new and old filename formats
<!-- /en -->

<!-- zh -->
### 修复
- **自动更新文件名兼容性修复** — v1.15.17 将文件名简化为 `ClaudeUsageTray.exe`，但旧版应用仍在查找 `-sc.exe` 后缀而导致更新失败
- 现在可以正确识别新旧文件名格式
<!-- /zh -->

<!-- ja -->
### 修正
- **自動更新のファイル名互換性を修正** — v1.15.17 でファイル名を `ClaudeUsageTray.exe` に簡略化しましたが、旧バージョンのアプリが `-sc.exe` サフィックスを探していたため更新に失敗していたバグを修正
- 新旧のファイル名を正しく認識するようになりました
<!-- /ja -->

---

## [1.15.17] - 2026-04-15

<!-- ko -->
### 변경
- **fd 빌드 제거** — CI 환경에서 178 MB 문제를 해결하지 못해 fd 빌드를 완전히 중단. sc 빌드만 유지.
- **파일명 단순화** — `ClaudeUsageTray-sc.exe` → `ClaudeUsageTray.exe` (SHA256 파일 동일)
- **한글 경로 지원** — 사용자명 또는 프로젝트 폴더명에 한글이 포함된 환경에서도 정상 동작

### ⚠️ v1.15.16 이하 사용자께

이번 버전부터 배포 파일명이 변경(`-sc.exe` → `.exe`)되어 **구버전의 자동 업데이트 감지가 작동하지 않습니다.**  
특히 경로에 한글이 포함된 환경에서는 자동 업데이트가 실패할 수 있습니다.  
**아래 링크에서 직접 다운로드**하여 기존 파일을 덮어쓰세요.

### 다운로드

| 파일 | 크기 | 설명 |
|------|------|------|
| `ClaudeUsageTray.exe` | ~78 MB | Self-contained — .NET 런타임 포함, 설치 불필요 |
<!-- /ko -->

<!-- en -->
### Changed
- **Removed fd build** — After multiple failed attempts to fix the 178 MB CI output, framework-dependent builds are discontinued. Only the self-contained build remains.
- **Simplified filename** — `ClaudeUsageTray-sc.exe` → `ClaudeUsageTray.exe` (SHA256 file likewise)
- **Korean path support** — Paths containing Korean characters (username or project folder names) are now fully supported

### ⚠️ Users on v1.15.16 or older

The asset filename changed in this release (`-sc.exe` → `.exe`), so **auto-update detection will not work** from older versions.  
If your path contains Korean (non-ASCII) characters, auto-update may also fail silently.  
Please **download manually** from the link below and overwrite your existing file.

### Download

| File | Size | Description |
|------|------|-------------|
| `ClaudeUsageTray.exe` | ~78 MB | Self-contained — includes .NET runtime, nothing to install |
<!-- /en -->

<!-- zh -->
### 变更
- **移除 fd 构建** — 多次尝试修复 CI 输出 178 MB 问题均未成功，停止提供 framework-dependent 构建，仅保留 self-contained 构建。
- **文件名简化** — `ClaudeUsageTray-sc.exe` → `ClaudeUsageTray.exe`（SHA256 文件同理）
- **韩文路径支持** — 用户名或项目文件夹名包含韩文字符的环境现在可正常运行

### ⚠️ v1.15.16 及以下版本用户

本次发布更改了资产文件名（`-sc.exe` → `.exe`），旧版本的**自动更新检测将无法工作**。  
如果路径中包含非 ASCII 字符（如韩文），自动更新也可能静默失败。  
请从下方链接**手动下载**并覆盖现有文件。
<!-- /zh -->

<!-- ja -->
### 変更
- **fd ビルドを廃止** — CI で 178 MB 問題を解決できなかったため、framework-dependent ビルドを廃止。self-contained ビルドのみ提供。
- **ファイル名を簡略化** — `ClaudeUsageTray-sc.exe` → `ClaudeUsageTray.exe`（SHA256 ファイルも同様）
- **韓国語パスのサポート** — ユーザー名やプロジェクトフォルダ名に韓国語が含まれる環境でも正常動作

### ⚠️ v1.15.16 以下のユーザーへ

今回のリリースからアセットのファイル名が変更（`-sc.exe` → `.exe`）されたため、旧バージョンの**自動アップデート検出が機能しません**。  
パスに韓国語（非 ASCII）文字が含まれる場合、自動更新がサイレントに失敗することもあります。  
下記リンクから**手動でダウンロード**して既存ファイルを上書きしてください。
<!-- /ja -->

---

## [1.15.16] - 2026-04-15

<!-- ko -->
### 수정
- **fd 빌드 격리 — sc/fd 빌드를 독립 job으로 분리** — 동일 job 내에서 sc 빌드 후 `--self-contained false`를 무시하는 GitHub Actions 환경 문제 근본 해결. sc와 fd를 각각 별도 runner에서 실행하여 완전한 환경 격리 보장

### 다운로드 안내

| 파일 | 크기 | 설명 |
|------|------|------|
| `ClaudeUsageTray-sc.exe` | ~78 MB | **Self-contained** — .NET 런타임 포함, 설치 불필요 **(권장)** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **Framework-dependent** — [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) 설치 필요 |
<!-- /ko -->

<!-- en -->
### Fixed
- **fd build isolation — sc and fd now run as separate jobs** — Root-cause fix for GitHub Actions ignoring `--self-contained false` after an sc build in the same job. Each build now runs on its own fresh runner with a completely isolated environment

### Which file should I download?

| File | Size | Description |
|------|------|-------------|
| `ClaudeUsageTray-sc.exe` | ~78 MB | **Self-contained** — includes .NET runtime, nothing to install **(recommended)** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **Framework-dependent** — requires [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) |
<!-- /en -->

<!-- zh -->
### 修复
- **fd 构建隔离 — sc 和 fd 拆分为独立 job** — 根本解决 GitHub Actions 在同一 job 中 sc 构建后忽略 `--self-contained false` 的问题。每个构建现在在独立的 runner 上运行，环境完全隔离
<!-- /zh -->

<!-- ja -->
### 修正
- **fd ビルドの分離 — sc と fd を独立した job に分割** — 同一 job 内で sc ビルド後に `--self-contained false` が無視される GitHub Actions 環境の問題を根本解決。各ビルドを独立した runner で実行し、環境を完全に分離
<!-- /ja -->

---

## [1.15.15] - 2026-04-15

<!-- ko -->
### 수정
- **fd 빌드 크기 확정 (~25 MB)** — `net9.0-windows`(버전 미지정) TFM이 GitHub Actions 환경에서 WPF 런타임을 포함해 155 MB를 만드는 문제 확인. `net9.0-windows10.0.17763.0`으로 복귀하여 일관된 fd 크기(~25 MB) 확보
- `dotnet clean` 단계에 `-r win-x64` 추가 — RID 지정 없이 clean 시 sc 빌드 캐시가 완전히 제거되지 않던 문제 보완

### 참고
- v1.15.13·14 에서 예고한 "fd ~1.4 MB" 는 로컬에서만 재현됐고 CI 환경에서는 달성하지 못했습니다.
- 현재 fd 크기 25 MB 는 `Microsoft.Windows.SDK.NET.dll` (23 MB) 이 원인이며 TFM을 유지하는 한 제거 불가합니다.

### 다운로드 안내

| 파일 | 크기 | 설명 |
|------|------|------|
| `ClaudeUsageTray-sc.exe` | ~78 MB | **Self-contained** — .NET 런타임 포함, 설치 불필요 **(권장)** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **Framework-dependent** — [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) 설치 필요 |
<!-- /ko -->

<!-- en -->
### Fixed
- **fd build size stabilized (~25 MB)** — `net9.0-windows` (unversioned) TFM was found to include the full WPF runtime on GitHub Actions (155 MB). Reverted to `net9.0-windows10.0.17763.0` for consistent fd output (~25 MB)
- Added `-r win-x64` to the `dotnet clean` step — without a RID, the sc build cache was not fully cleared

### Note
- The "fd ~1.4 MB" announced in v1.15.13 and v1.15.14 only reproduced locally and could not be achieved in CI.
- The current fd size of 25 MB is caused by `Microsoft.Windows.SDK.NET.dll` (23 MB), which cannot be removed while keeping the current TFM.

### Which file should I download?

| File | Size | Description |
|------|------|-------------|
| `ClaudeUsageTray-sc.exe` | ~78 MB | **Self-contained** — includes .NET runtime, nothing to install **(recommended)** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **Framework-dependent** — requires [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) |
<!-- /en -->

<!-- zh -->
### 修复
- **fd 构建大小稳定（~25 MB）** — 确认 `net9.0-windows`（无版本号）TFM 在 GitHub Actions 上会引入完整 WPF 运行时（155 MB）。已回退到 `net9.0-windows10.0.17763.0` 以获得稳定的 fd 大小（~25 MB）
- `dotnet clean` 步骤添加 `-r win-x64` — 不指定 RID 时 sc 构建缓存未被完全清除

### 说明
- v1.15.13·14 中预告的"fd ~1.4 MB"仅在本地环境复现，CI 环境无法实现
- 当前 fd 大小 25 MB 源于 `Microsoft.Windows.SDK.NET.dll`（23 MB），在保持当前 TFM 的情况下无法去除
<!-- /zh -->

<!-- ja -->
### 修正
- **fd ビルドサイズを安定化（~25 MB）** — `net9.0-windows`（バージョン未指定）TFM が GitHub Actions 上で WPF ランタイムを含めてしまい 155 MB になる問題を確認。`net9.0-windows10.0.17763.0` に戻すことで安定した fd サイズ（~25 MB）を確保
- `dotnet clean` ステップに `-r win-x64` を追加 — RID 未指定では sc ビルドのキャッシュが完全にクリアされなかった問題を補完

### 備考
- v1.15.13·14 で予告した「fd ~1.4 MB」はローカル環境でのみ再現し、CI 環境では達成できませんでした
- 現在の fd サイズ 25 MB は `Microsoft.Windows.SDK.NET.dll`（23 MB）が原因で、現行 TFM を維持する限り除去できません
<!-- /ja -->

---

## [1.15.14] - 2026-04-15

<!-- ko -->
### 수정
- **fd 빌드 크기 여전히 큰 문제 근본 수정** — sc 빌드 후 `obj/` 캐시가 남아 fd 빌드가 self-contained 아티팩트를 재사용하던 문제 해결. sc → `dotnet clean` → fd 순서로 워크플로우 변경
<!-- /ko -->

<!-- en -->
### Fixed
- **Root-cause fix for fd still building large** — sc build left behind `obj/` cache, causing the fd build to reuse self-contained artifacts without rebuilding. Workflow now runs `dotnet clean` between sc and fd builds
<!-- /en -->

<!-- zh -->
### 修复
- **fd 构建仍然偏大的根本原因修复** — sc 构建后遗留的 `obj/` 缓存导致 fd 构建复用了 self-contained 产物。已在 sc 和 fd 构建之间插入 `dotnet clean`
<!-- /zh -->

<!-- ja -->
### 修正
- **fd ビルドが依然として大きい根本原因を修正** — sc ビルド後に残った `obj/` キャッシュにより、fd ビルドが self-contained のアーティファクトを再利用していた問題を解消。ワークフローを sc → `dotnet clean` → fd の順に変更
<!-- /ja -->

---

## [1.15.13] - 2026-04-15

<!-- ko -->
### 변경
- **Windows 알림 방식 변경: WinRT 토스트 → 시스템 트레이 balloon tip**
  - `Microsoft.Toolkit.Uwp.Notifications` 패키지 제거, TFM을 `net9.0-windows10.0.17763.0` → `net9.0-windows`로 하향
  - 이로 인해 `Microsoft.Windows.SDK.NET.dll` (23 MB)이 fd 빌드에서 제거됨

### ⚠ 알림 외관 변경
- **이전**: Windows 알림 센터 스타일의 WinRT 토스트 팝업
- **이후**: 시스템 트레이 아이콘 우측 하단의 balloon tip (Windows 10/11 모두 알림 센터에 기록됨)

### 다운로드 안내

| 파일 | 크기 | 알림 방식 |
|------|------|-----------|
| `ClaudeUsageTray-sc.exe` | ~72 MB | Balloon tip (트레이 아이콘 풍선) |
| `ClaudeUsageTray-fd.exe` | ~1.5 MB | Balloon tip (트레이 아이콘 풍선) |

> fd 파일이 1.5 MB인 이유: WinRT 런타임 DLL(23 MB)을 완전히 제거했습니다.  
> .NET 9 Desktop Runtime이 설치된 환경이라면 fd를 추천합니다.
<!-- /ko -->

<!-- en -->
### Changed
- **Notification method: WinRT toast → system tray balloon tip**
  - Removed `Microsoft.Toolkit.Uwp.Notifications` package; downgraded TFM from `net9.0-windows10.0.17763.0` to `net9.0-windows`
  - This eliminates `Microsoft.Windows.SDK.NET.dll` (23 MB) from the fd build

### ⚠ Notification appearance change
- **Before**: WinRT toast popup in the Windows notification center style
- **After**: Balloon tip from the system tray icon (still logged to the Action Center on Windows 10/11)

### Which file should I download?

| File | Size | Notifications |
|------|------|---------------|
| `ClaudeUsageTray-sc.exe` | ~72 MB | Balloon tip (tray icon bubble) |
| `ClaudeUsageTray-fd.exe` | ~1.5 MB | Balloon tip (tray icon bubble) |

> Why is fd only 1.5 MB? The WinRT runtime DLL (23 MB) has been completely removed.  
> If you have .NET 9 Desktop Runtime installed, fd is the recommended download.
<!-- /en -->

<!-- zh -->
### 变更
- **通知方式变更：WinRT Toast → 系统托盘气泡提示**
  - 移除 `Microsoft.Toolkit.Uwp.Notifications` 包；目标框架从 `net9.0-windows10.0.17763.0` 降级为 `net9.0-windows`
  - fd 构建中的 `Microsoft.Windows.SDK.NET.dll`（23 MB）已完全删除

### ⚠ 通知外观变更
- **之前**：Windows 操作中心样式的 WinRT Toast 弹窗
- **之后**：来自系统托盘图标的气泡提示（在 Windows 10/11 上仍会记录到操作中心）

### 应该下载哪个文件？

| 文件 | 大小 | 通知方式 |
|------|------|---------|
| `ClaudeUsageTray-sc.exe` | ~72 MB | 气泡提示（托盘图标气泡） |
| `ClaudeUsageTray-fd.exe` | ~1.5 MB | 气泡提示（托盘图标气泡） |

> fd 为何只有 1.5 MB？WinRT 运行时 DLL（23 MB）已被完全移除。  
> 如果已安装 .NET 9 Desktop Runtime，推荐使用 fd 版本。
<!-- /zh -->

<!-- ja -->
### 変更
- **通知方式の変更：WinRT トースト → システムトレイのバルーンチップ**
  - `Microsoft.Toolkit.Uwp.Notifications` パッケージを削除；TFM を `net9.0-windows10.0.17763.0` から `net9.0-windows` にダウングレード
  - fd ビルドから `Microsoft.Windows.SDK.NET.dll`（23 MB）を完全に除去

### ⚠ 通知の見た目の変更
- **以前**：Windows 通知センタースタイルの WinRT トーストポップアップ
- **以後**：システムトレイアイコンからのバルーンチップ（Windows 10/11 ではアクションセンターに記録されます）

### どのファイルをダウンロードすればいい？

| ファイル | サイズ | 通知方式 |
|----------|--------|---------|
| `ClaudeUsageTray-sc.exe` | ~72 MB | バルーンチップ（トレイアイコンの吹き出し） |
| `ClaudeUsageTray-fd.exe` | ~1.5 MB | バルーンチップ（トレイアイコンの吹き出し） |

> fd が 1.5 MB の理由：WinRT ランタイム DLL（23 MB）を完全に除去しました。  
> .NET 9 Desktop Runtime がインストール済みの環境では fd をおすすめします。
<!-- /ja -->

---

## [1.15.12] - 2026-04-15

<!-- ko -->
### 수정
- **fd 빌드 크기 버그 (178 MB → 25 MB)** — sc 빌드의 `-r win-x64` RID가 fd 빌드에 전파되어 fd가 self-contained로 빌드되던 문제 수정. fd 빌드에 `-r win-x64`를 명시하여 해결

### 개선
- **릴리즈 파일 설명 추가** — 어떤 파일을 받아야 하는지 릴리즈 노트 및 README에 안내 추가

### 다운로드 안내

| 파일 | 크기 | 설명 |
|------|------|------|
| `ClaudeUsageTray-sc.exe` | ~77 MB | **Self-contained** — .NET 런타임 포함. 아무것도 설치 없이 바로 실행 **(권장)** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **Framework-dependent** — 파일이 더 작지만 [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) 설치 필요 |
<!-- /ko -->

<!-- en -->
### Fixed
- **fd build size bug (178 MB → 25 MB)** — The `-r win-x64` RID from the sc build leaked into the fd build, causing fd to be built as self-contained. Fixed by explicitly setting `-r win-x64` on the fd build step

### Improved
- **Release file descriptions** — Added download guide to release notes and README so it's clear which file to grab

### Which file should I download?

| File | Size | Description |
|------|------|-------------|
| `ClaudeUsageTray-sc.exe` | ~77 MB | **Self-contained** — includes the .NET runtime. Just download and run — nothing to install **(recommended)** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **Framework-dependent** — smaller file, but requires [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) to be installed |
<!-- /en -->

<!-- zh -->
### 修复
- **fd 构建体积 bug（178 MB → 25 MB）** — sc 构建的 `-r win-x64` RID 泄漏到 fd 构建中，导致 fd 实际上以 self-contained 模式构建。已通过在 fd 构建步骤中显式指定 `-r win-x64` 修复

### 改进
- **发布文件说明** — 在发布说明和 README 中添加下载指引，明确应下载哪个文件

### 应该下载哪个文件？

| 文件 | 大小 | 说明 |
|------|------|------|
| `ClaudeUsageTray-sc.exe` | ~77 MB | **独立运行版** — 包含 .NET 运行时，下载即用，无需安装 **（推荐）** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **依赖框架版** — 文件更小，但需要先安装 [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) |
<!-- /zh -->

<!-- ja -->
### 修正
- **fd ビルドサイズのバグ（178 MB → 25 MB）** — sc ビルドの `-r win-x64` RID が fd ビルドに引き継がれ、fd が self-contained としてビルドされていた問題を修正。fd ビルドに `-r win-x64` を明示することで解決

### 改善
- **リリースファイルの説明を追加** — どのファイルをダウンロードすべきか、リリースノートと README に案内を追加

### どのファイルをダウンロードすればいい？

| ファイル | サイズ | 説明 |
|----------|--------|------|
| `ClaudeUsageTray-sc.exe` | ~77 MB | **自己完結型** — .NET ランタイム同梱。ダウンロードしてすぐ実行できます **（推奨）** |
| `ClaudeUsageTray-fd.exe` | ~25 MB | **フレームワーク依存型** — ファイルが小さい分、[.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) のインストールが必要 |
<!-- /ja -->

---

## [1.15.11] - 2026-04-15

<!-- ko -->
### 수정
- **자동 업데이트 프로세스 중단** — TEMP 경로에 공백이 있는 환경(예: 사용자명에 공백)에서 업데이트 후 트레이 프로세스만 종료되고 파일이 교체되지 않던 버그 수정 (`cmd.exe /c` 경로 인용 방식 수정)
- **SHA256 검증 무시 버그** — 다운로드 스트림이 열려 있는 동안 동일 파일을 열어 검증하려다 실패하여 SHA256 검증이 항상 스킵되던 버그 수정
- 업데이트 스크립트 `copy` 실패 시 `%TEMP%\claude_update_error.log`에 오류 기록
<!-- /ko -->

<!-- en -->
### Fixed
- **Auto-update process hang** — Fixed file not being replaced when TEMP path contains spaces (e.g. username with a space). `cmd.exe /c` argument now uses double-double-quoting to handle paths correctly
- **SHA256 verification always skipped** — Download stream was held open with `FileShare.None`, preventing the SHA256 check from reading the same file. Stream is now closed before verification runs
- Batch script now logs `copy` failures to `%TEMP%\claude_update_error.log`
<!-- /en -->

<!-- zh -->
### 修复
- **自动更新进程挂起** — 修复 TEMP 路径包含空格时（如用户名有空格）更新后只有托盘进程退出、文件未被替换的问题（修正 `cmd.exe /c` 路径引号处理方式）
- **SHA256 校验被跳过** — 下载流以 `FileShare.None` 打开时阻止了校验代码读取同一文件，导致 SHA256 校验始终被跳过，现已修复
- 批处理脚本 `copy` 失败时记录错误到 `%TEMP%\claude_update_error.log`
<!-- /zh -->

<!-- ja -->
### 修正
- **自動更新プロセスのハング** — TEMP パスにスペースが含まれる環境（例：ユーザー名にスペース）でトレイプロセスのみ終了し、ファイルが置換されないバグを修正（`cmd.exe /c` の引数クォート方式を修正）
- **SHA256 検証が常にスキップ** — ダウンロードストリームが `FileShare.None` で開かれており、同じファイルを検証コードが開けず SHA256 検証が常にスキップされていたバグを修正
- バッチスクリプトの `copy` 失敗時に `%TEMP%\claude_update_error.log` にエラーを記録
<!-- /ja -->

---

## [1.15.10] - 2026-04-14

<!-- ko -->
### 변경
- **릴리즈 파일명 정리** — 어떤 파일을 받아야 할지 명확히 알 수 있도록 이름 변경
  - `ClaudeUsageTray.exe` → `ClaudeUsageTray-sc.exe` (self-contained, .NET 불필요)
  - `ClaudeUsageTray-framework-dependent.exe` → `ClaudeUsageTray-fd.exe`
  - SHA256 파일도 동일한 접미사로 통일 (`-sc.sha256`, `-fd.sha256`)
- **자동 업데이트 SHA256 검증 수정** — 이전 버전에서 `.txt` 확장자로 인해 SHA256 검증이 스킵되던 버그 수정
<!-- /ko -->

<!-- en -->
### Changed
- **Release file naming clarified** — renamed files so it's obvious which one to download
  - `ClaudeUsageTray.exe` → `ClaudeUsageTray-sc.exe` (self-contained, no .NET needed)
  - `ClaudeUsageTray-framework-dependent.exe` → `ClaudeUsageTray-fd.exe`
  - SHA256 files unified with matching suffix (`-sc.sha256`, `-fd.sha256`)
- **Auto-update SHA256 verification fixed** — previous releases silently skipped hash verification due to `.txt` extension mismatch
<!-- /en -->

<!-- zh -->
### 变更
- **发布文件命名优化** — 重命名文件，让用户一眼就能知道应该下载哪个
  - `ClaudeUsageTray.exe` → `ClaudeUsageTray-sc.exe`（自包含版，无需 .NET）
  - `ClaudeUsageTray-framework-dependent.exe` → `ClaudeUsageTray-fd.exe`
  - SHA256 文件统一使用相同后缀（`-sc.sha256`、`-fd.sha256`）
- **自动更新 SHA256 校验修复** — 修复因 `.txt` 扩展名导致哈希校验被静默跳过的问题
<!-- /zh -->

<!-- ja -->
### 変更
- **リリースファイル名の整理** — どのファイルをダウンロードすればよいか一目でわかるよう名称変更
  - `ClaudeUsageTray.exe` → `ClaudeUsageTray-sc.exe`（セルフコンテインド、.NET 不要）
  - `ClaudeUsageTray-framework-dependent.exe` → `ClaudeUsageTray-fd.exe`
  - SHA256 ファイルも同じ接尾辞に統一（`-sc.sha256`、`-fd.sha256`）
- **自動更新の SHA256 検証修正** — `.txt` 拡張子の不一致によりハッシュ検証がスキップされていたバグを修正
<!-- /ja -->

---

## [1.15.9] - 2026-04-14

<!-- ko -->
### 수정
- **파일 잠금으로 인한 팝업** — Claude 앱 업데이트 후 "다른 응용 프로그램에서 사용 중" 팝업이 가끔 뜨던 문제 수정. `SessionMonitor`가 `.jsonl` 파일을 `FileShare.Read`로 열어 Claude의 쓰기를 차단하던 문제를 `FileShare.ReadWrite`로 변경하여 해결
<!-- /ko -->

<!-- en -->
### Fixed
- **File lock popup** — Fixed occasional "file is being used by another application" popup after Claude app updates. `SessionMonitor` was opening `.jsonl` files with `FileShare.Read`, blocking Claude's writes. Changed to `FileShare.ReadWrite` to allow concurrent access
<!-- /en -->

<!-- zh -->
### 修复
- **文件锁定弹窗** — 修复 Claude 应用更新后偶发"文件正被其他应用程序使用"弹窗。`SessionMonitor` 以 `FileShare.Read` 打开 `.jsonl` 文件导致阻塞 Claude 写入，改为 `FileShare.ReadWrite` 解决
<!-- /zh -->

<!-- ja -->
### 修正
- **ファイルロックポップアップ** — Claude アップデート後に「別のアプリケーションが使用中」ポップアップが表示される問題を修正。`SessionMonitor` が `FileShare.Read` で `.jsonl` ファイルを開き Claude の書き込みをブロックしていた問題を `FileShare.ReadWrite` に変更して解決
<!-- /ja -->

---

## [1.15.8] - 2026-04-13

<!-- ko -->
### 수정
- **릴리즈 워크플로우 exe 파일명 충돌** — framework-dependent exe를 릴리즈에 게시할 때 이름이 `ClaudeUsageTray.exe`로 같아 GitHub가 같은 애셋으로 인식하던 문제 수정. 게시 전 `ClaudeUsageTray-framework-dependent.exe`로 리네임
<!-- /ko -->

<!-- en -->
### Fixed
- **Release workflow exe filename conflict** — Both exe files had the same name causing GitHub to treat them as the same asset. Renamed framework-dependent exe to `ClaudeUsageTray-framework-dependent.exe` before publishing
<!-- /en -->

<!-- zh -->
### 修复
- **发布工作流 exe 文件名冲突** — 两个 exe 文件名相同导致 GitHub 视为同一资产。发布前将 framework-dependent exe 重命名为 `ClaudeUsageTray-framework-dependent.exe`
<!-- /zh -->

<!-- ja -->
### 修正
- **リリースワークフロー exe ファイル名衝突** — 両方の exe が同じ名前で GitHub が同じアセットとして認識していた問題を修正。公開前に framework-dependent exe を `ClaudeUsageTray-framework-dependent.exe` にリネーム
<!-- /ja -->

---

## [1.15.7] - 2026-04-13

<!-- ko -->
### 수정
- **릴리즈 워크플로우 SHA256 경로 오류** — 빌드 출력 경로(`ClaudeUsageTray/bin/sc/`)와 SHA256 단계 경로가 맞지 않아 파일을 못 찾던 문제 수정
<!-- /ko -->

<!-- en -->
### Fixed
- **Release workflow SHA256 path mismatch** — Fixed path mismatch between build output (`ClaudeUsageTray/bin/sc/`) and SHA256 step
<!-- /en -->

<!-- zh -->
### 修复
- **发布工作流 SHA256 路径不匹配** — 修复了构建输出路径与 SHA256 步骤路径不匹配的问题
<!-- /zh -->

<!-- ja -->
### 修正
- **リリースワークフロー SHA256 パス不一致** — ビルド出力パスと SHA256 ステップのパスが一致しない問題を修正
<!-- /ja -->

---

## [1.15.6] - 2026-04-13

<!-- ko -->
### 보안
- **ntfy 토픽 입력 검증 강화** — 토픽 이름 20자 미만 저장 차단, 허용 문자(a-z0-9-_.@)외 입력 차단
- **ntfy 보안 경고 추가** — 설정창에 "예측 불가능한 긴 이름 사용" 경고 텍스트 표시 (4개 언어)
- **exe SHA256 해시 검증** — GitHub에서 다운로드한 업데이트 파일의 해시를 검증하여 변조된 파일 설치 방지. sha256 파일 불일치 시 설치 차단 및 에러 표시
- **릴리즈 SHA256 자동 게시** — GitHub Actions에서 릴리즈 시 SHA256.txt 파일을 자동으로 함께 게시

### 수정
- **업데이트 다운로드 에러 처리 강화** — SHA256 불일치·네트워크 오류 발생 시 빨간색 에러 메시지 표시, 버튼 복구
<!-- /ko -->

<!-- en -->
### Security
- **ntfy topic input validation** — Topic names under 20 characters are now blocked; only allowed characters (a-z0-9-_.@)
- **ntfy security warning** — Settings window now shows a security warning to use a long, unpredictable topic name (4 languages)
- **exe SHA256 hash verification** — Verifies the hash of downloaded updates against SHA256.txt published on GitHub; blocks installation on mismatch with error message
- **Automated SHA256 publishing** — GitHub Actions now automatically publishes SHA256.txt alongside the exe on each release

### Fixed
- **Update download error handling** — SHA256 mismatch and network errors now display a red error message and restore the button
<!-- /en -->

<!-- zh -->
### 安全
- **ntfy 主题输入验证** — 主题名称少于 20 字时保存被阻止；只允许 a-z0-9-_.@ 字符
- **ntfy 安全警告** — 设置窗口显示安全警告，提示使用难以预测的长主题名（4种语言）
- **exe SHA256 哈希验证** — 验证从 GitHub 下载的更新文件哈希，不匹配时阻止安装并显示错误
- **自动发布 SHA256** — GitHub Actions 在每次发布时自动同时发布 SHA256.txt

### 修复
- **更新下载错误处理** — SHA256 不匹配和网络错误时显示红色错误消息并恢复按钮
<!-- /zh -->

<!-- ja -->
### セキュリティ
- **ntfy トピック入力検証** — 20文字未満のトピック名は保存をブロック、使用可能文字(a-z0-9-_.@)以外をブロック
- **ntfy セキュリティ警告** — 設定ウィンドウに予測困難な長いトピック名を使用するようセキュリティ警告を表示（4言語）
- **exe SHA256 ハッシュ検証** — GitHub からダウンロードした更新ファイルのハッシュを SHA256.txt と照合、不一致時はインストールをブロックしエラー表示
- **SHA256 自動公開** — GitHub Actions がリリース時に SHA256.txt を自動的に一緒に公開

### 修正
- **アップデートダウンロードエラー処理** — SHA256 不一致・ネットワークエラー発生時に赤いエラーメッセージを表示しボタンを復元
<!-- /ja -->

---

## [1.15.3] - 2026-04-02

<!-- ko -->
### 수정
- **추가 사용량 한도 미설정 시 표시 오류** — 월 한도 미설정 계정에서 프로그레스 바가 0%로 표시되던 문제 수정. 한도 없을 경우 프로그레스 바/퍼센트를 숨기고 사용 크레딧 수만 표시
- **계정 전환 시 이전 계정 데이터 표시** — 이전 계정에서 발생한 rate-limit 대기(`Retry-After`)가 새 계정 API 조회를 막던 버그 수정. 계정 전환 시 대기 상태 초기화
- **계정 이름 설정 기능 제거** — 실용성이 낮아 설정 화면에서 제거
- **토큰 갱신 시 계정 전환 오감지** — 앱이 토큰을 갱신하며 `credentials.json`을 쓸 때 `FileSystemWatcher`가 계정 전환으로 오인하던 self-trigger 루프 수정
- **트레이 툴팁 계정 레이블 제거** — `[xxxxxxxx]` 형태의 의미없는 orgUuid 8자리 표시 제거

### 추가
- `build.bat` — 빌드 + 기존 프로세스 종료 + 자동 실행
<!-- /ko -->

<!-- en -->
### Fixed
- **Extra usage display with no limit set** — Progress bar showed 0% when no monthly limit was configured. Now hides the bar/percentage and shows only the used credit count
- **Previous account data shown after account switch** — Rate-limit backoff from the previous account was blocking API calls for the new account. Backoff is now cleared on account switch
- **Account name setting feature removed** — Removed from Settings due to low utility
- **Self-triggered account switch on token refresh** — App writing refreshed token to `credentials.json` was incorrectly detected as an account switch by `FileSystemWatcher`. Fixed with `_isSelfWriting` flag
- **Tray tooltip account label removed** — Removed meaningless `[xxxxxxxx]` orgUuid display from tooltip

### Added
- `build.bat` — build, kill existing process, and launch
<!-- /en -->

---

## [1.15.2] - 2026-04-01

<!-- ko -->
### 수정
- **로그아웃/재로그인 시 사용량 미갱신** — `FileSystemWatcher`가 `Changed` 이벤트만 감지하여 파일 삭제(로그아웃)/생성(로그인) 시 계정 전환이 감지되지 않던 문제 수정. `Created`, `Deleted` 이벤트 및 `FileName` 필터 추가
<!-- /ko -->

<!-- en -->
### Fixed
- **Usage not refreshing after logout/login** — `FileSystemWatcher` only watched `Changed` events, so file deletion (logout) and creation (login) were not detected. Added `Created`, `Deleted` events and `FileName` notify filter
<!-- /en -->

<!-- zh -->
### 修复
- **注销/重新登录后使用量不更新** — `FileSystemWatcher` 仅监听 `Changed` 事件，导致文件删除（注销）和创建（登录）时无法检测账号变化。添加 `Created`、`Deleted` 事件及 `FileName` 过滤器
<!-- /zh -->

<!-- ja -->
### 修正
- **ログアウト/再ログイン後に使用量が更新されない** — `FileSystemWatcher` が `Changed` イベントのみ監視していたため、ファイル削除（ログアウト）・作成（ログイン）時にアカウント変更を検出できなかった問題を修正。`Created`・`Deleted` イベントと `FileName` フィルタを追加
<!-- /ja -->

---

## [1.15.1] - 2026-04-01

<!-- ko -->
### 수정
- **Alt+F4 후 트레이 클릭 시 크래시** — 팝업 창이 실제로 닫힌 후 다시 열려는 시도로 `InvalidOperationException` 발생하던 문제 수정. `OnClosing` 오버라이드로 실제 종료 대신 숨김 처리
- **다중 계정 방식 재설계** — 폴더 선택 방식 제거. Claude 앱에서 로그아웃/로그인하면 `~/.claude/.credentials.json` 변경을 `FileSystemWatcher`로 자동 감지하여 즉시 새로고침. 계정별 히스토리를 `organizationUuid` 기준으로 분리 저장. 설정 창에서 현재 계정에 이름 부여 가능 (트레이 툴팁에 표시)
<!-- /ko -->

<!-- en -->
### Fixed
- **Crash after Alt+F4 then tray click** — After the popup window was closed with Alt+F4, clicking the tray icon threw `InvalidOperationException`. Fixed by overriding `OnClosing` to hide instead of close
- **Multi-account redesign** — Removed folder-selection approach. Now uses `FileSystemWatcher` on `~/.claude/.credentials.json` to auto-detect account switches done in the Claude app. Per-account history stored by `organizationUuid`. Account can be named in Settings and shown in tray tooltip
<!-- /en -->

<!-- zh -->
### 修复
- **Alt+F4 后点击托盘图标崩溃** — 通过 Alt+F4 关闭弹窗后点击托盘会抛出 `InvalidOperationException`，通过重写 `OnClosing` 改为隐藏窗口而非关闭来修复
- **多账号方式重新设计** — 移除文件夹选择方式。改用 `FileSystemWatcher` 监听 `~/.claude/.credentials.json`，在 Claude 应用切换账号后自动检测并刷新。按 `organizationUuid` 分别存储各账号历史记录。可在设置中为当前账号命名（显示在托盘提示中）
<!-- /zh -->

<!-- ja -->
### 修正
- **Alt+F4 後にトレイクリックでクラッシュ** — Alt+F4 でポップアップを閉じた後トレイをクリックすると `InvalidOperationException` が発生する問題を `OnClosing` のオーバーライドで修正（閉じる代わりに非表示に）
- **マルチアカウント方式の再設計** — フォルダ選択方式を廃止。`FileSystemWatcher` で `~/.claude/.credentials.json` を監視し、Claude アプリでのアカウント切替を自動検出して即時更新。`organizationUuid` 単位でアカウント別の履歴を保存。設定画面でアカウントに名前を付けてトレイツールチップに表示可能
<!-- /ja -->

---

## [1.15.0] - 2026-04-01

<!-- ko -->
### 추가
- **다중 계정 지원** — 설정 창에서 여러 Claude 계정(`.claude` 폴더 경로)을 등록하고 전환 가능. 트레이 우클릭 메뉴에서 빠른 계정 전환 지원 (Issue #5)

### 수정
- **동시 토큰 갱신 경쟁 조건** — 동시 다발 API 호출 시 자격증명 파일이 손상될 수 있던 문제 수정 (`SemaphoreSlim` 적용)
- **HttpClient 인스턴스 누수** — `UsageApiService`에서 매 인스턴스마다 `HttpClient`를 새로 생성하던 문제 수정 (static 공유)
- **업데이트 시 `NullReferenceException`** — `Process.MainModule` 이 null일 경우 크래시 가능성 수정
<!-- /ko -->

<!-- en -->
### Added
- **Multi-account support** — Register multiple Claude accounts (by `.claude` folder path) in Settings and switch between them. Quick account switching from the tray right-click menu (Issue #5)

### Fixed
- **Credential file race condition** — Concurrent API calls could corrupt the credentials file during token refresh; fixed with `SemaphoreSlim` locking
- **HttpClient instance leak** — `UsageApiService` was creating a new `HttpClient` per instance; changed to a shared static client
- **NullReferenceException on update** — `Process.MainModule` could be null, causing a crash during auto-update; added safe null fallback
<!-- /en -->

<!-- zh -->
### 新增
- **多账号支持** — 在设置窗口中注册多个 Claude 账号（`.claude` 文件夹路径）并切换。托盘右键菜单支持快速切换账号（Issue #5）

### 修复
- **凭据文件竞争条件** — 并发 API 调用时可能损坏凭据文件，已通过 `SemaphoreSlim` 锁定修复
- **HttpClient 实例泄漏** — `UsageApiService` 每次实例化时创建新 `HttpClient`，已改为共享静态实例
- **更新时 NullReferenceException** — `Process.MainModule` 可能为 null 导致崩溃，已添加安全 null 回退
<!-- /zh -->

<!-- ja -->
### 追加
- **マルチアカウント対応** — 設定画面で複数の Claude アカウント（`.claude` フォルダパス）を登録・切替可能。トレイ右クリックメニューからクイック切替にも対応（Issue #5）

### 修正
- **認証情報ファイルの競合状態** — 同時 API 呼び出し時にトークン更新で認証情報ファイルが破損する可能性を `SemaphoreSlim` で修正
- **HttpClient インスタンスリーク** — `UsageApiService` がインスタンスごとに `HttpClient` を生成していた問題を static 共有に変更
- **更新時 NullReferenceException** — `Process.MainModule` が null になり得るクラッシュを安全な null フォールバックで修正
<!-- /ja -->

---

## [1.14.0] - 2026-03-29

<!-- ko -->
### 추가
- **추가 구매 사용량 표시** — 추가 크레딧을 구매한 경우 팝업에 주황색 프로그레스 바와 크레딧 수치(사용량/한도) 표시. 미구매 시 섹션 자동 숨김 (Issue #36)
<!-- /ko -->

<!-- en -->
### Added
- **Extra Usage display** — Shows an amber progress bar and credit usage (used/limit) in the popup when extra credits are purchased. Section is hidden when not enabled (Issue #36)
<!-- /en -->

<!-- zh -->
### 新增
- **额外购买用量显示** — 购买了额外积分时，弹窗中显示橙色进度条及积分用量（已用/上限）。未购买时自动隐藏（Issue #36）
<!-- /zh -->

<!-- ja -->
### 追加
- **追加購入使用量表示** — 追加クレジットを購入している場合、ポップアップにオレンジ色のプログレスバーとクレジット使用量（使用/上限）を表示。未購入時はセクション非表示（Issue #36）
<!-- /ja -->

---

## [1.13.0] - 2026-03-29

<!-- ko -->
### 추가
- **키보드 단축키** — ESC / Ctrl+W / Alt+F4 로 팝업 닫기 (Issue #26)
- **트레이 우클릭 빠른 요약** — 팝업을 열지 않고 우클릭 메뉴에서 5h/7d 사용률과 초기화까지 남은 시간 바로 확인 (Issue #14)
- **7일 윈도우 소진 예측** — 현재 추세 기준 7일 할당량 소진 예상 시각 표시 (Issue #25)
- **업데이트 다운로드 진행률** — 업데이트 설치 시 다운로드 % 진행률 표시 (Issue #24)

### 수정
- **API 역직렬화 오류 수정** — `extra_usage`의 `used_credits` / `monthly_limit` 필드가 소수점 숫자로 반환될 때 JSON 역직렬화 실패하던 문제 수정 (`long?` → `double?`)
<!-- /ko -->

<!-- en -->
### Added
- **Keyboard shortcuts** — ESC / Ctrl+W / Alt+F4 to close the popup (Issue #26)
- **Tray right-click quick summary** — View 5h/7d usage and reset time directly from the tray context menu, without opening the popup (Issue #14)
- **7-day depletion forecast** — Shows estimated time the 7-day quota will run out based on current usage rate (Issue #25)
- **Update download progress** — Shows download percentage while applying an update (Issue #24)

### Fixed
- **API deserialization error** — Fixed JSON deserialization failure when `used_credits` / `monthly_limit` in `extra_usage` were returned as floating-point numbers (`long?` → `double?`)
<!-- /en -->

<!-- zh -->
### 新增
- **键盘快捷键** — ESC / Ctrl+W / Alt+F4 关闭弹窗（Issue #26）
- **托盘右键快速摘要** — 无需打开弹窗，直接在右键菜单查看 5h/7d 使用率及重置倒计时（Issue #14）
- **7天窗口耗尽预测** — 根据当前使用趋势显示 7 天配额预计耗尽时间（Issue #25）
- **更新下载进度** — 安装更新时显示下载百分比进度（Issue #24）

### 修复
- **API 反序列化错误** — 修复 `extra_usage` 中 `used_credits` / `monthly_limit` 返回小数时 JSON 解析失败的问题（`long?` → `double?`）
<!-- /zh -->

<!-- ja -->
### 追加
- **キーボードショートカット** — ESC / Ctrl+W / Alt+F4 でポップアップを閉じる（Issue #26）
- **トレイ右クリック簡易表示** — ポップアップを開かずに右クリックメニューで 5h/7d 使用率とリセット時間を確認（Issue #14）
- **7日間ウィンドウ枯渇予測** — 現在の使用ペースをもとに 7日割り当てが枯渇する予想時刻を表示（Issue #25）
- **アップデートダウンロード進捗** — アップデート適用中にダウンロードの % 進捗を表示（Issue #24）

### 修正
- **API デシリアライズエラー修正** — `extra_usage` の `used_credits` / `monthly_limit` が小数で返された際に JSON デシリアライズが失敗していた問題を修正（`long?` → `double?`）
<!-- /ja -->

---

## [1.12.1] - 2026-03-29

<!-- ko -->
### 수정
- **ntfy 스마트폰 알림 미전송 버그 수정** — HTTP 헤더에 한국어 제목을 넣을 때 .NET이 FormatException을 발생시켜 ntfy 요청 전체가 실패하던 문제. JSON API 방식으로 변경하여 해결 (Issue #23)
- **ntfy 토픽 Enter키 저장** — 토픽 입력 후 Enter를 눌러도 저장되지 않던 문제 수정 (Issue #23)
<!-- /ko -->

<!-- en -->
### Fixed
- **ntfy push notification not sent** — When the app locale was Korean, adding a Korean title to an HTTP header caused a .NET FormatException, silently aborting the entire ntfy request. Switched to JSON API to fix (Issue #23)
- **ntfy topic saved on Enter key** — Topic was not saved when pressing Enter; now handled via KeyDown event (Issue #23)
<!-- /en -->

<!-- zh -->
### 修复
- **ntfy 推送通知未发送** — 中文/韩文标题写入 HTTP 头时触发 FormatException，导致请求静默失败。改用 JSON API 方式解决（Issue #23）
- **ntfy 主题名 Enter 键保存** — 输入主题后按 Enter 不保存的问题已修复（Issue #23）
<!-- /zh -->

<!-- ja -->
### 修正
- **ntfy プッシュ通知が送信されないバグ修正** — 韓国語タイトルを HTTP ヘッダーに設定すると .NET が FormatException を投げて ntfy リクエスト全体が失敗していた問題を、JSON API 方式に変更して解決（Issue #23）
- **ntfy トピック名の Enter キー保存** — トピック入力後に Enter を押しても保存されなかった問題を修正（Issue #23）
<!-- /ja -->

---

## [1.12.0] - 2026-03-28

<!-- ko -->
### 추가
- **중복 실행 방지** — 이미 실행 중인 경우 새 인스턴스 시작 시 안내 메시지 표시 후 종료 (Issue #22)
- **5시간 소진 예측** — 현재 사용 추세 기준 할당량이 소진될 예상 시각 표시. 윈도우 내 소진이 예상될 때만 표시 (Issue #9)
- **토큰 비용 참고값 (USD)** — 오늘 사용한 토큰의 Sonnet API 기준 환산 비용 표시. Claude Code는 구독제이므로 참고용 (Issue #11)
<!-- /ko -->

<!-- en -->
### Added
- **Single instance enforcement** — Shows a message and exits if already running (Issue #22)
- **5-hour depletion forecast** — Shows estimated time the quota will run out based on current usage rate. Only shown when depletion is expected within the current window (Issue #9)
- **Token cost estimate (USD)** — Shows today's token usage converted to approximate USD at Sonnet API rates. For reference only — Claude Code uses a subscription model (Issue #11)
<!-- /en -->

<!-- zh -->
### 新增
- **防止重复启动** — 已在运行时，新实例启动后显示提示并退出（Issue #22）
- **5小时配额耗尽预测** — 根据当前使用趋势，显示配额预计耗尽时间。仅在当前窗口内预计耗尽时显示（Issue #9）
- **令牌费用参考值（USD）** — 显示今日令牌使用量按 Sonnet API 价格换算的参考费用。Claude Code 为订阅制，仅供参考（Issue #11）
<!-- /zh -->

<!-- ja -->
### 追加
- **重複起動防止** — 既に起動中の場合、新しいインスタンスはメッセージを表示して終了（Issue #22）
- **5時間クォータ枯渇予測** — 現在の使用ペースに基づき、クォータが枯渇する予想時刻を表示。ウィンドウ内で枯渇が見込まれる場合のみ表示（Issue #9）
- **トークンコスト参考値（USD）** — 本日のトークン使用量を Sonnet API 価格で換算した参考費用を表示。Claude Code はサブスクリプション制のため参考値（Issue #11）
<!-- /ja -->

---

## [1.11.0] - 2026-03-28

<!-- ko -->
### 개선
- **종료 버튼 시각적 구분 강화** — 종료 버튼을 평소에도 연한 빨간빛으로 표시해 파괴적 액션임을 명확히 구분 (Issue #17)
- **알림 테스트 피드백** — 알림 테스트 버튼 클릭 시 "✓ 전송됨" 결과 표시. ntfy 미설정 시 "ntfy 미설정" 안내 포함 (Issue #20)
<!-- /ko -->

<!-- en -->
### Improved
- **Quit button visual distinction** — Quit button now shows in a subtle red tint at rest, making it clearly distinct as a destructive action (Issue #17)
- **Test notification feedback** — Clicking the test notification button now shows "✓ Sent" result. Shows ntfy status if not configured (Issue #20)
<!-- /en -->

<!-- zh -->
### 改进
- **退出按钮视觉区分增强** — 退出按钮平时显示为淡红色，明确标识其为破坏性操作（Issue #17）
- **通知测试反馈** — 点击通知测试按钮后显示"✓ 已发送"结果，未设置 ntfy 时显示相应提示（Issue #20）
<!-- /zh -->

<!-- ja -->
### 改善
- **終了ボタンの視覚的区別強化** — 終了ボタンを通常時も淡い赤みで表示し、破壊的アクションであることを明確化（Issue #17）
- **通知テストのフィードバック** — 通知テストボタン押下後に「✓ 送信済み」を表示。ntfy 未設定時はその旨を案内（Issue #20）
<!-- /ja -->

---

## [1.10.0] - 2026-03-28

<!-- ko -->
### 추가
- **24시간 자동 업데이트 확인** — 앱이 켜진 상태에서 24시간마다 자동으로 새 버전을 확인 (Issue #19)
- **수동 업데이트 확인** — 푸터의 버전 텍스트(v1.x.x) 클릭 시 즉시 업데이트 체크. 최신 버전이면 "✓ 최신 버전입니다" 표시 후 3초 후 사라짐. 이전에 건너뛴 버전도 수동 체크 시 재표시 (Issue #19)
<!-- /ko -->

<!-- en -->
### Added
- **24-hour auto update check** — Automatically checks for new versions every 24 hours while the app is running (Issue #19)
- **Manual update check** — Click the version label (v1.x.x) in the footer to check immediately. Shows "✓ Already up to date" for 3 seconds if no update found. Skipped versions reappear on manual check (Issue #19)
<!-- /en -->

<!-- zh -->
### 新增
- **24小时自动检查更新** — 应用运行时每24小时自动检查新版本（Issue #19）
- **手动检查更新** — 点击底部版本号（v1.x.x）立即检查。无更新时显示"✓ 已是最新版本"，3秒后消失。手动检查时已跳过的版本也会重新显示（Issue #19）
<!-- /zh -->

<!-- ja -->
### 追加
- **24時間自動アップデート確認** — アプリ起動中、24時間ごとに自動で新バージョンを確認（Issue #19）
- **手動アップデート確認** — フッターのバージョンラベル（v1.x.x）をクリックして即時確認。最新版なら「✓ 最新バージョンです」を3秒表示。手動確認時はスキップ済みバージョンも再表示（Issue #19）
<!-- /ja -->

---

## [1.9.0] - 2026-03-28

<!-- ko -->
### 개선
- **헤더 ✕ 버튼 제거** — 팝업은 이미 클릭 아웃 시 자동으로 닫히므로 헤더의 중복된 닫기 버튼 제거. ↻ 새로고침 버튼만 유지 (Issue #16)
- **종료 버튼 시각적 구분** — 설정(⚙)과 종료 사이에 구분선 추가, 마우스 오버 시 붉은 계열 색상으로 파괴적 액션 명확화 (Issue #17)
- **설정창 닫기 후 메인 팝업 복귀** — 설정창을 닫으면 메인 팝업이 자동으로 다시 표시됨. 설정을 열어도 메인 팝업이 백그라운드에서 유지됨 (Issue #18)
- **알림 테스트 버튼 안내 추가** — "알림 테스트" 버튼이 Windows 토스트와 스마트폰 ntfy 알림을 동시에 테스트함을 버튼 하단에 명시
<!-- /ko -->

<!-- en -->
### Improved
- **Remove header ✕ button** — Popup already closes on click-outside (Deactivated), so the redundant close button in the header has been removed. Only the ↻ refresh button remains (Issue #16)
- **Quit button visual distinction** — Added a separator before the quit button; hover now shows red color to indicate a destructive action (Issue #17)
- **Return to main popup after closing settings** — Closing the settings window automatically brings the main popup back. The main popup also stays visible in the background while settings is open (Issue #18)
- **Test notification hint** — Clarified that the "Test notification" button tests both Windows toast and phone push (ntfy) simultaneously
<!-- /en -->

<!-- zh -->
### 改进
- **移除标题栏 ✕ 按钮** — 弹窗点击外部已自动关闭，标题栏的冗余关闭按钮已移除，仅保留 ↻ 刷新按钮（Issue #16）
- **退出按钮视觉区分** — 设置与退出之间添加分隔线，鼠标悬停时显示红色，明确提示破坏性操作（Issue #17）
- **关闭设置后返回主窗口** — 关闭设置窗口后主弹窗自动重新显示，打开设置时主弹窗保持后台可见（Issue #18）
- **测试通知说明** — 明确标注"测试通知"按钮同时测试 Windows 通知和手机推送（ntfy）
<!-- /zh -->

<!-- ja -->
### 改善
- **ヘッダーの ✕ ボタンを削除** — ポップアップはクリック外で自動的に閉じるため、ヘッダーの重複した閉じるボタンを削除。↻ 更新ボタンのみを残します（Issue #16）
- **終了ボタンの視覚的区別** — 設定と終了の間に区切り線を追加、ホバー時に赤色で破壊的操作を明示（Issue #17）
- **設定を閉じた後にメインポップアップへ戻る** — 設定ウィンドウを閉じると自動的にメインポップアップが再表示。設定中もメインポップアップはバックグラウンドで維持（Issue #18）
- **テスト通知の説明追加** — 「通知テスト」ボタンが Windows トーストとスマホ通知（ntfy）を同時にテストすることをボタン下部に明示
<!-- /ja -->

---

## [1.8.0] - 2026-03-28

<!-- ko -->
### 수정
- **단일 실행 파일 크래시 수정** — GitHub에서 다운로드한 exe 실행 시 `DllNotFoundException`으로 앱이 무음 종료되는 문제 수정. WPF 네이티브 DLL(`PresentationNative_cor3.dll` 등)을 단일 파일에 포함하도록 빌드 방식 변경 (Issue #7)
- **시작 프로그램 경로 수정** — 단일 파일 앱에서 `Assembly.Location`이 빈 문자열을 반환하는 문제 수정, 올바른 실행 파일 경로 사용
<!-- /ko -->

<!-- en -->
### Fixed
- **Single-file exe crash fix** — App was silently crashing with `DllNotFoundException` on launch when downloaded from GitHub. Fixed by bundling WPF native DLLs (`PresentationNative_cor3.dll` etc.) into the single-file exe (Issue #7)
- **Start with Windows path fix** — Fixed `Assembly.Location` returning empty string in single-file apps; now uses the correct executable path
<!-- /en -->

<!-- zh -->
### 修复
- **单文件启动崩溃修复** — 从 GitHub 下载的 exe 启动时因 `DllNotFoundException` 静默崩溃。修复方案：将 WPF 原生 DLL（`PresentationNative_cor3.dll` 等）打包进单文件（Issue #7）
- **开机启动路径修复** — 修复单文件应用中 `Assembly.Location` 返回空字符串的问题，改用正确的可执行文件路径
<!-- /zh -->

<!-- ja -->
### 修正
- **単一ファイル exe クラッシュ修正** — GitHub からダウンロードした exe を起動すると `DllNotFoundException` で無音終了する問題を修正。WPF ネイティブ DLL（`PresentationNative_cor3.dll` など）を単一ファイルに同梱するよう変更（Issue #7）
- **スタートアップパス修正** — 単一ファイルアプリで `Assembly.Location` が空文字を返す問題を修正し、正しい実行ファイルパスを使用するよう変更
<!-- /ja -->

---

## [1.7.0] - 2026-03-28

<!-- ko -->
### 추가
- **시간대별 사용량 차트** — 차트 섹션에 "7일 / 오늘" 토글 추가. 오늘 탭 선택 시 0시부터 현재 시각까지 시간대별 토큰 사용량 바 차트 표시, 현재 시각 바 보라색 강조 (Issue #15)
<!-- /ko -->

<!-- en -->
### Added
- **Hourly usage chart** — Added "7-Day / Today" toggle to the chart section. The Today tab shows token usage per hour from midnight to the current time, with the current hour highlighted in purple (Issue #15)
<!-- /en -->

<!-- zh -->
### 新增
- **时段用量图表** — 图表区域新增「7天 / 今日」切换。选择今日后显示从0时到当前时刻的每小时用量柱状图，当前时刻以紫色高亮（Issue #15）
<!-- /zh -->

<!-- ja -->
### 追加
- **時間帯別使用量チャート** — チャートセクションに「7日 / 今日」トグルを追加。今日タブでは0時から現在時刻までの時間帯別トークン使用量を表示し、現在時刻のバーを紫色でハイライト（Issue #15）
<!-- /ja -->

---

## [1.6.0] - 2026-03-28

<!-- ko -->
### 추가
- **업데이트 다이얼로그** — 새 버전 감지 시 체인지로그를 확인하고 업데이트 여부를 선택하는 팝업 표시
- **이번 버전 건너뛰기** — 업데이트 다이얼로그에서 특정 버전을 건너뛰면 해당 버전에 대해 다시 알림 없음
- **업데이트 다이얼로그 다국어 지원** — 한국어·영어·중국어·일본어로 현재 언어에 맞는 체인지로그 자동 표시
<!-- /ko -->

<!-- en -->
### Added
- **Update dialog** — When a new version is detected, a popup shows the changelog and lets you choose whether to update
- **Skip this version** — Skipping a version in the update dialog suppresses future notifications for that version
- **Multilingual update dialog** — Changelog is displayed in the user's language (Korean · English · Chinese · Japanese)
<!-- /en -->

<!-- zh -->
### 新增
- **更新对话框** — 检测到新版本时，弹出对话框显示更新日志并询问是否更新
- **跳过此版本** — 在更新对话框中跳过某版本后，不再显示该版本的提醒
- **多语言更新日志** — 根据系统语言自动显示对应的更新日志（韩语·英语·中文·日语）
<!-- /zh -->

<!-- ja -->
### 追加
- **アップデートダイアログ** — 新バージョンを検出したとき、更新内容を確認してアップデートを選択できるポップアップを表示
- **このバージョンをスキップ** — ダイアログでスキップしたバージョンは以降通知されない
- **多言語アップデートログ** — システム言語に合わせた更新内容を自動表示（韓国語·英語·中国語·日本語）
<!-- /ja -->

---

## [1.5.0] - 2026-03-28

<!-- ko -->
### 추가
- **글로벌 예외 핸들러** — 앱 시작 또는 런타임 중 크래시 발생 시 조용히 종료되는 대신 에러 메시지 창 표시, GitHub Issues 신고용 스택트레이스 포함

### 수정
- **Rate limit 배너 오표시 수정** — API가 정상 응답하고 5시간 사용률이 100% 미만이면 이전 rate limit 기록을 초기화하여 리셋 이후에도 경고 배너가 남아있는 문제 해결
<!-- /ko -->

<!-- en -->
### Added
- **Global exception handler** — Instead of silently crashing, the app now shows an error dialog with a stack trace for GitHub Issues reporting

### Fixed
- **Rate limit banner stale display** — Banner now clears automatically when the API responds successfully with usage below 100%
<!-- /en -->

<!-- zh -->
### 新增
- **全局异常处理** — 应用崩溃时不再静默退出，而是显示包含堆栈跟踪的错误对话框，方便在 GitHub Issues 反馈

### 修复
- **限速提示横幅误显示** — API 正常响应且使用率低于 100% 时，横幅现在会自动消失
<!-- /zh -->

<!-- ja -->
### 追加
- **グローバル例外ハンドラー** — クラッシュ時に静かに終了する代わりに、GitHub Issues 報告用のスタックトレース付きエラーダイアログを表示

### 修正
- **レート制限バナーの誤表示** — API が正常に応答し使用率が 100% 未満の場合、バナーが自動的に消えるように修正
<!-- /ja -->

---

## [1.4.0] - 2026-03-27

### 추가
- **OAuth 토큰 자동 갱신** — 만료 시 자동으로 `platform.claude.com/v1/oauth/token` 갱신 후 credentials.json 업데이트 (Issue #1)
- **7일 사용 추이 그래프** — 팝업 하단에 일별 토큰 사용량 바 차트 표시, 오늘 날짜 강조 (Issue #2)
- **닫기(✕) 버튼** — 팝업 헤더에 추가, 클릭 시 팝업만 닫히고 앱은 트레이에 유지 (Issue #3)
- **CSV 내보내기** — 최대 90일 사용 이력을 바탕화면에 CSV로 저장 (Issue #4)

### 개선
- 이력 데이터 로컬 자동 저장 (`~/.claude/claude-usage-tray-history.json`)

---

## [1.3.0] - 2026-03-27

### 추가
- **윈도우 시작 시 자동 실행** — 설정창 토글 한 번으로 레지스트리 등록/해제
- **알림 테스트 버튼** — 설정창에서 Windows 토스트 + ntfy 푸시 알림 즉시 테스트 가능

### 개선
- **API Retry-After 준수** — 429 응답의 `Retry-After` 헤더를 파싱하여 지정된 시간 동안 API 재호출 차단, 불필요한 반복 요청 방지
- **에러 메시지 개선** — Rate limit 시 "API 제한 중 — HH:mm:ss 이후 재시도" 형태로 재시도 가능 시각 표시

---

## [1.2.0] - 2026-03-27

### 추가
- **다음 갱신 카운트다운** — 헤더에 다음 자동 갱신까지 남은 시간을 1초 단위로 실시간 표시

### 개선
- **스마트 에러 UX** — API 조회 실패 시 기존 값을 0%로 초기화하지 않고 마지막 성공 데이터 유지
- **트레이 아이콘 상태 표시** — 데이터 조회 실패 시 트레이 아이콘이 회색 `?`로 변경되어 비정상 상태 직관적으로 전달
- **타임스탬프 개선** — 성공 시 `업데이트 HH:mm:ss`, 실패 시 `⚠ HH:mm:ss` 로 구분 표시
- **폴링 간격 조정** — 30초 → 2분 (API 호출 빈도 제한 준수)

---

## [1.1.0] - 2026-03-27

### 추가
- **자동 업데이트** — 앱 시작 시 GitHub Releases 최신 버전 확인, 팝업 배너에서 원클릭 업데이트
- **Windows 토스트 알림** — 5시간 윈도우 사용량 임계값(50/75/90/100%) 도달 시 알림
- **ntfy.sh 스마트폰 푸시 알림** — iOS·Android에서 실시간 사용량 알림 수신
- **설정 모달 창** — 팝업 외부 공간이 부족할 때 잘리지 않는 별도 모달 창으로 구현
- **다국어 지원** — 한국어·중국어·일본어·영어 (시스템 언어 자동 감지)
- **GitHub Actions 릴리즈 워크플로우** — `v*` 태그 푸시 시 단일 실행 파일 자동 빌드·배포
- **면책 조항** — README 및 설정창에 참고용 도구임을 명시

### 수정
- **API 헤더 수정** — `anthropic-version: 2023-06-01` → `anthropic-beta: oauth-2025-04-20` (429 오류 해결)
- **API 응답 모델 재작성** — 실제 응답 형식(`five_hour`, `seven_day`)에 맞게 전면 수정

---

## [1.0.0] - 2026-03-26

### 최초 릴리즈
- Windows 시스템 트레이 기반 Claude AI 사용량 모니터링
- 5시간 · 7일 API 할당량 진행 바 및 초기화 시간 표시
- 오늘의 토큰 통계 (입력 / 출력 / 캐시 읽기 / 캐시 쓰기)
- 로컬 `.jsonl` 세션 파일 기반 통계 집계
- Claude Code OAuth 토큰 자동 재사용 (별도 로그인 불필요)
- 다크 테마 팝업 UI (WPF .NET 9)
