Phụ huynh hay nhờ team mình (Cao Minh Computer – làm dịch vụ IT ở Hà Nội) "chặn game cho con". Thay vì cài mỗi nhà một kiểu, tụi mình viết hẳn một phần mềm quản lý máy tính cho phụ huynh tên **Con Ngoan**: chạy trên Windows 10/11, miễn phí, mã nguồn mở (GPL v3). Bài này không quảng cáo tính năng mà kể lại **các quyết định kỹ thuật** – chỗ nào Windows hỗ trợ sẵn, chỗ nào phải tự làm, và những cái bẫy gặp phải. Hy vọng có ích cho ai cần đụng tới Windows Service, HOSTS, AppLocker hay registry của user khác.

> Mã nguồn: https://github.com/cameraanninhso1-jpg/con-ngoan

![Giao diện chặn web & game](https://caominhcomputer.com/wp-content/uploads/2026/11/con-ngoan-chan-web-game.webp)

# 1. Kiến trúc: 3 tiến trình, 2 Named Pipe

```
ConNgoan.exe (WPF, chạy với quyền admin)  ──pipe (ACL: Administrators)──►  ConNgoan.Service.exe (LocalSystem)
                                                                            ▲
ConNgoan.Agent.exe (khay hệ thống, trong phiên của con) ──pipe (ACL: user)──┘
                    SQLite ở %ProgramData%\ConNgoan (ACL: SYSTEM + Administrators)
```

- **Service (LocalSystem)** làm mọi việc cần quyền cao: ghi HOSTS, áp AppLocker qua CSP, đếm giờ, khóa phiên, cập nhật. Không mở cổng mạng nào.
- **Giao diện WPF** chỉ chạy khi phụ huynh mở, manifest `requireAdministrator`, đăng nhập bằng mật khẩu riêng (PBKDF2-SHA256 600k vòng).
- **Agent** chạy quyền user thường trong phiên của con: hiện thời gian còn lại, gửi số giây không thao tác (`GetLastInputInfo`), cho con "xin thêm giờ".

Vài chi tiết bảo mật của pipe:

- Tạo bằng `NamedPipeServerStreamAcl.Create(..., PipeOptions.FirstPipeInstance, ...)` để process khác không chiếm tên pipe trước được.
- Pipe quản trị chỉ cho `BUILTIN\Administrators` – token admin chưa nâng quyền có SID Administrators dạng *deny-only* nên bị từ chối tự nhiên.
- Phía client kiểm tra **process bên kia đúng là service của mình** (`GetNamedPipeServerProcessId` → so đường dẫn exe) trước khi tin dữ liệu; phía server lấy SID của client (`GetNamedPipeClientProcessId` + token) chứ không tin SID do client gửi lên.
- Thông điệp một dòng JSON, tối đa 4 KB, whitelist lệnh.

# 2. Chặn web: HOSTS – đơn giản nhưng phải "giữ" được

Chặn bằng file `HOSTS` (`0.0.0.0 tiktok.com`) có ưu điểm không cần driver, không cần proxy, áp cho mọi trình duyệt. Cái khó là giữ nó:

- Chỉ sửa **một khối có marker riêng**, nội dung ngoài khối giữ nguyên từng byte; sao lưu trước khi ghi lần đầu.
- `FileSystemWatcher` theo dõi file, gom sự kiện 2 giây rồi kiểm tra – ai sửa/xóa khối thì service ghi lại ngay; thêm một vòng kiểm tra định kỳ phòng khi lỡ sự kiện.
- Sau khi ghi gọi `DnsFlushResolverCache` (fallback `ipconfig /flushdns`).
- **Chặn theo phiên**: HOSTS là toàn máy, nên service theo dõi sự kiện phiên (`SERVICE_CONTROL_SESSIONCHANGE` qua `OnSessionChange`) và chỉ bật khối chặn khi phiên đang hoạt động là của tài khoản con. Bố mẹ chuyển sang tài khoản mình thì khối chặn được gỡ.

Bẫy lớn nhất là **DNS-over-HTTPS** trong trình duyệt: bật DoH là bỏ qua HOSTS. Cách xử lý là ghi policy `DnsOverHttpsMode=off` cho Chrome/Edge/Brave/Cốc Cốc và `DNSOverHTTPS\Enabled=0` cho Firefox – nhưng **chỉ vào hive của con** (`HKEY_USERS\<SID con>\Software\Policies\...`), không ghi HKLM để khỏi ảnh hưởng trình duyệt của bố mẹ. Cùng chỗ đó ép `ForceGoogleSafeSearch`, `ForceYouTubeRestrict`, khóa ẩn danh.

# 3. Ghi registry của user đang không đăng nhập

DisallowRun, policy trình duyệt, khóa Task Manager đều nằm trong HKCU của **con** – trong khi code chạy ở service hoặc ở phiên của bố mẹ. Cách làm:

1. Nếu con đang đăng nhập: hive đã nạp ở `HKEY_USERS\<SID>`, mở trực tiếp.
2. Nếu không: bật `SeBackupPrivilege` + `SeRestorePrivilege`, `RegLoadKey(HKEY_USERS, "ConNgoanTmp_<SID>", ...\NTUSER.DAT)`, sửa xong `RegUnLoadKey`.

Bẫy gặp phải khi test:

- **Tài khoản mới tạo chưa đăng nhập lần nào chưa có NTUSER.DAT.** Giải pháp: ghi nhớ SID "chờ hồ sơ", service áp lại khi thấy profile xuất hiện (lần đăng nhập đầu).
- **Hai tiến trình cùng nạp một hive** (giao diện và service) → process đến sau nhận lỗi. Thêm lock theo SID trong process và retry khi `RegLoadKey` thất bại.
- `RegUnLoadKey` thất bại nếu còn handle mở – nhớ `using` mọi `RegistryKey` và retry vài lần sau `GC.Collect()`.
- Lưu giá trị gốc kèm **kiểu** (`DWord:1`, `String:off`) để hoàn tác đúng – chuỗi "123" không được khôi phục thành DWORD.

# 4. Chặn ứng dụng: 3 lớp

| Lớp | Cơ chế | Chặn được | Hạn chế |
|---|---|---|---|
| DisallowRun | `Policies\Explorer\DisallowRun` trong hive của con | App mở qua Explorer/Start | Chỉ so tên file; đổi tên là qua |
| AppLocker | Policy qua **MDM Bridge WMI** (`MDM_AppLocker_ApplicationLaunchRestrictions01_EXE03`) chạy dưới SYSTEM | Cả file portable, theo SID của con | Cần Pro/Enterprise/Education, dịch vụ AppIDSvc |
| Kill ngay | WMI `Win32_ProcessStartTrace` → kiểm tra owner SID → `Process.Kill()` | Bổ sung cho Windows Home | Process đã chạy vài chục ms |

AppLocker qua CSP là phần tốn công nhất: phải dựng XML theo đúng schema, gom quy tắc vào **một grouping riêng** để gỡ cài đặt chỉ xóa đúng phần của mình, rồi đọc lại để xác nhận. Nó chỉ ghi được từ SYSTEM nên giao diện gửi lệnh qua pipe để service làm. Chế độ "nghiêm ngặt" thêm quy tắc chỉ cho chạy từ `Program Files`/`Windows` + thư mục được phép – chặn được game chép sẵn trên USB.

# 5. Đếm giờ: đừng tin đồng hồ hệ thống

- Đếm theo **phiên WTS** (`WTSEnumerateSessions`, trạng thái active + không khóa), không phải "máy đang bật".
- Dùng `Stopwatch` (đồng hồ đơn điệu) và giới hạn mỗi nhịp tối đa 75 giây; bỏ khoảng sleep/hibernate (bắt sự kiện resume của service). Con chỉnh đồng hồ hay đổi múi giờ không làm sai số phút.
- Trừ thời gian không thao tác do Agent báo về (bỏ qua nếu có app giữ màn hình như xem video).
- Hết giờ: cảnh báo bằng `WTSSendMessage` từ service, chờ ân hạn rồi `WTSDisconnectSession` hoặc đăng xuất.

![Giới hạn thời gian theo ngày](https://caominhcomputer.com/wp-content/uploads/2026/11/con-ngoan-gioi-han-thoi-gian.webp)

# 6. Cập nhật tự động có chữ ký – không cần mua chứng chỉ

Phần mềm chạy LocalSystem trên máy khách nên kênh cập nhật là mục tiêu ngon nhất. Thiết kế:

- `version.json` (phiên bản, URL, SHA-256, size) được ký **ECDSA P-256**; khóa công khai nhúng trong app, khóa bí mật không bao giờ nằm trong repo.
- Service tải manifest + chữ ký, xác minh, **từ chối hạ cấp**, tải bộ cài vào thư mục chỉ SYSTEM/Admin ghi được (tránh TOCTOU), kiểm size + SHA-256, rồi chạy Inno Setup `/VERYSILENT`. Bọc bằng `cmd /c "setup ... & sc start <service>"` để service luôn được bật lại kể cả khi cài lỗi; lần khởi động sau ghi nhật ký thành công/thất bại.
- Test end-to-end trong Windows Sandbox: manifest bị sửa 1 byte → từ chối; manifest hợp lệ nhưng bộ cài sai hash → không cài; bản hợp lệ → cài và service tự khởi động lại.

Lưu ý: chữ ký này chỉ bảo vệ **kênh cập nhật**; còn cảnh báo SmartScreen "Unknown publisher" khi cài lần đầu thì vẫn cần chứng chỉ Authenticode.

# 7. Kiểm thử: Windows Sandbox là bạn

Phần lớn code đụng tới HOSTS, registry, AppLocker, tài khoản Windows – không thể chạy trên máy dev. Mỗi lượt test, một file `.wsb` mở Windows Sandbox, map thư mục build vào, chạy script: cài bằng bộ cài thật → chạy xUnit end-to-end (tạo tài khoản con, `Process.Start` với `UserName/Password` + `LoadUserProfile` để tạo profile, kiểm DisallowRun/AppLocker/HOSTS thật) → tự thao tác UI bằng UI Automation và chụp màn hình → gỡ cài đặt và kiểm máy sạch. Sandbox bị hủy sau mỗi lần nên không lo bẩn máy.

# Kết

Một vài điều rút ra:

- Windows có sẵn khá nhiều "đồ chơi" (HOSTS, policy, AppLocker, WTS) – phần khó là **gỡ sạch** và **không ảnh hưởng tài khoản khác**.
- Viết cho trẻ con thì quyền riêng tư phải đặt lên đầu: Con Ngoan không chụp màn hình, không keylog, không telemetry, dữ liệu nằm local.
- Không chặn được 100% (điện thoại phát Wi-Fi + VPN là chịu), phần mềm chỉ hỗ trợ bố mẹ.

Repo còn nhiều chỗ cần cải thiện (giao diện tiếng Anh, Authenticode signing, chặn theo DNS thay vì HOSTS…). Anh em quan tâm cứ mở issue/PR, hoặc để lại comment góp ý. Nếu thấy hữu ích thì cho dự án xin một star trên GitHub nhé!

- Mã nguồn: https://github.com/cameraanninhso1-jpg/con-ngoan
- Trang tải & hướng dẫn: https://caominhcomputer.com/phan-mem-con-ngoan/
