# deskcal — lịch việc + nhắc việc chạy nền Windows

Tray app C# / WinForms. Cửa sổ **lịch tháng** để xem và nhập việc, chạy nền dưới khay
hệ thống, việc đến giờ thì bắn toast notification native của Windows. Không server,
không browser, không localhost.

Dữ liệu: `%LOCALAPPDATA%\deskcal\deskcal.db` (SQLite).
Log: `%LOCALAPPDATA%\deskcal\deskcal.log`.

## Build và chạy

Cần .NET SDK 10.

```
dotnet run
```

Publish ra một file exe để dùng thật:

```
dotnet publish -c Release -o dist
```

Ra `dist\deskcal.exe` (~26MB, một file duy nhất). Bản này cần
`Microsoft.WindowsDesktop.App 10.x` trên máy — đã có. Muốn exe chạy được cả trên máy
chưa cài .NET thì thêm `-p:SelfContained=true`, đổi lại file phồng lên ~134MB.

## Dùng

Double-click icon dưới khay → mở lịch tháng.

| Hành động | Cách làm |
|---|---|
| Thêm việc | **Click vào ô ngày** → form mở sẵn ngày đó |
| Sửa việc | Click vào chính dòng việc trong lịch |
| Xoá việc | Mở việc ra → nút **Xoá** |
| Đánh dấu xong | **Click vòng tròn bên trái việc** ngay trên lịch |
| Ngày quá đông | Click **+N việc nữa** → menu liệt kê cả ngày |
| Đổi tháng | `‹` `›`, hoặc mũi trái/phải; `T` về tháng này |
| Đóng | `Esc` hoặc nút X — app vẫn chạy nền, không thoát |
| Thêm nhanh | `Ctrl+N` |

### Trạng thái xong / chưa xong

Mỗi việc trên lịch có một **vòng tròn trạng thái** bên trái:

| Hiện | Nghĩa |
|---|---|
| Vòng tròn rỗng, viền theo màu nhãn | Chưa xong |
| Vòng tròn đầy + dấu tick, chip xám, chữ gạch ngang | Đã xong |

**Click thẳng vào vòng tròn** là đổi trạng thái, không cần mở form. Click vào phần
còn lại của chip mới mở form sửa. Vùng bấm của vòng tròn được nới rộng hơn hình vẽ
cho dễ trúng.

Trạng thái lưu theo **từng lần lặp** trong bảng `completions(task_id, occurs_on)`,
nên tick buổi standup hôm nay không làm buổi mai thành xong. Trong form sửa, ô
**Đã xong (chỉ ngày dd/MM)** áp cho đúng lần lặp đang mở.

Việc đã xong cũng **không bắn toast nữa**, và biến khỏi mục Quá hạn trong menu tray.

Mỗi nhãn có màu riêng: work đỏ, ops cam, deadline tím, personal xanh.

### Gõ ngày và giờ

Ô **Ngày** và hai ô **Giờ** đều là `StepBox`: gõ tay được, và có hai mũi **▲▼** để
tăng/giảm — ngày thì ±1 ngày, giờ thì ±15 phút. Mũi trên/dưới của bàn phím và cuộn
chuột cũng ăn. Không còn dropdown chỉ có vài giờ định sẵn.

| Ô | Nhận |
|---|---|
| Ngày | `Hôm nay` · `Mai` · `Ngày mốt` · `Tuần sau` · `+3` (3 ngày nữa) · `15/09` · `2026-12-25` |
| Giờ | để trống · `14:30` · `14h30` · `1430` · `9` · `9h` |

Bấm ▲ từ ô giờ đang trống thì nhảy về `09:00` (giờ kết thúc: `10:00`). Rời ô giờ bắt
đầu mà chưa điền giờ kết thúc thì tự điền `+1 tiếng`.

**Hoặc bấm icon đồng hồ** trong ô giờ để mở mặt đồng hồ chọn trực quan: vành ngoài
1–12, vành trong 00 và 13–23, chọn giờ xong tự sang bước chọn phút (bước 5 phút).
Bấm vào số giờ trên đầu popup để quay lại bước chọn giờ. Muốn phút lẻ ngoài bội số 5
thì gõ tay hoặc dùng ▲▼.

