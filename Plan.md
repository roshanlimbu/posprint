# Windows C# ESC/POS Silent POS Print Service

A technical plan to implement a standalone, high-speed Windows print service in **C# (.NET 8)** for NepalHms. This service enables silent, zero-dialog receipt printing with razor-sharp hardware ROM typography and automatic paper cutting directly from web browser billing screens.

## User Review Required

> [!IMPORTANT]
> **Shift in Printing Paradigm**: Moving from browser graphic printing (`window.print()`) to direct **ESC/POS byte streaming** bypasses the operating system's graphics engine. The receipt structure will be styled using hardware font commands rather than HTML/CSS, resulting in 100% crisp character rendering at max print speed.

> [!TIP]
> **Self-Contained Deployment**: We plan to publish the C# application as a **single-file self-contained `.exe`** (approx. 20–30 MB) using .NET 8. Cashier desktop terminals on Windows will not require manual installation of the .NET runtime or any dependencies—cashiers simply double-click the program or place it in their Windows Startup folder.

## Open Questions

> [!CAUTION]
> **1. Background Service vs. System Tray Application**  
> Would you prefer this program to run as a **System Tray Icon** (which cashiers can see near the Windows clock, with a right-click menu to change the target printer name and view status), or as a silent background **Windows Service** installed via command line (`sc.exe`)? _(Recommended: System Tray Icon for easier troubleshooting on cashier computers)._

> [!NOTE]
> **2. Cash Drawer Kick-Out & Hardware Triggers**  
> Do your POS-76 printers have an RJ11/RJ12 cash drawer attached? We can automatically append the standard ESC/POS drawer-kick command (`0x1B 0x70 0x00 0x32 0x32`) immediately before cutting the paper upon every invoice billing printout. Should this be enabled by default in `config.json`?

## Proposed Changes

We will place the standalone Windows service source code within a dedicated tools sub-directory inside the repository to keep it cleanly isolated from the core Laravel application.

### Windows POS Print Service (`tools/windows-pos-service/`)

#### [NEW] [PosPrintService.csproj](file:///Volumes/Workspace/office/NepalHms/tools/windows-pos-service/PosPrintService.csproj)

- .NET 8.0 project file configured for WinForms/Console startup and single-file publish targeting `win-x64` and `win-x86`.

#### [NEW] [Program.cs](file:///Volumes/Workspace/office/NepalHms/tools/windows-pos-service/Program.cs)

- Initializes an `HttpListener` on `http://127.0.0.1:9111/print/` (or customizable port).
- Manages CORS headers (`Access-Control-Allow-Origin: *`) to permit direct API calls from any web browser tab.
- Manages the System Tray icon UI and life cycle.

#### [NEW] [EscPosBuilder.cs](file:///Volumes/Workspace/office/NepalHms/tools/windows-pos-service/EscPosBuilder.cs)

- Translates incoming JSON invoice data into raw binary ESC/POS formatting commands.
- Handles column text padding, DOS OEM Code Page 437/850 encoding, bold headers (`ESC E`), center/left alignments (`ESC a`), and auto-cut commands (`GS V 66`).

#### [NEW] [RawPrinterHelper.cs](file:///Volumes/Workspace/office/NepalHms/tools/windows-pos-service/RawPrinterHelper.cs)

- Wraps Windows native Spooler APIs (`winspool.drv`: `OpenPrinter`, `StartDocPrinter`, `WritePrinter`, `ClosePrinter`) using P/Invoke to send raw bytes directly to any installed Windows printer queue without driver raster modification.

#### [NEW] [config.json](file:///Volumes/Workspace/office/NepalHms/tools/windows-pos-service/config.json)

- Simple user-editable configuration file:
  ```json
  {
    "PrinterName": "pos76",
    "ListenPort": 9111,
    "AutoCut": true,
    "OpenCashDrawer": false,
    "CharacterEncoding": 437
  }
  ```

---

### Laravel Frontend Integration (Optional / Auto-Fallback)

#### [MODIFY] [show.blade.php](file:///Volumes/Workspace/office/NepalHms/resources/views/billing/invoices/show.blade.php)

- Upgrade the **Print Invoice** action button in the billing interface with intelligent silent printing:
  1. Attempt a fast background `fetch("http://127.0.0.1:9111/print/", { method: 'POST', ... })` with the structured invoice JSON payload.
  2. If the local Windows C# service responds with 200 OK, show a toast notification: _"Receipt printed successfully"_.
  3. If the fetch fails or times out (e.g., user is on an iPad, macOS laptop, or service isn't running), automatically drop back to standard browser PDF print dialog (`window.print()`).

## Verification Plan

### Automated / Local Verification

- Compile the C# tool using `dotnet build` / `dotnet publish` for Windows.
- On a development machine running the emulator or local service, execute a PowerShell test POST request containing mock invoice JSON:
  ```powershell
  Invoke-RestMethod -Uri "http://127.0.0.1:9111/print/" -Method Post -ContentType "application/json" -Body '{"InvoiceNumber":"Test-101","PatientName":"John Doe","GrandTotal":150.0,"Items":[{"Name":"OPD Flat","Qty":"1","Total":150.0}]}'
  ```
- Confirm HTTP 200 response and instant appearance of receipt data in target print queue or ESC/POS emulator.

### Manual Hardware Verification

- Deploy the published single-file `.exe` onto a Windows cashier computer connected to the physical **POS-76** hardware thermal printer.
- Open the NepalHms web billing screen in Chrome, click **Print Invoice**, and verify:
  1. No browser print preview dialog pops up.
  2. Printer fires instantly with razor-sharp hardware font rendering.
  3. Paper is cleanly auto-cut at the end of the receipt.
