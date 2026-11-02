# Quy tắc làm việc trong SwiftRide

- Tổng RAM dành cho tiến trình dự án, build, kiểm thử và container không vượt 12 GB.
- Chỉ bật service cần cho công việc hiện tại và các dependency bắt buộc của service đó.
- Dừng service không dùng để giải phóng RAM; không xóa volume hoặc dữ liệu để giảm RAM.
- Observability chạy theo profile khi cần kiểm tra metrics, logs hoặc traces.
- Build và kiểm thử nặng chạy tuần tự; dừng container dự án trước khi build.
- Khi kết thúc kiểm thử, dừng service đã bật riêng cho kiểm thử. Chỉ giữ hệ thống chạy khi
  người dùng cần truy cập.
- Không dừng ứng dụng hoặc service ngoài dự án.

## Format code

- Áp dụng cho toàn bộ mã nguồn; chỉ đổi format, giữ nguyên logic và hành vi chương trình.
- Độ rộng mục tiêu là 100 ký tự, tính cả thụt lề.
- Mỗi câu lệnh một dòng; không dồn nhiều câu lệnh lên cùng dòng.
- Khối lệnh xuống dòng và thụt lề rõ cấu trúc; không gom thân hàm, vòng lặp hoặc nhánh điều
  kiện thành một dòng chỉ để rút ngắn code.
- Tham số, đối số, mảng và object trong `()`, `[]`, `{}` gom thành một dòng khi cả dòng
  không vượt 100 ký tự. Áp dụng tương tự cho thuộc tính JSX ngắn. Quy tắc này dành cho nhóm
  dữ liệu và biểu thức, không áp dụng cho khối lệnh.
- Khi nhóm dài vượt độ rộng mục tiêu, xuống dòng và thụt lề theo cấu trúc ngoặc; dấu đóng
  ngoặc căn theo cấu trúc lệnh.
- Không giữ nhóm ngắn trên nhiều dòng chỉ vì có dấu phẩy cuối.
- Giữ nguyên nội dung chuỗi, SQL, template và chú thích cần thiết; không ngắt nội dung
  literal để ép đủ độ rộng dòng.
- Dùng cấu hình formatter hiện có: `.prettierrc.json` cho frontend/JavaScript,
  `backend/.csharpierrc.json` và `backend/.editorconfig` cho C#, `pyproject.toml` cho Python.
- Format phần mã đã sửa trước khi bàn giao; không tạo bản sao, file log hoặc file tạm
  trong repo để phục vụ format.