`15/09` không có năm thì hiểu là năm nay; **nếu ngày đó đã qua thì hiểu là sang năm**.
Nên muốn nhập ngày trong quá khứ thì phải ghi rõ năm: `2026-08-15`.

Gõ sai thì hiện dòng đỏ và không cho đóng form, không tạo task rác.

### Nhắc trước

Ô **Nhắc trước**: đúng giờ / 5 / 10 / 15 / 30 phút / 1 / 2 giờ / 1 ngày trước.
Toast sẽ bắn sớm hơn giờ việc đúng bằng khoảng đó.

## Thanh khay hệ thống

Click phải icon: danh sách **Quá hạn** / **Hôm nay** / **Sắp tới**, click một việc để
tick xong ngay, Shift+click để nhảy sang lịch ngày đó. Kèm **Mở lịch**, **Thêm việc…**,
**Lớp lịch mờ trên desktop**, **Độ mờ**, **Chạy cùng Windows**, **Mở thư mục dữ liệu**, **Thoát**.

### Không thấy icon dưới khay?

**Windows 11 mặc định nhét icon mới vào vùng ẩn.** Bấm mũi nhọn `^` bên trái đồng hồ,
kéo icon deskcal ra thanh taskbar — kéo một lần là Windows nhớ.

Hoặc Settings → Personalization → Taskbar → **Other system tray icons** → bật **deskcal**.

Windows lưu trạng thái này ở `HKCU\Control Panel\NotifyIconSettings\<hash>` với giá trị
`IsPromoted` (1 = hiện thẳng lên khay). Key đánh theo đường dẫn exe, nên **đổi chỗ exe
là icon lại về vùng ẩn**.

Lần chạy đầu app bắn một toast chào. Cờ đánh dấu là
`%LOCALAPPDATA%\deskcal\.welcomed` — xoá file đó thì lần sau chào lại.

## Chạy cùng Windows

Menu tray → **Chạy cùng Windows**. Ghi đường dẫn exe vào
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

Trỏ vào `dist\deskcal.exe`, đừng trỏ vào `bin\Debug\...`, vì `dotnet build` sẽ ghi đè
file trong `bin` lúc bạn build lại. Chỉ ghi HKCU nên không cần admin, và không hiện
cửa sổ console — app là WinExe.

## Lớp lịch mờ trên desktop

Menu khay → **Lớp lịch mờ trên desktop**. Một cửa sổ không viền, trong suốt, bo góc, nằm
ngay trên hình nền và **dưới mọi cửa sổ khác**.

- **Không đụng vào wallpaper.** Ảnh nền Windows giữ nguyên; Spotlight vẫn tự đổi ảnh mỗi
  ngày như thường.
- **Bấm được.** Tick việc xong, click ô ngày để thêm, click việc để sửa — ngay trên
  desktop, không phải mở cửa sổ lịch.
- **Tự bám theo màn hình.** Đọc `Screen.PrimaryScreen` mỗi lượt quét 60s, nên đổi độ phân
  giải, cắm màn ngoài, hay mang sang máy khác (4K ↔ 1440p) là tự xếp lại. Tỉ lệ khung giữ
  nguyên ở mọi màn (~1.54:1), chỉ khác độ nét.
- **Nằm bên phải**, chừa 44% bề ngang bên trái cho icon desktop.

### Độ mờ

Menu khay → **Độ mờ**: 15 / 25 / 40 / 60 / 80%. Mặc định **25%**.

Càng trong càng dịu mắt nhưng càng khó đọc, và mức đọc được phụ thuộc ảnh nền: trên vùng
tối thì 25% vẫn rõ, trên vùng sáng (đèn, mây trắng) thì phải 40–60%. Đổi mức là thấy ngay,
không cần khởi động lại.

### Ba cờ cửa sổ

Thiếu cái nào là hỏng, nên cả ba đều có test:

| Cờ | Không có thì |
|---|---|
| `WS_EX_NOACTIVATE` | Bấm vào là cướp focus của app đang gõ dở |
| `WS_EX_TOOLWINDOW` | Chiếm một ô trong Alt+Tab |
| Chặn `WM_WINDOWPOSCHANGING` ép `HWND_BOTTOM` | Bấm một cái là nó nhảy lên trước mọi cửa sổ |

