# Xử lý sự cố

> Cần hỗ trợ: **Hotline 0936.203.209** · <https://caominhcomputer.com/>

## Nơi xem log
| Nguồn | Vị trí |
|---|---|
| Nhật ký hoạt động | Trong app → *Nhật ký hoạt động* (có thể xuất CSV) |
| Lỗi kỹ thuật của dịch vụ | Event Viewer → Windows Logs → Application, nguồn `Con Ngoan` |
| Lỗi khi cài/gỡ (chế độ dòng lệnh) | `C:\ProgramData\ConNgoan\setup-error.log` |
| Log của bộ cài | `%TEMP%\Setup Log yyyy-mm-dd #nnn.txt` |
| Sao lưu HOSTS | `C:\ProgramData\ConNgoan\backups\` (`hosts-original.bak` = bản trước lần sửa đầu tiên) |

## Đã bấm "Lưu & áp dụng" nhưng vẫn vào được website
1. Đóng **hoàn toàn** trình duyệt rồi mở lại (trình duyệt giữ cache DNS và kết nối cũ).
2. Kiểm tra `ping facebook.com` trả về `0.0.0.0`. Nếu không: *Bảo mật & gỡ app → Kiểm tra ngay*.
3. Trình duyệt bật *Secure DNS* với nhà cung cấp tùy chỉnh có thể bỏ qua HOSTS – tắt trong cài đặt trình duyệt.
4. VPN/proxy/extension: xem docs/SECURITY.md.

## Báo lỗi "Không có quyền ghi file HOSTS" / "đọc lại không khớp"
Phần mềm diệt virus (Kaspersky, Bitdefender, Avast…) có thể bảo vệ HOSTS. Thêm ngoại lệ cho
`C:\Program Files\ConNgoan\CaoMinhComputer.exe` và `CaoMinhComputer.Service.exe` hoặc tắt "HOSTS protection".
Windows Defender đôi khi cảnh báo `SettingsModifier:Win32/HostsFileHijack` khi HOSTS chặn các tên miền lớn – chọn
*Allow on device* nếu chính bạn đã cấu hình chặn.

## Chặn ứng dụng chưa có hiệu lực
- Đã chọn *Tài khoản của con* ở mục *Khóa cài đặt* chưa? Tài khoản đó đã đăng nhập ít nhất một lần chưa?
- DisallowRun áp dụng từ lần mở ứng dụng tiếp theo qua Start/Desktop; một số trường hợp cần đăng xuất/đăng nhập lại.
- Ứng dụng mở qua launcher: bật *Tự động đóng ứng dụng bị chặn* (dịch vụ phải đang chạy).

## Giới hạn thời gian không có tác dụng
- Đã bật *Bật giới hạn thời gian* và bấm **Lưu & áp dụng** ở mục *Thời gian sử dụng* chưa?
- Đã đánh dấu *Tài khoản của con* ở mục *Khóa cài đặt* chưa? Thời gian chỉ tính cho các tài khoản này.
- Dịch vụ nền phải đang chạy (thông báo đỏ "dịch vụ nền không phản hồi" nghĩa là chưa cưỡng chế được).
- Thời gian chỉ tính khi con đăng nhập và không khóa màn hình; máy bật ở màn hình đăng nhập không bị tính.
- Con bị khóa nhưng cần dùng tiếp: phụ huynh mở app → *Thời gian sử dụng* → **+15/+30/+60 phút** hoặc *Tạm dừng giới hạn hôm nay*.

## Giờ giải lao không mở/chặn đúng giờ
Dịch vụ kiểm tra mỗi 30–60 giây nên có thể trễ tối đa 1 phút. Lịch tính theo múi giờ lúc phụ huynh lưu cấu hình
(hiển thị ở đầu trang *Thời gian sử dụng*). Đóng/mở lại trình duyệt nếu trang đã mở từ trước khi chặn lại.

## Con báo "ứng dụng bị chặn bởi quản trị viên" (AppLocker)
Do chế độ nghiêm ngặt hoặc danh sách chặn. Cách cho phép: cài ứng dụng bằng tài khoản phụ huynh vào Program Files, hoặc thêm
thư mục của ứng dụng vào *Khóa cài đặt → Thư mục được phép thêm*, rồi **Lưu & áp dụng**. Ứng dụng học trực tuyến cài theo
người dùng (Zoom, Teams, OneDrive) đã được cho phép mặc định.

## Tắt khẩn cấp AppLocker của Con Ngoan
Trong app: *Bảo mật & gỡ app → Tạm tắt bảo vệ*. Không mở được app: PowerShell (Run as administrator):
```powershell
& "C:\Program Files\ConNgoan\CaoMinhComputer.exe" --uninstall-cleanup
```
Lệnh này gỡ quy tắc AppLocker/HOSTS/DisallowRun do Con Ngoan tạo (không xóa cấu hình). Mở lại app và bật bảo vệ để áp dụng lại.
AppLocker do dịch vụ nền (SYSTEM) quản lý; nếu dịch vụ đã dừng, lệnh trên tự tạo tác vụ SYSTEM tạm thời để gỡ.

## "AppLocker: dịch vụ nền không phản hồi"
Giao diện không tự ghi được AppLocker (cần quyền SYSTEM) mà nhờ dịch vụ nền. Khởi động dịch vụ ở *Bảo mật & gỡ app* rồi
bấm **Lưu & áp dụng** lại.

## Không thấy biểu tượng Con Ngoan ở khay hệ thống của con
Agent chỉ hiện trong tài khoản đã đánh dấu là của con và khi dịch vụ nền đang chạy. Agent khởi động cùng Windows – sau khi cài
lần đầu, con cần đăng xuất/đăng nhập lại. Nếu con tắt Agent, thời gian vẫn được tính đầy đủ.

## Dịch vụ không chạy
```powershell
sc.exe query CaoMinhComputerService
sc.exe start CaoMinhComputerService
```
Xem Event Viewer. Nếu database hỏng: dừng dịch vụ, đổi tên `C:\ProgramData\ConNgoan\caominh.db` (cùng các file
`-wal`, `-shm`) rồi mở lại app (sẽ tạo database mới, cần thiết lập lại).

## Quên mật khẩu quản trị Con Ngoan
Đăng nhập tài khoản Windows Administrator → mở app → *Quên mật khẩu?* → *Đặt lại* → tạo mật khẩu mới.

## Quên cả mật khẩu Windows của phụ huynh
Dùng quy trình khôi phục tài khoản của Microsoft/Windows (tài khoản Microsoft: account.live.com/password/reset;
tài khoản cục bộ: câu hỏi bảo mật hoặc đĩa khôi phục). Con Ngoan không cung cấp cách vượt qua bảo mật Windows.

## Khôi phục HOSTS thủ công
Mở Notepad bằng *Run as administrator* → mở `C:\Windows\System32\drivers\etc\hosts` → xóa khối giữa
`# >>> Con Ngoan BEGIN` và `# <<< Con Ngoan END <<<` → lưu → `ipconfig /flushdns`.
Lưu ý: nếu dịch vụ đang chạy và bảo vệ chưa tạm tắt, khối chặn sẽ được khôi phục lại – hãy *Tạm tắt bảo vệ* trước.
