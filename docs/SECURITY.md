# Bảo mật, giới hạn kỹ thuật và mô hình bảo vệ

## Mô hình tin cậy
- **Ranh giới bảo mật là tài khoản Windows.** Tài khoản của con phải là **Standard User**; phụ huynh giữ
  Administrator. Một tài khoản Administrator luôn có thể gỡ mọi phần mềm kiểm soát – không phần mềm hợp pháp nào
  thay đổi được điều đó.
- Giao diện yêu cầu UAC (`requireAdministrator`). Con dùng Standard User sẽ phải nhập mật khẩu Windows của phụ huynh
  mới mở được, sau đó còn phải qua đăng nhập quản trị riêng của Con Ngoan.
- Dịch vụ `CaoMinhComputerService` chạy LocalSystem vì phải sửa HOSTS (thuộc System32) và đọc chủ sở hữu tiến trình.
  Dịch vụ không mở cổng mạng, không nhận lệnh từ bên ngoài, chỉ đọc cấu hình trong database.

## Dữ liệu
| Mục | Bảo vệ |
|---|---|
| `C:\ProgramData\ConNgoan` | ACL được đặt lại: chỉ `SYSTEM` + `Administrators` (bỏ kế thừa → Users không đọc được) |
| Mật khẩu quản trị | PBKDF2-HMAC-SHA256, 600.000 vòng, salt 16 byte ngẫu nhiên, so sánh thời gian hằng |
| Đăng nhập sai | 5 lần → khóa 60 giây, tăng dần tới 16 phút; ghi nhật ký (không ghi mật khẩu) |
| Nhật ký | Không chứa mật khẩu/token, không ghi nội dung web, phím bấm, ảnh màn hình |
| Khôi phục quản trị | Không có mật khẩu mặc định/cửa sau. Người có quyền Administrator Windows bấm "Quên mật khẩu?" để đặt lại; cấu hình giữ nguyên; có ghi nhật ký |

## Chặn website theo tài khoản (1.3.0)
HOSTS áp dụng cho cả máy, nhưng Windows bản client (Home/Pro) chỉ có **một phiên người dùng hoạt động** tại một thời điểm
(chuyển người dùng hay Remote Desktop đều ngắt phiên kia). Dịch vụ nhận sự kiện đăng nhập/chuyển người dùng/khóa/mở khóa và:
- phiên **của con** đang hoạt động → ghi khối chặn vào HOSTS (trong vài giây, kiểm tra lại sau 5 giây);
- phiên **tài khoản khác** (phụ huynh) đang hoạt động → gỡ khối chặn;
- chưa ai đăng nhập hoặc không xác định được → chặn (an toàn mặc định);
- chưa đánh dấu tài khoản con, hoặc phụ huynh chọn "Mọi tài khoản" → luôn chặn.
Việc đổi trạng thái được ghi nhật ký ("Bật chặn website…/Tạm mở chặn website…"), không bị coi là HOSTS bị can thiệp.
Giới hạn: tiến trình chạy ngầm của phụ huynh trong phiên đã ngắt (ví dụ đồng bộ đám mây) cũng bị chặn các tên miền đó trong lúc con dùng máy.

## Tạo tài khoản con & đổi người dùng (1.4.0)
- Tài khoản tạo trong app là tài khoản cục bộ (NetUserAdd) loại **Standard User**, chỉ thuộc nhóm Users, mật khẩu không hết hạn.
  Mật khẩu chỉ chuyển thẳng cho Windows, không lưu và không ghi nhật ký. Cho phép để trống mật khẩu (trẻ nhỏ).
- Tài khoản chưa đăng nhập lần nào chưa có registry riêng nên chưa ghi được DisallowRun: app ghi nhớ và dịch vụ tự áp dụng ở lần
  đăng nhập đầu (kèm cập nhật AppLocker cho thư mục hồ sơ của con).
- "Đổi người dùng" chỉ ngắt phiên hiện tại về màn hình đăng nhập (như *Switch user* của Windows), không bao giờ tự đăng nhập
  vào tài khoản khác – Windows vẫn yêu cầu mật khẩu của tài khoản được chọn. Giao diện quản trị tự đăng xuất trước khi chuyển.

