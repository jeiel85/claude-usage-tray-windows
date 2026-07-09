# multi-pc-usage-sync - Work Plan

## TL;DR (For humans)
<!-- Fill this LAST, after the detailed plan below is written, so it summarizes the REAL plan. -->
<!-- Plain English for a non-engineer: NO file paths, NO todo numbers, NO wave/agent/tool names. -->

**What you'll get:** A concrete implementation plan for keeping two Windows PCs on the same account visually aligned even when one PC cannot call the usage API. The plan uses a shared folder to exchange safe display snapshots, not credentials or raw logs.

**Why this approach:** The app already mixes account-level API quota with PC-local token logs. A per-device snapshot model preserves that distinction, avoids cloud file conflicts, and can reduce duplicate API calls.

**What it will NOT do:** It will not sync OAuth credentials, refresh tokens, raw session logs, or local databases. It will not introduce a hosted backend in the first pass. It will not change release/versioning as part of this planning-only commit.

**Effort:** Medium
**Risk:** Medium - the core logic is straightforward, but stale/corrupt cloud-synced files and UI trust labeling need careful handling.
**Decisions to sanity-check:** Shared-folder first, sync off by default, 5-minute API quota TTL, one file per device/provider/date.

Your next move: Execute this plan when ready. Full execution detail follows below.

---

> TL;DR (machine): Medium-risk docs-approved feature plan: add optional shared-folder per-device usage snapshots, merge newest account quota plus summed local totals, test stale/corrupt/offline cases.

## Scope
### Must have
- Add optional multi-PC usage sync behind settings; default disabled.
- Add a `UsageSyncService` that reads/writes display-only JSON snapshots under a user-selected shared folder.
- Use one file per `accountKey/provider/deviceId/localDate` to avoid cloud merge conflicts.
- Merge account-level quota windows by newest successful non-stale snapshot.
- Merge local token totals by summing latest same-day device snapshots per provider.
- Preserve local-only behavior when sync is disabled, folder missing, snapshot corrupt, or all remote snapshots stale.
- Show sync provenance in the popup/status text without replacing existing error guidance.
- Add four-language localized strings for every new visible label/note.
- Add unit/integration tests for snapshot contract, merge semantics, settings persistence, and refresh fallback.
- Verify through the actual WPF surface: settings toggle/folder path, popup display, stale fallback.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- Do not sync `.credentials.json`, `.codex/auth.json`, OAuth access/refresh tokens, raw `*.jsonl` logs, SQLite DB files, or full history files.
- Do not put a shared mutable aggregate JSON in the sync folder.
- Do not let a remote snapshot with an error overwrite a fresher local success.
- Do not mark stale remote data as current without an explicit stale/source label.
- Do not add a cloud backend, account system, or server dependency in this implementation.
- Do not weaken existing API backoff behavior for 429/403.
- Do not remove local log scanning; remote sync is a fallback/augmentation layer only.
- Do not change unrelated visual layout, provider priority, weather, update, or notification behavior.

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: tests-after with focused xUnit coverage, plus WPF integration/manual QA.
- Evidence: write command output and screenshots/log notes under `.omo/evidence/task-<N>-multi-pc-usage-sync.<ext>`.
- Minimum automated commands:
  - `dotnet test ClaudeUsageTray.Tests/ClaudeUsageTray.Tests.csproj --filter "FullyQualifiedName~UsageSync|FullyQualifiedName~Settings|FullyQualifiedName~MainViewModel"`
  - `dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo`
  - `dotnet test ClaudeUsageTray.Tests/ClaudeUsageTray.Tests.csproj`
- Manual QA gate:
  - Launch the app locally.
  - Enable sync with a temp shared folder.
  - Seed a second-device snapshot.
  - Open Settings and UsagePopup to verify source labels, merged token totals, and stale fallback.

