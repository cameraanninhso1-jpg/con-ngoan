# Đăng ký ký số miễn phí với SignPath Foundation

SignPath Foundation cấp chữ ký số **miễn phí** cho dự án mã nguồn mở. Sau khi được duyệt, mỗi lần phát hành (push tag `v*`)
GitHub Actions tự gửi file cho SignPath ký – khóa ký do SignPath giữ trong HSM, không ai phải giữ file chứng chỉ.
Windows sẽ hiện nhà phát hành **“SignPath Foundation”**.

## 1. Nộp hồ sơ

Trang đăng ký: <https://signpath.org/> → mục **Apply** (bạn tự điền và gửi, dùng email công ty).
Họ thường hỏi các thông tin dưới đây – trả lời bằng tiếng Anh, có thể dán nguyên văn:

| Field | Answer |
|---|---|
| Project name | Con Ngoan |
| Repository | https://github.com/cameraanninhso1-jpg/con-ngoan |
| Homepage | https://caominhcomputer.com/phan-mem-con-ngoan/ |
| License | GPL-3.0-only (OSI approved) – see LICENSE |
| Short description | Free, open-source parental-control app for Windows 10/11: website/app blocking via HOSTS, DisallowRun and AppLocker, screen-time limits, install lock, optional Telegram notifications for parents. No ads, no telemetry. |
| Maintainer / organisation | CÔNG TY TNHH CAO MINH THĂNG LONG (Cao Minh Computer), Hanoi, Vietnam – hotline +84 936 203 209 |
| Build system | GitHub Actions on `windows-latest` – `.github/workflows/release.yml` (dotnet publish + Inno Setup), triggered by version tags |
| Artifacts to sign | (1) `ConNgoan*.exe` / `ConNgoan*.dll` / `CaoMinhComputer.*.dll` (zip from the build), (2) `ConNgoan-Setup-x64.exe` (Inno Setup installer) |
| Why code signing | The installer is blocked by Smart App Control on new Windows 11 PCs and shows “Unknown publisher” in SmartScreen/UAC, which scares non-technical parents. |
| Transparency / privacy | The app is visible (tray icon in the child’s session, listed in Apps and services), takes no screenshots, logs no keystrokes or browsing history, stores data locally only; network access only for the opt-in update check and opt-in Telegram bot owned by the parent. Code signing policy and privacy statement are in README.md. |
| Release history | v1.6.0 published on GitHub Releases; VirusTotal 0/67 |

> Lưu ý: SignPath xét duyệt thủ công và có quyền từ chối (ví dụ dự án quá mới, ít người dùng, hoặc loại phần mềm “giám sát”).
> Hồ sơ nhấn mạnh tính minh bạch: phần mềm hiện rõ trên máy của con, không chụp màn hình, không thu thập dữ liệu.
> Nếu bị từ chối hoặc chờ lâu: phát hành tiếp bản chưa ký (đã có hướng dẫn vượt cảnh báo) và cân nhắc mua chứng chỉ OV.

## 2. Sau khi được duyệt – cấu hình (làm một lần)

1. Trên SignPath: tạo **Project** `con-ngoan`, liên kết **GitHub** repository (cài GitHub App của SignPath theo hướng dẫn của họ).
2. Tạo 2 **Artifact configuration**:
   - `app-files`: file zip chứa các `.exe`/`.dll` → ký Authenticode từng file bên trong;
   - `installer`: một file `.exe` → ký Authenticode.
3. Tạo **Signing policy** `release-signing` (dùng chứng chỉ release của SignPath Foundation), bật phê duyệt thủ công nếu họ yêu cầu.
4. Tạo **API token** (CI user) có quyền submit vào project.
5. Trên GitHub: *Settings › Secrets and variables › Actions*
   - **Secret** `SIGNPATH_API_TOKEN` = token ở bước 4
   - **Variables** `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` = `con-ngoan`, `SIGNPATH_POLICY_SLUG` = `release-signing`

## 3. Phát hành bản có chữ ký

1. Tăng `Version` trong `Directory.Build.props` (ví dụ 1.6.1), commit, push.
2. Tạo tag và push: `git tag v1.6.1` → `git push origin v1.6.1`.
3. GitHub Actions build → gửi SignPath ký file chương trình → tạo bộ cài → ký bộ cài → tạo GitHub Release.
   (Nếu policy cần phê duyệt: vào SignPath bấm **Approve** cho 2 yêu cầu ký.)
4. Ký `version.json` cho bản cập nhật như cũ (`scripts\cmc-release.ps1`, xem UPDATE.md) với link GitHub của đúng phiên bản.
5. Cập nhật số phiên bản, SHA-256 và link VirusTotal trên trang `phan-mem-con-ngoan`.
