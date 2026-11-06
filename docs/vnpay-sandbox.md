# VNPAY sandbox mới

Mô-đun được viết mới, không dùng adapter/worker VNPAY cũ. Trang `/payments/sandbox` tạo giao
dịch thử kết nối, không cộng ví. Mục **Ví SwiftRide** (`/#wallet`) tạo yêu cầu `WalletTopUp`
và cộng số dư mô phỏng sau khi xác minh thanh toán. Xem [ví và nạp tiền](wallet-topup.md).
COD tiếp tục hoạt động riêng; thanh toán chuyến bằng ví, doanh thu Driver và rút tiền chưa bật.

## Cấu hình

Điền `.env` (không commit hoặc chụp màn hình secret):

```dotenv
VNPAY_SANDBOX_ENABLED=true
VNPAY_TMN_CODE=<merchant cấp bởi VNPAY>
VNPAY_HASH_SECRET=<secret cấp bởi VNPAY>
VNPAY_RETURN_URL=http://localhost:3000/payments/return
VNPAY_CLIENT_IP=127.0.0.1
```

Adapter cố định host sandbox để tránh gửi nhầm sang production. Biến `VNPAY_URL` cũ không
được sử dụng. `VNPAY_CLIENT_IP` là địa chỉ thử nghiệm được cấu hình rõ ràng; khi mở cho người
dùng thật phải lấy IP khách qua chuỗi proxy tin cậy, không tin trực tiếp header từ Internet.

Return URL localhost dành cho trình duyệt đang chạy trên máy phát triển. IPN phải là HTTPS
công khai: `<địa chỉ tunnel trỏ tới localhost:3000>/api/vnpay/ipn`. Đăng ký IPN này với VNPAY.
Quick Tunnel đổi địa chỉ sau khi khởi động lại thì cần cập nhật IPN. Không đổi `AUTH_URL`
hoặc Keycloak chỉ để nhận IPN. Không cần đăng nhập để nhận callback; Payment Service kiểm tra
chữ ký HMAC-SHA512, merchant, mã giao dịch và số tiền trước khi ghi nhận.

Với PowerShell, build theo thứ tự để giữ RAM trong giới hạn:

```powershell
docker compose --profile "*" stop
docker compose build payment
docker compose build gateway
docker compose build web
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 up
```

Mở `http://localhost:3000/payments/sandbox`, đăng nhập bằng User, nhập từ 10.000 đến
5.000.000 VND. Đây là giao dịch test, sử dụng phương tiện thanh toán thử do VNPAY cung cấp.
Sau thanh toán, trang Return chỉ lấy mã giao dịch để đọc bản ghi đã xác thực quyền sở hữu.
Tham số thành công/thất bại trên URL không làm thay đổi trạng thái hoặc số dư.

## Cấu trúc và hành vi

- Domain: `SandboxPayment`, trạng thái Pending / Succeeded / Failed / NeedsReview.
- Application: `SandboxPayments`, ports `ISandboxStore` và `ISandboxVnpay`.
- Infrastructure/Sandbox: ký checkout, kiểm tra IPN/querydr, PostgreSQL repository, worker.
- API: `/payments/sandbox`, `/{reference}`, `/{reference}/reconcile`, `/payments/vnpay/ipn`.
- Frontend: feature payments, trang sandbox/return và public proxy `/api/vnpay/ipn`.
- Migration thêm bảng `SandboxPayments`, không xóa dữ liệu Payment, Wallet, Journal cũ.

Idempotency-Key gắn với chủ sở hữu và số tiền; retry cùng key nhận cùng reference. Callback
khóa giao dịch bằng PostgreSQL advisory transaction lock; unique index chặn trùng request và
provider transaction. IPN trả đúng `RspCode`/`Message`; `00` xử lý, `02` đã xác nhận.
Các kết quả chưa rõ không được coi là thành công. Chỉ status 00 và response 00 với mã giao dịch
hợp lệ mới được xác nhận. Status 02 là thất bại cuối cùng. Không tự thất bại chỉ vì hết hạn URL.

Worker chạy mỗi phút, lấy tối đa 20 bản ghi, tối đa 12 lần tự đối soát mỗi giao dịch; lần đầu sau
2 phút và giãn dần. Khi hết lượt, giao dịch chưa xác định vẫn được giữ để người dùng/Ops
kiểm tra thủ công. Khi mở trang kết quả, frontend yêu cầu backend đối soát ngay, độc lập với
lịch nền. Các yêu cầu tương tác và worker dùng chung khóa ngắn 15 giây để không gọi trùng.
Nút kiểm tra hiển thị thời gian chờ; không tiêu thụ số lượt đối soát tự động. Nếu VNPAY trả mã
94 (truy vấn trùng trong thời gian giới hạn), backend lùi lượt gọi tiếp theo 5 phút; IPN vẫn
được nhận ngay. Đây là giới hạn phía nhà cung cấp, không phải thời gian chờ bắt buộc mọi giao dịch.

