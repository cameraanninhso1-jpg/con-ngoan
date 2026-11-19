# Telegram – thông báo & điều khiển từ xa (tùy chọn)

## Cài đặt (5 phút)
1. Trên điện thoại mở Telegram, tìm **@BotFather** → gõ `/newbot` → đặt tên → nhận **Bot Token** (dạng `123456789:ABC...`).
2. Tìm **@userinfobot** → bấm Start → ghi lại **Id** (số dương) – đây là Chat ID của bạn.
3. Mở bot vừa tạo → bấm **Start** (bắt buộc, nếu không bot không nhắn được cho bạn).
4. Trên máy tính: Con Ngoan › **Bảo mật & gỡ app** › bật **Thông báo & điều khiển từ xa qua Telegram**, dán Token + Chat ID
   → **Lưu cấu hình Telegram** → **Gửi tin nhắn thử nghiệm**.

Không dùng Chat ID của nhóm: bot chỉ nhận lệnh trong tin nhắn riêng với đúng tài khoản của bạn.
Bảo vệ tài khoản Telegram bằng mật khẩu 2 lớp – ai đọc được tin nhắn của bạn sẽ điều khiển được máy của con.

## Lệnh
| Lệnh | Tác dụng |
|---|---|
| `/status` | Thời gian đã dùng / còn lại hôm nay của từng tài khoản con |
| `/cho 30` | Cấp thêm 30 phút hôm nay (5–240) |
| `/tuchoi` | Từ chối các yêu cầu xin giờ đang chờ |
| `/khoa` | Đưa máy của con về màn hình đăng nhập |
| `/muonmay 30` | Tạm tắt toàn bộ bảo vệ 30 phút (phải bấm **Xác nhận**), hết giờ tự bật lại |
| `/bat` | Bật lại bảo vệ ngay |

Khi con bấm **Xin thêm thời gian**, bạn nhận tin kèm nút: +số phút con xin, +15/+30/+60 hoặc Từ chối. Mỗi yêu cầu chỉ duyệt được một lần.
Báo cáo cuối ngày gửi lúc 21:00 (đổi được) nếu bật.

## Dữ liệu gửi đi
Tên tài khoản của con, tên máy, số phút sử dụng, lý do xin giờ, số lần ứng dụng bị chặn. Không chụp màn hình, không gửi nội dung web/tin nhắn/phím bấm.
