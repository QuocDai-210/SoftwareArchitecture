# Ví SwiftRide và nạp qua VNPAY sandbox

## Dùng thử

Đăng nhập User hoặc Driver, mở **Ví SwiftRide** trên thanh điều hướng, hoặc truy cập
`http://localhost:3000/#wallet`. User có thể nạp 10.000–5.000.000 VND qua VNPAY sandbox.
Driver xem ví và lịch sử, chưa có nút nạp theo phạm vi bản đầu đã thống nhất.

Ví hiển thị ba số dư: khả dụng, đang giữ và thu nhập chờ nhận. Tại mốc này chỉ chức năng nạp
được bật nên hai khoản sau bằng 0. Chưa bật giữ tiền chuyến, doanh thu Driver, rút mô phỏng
hoặc hoàn ví. Các số dư là tiền mô phỏng; COD vẫn thu tiền mặt và không cộng vào ví Driver.

Chọn **Nạp tiền vào ví**, nhập số tiền, hoàn tất VNPAY rồi xem trang kết quả. Backend xác minh
IPN hoặc querydr trước khi cộng ví. Link **Xem số dư và lịch sử ví** quay về ví. Nếu đóng trang
Return, IPN vẫn có thể hoàn tất nạp. Khi mở lại ví, xem **Yêu cầu nạp tiền** để theo dõi yêu cầu.

Trang `/payments/sandbox` còn dùng để thử kết nối độc lập. Giao dịch ở đó và các giao dịch thử
cũ không cộng tiền vào ví. Không cộng hồi tố dựa vào ảnh chụp, Return URL hoặc thông tin người
dùng gửi lên.

## Dữ liệu và tính nhất quán

- `WalletAccounts`: chủ ví là subject Keycloak; Available/Held/PendingEarnings là VND nguyên
  đồng, đều không âm. Ví chưa phát sinh được hiển thị số dư 0, tạo bản ghi khi ghi nhận tiền.
- `SandboxPayments.Purpose`: mặc định `Sandbox` cho dữ liệu cũ. Chỉ endpoint tạo nạp ví mới
  đặt `WalletTopUp`; client không được tự chọn mục đích hoặc chủ ví. `WalletCreditedAt` ghi
  thời điểm hoàn tất cộng ví.
- `WalletActivities`: lịch sử bất biến, khóa theo reference nạp, liên kết chủ ví và Journal.
- `Journals`: ghi kép Nợ VnpaySandboxClearing / Có Wallet:{OwnerId}:Available. Tổng Nợ = Có,
  PostgreSQL chặn cập nhật/xóa sổ cái và lịch sử ví.

Khóa theo payment reference trước, sau đó khóa theo chủ ví. IPN và querydr dùng cùng use case
xác nhận. Số dư, trạng thái thanh toán, lịch sử, journal và dấu cộng ví nằm trong một transaction
PostgreSQL; lỗi ở bất kỳ bước nào rollback toàn bộ. Unique reference, unique provider transaction
và unique journal ngăn ghi trùng. Hai giao dịch khác nhau cho cùng ví được tuần tự hóa theo chủ
ví, tránh mất một khoản cộng khi thao tác đồng thời.

Không thay đổi hoặc tự chuyển số dư từ bảng Wallets legacy. WalletAccounts là mô hình mới.

## API

| Endpoint                      | Quyền       | Nội dung                                         |
| ----------------------------- | ----------- | ------------------------------------------------ |
| GET `/wallets/me`             | User/Driver | Ba số dư của chính chủ, khả năng nạp             |
| GET `/wallets/history?page=1` | User/Driver | Lịch sử ví của chính chủ, 10 mục/trang           |
| GET `/wallets/topups?page=1`  | User/Driver | Yêu cầu nạp của chính chủ, 10 mục/trang          |
| POST `/wallets/topups`        | User        | `{ "amount": 100000 }`, bắt buộc Idempotency-Key |

Frontend gọi qua `/api/backend`, token chỉ giữ phía server. Ops/service không dùng endpoint
ví cá nhân để giả làm chủ ví. Cùng key và cùng amount nhận lại cùng reference; đổi amount hoặc
dùng lại key giữa sandbox test và top-up bị từ chối. Client lưu key khi lỗi mạng để retry an toàn.

## Kiểm thử

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-wallet.ps1
```

Script dừng container dự án trước khi build, tạo PostgreSQL tạm riêng với RAM tối đa 512 MB và
không mount volume dự án. Sau kiểm thử, script xóa đúng container tạm đó. Không xóa journal,
lịch sử hay ví trong PayDB của người dùng. Bật lại ứng dụng bằng `scripts/manage.ps1 up`.

Kiểm thử thật trên PostgreSQL bao gồm: tạo nạp cùng key đồng thời; đổi amount/mục đích;
16 callback cho hai giao dịch cùng ví; callback sai chữ ký/số tiền; sandbox test không cộng ví;
rollback khi ép lỗi ghi lịch sử; retry sau lỗi; IPN trùng querydr; phân quyền và phân trang;
không âm số dư; sổ cái cân bằng và không được sửa/xóa. Các test này không thay thế nghiệm thu
một lần nạp thật trên VNPAY sandbox bằng tài khoản User.
