# Rút tiền mô phỏng

Tính năng rút tiền chỉ chạy trong môi trường demo. Ứng dụng không gọi ngân hàng và không gửi lệnh
chuyển tiền thật.

## Luồng

1. User hoặc Driver nhập số tiền muốn rút. Demo không thu thập thông tin ngân hàng.
2. Payment Service kiểm tra số dư khả dụng, tạo yêu cầu có `Idempotency-Key`, giữ tiền và ghi journal
   trong cùng transaction.
3. Ops mở **Duyệt rút tiền**.
4. **Duyệt & mô phỏng chi** chuyển số tiền đang giữ sang tài khoản sổ cái
   `SimulatedWithdrawalClearing` và đánh dấu yêu cầu `Completed`.
5. **Từ chối & trả tiền về ví** yêu cầu lý do, hoàn tiền từ khoản đang giữ về khả dụng và đánh dấu
   yêu cầu `Rejected`.

Số tiền rút mỗi yêu cầu từ 10.000 đến 5.000.000 VND. Quyết định được ghi một lần; thao tác lặp lại
không tạo thêm bút toán. Mọi thao tác giữ, chi mô phỏng và hoàn tiền đều xuất hiện trong lịch sử ví.

## API

- `POST /wallets/withdrawals` — User/Driver gửi yêu cầu.
- `GET /wallets/withdrawals?page=1` — xem lịch sử yêu cầu của chính mình.
- `GET /ops/wallet-withdrawals?page=1` — Ops xem yêu cầu chờ.
- `POST /ops/wallet-withdrawals/{id}/decision` — Ops duyệt hoặc từ chối.

Migration `202611060006_WalletWithdrawals` tạo bảng yêu cầu rút trong PayDB. Payment Service áp dụng
migration khi khởi động.
