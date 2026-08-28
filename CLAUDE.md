# CLAUDE.md

Hướng dẫn cho Claude Code khi làm việc trong repo này. Đọc trước khi sửa gì.

## Đây là app gì

`deskcal` — tray app C# / WinForms trên Windows. Chạy nền dưới khay hệ thống, có cửa
sổ lịch tháng để nhập/sửa việc, và bắn toast notification native khi việc đến giờ.
Không server, không browser, không localhost. Dữ liệu trong SQLite ở
`%LOCALAPPDATA%\deskcal\deskcal.db`.

Chi tiết tính năng và quyết định thiết kế: xem [README.md](README.md).

## Lệnh hay dùng

```
dotnet build
dotnet publish -c Release -o dist
```

Chạy test (41 test):

```
bin\Debug\net9.0-windows10.0.19041.0\win-x64\deskcal.exe --self-test out.txt
```

Các flag khác: `--open` (mở luôn cửa sổ lịch), `--add "Tiêu đề|mai|14:30|work|WEEKLY"`,
`--test-toast`.

### Hai cái bẫy khi chạy lệnh

1. **`dotnet build` fail nếu app đang chạy.** Nó không ghi đè được `deskcal.exe` đang
   bị lock. Luôn `Stop-Process -Name deskcal -Force -ErrorAction SilentlyContinue`
   trước khi build. Lỗi hiện ra là `MSB3027 / MSB3021`.
2. **PowerShell KHÔNG chờ WinExe.** App là `OutputType=WinExe` nên PowerShell trả về
   ngay, `$LASTEXITCODE` là số rác. Muốn đọc kết quả `--self-test` thì phải
   `Start-Process -Wait -PassThru` rồi lấy `.ExitCode`.

App không có stdout. Mọi thứ ghi vào `%LOCALAPPDATA%\deskcal\deskcal.log` — đọc log
là cách duy nhất biết nó đang làm gì.

## Bố cục code

```
Program.cs       entry, single-instance mutex, các flag dòng lệnh
Store.cs         SQLite + lịch lặp (Occurrences) + điều kiện nhắc (DueNow), tự migrate cột
Parse.cs         đọc ngày/giờ người dùng gõ tay
Reminder.cs      timer 60s, dedupe, bắn bù, rate-limit, nhắc trước
Toast.cs         toast native qua Windows.UI.Notifications, đăng ký AUMID
CalendarView.cs  lưới lịch tháng vẽ tay + hit-test
Controls.cs      Btn / Field / StepBox / Drop / Check — control tự vẽ
Clock.cs         mặt đồng hồ chọn giờ
Theme.cs         bảng màu theo theme hệ thống, font, helper vẽ, MenuRenderer
MainWindow.cs    cửa sổ lịch + header điều hướng
EventForm.cs     form thêm/sửa việc
TrayApp.cs       NotifyIcon + context menu
Infra.cs         đường dẫn, log, autostart, vẽ icon lúc chạy
SelfTest.cs      41 test
```

Bốn bảng SQLite: `tasks` (việc gốc), `completions(task_id, occurs_on)` (xong theo từng
lần lặp), `notified(task_id, occurs_on)` (đã bắn toast).

## Quy ước

- **Comment trong code viết tiếng Việt không dấu.** Chuỗi hiện cho người dùng thì viết
  tiếng Việt có dấu đầy đủ.
- Comment giải thích **tại sao**, không diễn giải lại code. Ưu tiên ghi lại cái bẫy đã
  gặp, để lần sau không đạp lại.
- Thêm logic mới thì thêm test vào `SelfTest.cs`. Phần dễ sai nhất là lịch lặp, điều
  kiện nhắc, và parser ngày/giờ.

## Bẫy đã gặp — đừng đạp lại

Năm cái này đều tốn nhiều thời gian mới tìm ra, và **không cái nào phát hiện được bằng
cách đọc code**. Chỉ chụp ảnh cửa sổ ra xem mới thấy.

1. **`TextFormatFlags.NoClipping` là bắt buộc khi vẽ chữ Việt bằng `TextRenderer`.**
   Dấu nặng (`ậ ẹ ọ`) nằm thấp hơn đường descender của font; GDI cắt theo rect nên
   thiếu cờ này là mất dấu — "Họp" thành "Hop", "mẹ" thành "me". Dấu trên đầu không bị
   vì nó nằm trong phần ascent.

