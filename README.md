# PRN222 — ChatLab mở rộng

Ứng dụng chat TCP nhiều client, server Console và client WPF, C# / .NET 9.

## Các yêu cầu đã triển khai

- **Emoji nhiều màu:** 12 emoji trong bộ chọn, hiển thị màu trong bong bóng chat bằng vector WPF. Tin nhắn trên mạng vẫn là Unicode UTF-8. Emoji ngoài bộ này dùng cách hiển thị mặc định của font.
- **Gửi ảnh + preview:** chọn PNG/JPEG/GIF/BMP; xem trước ảnh đã chọn và tự tải ảnh nhận được để hiển thị trong chat. GIF hiển thị ảnh tĩnh. Ảnh tối đa 20 MB, preview giải mã ở chiều rộng 480 px.
- **File lớn từ 500 MB:** hỗ trợ đến 10 GB/file, kích thước dùng `long`, đọc/ghi theo buffer 64 KB, không nạp toàn bộ file vào RAM.
- **Asynchronous:** các thao tác kết nối, đọc mạng, ghi mạng và đọc/ghi file dùng API async, có cancellation.
- **Parallel:** tải xuống chia file thành 4 vùng byte, chạy `Parallel.ForEachAsync` với 4 kết nối TCP độc lập; server broadcast tới các client bằng `Task.WhenAll`.
- **Gửi ảnh khi đang truyền file:** chat, upload file, upload ảnh và download dùng kết nối riêng, hoạt động đồng thời.
- **Tiến độ / hủy / kiểm tra dữ liệu:** mỗi lượt truyền có thẻ riêng; SHA-256 được kiểm tra trước khi đổi file `.part` thành file đích. Lỗi/hủy tải không ghi đè file cũ.

## Chạy ứng dụng

Cần Windows và .NET 9 SDK với WPF (hoặc Visual Studio có workload .NET desktop development).

Tại thư mục chứa `ChatLab1.sln`:

```powershell
dotnet build ChatLab1.sln -m:1
dotnet run --project ChatServer --no-build
```

Mở hai terminal khác, mỗi terminal chạy:

```powershell
dotnet run --project ChatClient --no-build
```

Nhập tên Alice/Bob rồi bấm **Connect**. Server phải chạy trước client.

- **Send / Enter:** gửi tin nhắn.
- **Nút mặt cười:** chèn emoji.
- **Gửi ảnh:** chọn ảnh; thẻ ảnh xuất hiện ngay, các client nhận preview sau khi server nhận xong ảnh.
- **Gửi file (500 MB+):** chọn file; sau khi upload xong, server thông báo cho cả phòng.
- **Lưu file...:** chọn nơi lưu; tải bằng 4 kết nối song song.
- **Hủy:** dừng riêng lượt truyền đó. **Disconnect:** hủy mọi lượt truyền của phiên hiện tại.

Client đang dùng `127.0.0.1`. Để thử LAN, đổi `Host` trong `ChatClient/MainWindow.xaml.cs` sang IP server và cho phép TCP 5000, 5001 qua firewall của server.

## Kịch bản demo cho thầy

1. Chạy server và hai client Alice/Bob.
2. Gửi câu có emoji: `Xin chào 😊 ❤️ 💚 💙` và kiểm tra màu ở cả hai client.
3. Alice gửi một ảnh, Bob thấy preview ngay trong khung chat; lưu lại ảnh để kiểm tra.
4. Tạo file 512 MB nếu chưa có:

   ```powershell
   $demo = [System.IO.File]::Create((Join-Path $PWD 'demo-512MB.bin'))
   $demo.SetLength(512MB)
   $demo.Dispose()
   ```

5. Alice gửi file này. Khi tiến độ còn chạy, gửi thêm ảnh và tin nhắn. Bob phải nhận được ảnh/tin nhắn trước khi file lớn xong.
6. Khi file được thông báo, Bob bấm **Lưu file...**. Trong lúc tải, tiếp tục gửi ảnh/tin nhắn.
7. Đối chiếu SHA-256 bằng `Get-FileHash` trên file nguồn và file nhận.
8. Thử hủy một lượt tải, hoặc ngắt kết nối khi truyền; lượt khác và client còn lại vẫn hoạt động.

Trên localhost/SSD tốc độ rất cao, lượt truyền có thể kết thúc nhanh. Chọn file lớn hơn hoặc thử qua LAN để dễ quan sát; không có độ trễ giả trong ứng dụng.

## Cấu trúc

| File | Vai trò |
| --- | --- |
| `ChatServer/Program.cs` | Hai listener, quản lý phiên, broadcast, lưu upload, trả các vùng byte |
| `Shared/TransferProtocol.cs` | Metadata JSON có length-prefix, copy từng khối, upload, parallel download, hash |
| `ChatClient/MainWindow.xaml.cs` | Kết nối, chat, chọn file, preview, điều phối tác vụ |
| `ChatClient/Controls/TransferCard.cs` | Tiến độ, trạng thái, preview và nút hủy/lưu |
| `ChatClient/Controls/ColorEmoji.cs` | Vector emoji màu và chèn emoji vào TextBlock |
| `ChatClient/Controls/MessageBubble.xaml.cs` | Bong bóng chat của mình/người khác |
| `ChatLab.IntegrationTests` | Kiểm thử thật qua TCP, gồm file 512 MB |
| `ChatLab.UiSmoke` | Dựng WPF và render giao diện để kiểm tra bố cục |