Đã đo bằng `EnumWindows`: panel nằm áp chót trong z-order, chỉ còn `Progman` (chính
desktop) ở dưới.

Không dùng `WorkerW` kiểu Lively / Wallpaper Engine: cách đó không bấm được (bị lớp icon
chắn), lại là hack dựa vào nội bộ Explorer nên vỡ mỗi lần Explorer restart hoặc Windows
update.

## Nhắc việc hoạt động thế nào

- Việc **có giờ** → bắn đúng giờ đó (trừ đi "nhắc trước"). Việc **cả ngày** → bắn lúc `09:00`.
- Bảng `notified(task_id, occurs_on)` đảm bảo mỗi lần lặp chỉ bắn một lần.
- **Bắn bù**: lượt quét chạy ngay khi app khởi động, nên logon xong là bắt hết việc đã
  quá giờ. Nhưng **quá hạn hơn 12 giờ thì im** — khỏi sáng mở máy ăn một loạt toast.
  Ngưỡng 12 giờ đó đếm từ lúc **việc diễn ra**, không phải từ lúc đáng lẽ bắn. Đếm từ
  lúc đáng lẽ bắn thì việc đặt "nhắc trước 1 ngày" mà máy tắt suốt cửa sổ đó sẽ quá hạn
  trước cả khi đến giờ, và mất hút luôn — không báo gì cả.
- **Tối đa 5 toast mỗi lượt**, phần dư đợi lượt sau và được ghi vào log — không âm thầm cắt.
- Đổi ngày / giờ / kiểu lặp / nhắc-trước của việc → xoá dấu đã-bắn để nhắc lại.

Không dùng `SystemEvents.PowerModeChanged` để bắt lúc máy thức. Event đó bắn trên thread
riêng, kéo theo phải đồng bộ hoá truy cập SQLite. Timer của WinForms vẫn chạy tiếp sau khi
máy thức, nên chậm nhất là một lượt (60 giây) — đổi lấy việc toàn bộ app chỉ có một thread
thì rất đáng.

### Nếu không thấy toast

```
dist\deskcal.exe --test-toast
```

Không thấy gì thì kiểm tra theo thứ tự: **Focus Assist / Do not disturb** đang bật →
Settings → System → Notifications → **deskcal** bị tắt → xem log.

App tự đăng ký AUMID trong `HKCU\SOFTWARE\Classes\AppUserModelId\deskcal` (không cần
admin) để toast hiện tên "deskcal". Nếu WinRT lỗi hẳn thì tự động rơi về balloon tip.

## Dòng lệnh

| Lệnh | Việc |
|---|---|
| `deskcal.exe` | Chạy nền, chỉ hiện icon khay |
| `deskcal.exe --open` | Chạy nền và mở luôn cửa sổ lịch |
| `deskcal.exe --add "Họp team\|mai\|14:30\|work\|WEEKLY"` | Thêm việc rồi thoát |
| `deskcal.exe --test-toast` | Bắn một toast thử |
| `deskcal.exe --self-test out.txt` | Chạy 61 test, ghi kết quả ra file |

`--add` chỉ bắt buộc phần tiêu đề, các phần sau có mặc định. Nối lại hết phần sau `--add`
nên quên bọc nháy cũng không mất chữ. Tiện để gắn hotkey hoặc gọi từ script.

## Giao diện

Theme đọc từ `AppsUseLightTheme` trong registry, đọc lại **mỗi lần vẽ / mở menu** nên đổi
sáng/tối trong Settings là thấy ngay, không cần restart app.

Ba điều đã học được bằng cách chụp ảnh cửa sổ ra xem, không phải bằng suy luận:

- **`TextFormatFlags.NoClipping` là bắt buộc khi vẽ chữ Việt.** Dấu nặng (`ậ ẹ ọ`) nằm
  thấp hơn đường descender của font; GDI cắt theo rect nên thiếu cờ này là mất dấu —
  chữ thành "Hop", "me". Dấu trên đầu không bị vì nó nằm trong phần ascent.
- **`Panel` + `AutoSize` không dùng được nếu control con `Dock`.** Control docked không
  đóng góp vào `PreferredSize` nên panel co về 0 và hai nút Lưu/Huỷ biến mất. Dùng
  `TableLayoutPanel`.
