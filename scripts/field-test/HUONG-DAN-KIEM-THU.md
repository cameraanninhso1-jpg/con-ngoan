# Hướng dẫn kiểm thử Con Ngoan trên máy thật

Bộ này kiểm đúng những phần **chưa thử được trong Windows Sandbox**: Windows 10, con đăng nhập trực tiếp, hộp thoại UAC thật,
trình duyệt thật, sleep/khởi động lại. Ghi kết quả vào cột cuối (Đạt / Không đạt + mô tả) rồi gửi lại cùng thư mục `ket-qua-...`.

## Chuẩn bị (5 phút)
1. Chép cả thư mục này sang máy cần thử (USB hoặc thư mục chia sẻ).
2. Đăng nhập máy bằng **tài khoản phụ huynh** (Administrator). Nên đặt mật khẩu Windows mạnh cho tài khoản này –
   toàn bộ bảo vệ dựa vào việc con không biết mật khẩu Administrator.
3. Tài khoản cho con: từ bản 1.4.0 tạo ngay trong app ở bước B2 (không cần vào Settings của Windows).

## Phần A – Tự động (10 phút)
Chuột phải **CHAY-KIEM-THU.bat → Run as administrator**. Script cài app, kiểm tra dịch vụ, đo RAM/CPU, chụp màn hình,
rồi để cửa sổ Con Ngoan mở sẵn. Kết quả ở thư mục `ket-qua-<tên máy>-<thời gian>\tom-tat.txt`.

## Phần B – Cấu hình bằng tài khoản phụ huynh

| # | Thao tác | Kết quả mong đợi | Kết quả |
|---|---|---|---|
| B1 | Trong app: tạo tài khoản quản trị (mật khẩu ≥ 8 ký tự, có chữ và số) | Vào màn hình chính, sidebar có 8 mục | |
| B2 | **Khóa cài đặt**: bấm **Tạo tài khoản cho con**, tên `con`, đặt mật khẩu (hoặc để trống) → tài khoản hiện trong danh sách là *Standard User*, đã đánh dấu *Tài khoản của con*. Khi app hỏi *Đổi người dùng ngay?* chọn **Hủy** để cấu hình tiếp. (Nếu đã có sẵn tài khoản: đánh dấu *Tài khoản của con*; nếu là Administrator bấm *Chuyển thành Standard User*); chọn *Yêu cầu mật khẩu*; (Pro/Enterprise) bật *Chế độ nghiêm ngặt* → **Lưu & áp dụng** | Thông báo xanh; không có dòng báo lỗi đỏ | |
| B3 | **Chặn web & game**: tích Facebook, YouTube, Roblox; thêm `shopee.vn`; bật chặn `Discord.exe` (hoặc một game có trên máy) → **Lưu & áp dụng** | Thông báo xanh có "Đã áp dụng chặn ... tên miền" | |
| B4 | **Thời gian sử dụng**: bật giới hạn; hôm nay đặt **30 phút**; giữ *Khóa máy*, ân hạn 1 phút → **Lưu & áp dụng** | Thông báo xanh (không có cảnh báo "dịch vụ nền không phản hồi") | |
| B5 | **Giờ giải lao**: đặt một khung bắt đầu sau ~10 phút, dài 5 phút → **Lưu khung giờ** | Thông báo xanh | |

## Phần C – Đăng nhập bằng tài khoản của con (phần quan trọng nhất)
Trong app bấm **Đổi người dùng** (thanh bên trái, dưới tên quản trị) → **Đổi người dùng** → ở màn hình đăng nhập chọn `con`.
Lần đăng nhập đầu tiên Windows mất 1–2 phút để chuẩn bị tài khoản. Muốn quay lại phụ huynh: chuột phải biểu tượng
Con Ngoan ở khay hệ thống → *Đổi người dùng (để bố mẹ đăng nhập)*.

