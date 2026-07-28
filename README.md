# NepalHMS Silent ESC/POS Print Service (.NET 8 WinForms Tray Application)

A standalone, zero-dialog Windows printing service designed for **NepalHMS** and hospital billing kiosks. It listens for structured billing JSON payloads over HTTP from modern web browsers and converts them into hardware-native **ESC/POS byte streams** tailored for 80mm thermal receipt printers (**POS-76**, Epson, Bixolon, Star).

## 🚀 Features

- **Calibrated for 80mm Paper (POS-76):** Configured out of the box for 42-column standard thermal widths with smart natural word-wrapping for medical descriptions and long Bill Numbers.
- **Zero-Dialog Silent Printing:** Bypasses browser print preview (`window.print()`) and OS graphic spoolers for instant, sub-second hardware execution.
- **Accurate Hospital Receipt Typography:** Natively aligns **Item | Qty | Rate | Amt** 4-column item tables, Bikram Sambat (`DateBS`) dates, VAT exemption lines, patient demographics (`HospitalNo`, `Age/Sex`), and amount `"In words"` statements.
- **Interactive System Tray UI:** Runs seamlessly near the Windows clock. Cashiers can:
  - Check operational status and listening port at a glance.
  - **Switch Target Printers On-the-Fly** directly from a right-click menu (auto-detects **POS-76** and installed print queues).
  - **Perform Hardware Test Prints (Hospital Sample)** instantly to verify communication, layout alignment, and auto-cutter functionality.
  - View real-time timestamped diagnostic activity logs in a clean floating window.
- **Single-File Deployment:** Publishes as a self-contained `.exe` (~20–30 MB) with **no manual .NET Runtime installations required on cashier terminals**.
- **CORS & Multi-Tab Friendly:** Preconfigured headers permit background asynchronous JavaScript `fetch()` calls from local or HTTPS hospital domain tabs.

---

## 🛠️ Building & Publishing for Cashier Deployment

### Prerequisites

- [_.NET 8 SDK (Windows Desktop Target)_](https://dotnet.microsoft.com/download/dotnet/8.0) installed on a Windows workstation or development PC.

### Command Line Instructions

Open Terminal / PowerShell in the repository directory and run:

1. **Test Compile / Run Locally:**

   ```powershell
   cd PosPrintService
   dotnet build -c Release
   dotnet run
   ```

2. **Publish as a Single-File Self-Contained `.exe` (For Windows 64-bit Cashier PCs):**

   ```powershell
   dotnet publish PosPrintService/PosPrintService.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
   ```

3. **Deploy to Cashier Terminals:**
   - Copy `PosPrintService.exe` from `./publish` to the cashier desktop terminal (or place it in the Windows Startup folder `shell:startup`).
   - Double-click to run! Look for the green printer badge icon near the Windows system clock.

---

## ⚙️ Configuration (`config.json`)

The service generates and reads from a simple `config.json` in its root directory:

```json
{
  "PrinterName": "POS-76",
  "ListenPort": 9111,
  "AutoCut": true,
  "OpenCashDrawer": false,
  "CharacterEncoding": 437,
  "ReceiptWidth": 42,
  "LogRequests": true
}
```

_Note: Cashiers can alter `PrinterName` without opening this file simply by right-clicking the system tray icon and selecting their active printer queue from the menu._

---

## 🔌 Web App / Frontend Integration

In your hospital billing system (Laravel, React, Vue, blade), replace standard print button clicks with a background `fetch()` request:

```javascript
async function printSilentReceipt(invoiceData) {
  const SERVICE_URL = 'http://127.0.0.1:9111/print/';

  try {
    const response = await fetch(SERVICE_URL, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(invoiceData),
    });

    const result = await response.json();
    if (response.ok && result.success) {
      console.log('✅ Silent receipt printed:', result.message);
      return true;
    } else {
      console.warn('⚠️ Print Service replied with error:', result.error);
      fallbackToBrowserPrint();
    }
  } catch (error) {
    console.error('❌ Could not reach local print service:', error);
    fallbackToBrowserPrint();
  }
}

function fallbackToBrowserPrint() {
  console.info('Falling back to standard browser PDF print dialog...');
  window.print();
}
```

### Example Nepali Hospital Invoice Payload Structure

This JSON payload accurately reproduces the standard NON-VAT Hospital receipt layout on 80mm POS-76 thermal paper:

```json
{
  "HospitalName": "Family Care Hospital",
  "Address": "Kathmandu, Nepal",
  "PanNumber": "602XXXXXX",
  "InvoiceType": "NON-VAT INVOICE",
  "CopyType": "COPY OF ORIGINAL",
  "BillNo": "Billing-01-83-84-000001",
  "TxnDate": "2026-07-23 13:25",
  "IssueDate": "2026-07-23",
  "DateBS": "20830407",
  "Counter": "Billing-01",
  "Payment": "Cash",
  "PatientName": "test test",
  "HospitalNo": "HN-2026-000001",
  "AgeSex": "23 / unknown",
  "CurrencyPrefix": "Rs.",
  "SubTotal": 150.0,
  "VatExempt": 150.0,
  "Total": 150.0,
  "PaidByPatient": 150.0,
  "Paid": 150.0,
  "InWords": "In words: NPR One Hundred Fifty Rupees Only",
  "FooterNotes": "Computer-generated receipt\nThank you",
  "Items": [
    {
      "Item": "Flat OPD Consultation",
      "Qty": "1.00",
      "Rate": 150.0,
      "Amt": 150.0
    }
  ]
}
```

## 🔍 API Endpoints

- `GET http://127.0.0.1:9111/status` — Returns server operational status, port, active target printer, and a list of all detected Windows printer queues.
- `POST http://127.0.0.1:9111/print/` — Accepts structured invoice JSON and immediately transmits raw ESC/POS binary byte streams directly to the printer hardware queue.
