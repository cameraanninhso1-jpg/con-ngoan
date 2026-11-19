# Hướng dẫn build, cài đặt và cấu hình Con Ngoan

## 1. Build từ mã nguồn

### Yêu cầu
- Windows 10 (1809+) hoặc Windows 11, 64-bit
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) – `winget install Microsoft.DotNet.SDK.10`
- [Inno Setup 6](https://jrsoftware.org/isdl.php) để tạo file cài đặt – `winget install JRSoftware.InnoSetup`

### Lệnh
```powershell
cd Con Ngoan
dotnet restore
dotnet build CaoMinhComputer.sln -c Release
dotnet test  CaoMinhComputer.sln -c Release

# Hoặc dùng script:
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```
`publish.ps1` publish UI + Service (win-x64, self-contained) vào `artifacts\publish` rồi gọi `ISCC.exe` tạo
`artifacts\installer\CaoMinhComputer-Setup-x64.exe`.

### Self-contained hay framework-dependent?
| Kiểu | Ưu điểm | Nhược điểm |
|---|---|---|
| Self-contained (mặc định) | Máy đích không cần cài .NET | Thư mục cài 158,3 MB, installer 47,0 MB |
| Framework-dependent (`-FrameworkDependent`) | Thư mục cài 28,1 MB | Máy đích phải có **.NET 10 Desktop Runtime** x64 |

Số liệu đo trên bản 1.0.0 (win-x64, .NET SDK 10.0.404).

### Lưu ý: Smart App Control / ký số
Trên Windows 11 bật **Smart App Control**, Windows chặn mọi EXE/DLL chưa ký số và chưa có uy tín – kể cả bản bạn tự
build (lỗi `0x800711C7 An Application Control policy has blocked this file`). Khi đó `dotnet test` và chính ứng dụng
sẽ không chạy. Cách xử lý đúng: **ký số** bằng chứng thư Code Signing hợp lệ (mục 5), hoặc build/test trên máy/VM
không bật Smart App Control. Không nên tắt Smart App Control trên máy của người dùng cuối.

## 2. Cài đặt
1. Đăng nhập **tài khoản Windows của phụ huynh** (Administrator).
2. Chạy `CaoMinhComputer-Setup-x64.exe`, chấp nhận UAC.
3. Giữ tùy chọn *Tự khởi động dịch vụ bảo vệ cùng Windows*.
4. Bộ cài sẽ: chép file vào `C:\Program Files\ConNgoan`, tạo `C:\ProgramData\ConNgoan` (ACL chỉ SYSTEM +
   Administrators), cài và khởi động dịch vụ `CaoMinhComputerService`. Log cài đặt: `%TEMP%\Setup Log *.txt`.

## 3. Cấu hình lần đầu (khuyến nghị theo thứ tự)
1. Mở Con Ngoan → **tạo tài khoản quản trị** (khác mật khẩu Windows).
2. **Khóa cài đặt** → nếu máy chưa có tài khoản cho con, bấm **Tạo tài khoản cho con** (app tạo tài khoản Windows loại
   Standard User và tự đánh dấu là của con – không cần vào Settings của Windows). Nếu đã có sẵn: đánh dấu *Tài khoản của con*
   → bấm **Chuyển thành Standard User** nếu tài khoản con đang là Administrator → chọn *Yêu cầu mật khẩu*
   (hoặc *Tự động từ chối*) → **Lưu & áp dụng**.
3. **Chặn web & game** → chọn nền tảng, nhập tên miền khác, bật ứng dụng cần chặn → **Lưu & áp dụng**.
   Thông báo xanh = đã ghi và đọc lại xác nhận; đỏ = lỗi thật (ví dụ antivirus khóa HOSTS).
4. Đăng xuất tài khoản của con rồi đăng nhập lại để quyền mới có hiệu lực. Muốn chuyển nhanh giữa phụ huynh và con, bấm
   **Đổi người dùng** (thanh bên trái của app, hoặc menu khay hệ thống trong tài khoản con): Windows về màn hình đăng nhập,
   chọn tài khoản cần dùng; ứng dụng đang mở của tài khoản kia vẫn giữ nguyên. Tài khoản con mới tạo chưa đăng nhập lần nào
   thì chặn ứng dụng sẽ được dịch vụ tự áp dụng ở lần đăng nhập đầu tiên.

### Kiểm thử nhanh trên máy thật
- Mở `C:\Windows\System32\drivers\etc\hosts` bằng Notepad: thấy khối `# >>> Con Ngoan BEGIN ... END <<<`.
- `ping facebook.com` → trả về `0.0.0.0` / `::`.
- Xóa khối đó (bằng quyền admin) và lưu → sau ~2 giây dịch vụ khôi phục, nhật ký có dòng "đã khôi phục".
- Đăng nhập tài khoản con, mở ứng dụng bị chặn từ Start → Windows báo *"This operation has been cancelled due to restrictions"*.
- Từ tài khoản con, chạy một bộ cài cần quyền admin → UAC hỏi mật khẩu quản trị (hoặc bị từ chối).

## 4. Gỡ cài đặt
- Trong app: **Bảo mật & gỡ app → Gỡ cài đặt...** (cần mật khẩu quản trị), hoặc
- Windows **Settings → Apps → Con Ngoan → Uninstall** (cần quyền Administrator Windows).

Bộ gỡ sẽ: dừng và xóa dịch vụ; gỡ khối HOSTS; xóa các giá trị DisallowRun do app tạo; khôi phục
`ConsentPromptBehaviorUser` và `DisableUserInstalls` về giá trị gốc; hỏi có xóa `C:\ProgramData\ConNgoan` không.

### Gỡ thủ công (khi bộ gỡ hỏng)
```powershell
# Chạy PowerShell "Run as administrator"
sc.exe stop CaoMinhComputerService
& "C:\Program Files\ConNgoan\CaoMinhComputer.exe" --uninstall-cleanup
sc.exe delete CaoMinhComputerService
```
Nếu không còn file exe: mở HOSTS bằng Notepad (admin) và xóa đúng khối giữa hai dòng `Con Ngoan BEGIN/END`;
bản gốc có ở `C:\ProgramData\ConNgoan\backups\hosts-original.bak`.

## 5. Ký số khi phát hành
Không dùng chứng thư tự ký để "tạo tin cậy". Khi phát hành thật:
1. Mua chứng thư **Code Signing** (OV/EV) từ CA (DigiCert, Sectigo, GlobalSign…) hoặc dùng Azure Trusted Signing.
2. Ký các exe trong `artifacts\publish` và installer:
   ```powershell
   signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a artifacts\publish\Con Ngoan*.exe
   signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a artifacts\installer\CaoMinhComputer-Setup-x64.exe
   ```
3. Hoặc bỏ comment dòng `SignTool=` trong `installer\CaoMinhComputer.iss` để Inno Setup tự ký.

## Cập nhật phần mềm
Từ bản 1.5.0: **Giới thiệu › Cập nhật phần mềm** → **Kiểm tra cập nhật**. Có bản mới thì bấm **Cài phiên bản …**; app tự đóng, cài xong (1–2 phút) mở lại app. Chỉ bản có chữ ký số của Cao Minh mới được cài. Chi tiết và quy trình phát hành: [UPDATE.md](UPDATE.md).