| # | Thao tác | Kết quả mong đợi | Kết quả |
|---|---|---|---|
| C0 | (Tài khoản `con` vừa tạo trong app) Mở **Nhật ký** sau khi quay lại phụ huynh | Có dòng "Tài khoản của con đã đăng nhập lần đầu: con – áp dụng chặn ứng dụng" | |
| C1 | Nhìn khay hệ thống (góc phải thanh tác vụ, có thể trong mũi tên ^) | Có biểu tượng Con Ngoan; rê chuột thấy "Còn ... phút" | |
| C2 | Mở Chrome/Edge/Cốc Cốc vào facebook.com, youtube.com, shopee.vn | Không vào được (lỗi kết nối/không tìm thấy trang) | |
| C3 | Mở Discord/game đã chặn từ Start menu | Windows báo bị chặn bởi quản trị viên / ứng dụng không mở | |
| C4 | (Pro/Enterprise + nghiêm ngặt) Tải một file .exe bất kỳ hoặc chép game portable ra Desktop rồi chạy | Bị chặn | |
| C5 | Chạy một bộ cài cần quyền admin (ví dụ bộ cài Chrome/7-Zip bản .exe) | Hiện hộp thoại UAC hỏi **tài khoản + mật khẩu Administrator** | |
| C6 | Chuột phải biểu tượng khay → *Xin thêm thời gian...* → 15 phút, ghi lý do → Gửi | Thông báo "Đã gửi yêu cầu" | |
| C7 | Để máy không chạm chuột/bàn phím 10 phút, rồi xem lại thời gian còn lại | Thời gian còn lại **không giảm** trong lúc rời máy | |
| C8 | Đến giờ giải lao (B5): mở lại facebook.com (đóng/mở lại trình duyệt) | Vào được trong giờ giải lao; hết giờ bị chặn lại, trình duyệt bị đóng sau cảnh báo | |
| C9 | Dùng tiếp đến khi hết 30 phút | Có cảnh báo "còn 5 phút", "còn 1 phút", rồi "hết thời gian... sẽ khóa sau 1 phút", sau đó về màn hình đăng nhập | |
| C10 | Đăng nhập lại `con` ngay | Mở lại được các ứng dụng đang dở (không mất bài); khoảng 30 giây sau lại bị khóa | |

## Phần D – Quay lại tài khoản phụ huynh
| # | Thao tác | Kết quả mong đợi | Kết quả |
|---|---|---|---|
| D1 | Mở app → **Thời gian sử dụng** | Thấy mục *Con đang xin thêm giờ* (C6); bấm **Đồng ý** → nhập mật khẩu quản trị | |
| D2 | Đăng nhập lại `con` | Dùng được thêm 15 phút; Agent báo "Đã được thêm giờ" | |
| D3 | **Nhật ký hoạt động** | Có các dòng: bắt đầu/kết thúc phiên, cảnh báo, đã khóa phiên, xin thêm giờ, giờ giải lao | |
| D4 | **Thời gian sử dụng → Báo cáo 7 ngày** | Có cột hôm nay và danh sách phiên của `con` | |

## Phần E – Sleep và khởi động lại
| # | Thao tác | Kết quả mong đợi | Kết quả |
|---|---|---|---|
| E1 | Khi `con` đang dùng: ghi lại thời gian còn lại → Sleep máy 10 phút → bật lại | Thời gian còn lại gần như không đổi (thời gian ngủ không bị tính) | |
| E2 | Khởi động lại máy, đăng nhập `con` | Agent tự hiện; thời gian đã dùng hôm nay vẫn giữ nguyên; các chặn vẫn còn | |
| E3 | (Phụ huynh) Mở Notepad bằng *Run as administrator*, xóa khối `ConNgoan` trong HOSTS, lưu | Vài giây sau khối chặn tự xuất hiện lại; Nhật ký có dòng "đã khôi phục" | |

## Phần F – Gỡ cài đặt
Chuột phải **GO-CAI-DAT-VA-KIEM-TRA.bat → Run as administrator** (hoặc *Settings → Apps → Con Ngoan → Uninstall*).
Sau đó đăng nhập `con`: facebook.com, Discord/game và file .exe tải về phải **dùng lại được bình thường**.

## Gửi lại
Gửi các thư mục `ket-qua-...` và bảng này (đã điền cột Kết quả). Nếu có lỗi, chụp màn hình thông báo lỗi.
