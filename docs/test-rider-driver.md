# Kịch bản Rider đặt xe và Driver mô phỏng

Web chỉ dành cho Rider. Driver được điều khiển qua API/Postman hoặc công cụ cục bộ.
`SIMULATION_AUTO_ACCEPT_DRIVERS=true` chỉ tự nhận lời mời; không tự thực hiện toàn bộ
đón/trả và thu COD. Công cụ dưới đây thực hiện các bước đó qua API với đúng tài khoản Driver.

## Chuẩn bị

1. Mở Docker Desktop, chạy `powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 build`.
2. Chạy `powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 up`.
3. Mở http://localhost:3000, đăng nhập `rider001` bằng `RIDER_PASSWORD` trong `.env`.
4. Chọn hai địa chỉ gần nhau tại TP.HCM, chọn COD hoặc Wallet. Ví phải đủ tiền.
5. Tạo chuyến. Sao chép ID sau `#ride/` trên thanh địa chỉ.

## Chạy Driver cho chuyến trên web

Mở PowerShell tại thư mục dự án, thay ID trong lệnh bằng ID chuyến vừa tạo:

```powershell
py -3 scripts/drive-trip.py ID_CHUYEN --rider rider001 --speed 20
```

Giữ cửa sổ này chạy, quay về web bấm **Đồng ý giá**. Công cụ chờ xác nhận của Rider,
tự đăng nhập đúng Driver được ghép, nhận chuyến, bắt đầu đến đón, đợi xe tới nơi rồi
đón khách, đi đến điểm trả và trả khách. COD được Driver xác nhận thu giả lập; ví tự quyết toán.
Không nhập tài khoản Driver bất kỳ: chỉ Driver được ghép mới có lời mời và quyền nhận chuyến.

Chạy lệnh trước khi xác nhận giá để tránh lời mời hết hạn 60 giây. Nếu hết hạn và có báo giá
mới, Rider phải xác nhận lại. Công cụ theo Driver mới được ghép. Công cụ không tự chấp nhận giá.

## Kết quả cần thấy

- Rider: `AwaitingDriverAcceptance` → `DriverAccepted` → `EnRoute` → `PickedUp`
  → `PaymentPending` → `Paid`.
- Xe di chuyển trên tuyến; tốc độ 20× dành cho demo. Có thể dùng 1, 5 hoặc 10.
- Ví: giữ tiền trước lời mời; hoàn tất thì hết khoản giữ, Driver nhận đúng doanh thu.
- COD: chuyến hoàn tất sau xác nhận thu; không cộng doanh thu COD vào ví Driver.
- Terminal in `Paid` và thoát. `PaymentReview` cần xem lỗi thanh toán, không tự coi là thành công.
- Ctrl+C chỉ dừng công cụ Driver, không hủy chuyến. Có thể chạy lại lệnh cho cùng chuyến.

## Kiểm thử API độc lập

```powershell
py -3 scripts/check-simulation.py --rider rider098 --method Wallet
py -3 scripts/check-simulation.py --rider rider097 --method Cod
py -3 scripts/check-wallet-concurrency.py --rider rider094
py -3 scripts/check-matching-boundaries.py
```

Các script này tạo dữ liệu giả lập và lịch sử được giữ lại. Dùng Rider rảnh và đủ số dư.
Postman: import hai file trong `postman/`, điền mật khẩu cục bộ, chạy riêng folder
**7 - Sequential ride runner**; hướng dẫn đầy đủ tại [Postman guide](postman-guide.md).