- **Không đặt toạ độ tuyệt đối.** Máy này có 2 màn hình khác DPI (chính 150%, phụ 100%),
  nên mọi hằng số pixel đều sai ở một trong hai, và `AutoScaleMode.Dpi` thì coi số của bạn
  là pixel ở DPI hệ thống chứ không phải 96. Cách chắc chắn: `TableLayoutPanel` +
  `AutoSize`, khoảng cách tính theo `Font.Height` — font tính bằng point nên tự đúng.

### Control tự vẽ

Toàn bộ ô nhập, nút, dropdown, checkbox trong [Controls.cs](Controls.cs) là **control tự
vẽ**, không dùng control mặc định của WinForms. Lý do từng cái:

| Control | Thay cho | Vì sao |
|---|---|---|
| `Btn` | `Button` | Bo góc, 4 kiểu (primary/default/subtle/danger), hover + pressed |
| `Field` | `TextBox` viền vuông | Bo góc, viền đổi sang màu nhấn khi focus |
| `StepBox` | `ComboBox` giờ định sẵn | Gõ tay + ▲▼ chọn bất kỳ giá trị nào |
| `Drop` | `ComboBox` | Dropdown của ComboBox do **hệ thống** vẽ, không nhận màu nền — ở dark mode nó là ô sáng trợn giữa form tối. Popup tự vẽ thì theme được, và thêm được dot màu + dấu tick |
| `Check` | `CheckBox` | Ô bo góc, tick vẽ bằng nét, nền màu nhấn khi bật |
| `ClockPopup` | — | Mặt đồng hồ 24h chọn giờ, hai vành, có kim |

Vòng tròn trạng thái trên chip cũng vẽ tay, và `CalendarView.HitTest` trả về thêm cờ
`OnStatus` để phân biệt bấm vào vòng tròn (đổi trạng thái) với bấm vào chip (mở form).

Icon (mũi `‹ ›` ở header, đồng hồ trong ô giờ, dấu tick, chevron của dropdown) đều
**vẽ bằng vector**, không dùng glyph font — ký tự font ở cỡ nhỏ vừa lệch tâm vừa nhỏ.

`Painted` là lớp nền: WinForms không có nền trong suốt thật, nên mỗi control tự tô lại
màu nền của cha trước khi vẽ hình bo góc — không làm vậy thì bốn góc bị viền đen.

Việc trong lịch vẽ dạng **chip pastel** theo nhãn (`Theme.TagFill`) thay cho dot nhỏ,
phân loại bằng mắt nhanh hơn.

Giới hạn thật của WinForms, không lách được: **không có đổ bóng trên control con, không
blur, không animation**. Bo góc của cửa sổ ngoài cùng là do Windows 11 tự bo.

Một chi tiết học được khi vẽ số trên mặt đồng hồ: `TextRenderer` (GDI) vẽ bằng
**ClearType subpixel**, ở cỡ chữ rất nhỏ viền màu lấn hết nét nên từng con số hiện ra
một màu khác nhau (11 xanh, 1 tím, 2 cam). Chuyển sang `Graphics.DrawString` với
`TextRenderingHint.AntiAliasGridFit` — antialias xám, không có viền màu. Chỉ dùng cho
chữ số; chữ tiếng Việt vẫn dùng `TextRenderer` + `NoClipping` vì lý do dấu nặng ở trên.

`CalendarView` vẽ tay bằng `OnPaint` (42 ô × nhiều việc = hàng trăm control thì vừa chậm
vừa không tạo được dáng này), và tự nhân số đo theo `DeviceDpi` của chính nó.

`MenuRenderer` thay `ToolStripProfessionalRenderer` mặc định: bỏ dải gradient xám ở lề
trái, bỏ highlight xanh viền vuông, đổi sang ô bo tròn mờ.

Icon vẽ lúc chạy, dựng ở kích thước gấp 8 lần rồi thu nhỏ bằng bicubic — vẽ trực tiếp ở
16px thì nét chéo dấu tick bị nhoè.

## Cấu hình

| Env | Mặc định | Ý nghĩa |
|---|---|---|
| `DESKCAL_DATA` | `%LOCALAPPDATA%\deskcal` | Thư mục chứa DB và log |