## Telegram (1.6.0, tùy chọn – mặc định TẮT)
- Chỉ hoạt động khi phụ huynh tự nhập Bot Token + Chat ID và bật. Token lưu trong database (thư mục chỉ SYSTEM/Administrators đọc được).
- Dữ liệu gửi qua máy chủ Telegram: tên tài khoản của con, tên máy, số phút đã dùng/còn lại, lý do xin giờ, số lần ứng dụng bị chặn.
  **Không chụp màn hình**, không gửi nội dung web, phím bấm hay tin nhắn của con.
- Chỉ nhận lệnh từ tin nhắn **riêng** với bot, đúng Chat ID của phụ huynh (tin trong nhóm bị bỏ qua, Chat ID nhóm không được chấp nhận).
- Lệnh tạm tắt bảo vệ (`/muonmay`) phải bấm xác nhận trong 3 phút, tối đa 240 phút, tự bật lại khi hết giờ (do dịch vụ thực hiện, không phụ thuộc Telegram).
- Duyệt xin giờ chỉ có hiệu lực một lần cho mỗi yêu cầu (bấm lại không cấp thêm). Mọi lệnh được ghi nhật ký với tiền tố [Telegram].
- Ai đọc được tin nhắn Telegram của phụ huynh hoặc có Bot Token thì điều khiển được: bảo vệ tài khoản Telegram bằng mật khẩu 2 lớp.

## Chính sách trình duyệt & chống can thiệp (1.6.0)
- Chỉ ghi vào registry riêng của tài khoản con (`HKEY_USERS\<SID con>\Software\Policies\...`), **không** ghi HKLM, nên trình duyệt của phụ huynh không bị ảnh hưởng.
  Bản thử nghiệm 1.8.0 (bên thứ ba) từng ghi HKLM – bản 1.6.0 tự gỡ các giá trị đó.
- Gồm: tắt DNS-over-HTTPS (chống lách HOSTS), SafeSearch Google/Bing + YouTube hạn chế, khóa ẩn danh; chặn tiện ích là tùy chọn
  (mặc định tắt vì cũng tắt tiện ích đã cài). Giá trị gốc được lưu để hoàn tác khi bỏ chọn tài khoản, tạm tắt hoặc gỡ cài đặt.
- Chống can thiệp: DisableTaskMgr, DisableRegistryTools, DisableCMD=2 (vẫn chạy file .bat) cho tài khoản con; danh sách tài khoản đã khóa được theo dõi để gỡ đúng.
- Giới hạn: trình duyệt portable/không hỗ trợ chính sách, VPN cài bằng quyền quản trị, hoặc DNS đổi ở router vẫn có thể lách – kết hợp chặn ứng dụng/AppLocker.

## Cập nhật phần mềm (1.5.0)
- Dịch vụ SYSTEM chỉ cài bản cập nhật khi `version.json` có chữ ký ECDSA P-256 hợp lệ (khóa công khai gắn trong app,
  khóa bí mật Cao Minh giữ ngoài mã nguồn) **và** bộ cài tải về khớp kích thước + SHA-256 trong manifest đã ký. HTTPS chỉ là lớp phụ.
- Không hạ cấp: chỉ nhận phiên bản mới hơn bản đang chạy, nên manifest cũ (dù ký hợp lệ) không dùng lại được.
- Bộ cài tải vào `%ProgramData%\Con Ngoan\updates` (ACL chỉ SYSTEM + Administrators) → tài khoản con không tráo được file giữa lúc kiểm tra và lúc chạy.
- Chỉ phụ huynh (qua giao diện quản trị, Named Pipe có ACL Administrators) mới yêu cầu cài; tài khoản con không bật/tắt được kiểm tra tự động.
- Giới hạn: ai có khóa bí mật sẽ phát hành được bản cập nhật cho mọi máy – xem mục "Khóa bí mật" trong docs/UPDATE.md.

## Chặn website bằng HOSTS – giới hạn
HOSTS được Windows DNS Client và hầu hết trình duyệt tôn trọng, nhưng **không** chặn được:
- VPN, proxy, Tor, extension đổi DNS;
- Trình duyệt/ứng dụng bật **DNS-over-HTTPS với máy chủ tùy chỉnh** (có thể bỏ qua HOSTS);
- Truy cập bằng địa chỉ IP trực tiếp, tên miền phụ chưa có trong danh sách (HOSTS không hỗ trợ `*`);
- Ứng dụng có IP cứng hoặc kết nối đã mở trước khi áp dụng (cần khởi động lại ứng dụng/trình duyệt).

