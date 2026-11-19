# Phát hành Con Ngoan cho mọi người tải

Gồm 3 nơi:

| Nội dung | Ở đâu | Địa chỉ |
|---|---|---|
| Trang giới thiệu + nút tải | WordPress, trang slug `phan-mem-con-ngoan` | `https://caominhcomputer.com/phan-mem-con-ngoan/` |
| Bộ cài `ConNgoan-Setup-x64.exe` | **GitHub Releases** (khuyên dùng) hoặc thư mục `con-ngoan` trên hosting | xem bên dưới |
| `version.json`, `version.json.sig` (tự cập nhật), PDF hướng dẫn | Thư mục `con-ngoan` ở gốc website (qua FTP / File Manager) | `https://caominhcomputer.com/con-ngoan/...` |

> Trang WordPress dùng slug **`phan-mem-con-ngoan`**, không dùng `con-ngoan`, vì thư mục thật `/con-ngoan/` trên hosting
> (nơi app tìm `version.json`) sẽ “đè” lên trang WordPress cùng tên.

## A. Đưa bộ cài lên GitHub Releases (miễn phí, tải nhanh)

GitHub chỉ dùng để chứa file bộ cài – **không cần** đưa mã nguồn lên.

1. Tạo tài khoản tại <https://github.com/signup> (bạn tự tạo và tự đặt mật khẩu; nên bật xác minh 2 bước).
   Gợi ý tên tài khoản tổ chức: `caominhcomputer`.
2. Bấm **+** (góc trên phải) → **New repository**:
   - Repository name: `con-ngoan`
   - Chọn **Public** (để ai cũng tải được bộ cài; repo chỉ chứa README, không chứa mã nguồn)
   - Tích **Add a README file** → **Create repository**.
3. Sửa README (biểu tượng bút chì) – nội dung gợi ý:
   ```
   # Con Ngoan – phát triển bởi Cao Minh Computer
   Phần mềm miễn phí giúp phụ huynh quản lý máy tính Windows của con.
   Trang chính thức, hướng dẫn và hỗ trợ: https://caominhcomputer.com/phan-mem-con-ngoan/
   Hotline/Zalo: 0936 203 209
   ```
4. Vào tab **Releases** (cột phải) → **Create a new release**:
   - **Choose a tag** → gõ `v1.6.0` → *Create new tag*
   - **Release title**: `Con Ngoan 1.6.0`
   - **Description**: tóm tắt có gì mới + mã SHA-256 (in ở bước C)
   - Kéo thả file `ConNgoan-Setup-x64.exe` vào ô **Attach binaries**, chờ tải lên xong (53 MB)
   - Bấm **Publish release**.
5. Link tải **luôn trỏ tới bản mới nhất** (không đổi khi phát hành bản sau):
   ```
   https://github.com/cameraanninhso1-jpg/con-ngoan/releases/latest/download/ConNgoan-Setup-x64.exe
   ```
   Gửi link này cho Claude để gắn vào nút **Tải Con Ngoan** trên trang WordPress.

**Không dùng GitHub?** Tải `ConNgoan-Setup-x64.exe` vào thư mục `con-ngoan` trên hosting (bước B);
link tải là `https://caominhcomputer.com/con-ngoan/ConNgoan-Setup-x64.exe`. Hosting cần cho phép file 53 MB và đủ băng thông.

## B. Thư mục `con-ngoan` trên hosting

Dùng FTP (FileZilla) hoặc **File Manager** trong cPanel/DirectAdmin của hosting – **không** dùng thư viện Media của WordPress
(WordPress đổi đường dẫn file).

1. Mở thư mục gốc website (thường là `public_html`), tạo thư mục **`con-ngoan`**.
2. Tải lên:
   - `Huong-dan-su-dung-Con-Ngoan.pdf` (trong `docs/website/xuat-ban/`)
   - (khi phát hành bản cập nhật) `version.json` và `version.json.sig` – xem [UPDATE.md](UPDATE.md)
   - (nếu không dùng GitHub) `ConNgoan-Setup-x64.exe`
3. Mở thử `https://caominhcomputer.com/con-ngoan/Huong-dan-su-dung-Con-Ngoan.pdf` trên trình duyệt.

Bản đầu tiên (1.6.0) **chưa cần** `version.json`: app sẽ báo “máy chủ chưa có thông tin phiên bản mới”, đó là bình thường.

## C. Ảnh và trang WordPress

1. **Media → Add New** → tải 9 ảnh trong `docs/website/anh/` (giữ nguyên tên file `con-ngoan-....webp`).
2. Tạo 3 trang (**Pages → Add New**), để **Bản nháp**, Claude điền nội dung qua kết nối WordPress:

   | Tiêu đề | Slug |
   |---|---|
   | Con Ngoan – phần mềm quản lý máy tính cho con, miễn phí | `phan-mem-con-ngoan` |
   | Chính sách quyền riêng tư – Con Ngoan | `con-ngoan-chinh-sach-rieng-tu` |
   | Điều khoản sử dụng – Con Ngoan | `con-ngoan-dieu-khoan-su-dung` |

   Nội dung đã soạn sẵn ở `docs/website/xuat-ban/*.html` (dán vào chế độ **Code editor** nếu làm tay).
3. Xem trước bản nháp trên điện thoại và máy tính → **Publish**. Thêm trang vào menu website và trang chủ.

## D. Trước khi giới thiệu rộng rãi

- [ ] Gửi bộ cài cho Microsoft kiểm tra: <https://www.microsoft.com/en-us/wdsi/filesubmission> → *Software developer* →
      ghi chú “parental control software, modifies HOSTS for website blocking”. Giảm báo nhầm của Defender.
- [ ] Kiểm tra trên <https://www.virustotal.com>; nếu hãng nào báo nhầm, gửi báo cáo “false positive” cho hãng đó.
- [ ] Cài thử trên 10–20 máy của người quen (Win 10/11, Home/Pro) trong 1–2 tuần, gom góp ý qua Zalo.
- [ ] Quay video hướng dẫn 3–5 phút, đăng YouTube và nhúng vào trang `phan-mem-con-ngoan`.
- [ ] Nhờ người có chuyên môn pháp lý xem lại *Chính sách quyền riêng tư* và *Điều khoản sử dụng*.

## E. Mỗi lần ra bản mới

1. Build: `powershell -ExecutionPolicy Bypass -File scripts\publish.ps1` (sau khi tăng số phiên bản – xem UPDATE.md).
2. GitHub → Releases → **Draft a new release** với tag mới, đính kèm `ConNgoan-Setup-x64.exe`, **Publish**.
3. Ký `version.json` bằng `scripts\cmc-release.ps1` với `-Url` là link GitHub của **đúng phiên bản**:
   `https://github.com/cameraanninhso1-jpg/con-ngoan/releases/download/v1.6.1/ConNgoan-Setup-x64.exe`
4. Tải `version.json` + `version.json.sig` lên thư mục `con-ngoan` **sau cùng**. Máy khách tự thấy bản mới trong 24 giờ.
5. Cập nhật số phiên bản, ngày và SHA-256 trên trang `phan-mem-con-ngoan` (chạy `python docs/website/xuat-ban.py`).