## Giao thức và luồng dữ liệu

### Cổng 5000 — chat

```text
[4 byte độ dài little-endian][UTF-8]
```

Client gửi username đầu tiên; server trả `[NAME]`, `[TOKEN]`, `[ONLINE]`.
Tin nhắn có dạng `[Alice]Xin chào`; thông báo tệp là `[ATTACHMENT]` + JSON metadata.
Token ngẫu nhiên gắn upload/download với phiên chat đang kết nối.

Mỗi client có `SemaphoreSlim` bảo vệ **cả header và body** khi gửi. Không giữ khóa đó trong suốt lượt truyền file, vì file chạy ở kết nối riêng.

### Cổng 5001 — ảnh và file

Mỗi kết nối bắt đầu bằng `[4 byte độ dài][JSON TransferRequest]`.

- Upload: metadata → server trả `ready` → đúng `Size` byte nhị phân → server tính hash, công bố metadata, trả xác nhận.
- Download: gửi ID, Offset, Count → server kiểm tra vùng byte → trả `ready` → đúng `Count` byte nhị phân.
- Metadata tối đa 64 KB. Nội dung file không được mã hóa base64, không nằm trong frame JSON.
- Upload hoàn thành trước khi người nhận tải; đây là mô hình lưu trên server rồi tải xuống.

Download dùng 4 kết nối cho 4 vùng không chồng nhau. Ví dụ file 512 MB:

```text
Kết nối 1:   0–128 MB
Kết nối 2: 128–256 MB
Kết nối 3: 256–384 MB
Kết nối 4: 384–512 MB
```

Các mốc cuối là exclusive. Với kích thước lẻ, phép chia bằng `long` đảm bảo không thiếu hoặc trùng byte. File rỗng cũng được hỗ trợ.

## Kiểm thử

Chạy server riêng trước khi chạy integration test; để cổng 5000/5001 trống trước khi khởi động server:

```powershell
dotnet run --project ChatLab.IntegrationTests -p:UseSharedCompilation=false
```

Bộ kiểm thử dùng giao thức thực tế của client để kiểm tra:

- Hai phiên chat và token.
- Upload/download file 512 MiB, kiểm tra kích thước và SHA-256.
- Chat Unicode và ảnh nhận được khi upload/download lớn chưa hoàn thành.
- Hủy tải: xóa `.part`, không phá file đích cũ.
- Checksum sai bị từ chối.
- File rỗng, vùng byte không hợp lệ, metadata quá lớn.

Cần khoảng 2 GB dung lượng trống cho dữ liệu thử. File phía test được dọn khi kết thúc; bản upload trên server nằm trong thư mục `Transfers` cạnh file chạy server.

Kiểm tra render WPF (không thay thế toàn bộ thao tác người dùng thủ công):

```powershell
dotnet run --project ChatLab.UiSmoke -p:UseSharedCompilation=false
```

Kết quả: `artifacts/ui-smoke.png`.

## Các câu hỏi vấn đáp

**Tại sao không gửi file 500 MB trong một message?**
Vì sẽ phải cấp phát buffer rất lớn, tăng RAM và có thể chặn các message sau trên cùng kết nối. Ở đây file đi từng khối trên kết nối riêng.

**Async khác parallel thế nào?**
Async giúp không chặn luồng khi chờ I/O. Parallel ở phần tải xuống là bốn tác vụ đọc các vùng file cùng lúc qua bốn socket. Chỉ thêm `async` vào hàm chưa tạo ra cơ chế tải bốn phần này.

**Tại sao dùng `ReadExactlyAsync`?**
Một lần `ReadAsync` có thể trả ít byte hơn yêu cầu. Header/metadata phải đọc đủ; phần file cũng lặp cho đến khi đủ byte đã khai báo.

**Nếu bỏ `SemaphoreSlim` khi broadcast?**
Hai tác vụ có thể ghi xen kẽ header/body vào cùng socket. Phía nhận sẽ hiểu sai độ dài và nội dung.

**Vì sao ảnh vẫn đến trong lúc gửi file?**
Mỗi upload có socket riêng; UI không chờ đồng bộ. Kênh chat và các transfer khác không xếp hàng sau toàn bộ file lớn. Chúng vẫn chia sẻ băng thông mạng nên tốc độ tùy đường truyền.

**Tại sao dùng `long` cho size/offset?**
`int` bị giới hạn khoảng 2 GB. `long` cho phép xử lý file lớn hơn mà không tràn số.

**Tại sao dùng `.part` và SHA-256?**
Không hiển thị file chưa tải xong như file hoàn chỉnh. Hash phát hiện nội dung tải về không khớp metadata trước khi thay thế file đích.

## Giới hạn của bản lab

- Không có tài khoản, TLS, lưu lịch sử chat hay resume transfer; token là định danh phiên, không thay thế xác thực bảo mật.
- Mỗi transfer tối đa 30 phút trên server; người dùng có thể hủy sớm.
- Server giữ file trong `Transfers` cạnh executable; metadata nằm trong RAM, nên file của phiên chạy cũ không được quảng bá lại sau restart. Có thể dọn thư mục này khi server đã dừng và không cần file cũ nữa.
- Nút gửi ảnh giới hạn 20 MB để preview; có thể gửi ảnh lớn hơn bằng nút gửi file, khi đó không tự preview.
- Ảnh tải xong mới hiển thị; không stream preview theo từng dòng ảnh.