Cách giảm rủi ro: tài khoản con là Standard User (không cài được VPN/extension cần quyền admin), dùng Group Policy
của trình duyệt để tắt DoH tùy chỉnh (giai đoạn sau), hoặc DNS lọc ở router.
Kiến trúc đã tách `IWebsiteBlockingProvider` để thay bằng provider **Windows Filtering Platform** hoặc
**DNS filtering có kiểm soát** ở phiên bản sau mà không đổi UI. Không viết driver kernel trong MVP.

Con Ngoan **không** sửa proxy, không cài chứng chỉ gốc, không giải mã HTTPS.

## AppLocker (Giai đoạn 3)
- Áp dụng qua **AppLocker CSP (MDM Bridge WMI)** – cách Intune dùng – bởi dịch vụ nền chạy SYSTEM; giao diện gửi lệnh
  `applocker` qua Named Pipe. Hỗ trợ Windows Pro, Enterprise, Education; Windows Home không có AppLocker.
  Đã kiểm chứng trong Windows Sandbox: ghi bằng `Set-AppLockerPolicy` (Local GPO) **không được thực thi**, còn CSP có hiệu lực
  ngay, kể cả với file con đã từng chạy.
- Toàn bộ quy tắc nằm trong nhóm CSP riêng `Con Ngoan`, tên quy tắc bắt đầu bằng `Con Ngoan:`, áp dụng cho
  **SID tài khoản con** (không bao giờ chặn Administrators/Everyone). App thêm một quy tắc "cho phép mọi người" để không chặn
  nhầm ai khác, trừ khi Group Policy của tổ chức đã có quy tắc AppLocker riêng (khi đó chỉ thêm quy tắc Deny cho con).
- Chặn ứng dụng: quy tắc `*\Ten.exe` – đổi tên file sẽ né được, nhưng **chế độ nghiêm ngặt** chặn mọi file ngoài
  `%WINDIR%`, `%PROGRAMFILES%` và thư mục được phép, nên file đổi tên/tải về/portable/USB đều bị chặn.
  Giới hạn đã biết: một số thư mục trong `%WINDIR%` mà người dùng ghi được (ví dụ `C:\Windows\Temp`) – giới hạn chung của AppLocker mặc định.
- Cần dịch vụ Application Identity (AppIDSvc): app bật bằng `appidtel start`, dịch vụ nền kiểm tra 10 phút/lần và tự bật lại.
- Gỡ/tạm tắt bảo vệ/gỡ cài đặt: xóa đúng nhóm CSP `Con Ngoan`. Bộ gỡ cài đặt làm việc này trước khi dừng dịch vụ;
  nếu dịch vụ đã dừng thì chạy `CaoMinhComputer.Service.exe --remove-applocker` một lần bằng tác vụ SYSTEM tạm thời.
  Khôi phục khẩn cấp (PowerShell admin): `& "C:\Program Files\ConNgoan\CaoMinhComputer.exe" --uninstall-cleanup`.

## Agent trong phiên của con
- Chạy với quyền người dùng thường, hiển thị công khai ở khay hệ thống; tự thoát trong tài khoản không phải của con.
- Chỉ gửi: số giây không thao tác và yêu cầu xin thêm giờ. Không ghi phím, không chụp màn hình, không đọc nội dung.
- Nếu con tắt Agent, dịch vụ mở lại Agent trong phiên của con (với biến môi trường của chính tài khoản con) – có thể tắt trong mục Bảo mật.
- Pipe `CaoMinhComputer.Agent.v1` cho phép người dùng đã đăng nhập kết nối, nhưng dịch vụ chỉ phục vụ khi tiến trình kết nối
  đúng là `CaoMinhComputer.Agent.exe` trong thư mục cài đặt (con không ghi được vào Program Files); SID/phiên lấy từ chính
  tiến trình đó, không tin dữ liệu tự khai. Luôn có sẵn một instance pipe để tiến trình khác không chiếm được tên.
- Con tắt Agent → không có báo cáo idle → thời gian được tính đầy đủ (an toàn cho phụ huynh), chỉ mất tính năng xin thêm giờ.
- "Màn hình đang được giữ sáng" (ES_DISPLAY_REQUIRED) là trạng thái của cả hệ thống: nếu bất kỳ ứng dụng nào trên máy đang giữ
  màn hình sáng (video, họp trực tuyến, một số game/launcher), thời gian rời máy vẫn được tính. Đây là chiều an toàn
  (tính thừa chứ không bỏ sót); đã quan sát được trong kiểm thử Sandbox.