2. **KHÔNG đặt toạ độ pixel tuyệt đối.** Máy dev có 2 màn hình khác DPI (chính 150%,
   phụ 100%), nên mọi hằng số pixel đều sai ở một trong hai. `AutoScaleMode.Dpi` cũng
   không cứu được: nó coi số của bạn là pixel ở DPI **hệ thống**, không phải 96.
   Cách chắc chắn: `TableLayoutPanel` + `AutoSize`, khoảng cách tính theo `Font.Height`.
   Font tính bằng point nên tự đúng ở mọi DPI. `CalendarView` vẽ tay thì nhân theo
   `DeviceDpi` của chính nó.

3. **`Panel` + `AutoSize` không dùng được nếu control con `Dock`.** Control docked không
   đóng góp vào `PreferredSize` nên panel co về 0 và control con biến mất. Dùng
   `TableLayoutPanel`.

4. **Control tự vẽ có `DefaultSize` là 0×0.** Không đặt `Height` trong constructor thì
   nó vẽ ra một vạch mỏng 1px và coi như biến mất.

5. **`TextRenderer` vẽ bằng ClearType subpixel.** Ở cỡ chữ rất nhỏ (số trên mặt đồng
   hồ), viền màu lấn hết nét nên từng con số hiện ra một màu khác nhau. Chữ số thì
   dùng `Graphics.DrawString` + `TextRenderingHint.AntiAliasGridFit` (antialias xám).
   Chữ tiếng Việt vẫn phải dùng `TextRenderer` vì lý do (1).

## Cách kiểm tra giao diện

Logic có test, nhưng **giao diện thì phải xem bằng mắt**. Không có cách nào khác.

Chụp đúng cửa sổ của app bằng `PrintWindow` (chụp được cả khi cửa sổ nằm dưới cửa sổ
khác), rồi đọc file PNG:

- **Bắt buộc gọi `SetProcessDpiAwarenessContext(-4)`** (per-monitor-v2) trong tiến
  trình PowerShell trước khi đo. Nếu không, `GetWindowRect` trả toạ độ đã bị ảo hoá
  theo DPI khác và `PrintWindow` vẽ nội dung vào bitmap sai cỡ → ảnh bị crop, dẫn tới
  kết luận sai là "app bị cắt mất nội dung". Đã sai vì chuyện này hai lần.
- `SetProcessDPIAware()` là **chưa đủ** — nó chỉ system-aware.
- **Đừng chụp toàn màn hình.** Nó lấy cả cửa sổ khác của người dùng.

Muốn chụp form/popup thì bấm chuột bằng `mouse_event` vào đúng toạ độ. Kích thước form
đổi theo màn hình nó mở ra, nên **đo toạ độ từ ảnh vừa chụp**, đừng dùng lại toạ độ cũ.

## Đừng làm

- Đừng dùng control mặc định của WinForms cho UI mới (`Button`, `TextBox` viền vuông,
  `ComboBox`). Dùng `Btn` / `Field` / `Drop` / `Check` trong `Controls.cs`. Riêng
  `ComboBox` có lý do cứng: dropdown của nó do **hệ thống** vẽ, không nhận màu nền,
  ở dark mode là một ô sáng trợn giữa form tối.
- Đừng thêm `SystemEvents.PowerModeChanged` để bắt lúc máy thức. Event đó bắn trên
  thread riêng, kéo theo phải đồng bộ hoá truy cập SQLite. Timer WinForms vẫn chạy
  tiếp sau khi máy thức, chậm nhất một lượt 60 giây — đổi lấy việc toàn app một thread
  thì đáng.
- Đừng commit `data/`, `bin/`, `obj/`, `dist/` (xem `.gitignore`). DB thật ở
  `%LOCALAPPDATA%\deskcal`.

## Còn nợ

- **Sửa việc lặp lại là sửa cả chuỗi.** Không hoãn riêng được một lần lặp.
- Chỉ có view tháng, không có tuần/ngày/danh sách.
- Không có ngày lễ.
- Toast không có nút bấm — app unpackaged muốn có nút "Xong" trên toast thì phải đăng
  ký một COM activator.
- Dropdown của `MessageBox` vẫn do hệ thống vẽ nên dark mode không tối theo form.
- Ở màn hình 150%, mỗi ô ngày chỉ vừa 1 chip + dòng "+N việc nữa" (màn 100% được 3).
