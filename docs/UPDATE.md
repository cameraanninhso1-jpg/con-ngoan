# Cập nhật phần mềm (từ bản 1.5.0)

## Cách hoạt động trên máy phụ huynh
- Dịch vụ Con Ngoan (chạy quyền SYSTEM) tải `version.json` và `version.json.sig` mỗi ngày một lần
  (tắt được bằng công tắc *Tự kiểm tra mỗi ngày* trong **Giới thiệu › Cập nhật phần mềm**), hoặc khi phụ huynh bấm **Kiểm tra cập nhật**.
- Chỉ tin `version.json` có **chữ ký ECDSA P-256 hợp lệ** bằng khóa bí mật của Cao Minh. Khóa công khai nằm sẵn trong app
  (`UpdateKeys.Trusted` trong `src/CaoMinhComputer.Core/Services/UpdateService.cs`). File bị sửa hoặc ký bằng khóa khác → từ chối và ghi nhật ký lỗi.
- Chỉ nhận phiên bản **mới hơn** bản đang chạy (không hạ cấp).
- Khi có bản mới: thanh bên trái hiện *● Có bản mới x.y.z*, trang Giới thiệu hiện ghi chú phiên bản và nút **Cài phiên bản x.y.z**.
- Phụ huynh bấm cài → dịch vụ tải bộ cài vào `%ProgramData%\Con Ngoan\updates` (chỉ SYSTEM/Administrators ghi được),
  kiểm tra **kích thước + SHA-256** đúng như trong `version.json` đã ký, rồi cài im lặng. Cấu hình, nhật ký, chặn web/ứng dụng giữ nguyên.
  Sau khi cài (kể cả khi lỗi) dịch vụ luôn được khởi động lại; nhật ký ghi *Đã cập nhật thành công…* hoặc *KHÔNG thành công…*.
- Tài khoản của con không thể bật/tắt, hoãn hay giả mạo bản cập nhật.
- Dữ liệu gửi đi khi kiểm tra: chỉ là yêu cầu tải file (User-Agent `CaoMinhComputer-Updater/1.0`), không gửi thông tin máy hay con.

## Đặt file trên website
Mặc định app đọc: `https://caominhcomputer.com/con-ngoan/version.json` (và `version.json.sig` cùng thư mục).
Bộ cài có thể đặt ở bất kỳ link HTTPS nào (website, GitHub Releases…) – link ghi trong `version.json`.

```
https://caominhcomputer.com/con-ngoan/
    version.json
    version.json.sig
    CaoMinhComputer-Setup-1.5.1.exe     (hoặc link GitHub Releases)
```
Với WordPress: tải 3 file lên qua FTP/File Manager của hosting vào thư mục `cmc-update` ở gốc website
(không dùng thư viện Media vì WordPress đổi tên/đường dẫn file). Mở thử link `version.json` trên trình duyệt phải thấy nội dung JSON.

## Quy trình phát hành một bản mới (ví dụ 1.5.1)
1. Sửa `Version` trong `Directory.Build.props`, `MyAppVersion` trong `installer/CaoMinhComputer.iss`, `$Version` trong `scripts/publish.ps1`.
2. Build bộ cài:
   ```
   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
   ```
3. Đổi tên `artifacts\installer\CaoMinhComputer-Setup-x64.exe` thành `CaoMinhComputer-Setup-1.5.1.exe` và tải lên website.
4. Ký (khóa bí mật nằm ngoài thư mục mã nguồn):
   ```
   powershell -ExecutionPolicy Bypass -File scripts\cmc-release.ps1 sign -Key "<đường dẫn>\cmc-update-private.pem" `
       -Installer artifacts\installer\CaoMinhComputer-Setup-1.5.1.exe -Version 1.5.1 `
       -Url https://caominhcomputer.com/con-ngoan/CaoMinhComputer-Setup-1.5.1.exe `
       -Notes "Sửa lỗi ...; thêm ..." -Out artifacts\release-1.5.1
   ```
5. Kiểm tra lại rồi tải `version.json` + `version.json.sig` lên **sau cùng** (sau khi bộ cài đã lên website):
   ```
   powershell -ExecutionPolicy Bypass -File scripts\cmc-release.ps1 verify -Out artifacts\release-1.5.1 -Installer artifacts\installer\CaoMinhComputer-Setup-1.5.1.exe
   ```
6. Thử trên một máy của mình: Giới thiệu › **Kiểm tra cập nhật** → **Cài phiên bản 1.5.1** → mở **Nhật ký** xem dòng *Đã cập nhật thành công*.

Phát hành từ từ: đưa `version.json` mới lên sau khi máy của bạn và vài khách thân đã cài thử ổn (có thể cài tay bằng bộ cài trước).

## Khóa bí mật
- File `cmc-update-private.pem` – **không** đưa vào mã nguồn/GitHub, không gửi qua chat/email. Sao lưu ra 2 USB cất ở 2 nơi.
- Mất khóa: các máy đã cài sẽ không nhận bản cập nhật nào nữa (phải gửi bộ cài mới để khách tự cài lại).
- Lộ khóa: người có khóa có thể đẩy phần mềm giả tới máy khách. Khi đó tạo khóa mới (`cmc-release.ps1 keygen`),
  phát hành ngay một bản ký bằng khóa cũ có `UpdateKeys.Trusted` chỉ chứa khóa mới, rồi hủy khóa cũ.
- Khi build tự động bằng GitHub Actions: lưu nội dung khóa trong *Repository secrets*, không bao giờ in ra log.

## Kiểm thử
- Unit test: `tests/CaoMinhComputer.Core.Tests/UpdateTests.cs` (chữ ký đúng/sai, khóa lạ, dữ liệu thiếu, so phiên bản, SHA-256).
- Đầu-cuối trong Windows Sandbox: `scripts\prepare-update-e2e.ps1 -Key <khóa>` trên máy build, sau đó chạy `scripts\sandbox.ps1 -Name E2E-Update`.
  Test cài 1.4.0 rồi: (1) manifest bị sửa → từ chối; (2) bộ cài sai SHA-256 → không cài; (3) bản 1.4.0.99 hợp lệ → dịch vụ tự cài và khởi động lại.
  Có thể trỏ app tới feed khác bằng `HKLM\SOFTWARE\Con Ngoan\UpdateUrl` (chỉ quản trị ghi được; chữ ký vẫn bắt buộc).