Giao diện tiếp tục đọc trạng thái mỗi 3 giây rồi giãn về 10 giây, không dừng ở mốc 2 phút.
Lỗi mạng tạm thời được thử lại; dừng khi có kết quả cuối cùng hoặc khi phiên đăng nhập/quyền
truy cập không hợp lệ. Polling chỉ đọc DB, không liên tục gọi VNPAY. Trường `LastIpnAt` được
lưu sau callback hợp lệ để phân biệt giao dịch nhận IPN với giao dịch chỉ được đối soát.
Query không tìm thấy, lỗi mạng, chữ ký sai không làm biến giao dịch thành thành công/thất bại.
Không giữ transaction DB khi gọi VNPAY. Không log payload hoặc URL có chữ ký.

## Kiểm thử

```powershell
dotnet test backend/tests/SwiftRide.Checks --filter "FullyQualifiedName~SandboxPaymentTests" -m:1
py -3 scripts/check-vnpay.py
node scripts/check-sandbox-polling.mjs
```

Lệnh thứ hai cần hệ thống đang chạy. Nó tạo fixture sandbox riêng, gửi callback được ký bởi
merchant local qua web → gateway → Payment Service, kiểm tra 8 callback đồng thời và xóa đúng
fixture của nó. Đây là kiểm thử nội bộ, không chứng minh VNPAY đã gửi IPN từ Internet.
Có thể dùng `--ipn-url https://<tunnel>/api/vnpay/ipn` để kiểm thử callback qua tunnel,
không cần mở trang Return. Kiểm thử polling dùng đồng hồ giả để mô phỏng IPN đến muộn.

## Đăng ký IPN phía VNPAY

URL website khi đăng ký merchant và Return URL không thay thế đăng ký IPN.
Trong [VNPAYGW SIT Testing](https://sandbox.vnpayment.vn/vnpaygw-sit-testing/ipn), đăng nhập
merchant, mở **Cấu hình IPN URL**, chọn đúng Terminal Code, bấm **Sửa**, nhập URL HTTPS kết thúc
bằng `/api/vnpay/ipn` rồi bấm **Cập nhật**. Thông báo “Cập nhật thành công!” xác nhận đã lưu.
Tiếp theo cần thử giao dịch để kiểm tra callback thực tế.

Nếu tài khoản không truy cập được phần cấu hình, dùng email đăng ký merchant gửi yêu cầu
hỗ trợ đến `kythuatctt@vnpay.vn`, địa chỉ
[hỗ trợ kỹ thuật chính thức](https://sandbox.vnpayment.vn/apis/contact/). Nội dung gợi ý:

```text
Tiêu đề: Yêu cầu cấu hình IPN cho merchant sandbox SwiftRide

Tôi đang tích hợp VNPAY sandbox cho đồ án SwiftRide.
vnp_TmnCode: <mã merchant trong .env>
IPN URL (GET): https://<tunnel đang chạy>/api/vnpay/ipn
Return URL: http://localhost:3000/payments/return

Nhờ bộ phận kỹ thuật hướng dẫn/cập nhật IPN URL và xác nhận khi cấu hình hoàn tất.
Endpoint đã trả JSON RspCode/Message và kiểm tra checksum theo phiên bản 2.1.0.
```

Không cần gửi HashSecret. Giữ tunnel hoạt động; nếu Quick Tunnel đổi địa chỉ, phải cập nhật
lại IPN. Phản hồi 97 khi mở URL trống là đúng vì chưa có tham số/chữ ký VNPAY.

Nghiệm thu ngoài hệ thống: hoàn thành một lần nạp từ mục Ví SwiftRide, xác nhận bản ghi backend
Succeeded và WalletCreditedAt, kiểm tra số dư/lịch sử ví, IPN public và trường hợp bỏ trang Return.
Giao dịch thử kết nối trước đây giữ nguyên Purpose=Sandbox và không được cộng ví hồi tố.

Tài liệu gốc:

- [Thanh toán và IPN](https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html)
- [QueryDR](https://sandbox.vnpayment.vn/apis/docs/truy-van-hoan-tien/querydr%26refund.html)
- [Phân biệt Return và IPN](https://sandbox.vnpayment.vn/apis/docs/faqs/)