## Chặn ứng dụng – giới hạn
- **DisallowRun** (mọi edition): chỉ chặn chương trình mở qua Explorer (Start, Desktop, File Explorer, Run).
  Chương trình được tiến trình khác khởi chạy (launcher, `Update.exe`, cmd) không bị chặn.
- **Tự đóng ứng dụng** (dịch vụ, 20 giây/lần): chỉ đóng tiến trình thuộc tài khoản của con, không đụng tiến trình
  hệ thống/bảo mật/trợ năng (danh sách `ProtectedProcesses`). Đây là lớp bổ sung, **không** phải cơ chế tin cậy tuyệt
  đối (đổi tên file exe sẽ né được).
- Mức tin cậy cao (chặn cả portable theo publisher/đường dẫn/hash) cần **AppLocker** (Enterprise/Education) hoặc
  **WDAC** – thuộc Giai đoạn 3. App hiển thị rõ edition và khả năng hỗ trợ.
- Không sửa file game, không inject DLL, không đọc dữ liệu riêng tư của ứng dụng.

## Khóa cài đặt – phân biệt rõ
| Tình huống | Cơ chế chặn |
|---|---|
| Bộ cài cần quyền Administrator (Program Files, driver, service) | UAC + Standard User: hỏi mật khẩu quản trị hoặc tự động từ chối (`ConsentPromptBehaviorUser` = 3 / 0) |
| Gói MSI cài theo người dùng | Chính sách `DisableUserInstalls` = 1 (tùy chọn) |
| Bộ cài per-user (vào AppData) / ứng dụng portable | UAC **không** chặn được. Dùng danh sách chặn ứng dụng; triệt để cần AppLocker/WDAC |
| Quyền quản trị tài khoản | Hỗ trợ chuyển Admin → Standard User; chặn hạ quyền tài khoản đang dùng hoặc admin cuối cùng |

Con Ngoan **không tắt** UAC, Windows Defender, Windows Update. Mọi giá trị registry thay đổi đều được lưu bản
gốc (bảng `system_changes`) và khôi phục khi gỡ cài đặt. Chính sách do phần mềm khác đặt sẽ không bị gỡ.

## Quản lý thời gian – cách hoạt động và giới hạn
- Dịch vụ dùng Windows Terminal Services API (WTS) để biết phiên nào của con đang mở và có khóa màn hình hay không;
  nhận sự kiện đăng nhập/khóa/mở khóa và sleep/resume nên phản ứng ngay, ngoài ra kiểm tra 30 giây/lần khi con đang dùng máy.
- Thời gian đếm bằng đồng hồ đơn điệu, tối đa 75 giây mỗi nhịp, bỏ khoảng máy ngủ → chỉnh đồng hồ không cộng/trừ giờ được.
  Đổi đồng hồ được ghi nhật ký. Lịch tính theo múi giờ phụ huynh đã lưu nên con đổi múi giờ (Standard User được phép) không né được lịch.
- Hết giờ: thông báo (WTSSendMessage) → ân hạn → **ngắt phiên** (về màn hình đăng nhập, ứng dụng vẫn giữ) hoặc **đăng xuất**
  nếu phụ huynh chọn. Không bao giờ tắt máy.
- Giới hạn: lúc con để máy mở mà không thao tác vẫn được tính (cần tiến trình trong phiên của con để đo idle – giai đoạn sau).
  Tài khoản con phải là Standard User, nếu không con có thể dừng dịch vụ.
- Giờ giải lao gỡ tạm thời khối HOSTS và DisallowRun rồi áp dụng lại khi hết giờ; khóa cài đặt (UAC/MSI) không bị ảnh hưởng.
  Tùy chọn đóng trình duyệt chỉ đóng tiến trình thuộc phiên của con.

## Minh bạch / không chống gỡ phá hoại
Không ẩn tiến trình, không rootkit, không driver, không keylogger, không chặn gỡ cài đặt. Phụ huynh luôn tắt được
bảo vệ (cần mật khẩu quản trị) và gỡ cài đặt bằng tài khoản Administrator.