## Execution strategy
### Parallel execution waves
> Target 5-8 todos per wave. Fewer than 3 (except the final) means you under-split.
- Wave 1: data contract, settings contract, service tests.
- Wave 2: refresh integration, provider merge, UI/localization.
- Wave 3: integration/manual QA, docs, release bookkeeping if implementation changes app code.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | none | 2, 4, 5 | 3 |
| 2 | 1 | 4, 5, 6 | 3 |
| 3 | none | 6 | 1, 2 |
| 4 | 1, 2 | 6, 7 | 5 |
| 5 | 1, 2 | 6, 7 | 4 |
| 6 | 3, 4, 5 | 7, 8 | none |
| 7 | 6 | 8 | none |
| 8 | 7 | final verification | none |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [ ] 1. Define sync settings and snapshot models
  What to do / Must NOT do: Add model types for sync settings and snapshots. Extend `NotificationSettings` with `UsageSyncEnabled`, `UsageSyncFolderPath`, `UsageSyncApiSnapshotTtlMinutes` default `5`, and `UsageSyncLocalSnapshotTtlHours` default `24`. Add snapshot models such as `UsageSyncSnapshot`, `UsageSyncProviderSnapshot`, and `UsageSyncDeviceInfo` under `ClaudeUsageTray/Models` or a tightly scoped service file. Include schema version `1`, `accountKey`, `deviceId`, `deviceName`, `provider`, `localDate`, `observedAtUtc`, `quota`, `localTotals`, `errorKind`, and `source`. Must NOT store tokens, raw API bodies, file paths to raw logs, or machine usernames beyond `Environment.MachineName`.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 2, 4, 5
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray/Models/NotificationSettings.cs:5`, `ClaudeUsageTray/Services/SettingsService.cs:9`, `ClaudeUsageTray/Models/ProviderUsageSnapshot.cs`, `ClaudeUsageTray/Models/UsageData.cs`, `ClaudeUsageTray/Services/UsageApiService.cs:21`.
  Acceptance criteria (agent-executable): A new test serializes/deserializes a full snapshot and asserts no JSON property named `accessToken`, `refreshToken`, `LastRawResponse`, `rawLog`, `credentials`, or `auth` exists.
  QA scenarios (name the exact tool + invocation): happy: `dotnet test ClaudeUsageTray.Tests/ClaudeUsageTray.Tests.csproj --filter "FullyQualifiedName~UsageSyncSnapshot"` records schema round-trip evidence at `.omo/evidence/task-1-multi-pc-usage-sync.txt`; failure: corrupt/unknown schema JSON returns ignored snapshot result without throwing.
  Commit: Y | feat(sync): add usage snapshot data contract

- [ ] 2. Add `UsageSyncService` with atomic per-device file I/O
  What to do / Must NOT do: Implement `UsageSyncService` to compute a stable `deviceId`, build paths under `<syncRoot>/<accountHash>/<provider>/<localDate>/<deviceId>.json`, write atomically through a temp file, read all same-account snapshots, ignore stale/corrupt/foreign-schema files, and return structured read diagnostics. Use dynamic user/profile paths only. Must NOT directly read or copy `.claude`, `.codex`, `.gemini`, or OpenCode source files.
  Parallelization: Wave 1 | Blocked by: 1 | Blocks: 4, 5, 6
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray/Services/HistoryService.cs:96`, `ClaudeUsageTray/Services/HistoryService.cs:174`, `ClaudeUsageTray/Services/SessionMonitor.cs:96`, `ClaudeUsageTray/Services/SettingsService.cs:35`.
  Acceptance criteria (agent-executable): Unit tests create two fake device snapshots plus one corrupt file in a temp directory; read result returns two valid snapshots, one diagnostic, and no exception.
  QA scenarios (name the exact tool + invocation): happy: `dotnet test ClaudeUsageTray.Tests/ClaudeUsageTray.Tests.csproj --filter "FullyQualifiedName~UsageSyncService"` with evidence `.omo/evidence/task-2-multi-pc-usage-sync.txt`; failure: locked/corrupt file is skipped and local refresh continues.
  Commit: Y | feat(sync): add usage sync service

