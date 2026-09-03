## v1.0.2

## 🚀 Changelog
## 🐛 Bug Fixes

- [infra] Fix yt-dlp format ordering for video quality (#231) — @maihcx
- [download] Improve progress reporting (#229) — @maihcx
- [core] Add hasVideo/hasAudio and improve yt-dlp parsing (#228) — @maihcx

## 🧹 Maintenance

- [download] Add duration to YtAnalyzeResult and parser (#230) — @maihcx


---

## v1.0.1

## 🚀 Changelog
## 🐛 Bug Fixes

- [ui] Ensure stable sorting with ID tie-breaker (#216) — @maihcx

## ⚡ Performance

- [core] Implement per-client download progress senders (#219) — @maihcx

## 🧹 Maintenance

- [core-download] Add atomic persistence and robust checkpoints (#222) — @maihcx
- [core-download-infra] Refactor download manager for async lifecycle (#221) — @maihcx
- [download] Remove global max-concurrent-downloads and UI (#220) — @maihcx
- [cfs-ipc] Refactor ConfluxService lifecycle and IPC scoping (#218) — @maihcx
- [arch-continue] Add shared settings IPC and file lease (#217) — @maihcx


---

## v1.0.0

## 🚀 Changelog
- [cfs-ipc] Introduce typed IPC protocol and refactor CFS (#207) — @maihcx
- [arch] Restructure source tree under src (#205) — @maihcx
- [arch] Break refactor: add Contracts, Downloads, Infrastructure (#204) — @maihcx

## 🚀 Features

- [installer] Add 'Delete user data' uninstall option (#203) — @maihcx

## 🧹 Maintenance

- [ui-installer-runner] Switch basic TextBlock to WPF UI TextBlock (#210) — @maihcx
- [arch] Refactor download runtime to DI (#209) — @maihcx
- [core-cfs-runner] Refactor Core IPC, Runner sessions and download flow (#208) — @maihcx
- [arch] Introduce contracts, IPC topology and app protocol (#206) — @maihcx
- [installer] Change default RunAtStartup to true (#202) — @maihcx
- [installer] Refactor WPF helper converters (#201) — @maihcx


---

## v0.13.0

## 🚀 Changelog
## 🚀 Features

- [platform] Add multi-arch Windows installers (x64 + ARM64) (#194) — @maihcx

## 🐛 Bug Fixes

- [builder] Add versioned x64 installer fallback (#199) — @maihcx
- [builder] Prioritize x64 installer release asset (#197) — @maihcx

## ⚡ Performance

- [platform] Enable ReadyToRun publishing in build script (#195) — @maihcx

## 🧹 Maintenance

- [rule] Migrate issue templates to GitHub forms (#193) — @maihcx
- [rule] Convert issue templates to GitHub forms (#192) — @maihcx
- [rule] Update bug report template for Windows (#191) — @maihcx
- [rule] Add pull request template (#190) — @maihcx
- [deps] Bump Microsoft.Extensions.Hosting to 10.0.11 (#189) — @maihcx


---

## v0.12.1

## 🚀 Changelog
## 🐛 Bug Fixes

- [docs] Fix GPL license header punctuation (#185) — @maihcx
- [installer] Stabilize CFS messaging and refresh flow (#180) — @maihcx
- [installer] Run automatic updates on startup (#178) — @maihcx
- [installer] Adjust install path warning copy (#177) — @maihcx

## 🧹 Maintenance

- [ui] Add auto-update setting support (#187) — @maihcx
- [core] Default auto-update to true; add GetValue overload (#186) — @maihcx
- [docs] Clarify browser extension split (#183) — @maihcx
- [docs] Add PDownloader installer artifact (#181) — @maihcx


---

## v0.12.0

## 🚀 Changelog
## 🚀 Features

- [core] Add core auto-update subsystem (#173) — @maihcx
- [cfs-ipc] Add SendAsync and update protocol types (#172) — @maihcx
- [installer] Support pending updates and silent installer launch (#171) — @maihcx
- [installer] Persist installer settings and locked install path (#170) — @maihcx
- [installer] Add per-user/all-users install scope with elevation (#169) — @maihcx
- [installer] Add browser extension install option (#168) — @maihcx
- [installer] Add silent installer support (#167) — @maihcx

## 🧹 Maintenance

- [ui] Refactor updater to Core-hosted UpdateHostService (#175) — @maihcx
- [tray] Refactor tray update handling & async calls (#174) — @maihcx


---

## v0.11.2

## 🚀 Changelog
## 🐛 Bug Fixes

- [core] Revert remove yt-dlp YouTube workaround | see #155 (#165) — @maihcx
- [ui] Add retry status label mapping (#162) — @maihcx
- [installer] Use explicit Chromium registration state (#161) — @maihcx
- [installer] Preserve Chromium extension registrations on update (#160) — @maihcx

## 🧹 Maintenance

- [runner] Add retry flow to downloader UI (#164) — @maihcx
- [runner] Add combined visibility converters (#163) — @maihcx


---

## v0.11.1

## 🚀 Changelog
## 🐛 Bug Fixes

- [core] Remove yt-dlp YouTube workaround | see #138 (#155) — @maihcx

## 🧹 Maintenance

- [installer] Remove Gecko (Firefox) extension registration (#158) — @maihcx
- [extension] Move BrowserExtension to https://github.com/maihcx/PDownloader-browser-ext (#157) — @maihcx


---

## v0.11.0

## 🚀 Changelog
## 🚀 Features

- [installer] Register Chromium extensions externally (#150) — @maihcx
- [extension] Add media analyze/download and quality UI (#147) — @maihcx

## 🐛 Bug Fixes

- [ui] Replace locale strings with resource keys (#140) — @maihcx

## 🧹 Maintenance

- [webpage] Revamp docs UI: theme, runner preview, i18n & a11y (#152) — @maihcx
- [webpage] Add browser extension dialog and assets (#151) — @maihcx
- [extension] improve media detection & add checks (#149) — @maihcx
- [extension] Add Spotify detection and protected-preview handling (#148) — @maihcx
- [installer] Refactor installer: DI host & services (#146) — @maihcx
- [extension] Support Firefox listed/unlisted builds (#145) — @maihcx
- [extension] Use Vite for BrowserExtension builds (#144) — @maihcx
- [installer-core] Update browser extension install/update handling (#143) — @maihcx


---

## v0.10.0

## 🚀 Changelog
## 🚀 Features

- Add automatic retry & Retrying status (#136) — @maihcx
- Add bulk download actions and CFS commands, the final patch for #130 (#133) — @maihcx
- Add downloads actions flyout and batch commands (#130) — @maihcx
- Add Or/And visibility converters; improve converters (#128) — @maihcx
- Add Messages dialog and extend messenger service (#127) — @maihcx

## 🐛 Bug Fixes

- Update YouTube player_client workaround | #122 (#138) — @maihcx
- Use DownloadRunner.DownloaderCFSRest in CFS handler | #133 (#134) — @maihcx
- Use DownloadStatus enum for status handling (#129) — @maihcx

## ⚡ Performance

- Update bundled FFmpeg binaries (#124) — @maihcx

## 🧹 Maintenance

- Refactor YouTube handler error handling and flow (#135) — @maihcx
- Add DownloadRunner; move runner logic from AppRuntime (#132) — @maihcx
- Set explicit x64 Release build settings (#131) — @maihcx
- Unify build/publish settings across csproj files (#126) — @maihcx
- Update yt-dlp executable (#125) — @maihcx


---

## v0.9.1

## 🚀 Changelog
## 🐛 Bug Fixes

- Workaround for YouTube download/analyze issue (#122) — @maihcx
- Validate direct HTTP formats before filesize (#121) — @maihcx
- fix sorting by download end time (#119) — @maihcx

## 🧹 Maintenance

- Update QuickJS runtime (#120) — @maihcx
- [extension] Remove rounded corners for popup (#117) — @maihcx
- Refactor theme manager (#116) — @maihcx
- Code modernization and cleanup (#115) — @maihcx


---

## v0.9.0

## 🚀 Changelog
## 🚀 Features

- Add configurable file merge modes (#112) — @maihcx

## 🐛 Bug Fixes

- [extension] Improve Instagram resolver (#111) — @maihcx

## 🧹 Maintenance

- Translate error messages to English (#113) — @maihcx


---

## v0.8.0

## 🚀 Changelog
## 🚀 Features

- Add progress window auto-close behavior (#106) — @maihcx
- Add configurable temporary folder setting (#104) — @maihcx
- [extension] [extension] Bump browser extension to v0.3.15 (#103) — @maihcx
- Add Vimeo handling and content inspection (#97) — @maihcx
- [extension] Bump browser extension to v0.3.14 (#96) — @maihcx

## 🐛 Bug Fixes

- [extension] remove TikTok override (#102) — @maihcx
- [extension] Vimeo video download support (#94) — @maihcx
- [extension] Fix button mounting for videos in different contexts (#93) — @maihcx

## 🧹 Maintenance

- Add margin to ConfigPage StackPanel header (#109) — @maihcx
- Update About page version text and layout (#108) — @maihcx
- Collapse all ConfigPage CardExpanders by default (#107) — @maihcx
- Remove header status card from ConfigPage (#105) — @maihcx
- Translate yt-dlp messages to English (#98) — @maihcx


---

## v0.7.1

## 🚀 Changelog
## 🐛 Bug Fixes

- [vulnerability] Add ShellProcessLauncher and use for OpenFile (#82) — @maihcx
- [vulnerability] Make NativeMethods internal and partial (#81) — @maihcx
- [vulnerability] Add unelevated process launcher for post-install (#80) — @maihcx

## 🧹 Maintenance

- Enable smooth scrolling for release notes (#79) — @maihcx
- Add bottom margin to SettingsPage StackPanel (#78) — @maihcx


---

## v0.7.0

## 🚀 Changelog
## 🚀 Features

- Improve cookie dedup and expand browser support (#74) — @maihcx
- [extension] improve cookie & tab context (#73) — @maihcx


---

## v0.6.0

## 🚀 Changelog
## 🚀 Features

- Add Zen Browser policy entry (#71) — @maihcx
- Add cross-browser support and extension signing for Firefox (#69) — @maihcx


---

## v0.5.0

## 🚀 Changelog
## 🚀 Features

- Add user-agent and structured cookie jar support (#65) — @maihcx
- [extension] Add media candidate registry and detection system (#64) — @maihcx
- Add file hash calculation for downloads (#63) — @maihcx
- Add merge progress recovery and pause support (#62) — @maihcx
- Add recoverable merge support for downloads (#61) — @maihcx

## 🐛 Bug Fixes

- Disable download controls during merging (#60) — @maihcx


---

## v0.4.1

## 🚀 Changelog
## 🧹 Maintenance

- Add merge progress tracking for download operations (#58) — @maihcx
- Replace StatusToColorConverter with dynamic resource styles (#57) — @maihcx
- Replace emoji icons with WinUI symbols (#56) — @maihcx


---

## v0.4.0

## 🚀 Changelog
## 🚀 Features

- Add per-thread download progress visualization (#50) — @maihcx

## 🐛 Bug Fixes

- Report progress when merge phase starts (#54) — @maihcx
- Add resolved URL tracking and improve range validation (#53) — @maihcx
- Pause active downloads when adding items (#52) — @maihcx

## 🧹 Maintenance

- Reduce ObjectCornerRadius to 6 (#51) — @maihcx


---

## v0.3.1

## 🚀 Changelog
## 🐛 Bug Fixes

- [extension] Intercept only downloads started since activation (#44) — @maihcx
- Improve progress reporting with stable total tracking (#43) — @maihcx
- Fix GitHub repository URL in About page (#42) — @maihcx
- Make disk space requirement dynamic (#41) — @maihcx


---

## v0.3.0

## 🚀 Changelog
## 🚀 Features

- Add security improvements for HTTP bridge and file handling (#34) — @maihcx

## 🐛 Bug Fixes

- Fix race conditions in download progress handling (#35) — @maihcx

## 🧹 Maintenance

- Move About UI to AboutPage and update localization (#39) — @maihcx
- [extension] Add version history and update notifications (#38) — @maihcx
- [extension] Bump browser extension version to 0.2.3 (#36) — @maihcx


---

## v0.2.1

## 🚀 Changelog
## 🐛 Bug Fixes

- [extension] Add Instagram support and improve pointer tracking (#31) — @maihcx
- Add HTTP headers support and source-aware cookies (#30) — @maihcx

## 🧹 Maintenance

- [extension] Add referer to ytdlp download message (#29) — @maihcx
- Reorder download progress display (#28) — @maihcx
- Normalize Vietnamese size terminology in sort labels (#27) — @maihcx


---

## v0.2.0

## 🚀 Changelog
## 🚀 Features

- Add search, sort, and detail view to download list viewer (#25) — @maihcx

## 🐛 Bug Fixes

- Fixed XAML warning with  (#23) — @maihcx
- [extension] Fix video detection with tiktok (#22) — @maihcx
- [extension] Improve video link detection (#21) — @maihcx
- Improve installer update cleanup and safety (#20) — @maihcx

## 🧹 Maintenance

- Adjust NavigationView padding in MainWindow (#24) — @maihcx


---

## v0.1.4

## 🚀 Changelog
## 🐛 Bug Fixes

- Fix progress monitoring race conditions and simplify updates (#18) — @maihcx


---

## v0.1.3

## 🚀 Changelog
## 🐛 Bug Fixes

- Implement graceful runner shutdown sequence (#13) — @maihcx

## 🧹 Maintenance

- Refactor yt-dlp & ffmpeg utilities into ExternalTools (#16) — @maihcx
- Refactor Download module into modular services and organized namespaces (#15) — @maihcx
- Move UserDataStore to Utils folder (#14) — @maihcx


---

## v0.1.2

## 🚀 Changelog
## 🐛 Bug Fixes

- Fix BlurEffect binding and reduce referral blur (#7) — @maihcx


---

## v0.1.1

## 🚀 Changelog
## 🐛 Bug Fixes

- Direct HLS download via yt-dlp process (#5) — @maihcx


---

## v0.1.0

## 🚀 Changelog
- First release build (#2) — @maihcx


---

