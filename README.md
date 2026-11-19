# Con Ngoan – phần mềm quản lý máy tính cho con, miễn phí

**Con Ngoan** giúp phụ huynh chặn game, mạng xã hội, web không phù hợp, giới hạn giờ dùng máy tính và khóa cài phần mềm lạ
trên máy Windows của con. **Miễn phí 100%, mã nguồn mở, không quảng cáo, không theo dõi con.**
Phát triển và tài trợ bởi **Cao Minh Computer** (CÔNG TY TNHH CAO MINH THĂNG LONG).

- **Tải về & hướng dẫn:** <https://caominhcomputer.com/phan-mem-con-ngoan/>
- **Bản phát hành:** [GitHub Releases](https://github.com/cameraanninhso1-jpg/con-ngoan/releases/latest)
- **Hỗ trợ:** hotline / Zalo 0936 203 209

> *Con Ngoan is a free, open-source parental-control app for Windows 10/11 (website & app blocking, screen-time limits,
> install lock, optional Telegram notifications). The UI and documentation are in Vietnamese.*

## Tính năng

| | |
|---|---|
| Chặn web & game | 25 nền tảng (mạng xã hội, game, web game, công cụ vượt rào, livestream) + tên miền tùy chỉnh, qua file HOSTS có tự phục hồi; chỉ chặn khi tài khoản của con đang dùng máy |
| Chặn ứng dụng | DisallowRun + AppLocker (Windows Pro/Enterprise/Education) + đóng ngay ứng dụng bị chặn khi vừa mở |
| Thời gian dùng máy | Giới hạn phút/ngày, khung giờ, lịch theo ngày, giờ giải lao, cảnh báo trước khi hết giờ, khóa/đăng xuất khi hết giờ, con xin thêm giờ |
| Bảo vệ trình duyệt của con | Tắt DNS-over-HTTPS, SafeSearch + YouTube hạn chế, khóa ẩn danh, (tùy chọn) chặn tiện ích – chỉ áp cho tài khoản con |
| Khóa cài đặt | Tạo tài khoản con (Standard User) ngay trong app, UAC yêu cầu mật khẩu phụ huynh, chặn MSI theo người dùng |
| Chống can thiệp | Khóa Task Manager/Regedit/CMD trên tài khoản con, tự mở lại tiện ích khay hệ thống |
| Telegram (tùy chọn) | Báo con xin giờ kèm nút duyệt, `/status`, `/cho`, `/khoa`, `/muonmay` (có xác nhận), báo cáo cuối ngày |
| Cập nhật | Bản cập nhật ký ECDSA P-256, kiểm tra SHA-256, không hạ cấp |
| Gỡ cài đặt | Khôi phục HOSTS, chính sách, AppLocker, tài khoản về như trước khi cài |

**Quyền riêng tư:** không chụp màn hình, không ghi phím, không đọc tin nhắn, không ghi lịch sử web. Dữ liệu lưu cục bộ
(`%ProgramData%\ConNgoan`, chỉ SYSTEM + Administrators đọc được). Chi tiết: [Chính sách quyền riêng tư](https://caominhcomputer.com/con-ngoan-chinh-sach-rieng-tu/)
và [docs/SECURITY.md](docs/SECURITY.md).

## Kiến trúc

```
┌─────────────────────────────┐  Named Pipe   ┌──────────────────────────────────┐
│ ConNgoan.exe (WPF, admin)   │◄─(ACL admin)─►│ ConNgoan.Service.exe (LocalSystem)│
│ giao diện phụ huynh         │               │ HOSTS, AppLocker, thời gian,      │
└─────────────┬───────────────┘               │ cập nhật, Telegram (tùy chọn)     │
              │                               └───────────────┬──────────────────┘
              ▼                                               │ Named Pipe (ACL user)
   SQLite %ProgramData%\ConNgoan (ACL SYSTEM + Administrators) ▼
                                              ConNgoan.Agent.exe – khay hệ thống trong phiên của con
```

| Thư mục | Vai trò |
|---|---|
| `src/CaoMinhComputer.Core` | Nghiệp vụ, HOSTS, DisallowRun, AppLocker, chính sách Windows, cập nhật có chữ ký, P/Invoke |
| `src/CaoMinhComputer.Data` | SQLite (migration theo `user_version`), repository, `ProtectionCoordinator` |
| `src/CaoMinhComputer.UI` | Giao diện WPF tiếng Việt → `ConNgoan.exe` |
| `src/CaoMinhComputer.Service` | Windows Service → `ConNgoan.Service.exe` |
| `src/CaoMinhComputer.Agent` | Tiện ích khay (WinForms) → `ConNgoan.Agent.exe` |
| `tests/` | xUnit: unit test (`Core.Tests`) và test tích hợp/đầu-cuối chạy trong Windows Sandbox (`IntegrationTests`) |
| `installer/` | Inno Setup → `ConNgoan-Setup-x64.exe` |

## Build

Yêu cầu: Windows 10/11 x64, **.NET SDK 10**, **Inno Setup 6** (để tạo bộ cài).

```powershell
dotnet test tests\CaoMinhComputer.Core.Tests -c Release            # unit test
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1       # publish self-contained + bộ cài
powershell -ExecutionPolicy Bypass -File scripts\sandbox.ps1 -Name Test   # test đầy đủ trong Windows Sandbox
```

Bản phát hành chính thức được build bằng GitHub Actions ([.github/workflows/release.yml](.github/workflows/release.yml)) và ký số qua SignPath.

Tài liệu: [cài đặt](docs/INSTALL.md) · [bảo mật & giới hạn](docs/SECURITY.md) · [xử lý sự cố](docs/TROUBLESHOOTING.md) ·
[cập nhật](docs/UPDATE.md) · [Telegram](docs/TELEGRAM.md) · [phát hành](docs/PHAT-HANH.md)

## Chính sách ký số (Code signing policy)

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- Chỉ file được build từ mã nguồn trong kho này bằng GitHub Actions mới được ký; không ký file build trên máy cá nhân.
- **Committers & reviewers:** thành viên của Cao Minh Computer có quyền ghi vào kho này ([danh sách](https://github.com/cameraanninhso1-jpg/con-ngoan/graphs/contributors)).
- **Approvers:** chủ kho (Cao Minh Computer) – phê duyệt từng yêu cầu ký cho bản phát hành.
- **Quyền riêng tư:** chương trình không gửi thông tin nào sang hệ thống mạng khác trừ khi người dùng chủ động bật:
  kiểm tra cập nhật (tải `version.json` từ caominhcomputer.com, có thể tắt) và Telegram (gửi tới bot Telegram của chính phụ huynh).
  Xem [Chính sách quyền riêng tư](https://caominhcomputer.com/con-ngoan-chinh-sach-rieng-tu/).

## Giấy phép

Mã nguồn: **GNU GPL v3** – xem [LICENSE](LICENSE). Tên “Con Ngoan”, “Cao Minh Computer” và logo **không** thuộc giấy phép này –
xem [TRADEMARK.md](TRADEMARK.md). Điều khoản sử dụng bản phát hành: <https://caominhcomputer.com/con-ngoan-dieu-khoan-su-dung/>.

Báo lỗi / góp ý: [GitHub Issues](https://github.com/cameraanninhso1-jpg/con-ngoan/issues) hoặc hotline 0936 203 209.
Lỗ hổng bảo mật: vui lòng báo riêng qua hotline/Zalo, không đăng công khai.