- [ ] 3. Add settings UI and four-language localization
  What to do / Must NOT do: Add a compact "Multi-PC sync" section to `SettingsWindow` with an enable checkbox, folder path textbox, browse button, and status/help text. Bind new `MainViewModel` properties and persist through `LoadSettings`/`SaveSettings`. Add localization keys in ko/zh/ja/en. Use existing styles; do not introduce a new visual theme or oversized explanatory copy.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 6
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray/Views/SettingsWindow.xaml:20`, `ClaudeUsageTray/Views/SettingsWindow.xaml.cs:114`, `ClaudeUsageTray/Views/SettingsWindow.xaml.cs:146`, `ClaudeUsageTray/Services/LocalizationService.cs:5`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:451`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:617`.
  Acceptance criteria (agent-executable): A WPF integration test constructs `MainViewModel`, sets sync fields, executes `SaveSettingsCommand`, reloads settings, and asserts values persist.
  QA scenarios (name the exact tool + invocation): happy: launch settings, enable sync, choose temp folder, save, reopen, verify values remain; failure: empty folder path while enabled shows localized validation and does not crash. Evidence `.omo/evidence/task-3-multi-pc-usage-sync.png` plus `.txt`.
  Commit: Y | feat(settings): expose multi-pc sync options

- [ ] 4. Integrate Claude quota snapshots into refresh without increasing API pressure
  What to do / Must NOT do: In `RefreshClaudeAsync`, read remote successful quota snapshots before deciding display fallback, write a fresh local snapshot after local API success, and when local API is skipped/failed use the newest non-stale successful snapshot for `ClaudeVm.ShortPercent`, `ShortReset`, `LongPercent`, `LongReset`, extra usage if present, and API note/source label. Keep existing `_apiRetryAfter` logic. Must NOT call the API more frequently because sync is enabled.
  Parallelization: Wave 2 | Blocked by: 1, 2 | Blocks: 6, 7
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray/ViewModels/MainViewModel.cs:1143`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1154`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1161`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1202`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1351`, `ClaudeUsageTray/Services/UsageApiService.cs:52`.
  Acceptance criteria (agent-executable): A test or harness with local API failure plus a fresh remote successful snapshot results in non-error Claude quota display and a source note naming the remote device/time.
  QA scenarios (name the exact tool + invocation): happy: seed remote Claude quota snapshot in temp sync root, force local API failure through test double, run refresh, verify displayed quota comes from snapshot; failure: stale snapshot older than TTL is ignored and existing local error/cooldown guidance remains. Evidence `.omo/evidence/task-4-multi-pc-usage-sync.txt`.
  Commit: Y | feat(sync): merge shared Claude quota snapshots

- [ ] 5. Integrate local token totals per provider without double counting
  What to do / Must NOT do: After each provider obtains local totals, write device snapshots and compute merged same-day totals by provider. For Claude, sum `SessionStats` totals by latest snapshot per device/date. For Codex/Gemini/OpenCode, sum `ProviderUsageSnapshot` local totals by latest snapshot per device/date. Keep quota percentages account-level and local totals device-level. Must NOT sum two snapshots from the same `deviceId` for the same provider/date.
  Parallelization: Wave 2 | Blocked by: 1, 2 | Blocks: 6, 7
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray/ViewModels/MainViewModel.cs:1166`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1170`, `ClaudeUsageTray/ViewModels/CodexViewModel.cs:101`, `ClaudeUsageTray/ViewModels/GeminiViewModel.cs:75`, `ClaudeUsageTray/ViewModels/OpenCodeViewModel.cs:70`, `ClaudeUsageTray/Services/CodexUsageMonitor.cs:22`, `ClaudeUsageTray/Services/GeminiCliUsageMonitor.cs:39`, `ClaudeUsageTray/Services/OpenCodeUsageMonitor.cs:24`.
  Acceptance criteria (agent-executable): Tests seed two devices and assert total = desktop + laptop; then seed an older duplicate for desktop and assert only the newest desktop snapshot is counted.
  QA scenarios (name the exact tool + invocation): happy: local device has 10 input/5 output, remote device has 7 input/3 output, merged label/chart data shows 17/8; failure: remote file from yesterday or same device older file is ignored. Evidence `.omo/evidence/task-5-multi-pc-usage-sync.txt`.
  Commit: Y | feat(sync): aggregate per-device local totals

- [ ] 6. Surface sync provenance in popup/status safely
  What to do / Must NOT do: Add concise provenance strings such as `Synced from DESKTOP at 14:32` and `2 PCs merged` where space allows. Prefer existing note/data-source fields (`ClaudeVm.ApiNote`, `CodexDataSource`, provider notes) over adding large UI blocks. Stale data must explicitly say stale or fall back to local-only. Must NOT hide real local API errors unless a fresh successful snapshot is actively used.
  Parallelization: Wave 2 | Blocked by: 3, 4, 5 | Blocks: 7, 8
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray/Views/UsagePopup.xaml:545`, `ClaudeUsageTray/Views/UsagePopup.xaml:734`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1009`, `ClaudeUsageTray/ViewModels/MainViewModel.cs:1121`, `ClaudeUsageTray/Services/LocalizationService.cs:121`.
  Acceptance criteria (agent-executable): Localization tests assert every new visible string exists in ko/zh/ja/en; UI binding tests assert source label updates after merged refresh.
  QA scenarios (name the exact tool + invocation): happy: popup shows merged-device note with no overlapping text at desktop and laptop-sized work areas; failure: when sync folder disappears, popup returns to local-only note and does not throw. Evidence `.omo/evidence/task-6-multi-pc-usage-sync.png`.
  Commit: Y | feat(ui): show multi-pc sync provenance

