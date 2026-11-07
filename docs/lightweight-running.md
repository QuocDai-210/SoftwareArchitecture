# Chạy SwiftRide với ít tài nguyên

Chỉ chạy phần cần thiết. Các lệnh chọn service sẽ dừng container khác trong dự án và giữ
nguyên dữ liệu.

## Chạy riêng backend

Linux/macOS:

```bash
make trip       # hoặc matching, payment
```

Windows PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 trip
```

Compose tự chạy các dependency cần thiết. Chế độ này không bật Gateway/web, nên không dùng
được giao diện.

## Tiết kiệm thêm

- Observability tắt mặc định; chỉ bật khi cần:
  `docker compose --profile observability up -d --wait observability`
- SQL Server chỉ dùng khi chuyển dữ liệu Trip cũ.
- Dừng dự án bằng `make stop` hoặc `powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 stop`.
  Không dùng `docker compose down -v` nếu cần giữ dữ liệu.
- Chạy đầy đủ giao diện bằng `make up` hoặc `powershell -ExecutionPolicy Bypass -File .\scripts\manage.ps1 up`.

Chỉ kiểm tra nghiệp vụ/kiến trúc, không cần Docker:

```bash
./scripts/verify.sh --core
```

Giới hạn RAM cấu hình Compose có thể kiểm tra bằng `python3 -B scripts/check-resources.py`.
Đó là giới hạn tối đa của các container, không phải mức sử dụng thực tế. Chạy build và kiểm
thử nặng tuần tự để giảm cạnh tranh bộ nhớ.