Giờ bắn việc cả ngày (`09:00`) và ngưỡng quá hạn (`12h`) là property tĩnh trong
`Reminder.cs`, chưa có UI.

## Cấu trúc

```
Program.cs       entry, single-instance mutex, cac flag dong lenh
Store.cs         SQLite + lịch lặp + điều kiện nhắc, tự migrate thêm cột
Parse.cs         đọc ngày/giờ người dùng gõ tay
Reminder.cs      timer 60s, dedupe, bắn bù, rate-limit, nhắc trước
DesktopPanel.cs  cửa sổ mờ ghim đáy z-order, bấm được
Toast.cs         toast native qua Windows.UI.Notifications, đăng ký AUMID
CalendarView.cs  lưới lịch tháng vẽ tay, hit-test
Controls.cs      Btn / Field / StepBox / Drop / Check — control tự vẽ
Clock.cs         mặt đồng hồ chọn giờ
MainWindow.cs    cửa sổ lịch + header điều hướng
EventForm.cs     form thêm/sửa việc
TrayApp.cs       NotifyIcon + context menu
Theme.cs         màu theo theme hệ thống, font, MenuRenderer
Infra.cs         đường dẫn, log, autostart, vẽ icon lúc chạy
SelfTest.cs      61 test
```

Bốn bảng: `tasks` (việc gốc), `completions(task_id, occurs_on)` (tick xong theo từng lần
lặp), `notified(task_id, occurs_on)` (đã bắn toast).

## Test

```
dotnet build
bin\Debug\net10.0-windows10.0.19041.0\win-x64\deskcal.exe --self-test out.txt
```

App là WinExe nên không có stdout — kết quả ghi ra file, exit code 0 là pass hết.
Lưu ý PowerShell **không chờ** WinExe, phải dùng `Start-Process -Wait` nếu muốn đọc
file ngay sau đó.

61 test, phủ lịch lặp, điều kiện nhắc, nhắc-trước, parser ngày/giờ, ctor hai form,
layout ô ghi chú, và bố cục
cùng cờ cửa sổ của lớp mờ.

## So với bản Node cũ

Bản cũ là Express + SQLite + `index.html` làm hình nền desktop qua Lively Wallpaper.
Đã xoá. Hai lỗi của nó được sửa hẳn khi viết lại, và có test chặn:

- **Tick việc lặp lại bắn sai ngày.** Bản cũ PATCH vào task gốc và chỉ gán `done` cho
  đúng ngày gốc, nên tick ngày 20/9 của một việc DAILY làm *ngày gốc* hiện xong còn 20/9
  vẫn mở. Giờ mỗi lần lặp có bản ghi riêng trong `completions`.
- **`expand()` cắt ở 800 vòng.** Việc DAILY có ngày gốc cách đây hơn ~2.2 năm bị cắt
  trước khi tới hôm nay → không hiện và không nhắc. Giờ nhảy thẳng tới ngày đầu tiên
  trong khoảng.
- Kèm theo: **`MONTHLY` không còn trượt.** Bản cũ dùng `setMonth` nên gốc 31/1 cho ra
  03/03; giờ mỗi lần lặp tính từ gốc nên ra 28/2 rồi 31/3.

DB cũ ở `.\data\deskcal.db` được tự chuyển sang `%LOCALAPPDATA%\deskcal` lần chạy đầu.
Thư mục `data\` còn lại là bản gốc, xoá được.

## Còn thiếu

- **Sửa việc lặp lại là sửa cả chuỗi.** Không sửa được riêng một lần lặp (kiểu "chỉ hoãn
  buổi standup thứ Ba tuần này"). Form có ghi rõ dòng nhắc.
- **Chỉ có view tháng.** Không có tuần/ngày/danh sách như hình mẫu.
- **Không có ngày lễ.** Muốn có vạch đỏ "Quốc khánh" như hình mẫu thì phải nhập tay
  hoặc thêm bảng holiday.
- **Không có tham dự viên / họp online.** App local một người dùng, không có gì đỡ.
- **Toast không có nút bấm.** App unpackaged muốn có nút "Xong" trên toast thì phải
  đăng ký một COM activator.
- Dropdown `ComboBox` và hộp `MessageBox` vẫn do hệ thống vẽ nên không tối theo form
  ở dark mode.
- Chỉ Windows, chỉ x64, một instance.