- [ ] 7. Add focused automated coverage and update docs/changelog for implementation
  What to do / Must NOT do: Add xUnit coverage for snapshot schema, atomic write/read, stale filtering, corrupt filtering, merge by newest device snapshot, settings persistence, localization keys, and refresh fallback. Update README issue #78 when implementation starts/finishes and add CHANGELOG/version bump only when app code is actually implemented. Must NOT weaken existing tests to fit new behavior.
  Parallelization: Wave 3 | Blocked by: 4, 5, 6 | Blocks: 8
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray.Tests/Services/HistoryServiceTests.cs:5`, `ClaudeUsageTray.Tests/ViewModels/MainViewModelIntegrationTests.cs:37`, `AGENTS.md` release-cycle rules.
  Acceptance criteria (agent-executable): `dotnet test ClaudeUsageTray.Tests/ClaudeUsageTray.Tests.csproj` passes with 0 failed tests; `dotnet build ClaudeUsageTray/ClaudeUsageTray.csproj -c Release --nologo` exits 0.
  QA scenarios (name the exact tool + invocation): happy: full test/build logs stored at `.omo/evidence/task-7-multi-pc-usage-sync.txt`; failure: intentionally stale/corrupt snapshot tests prove fallback paths.
  Commit: Y | test(sync): cover multi-pc snapshot merge

- [ ] 8. Run real two-device simulation before release
  What to do / Must NOT do: Use two temp device folders or two seeded `deviceId` snapshots to simulate desktop/laptop. Launch the WPF app, enable sync, verify settings persistence, merged totals, remote quota fallback, stale fallback, and disabled-sync local-only behavior. If app code changed, follow project release cycle: build with 0 warnings/errors, bump version, update `CHANGELOG.md`, commit, tag `v{x.y.z}`, push master and tag, and verify GitHub Release asset. Must NOT claim completion from tests alone; the popup/settings surface must be observed.
  Parallelization: Wave 3 | Blocked by: 7 | Blocks: final verification
  References (executor has NO interview context - be exhaustive): `ClaudeUsageTray/ViewModels/MainViewModel.cs:677`, `ClaudeUsageTray/Views/SettingsWindow.xaml.cs:36`, `ClaudeUsageTray/Views/UsagePopup.xaml.cs`, `AGENTS.md` release-cycle rules.
  Acceptance criteria (agent-executable): Evidence includes screenshot(s), sync folder fixture listing, full build/test logs, and release verification if implementation commit changes app code.
  QA scenarios (name the exact tool + invocation): happy: app displays fresh remote quota plus `2 PCs merged`; failure: sync folder deleted while app runs leaves local-only display and no unhandled exception. Evidence `.omo/evidence/task-8-multi-pc-usage-sync.md`.
  Commit: Y | chore(release): release multi-pc usage sync

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit: verify every requirement from GitHub #78 and this plan maps to implemented code/tests/evidence; reject if credentials/raw logs can sync or sync is on by default.
- [ ] F2. Code quality review: inspect new service boundaries, settings persistence, exception handling, atomic write, stale/corrupt filtering, and localization; reject if `UsageSyncService` leaks into unrelated weather/update/notification behavior.
- [ ] F3. Real manual QA: run the WPF settings and popup scenario with a seeded second-device snapshot; record screenshots and exact sync fixture.
- [ ] F4. Scope fidelity: confirm no unrelated refactors, no version/release skipped after app code implementation, and no user/unrelated worktree changes were included.

## Commit strategy
- Planning-only commit now: `docs: plan multi-pc usage sync`.
- Future implementation should prefer atomic commits by behavior:
  - `feat(sync): add usage snapshot data contract`
  - `feat(sync): add usage sync service`
  - `feat(settings): expose multi-pc sync options`
  - `feat(sync): merge shared usage snapshots`
  - `test(sync): cover multi-pc snapshot merge`
  - release/version commit only after implementation verification, per project rules.
- Do not include unrelated dirty files. Stage by path/hunk and inspect staged diff before each commit.

## Success criteria
- Sync is off by default and can be enabled from Settings with a shared folder path.
- No credential, raw log, or local DB content is written to the sync folder.
- Fresh remote API quota snapshots can be used when local API fails, with clear provenance.
- PC-local daily token totals are summed across devices without double-counting same-device snapshots.
- Stale/corrupt/missing sync data never crashes refresh and never silently overrides fresher local success.
- New visible text exists in ko/zh/ja/en.
- Automated tests and Release build pass.
- WPF settings/popup manual QA is recorded.
- If implementation changes app code, project version, changelog, tag, push, and GitHub Release asset verification are completed.
