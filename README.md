# NepalHMS Silent ESC/POS Print Service (.NET 8 WinForms Tray Application)

A standalone, zero-dialog Windows printing service designed for **NepalHMS** and hospital billing kiosks. It listens for structured billing JSON payloads over HTTP from modern web browsers and converts them into hardware-native **ESC/POS byte streams** tailored for 80mm thermal receipt printers (**POS-76**, Epson, Bixolon, Star).

## 🚀 Features

- **Printer Profiles Without HMS Code Changes:** Set `ReceiptWidth`, encoding, cutter, drawer, and Windows queue once per workstation. Use 32 columns for most 58mm printers and 42–48 columns for most 80mm printers.
- **Zero-Dialog Silent Printing:** Bypasses browser print preview (`window.print()`) and OS graphic spoolers for instant, sub-second hardware execution.
- **Accurate Hospital Receipt Typography:** Natively aligns **Item | Qty | Rate | Amt** 4-column item tables, Bikram Sambat (`DateBS`) dates, VAT exemption lines, patient demographics (`HospitalNo`, `Age/Sex`), and amount `"In words"` statements.
- **Interactive System Tray UI:** Runs seamlessly near the Windows clock. Cashiers can:
  - Check operational status and listening port at a glance.
  - **Switch Target Printers On-the-Fly** directly from a right-click menu (auto-detects **POS-76** and installed print queues).
  - Configure printer name, port, receipt width, website origins, token, cutter, drawer, and logging from a Settings window.
  - **Perform Hardware Test Prints (Hospital Sample)** instantly to verify communication, layout alignment, and auto-cutter functionality.
  - View real-time timestamped diagnostic activity logs in a clean floating window.
- **Single-File Deployment:** Publishes as a self-contained `.exe` (~20–30 MB) with **no manual .NET Runtime installations required on cashier terminals**.
- **Authenticated Local API:** Exact-origin CORS, a generated shared token, a 1 MB request limit, and idempotent job IDs prevent unauthorized or duplicate receipts.
- **One Formatter for Billing Prints:** Both invoice receipts and Master Bill reports use the same workstation printer profile and width rules.

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
   dotnet publish PosPrintService.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
   ```

3. **Deploy to Cashier Terminals:**
   - Copy `PosPrintService.exe` and `config.json` from `./publish` to a writable folder such as `C:\NepalHMS\PosPrintService`.
   - Double-click to run. Look for the green printer badge icon near the Windows system clock.
   - Right-click the tray icon and open **Settings...** to select the printer, receipt width, website origin, and API token.

---

## ⚙️ Configuration (`config.json`)

The service generates and reads from a simple `config.json` in its root directory. Normal setup should be done from the tray icon's **Settings...** window; direct JSON editing is only needed for scripted rollout.

```json
{
  "PrinterName": "POS-76",
  "ListenPort": 9111,
  "AutoCut": true,
  "OpenCashDrawer": false,
  "CharacterEncoding": 437,
  "ReceiptWidth": 42,
  "LogRequests": true,
  "ApiToken": "",
  "AllowedOrigins": [
    "http://127.0.0.1:8000",
    "https://hms.example.com"
  ],
  "IdempotencyWindowMinutes": 10
}
```

_Note: Cashiers can alter `PrinterName` without opening this file simply by right-clicking the system tray icon and selecting their active printer queue from the menu._

On first launch, the service generates `ApiToken` and saves it to `config.json`.
For one workstation, copy that value into NepalHms as `POS_PRINT_TOKEN`. For
multiple cashier workstations, generate one strong shared token for the NepalHms
deployment and place the same value in every workstation's `ApiToken` and the
server's `POS_PRINT_TOKEN`. Add the exact NepalHms browser origin—scheme, host,
and optional port—to `AllowedOrigins`. Restart the tray service after changing
the listening port or security settings.

---

## 🔌 Web App / Frontend Integration

In your hospital billing system (Laravel, React, Vue, blade), replace standard print button clicks with a background `fetch()` request:

```javascript
async function printSilentReceipt(invoiceData) {
  const SERVICE_URL = 'http://127.0.0.1:9111/print/';
  const API_TOKEN = 'copy-the-generated-config-token-here';

  try {
    const response = await fetch(SERVICE_URL, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-PosPrint-Token': API_TOKEN,
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
  "DocumentType": "invoice",
  "JobId": "invoice-print-12345",
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
  "PaymentTransactionId": "TXN-001",
  "PatientName": "test test",
  "HospitalNo": "HN-2026-000001",
  "BuyerPan": "987654321",
  "BuyerAddress": "Kathmandu",
  "BuyerMobile": "9800000000",
  "AgeSex": "23 / unknown",
  "CurrencyPrefix": "Rs.",
  "SubTotal": 150.0,
  "Taxable": 0.0,
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
      "Amt": 150.0,
      "HsCode": "HCS"
    }
  ]
}
```

## 🔍 API Endpoints

- `GET http://127.0.0.1:9111/status` — Returns server operational status, port, active target printer, and a list of all detected Windows printer queues.
- `POST http://127.0.0.1:9111/print/` — Requires `X-PosPrint-Token`, a supported `DocumentType` (`invoice` or `master_bill`), and a unique `JobId`; then transmits raw ESC/POS bytes directly to the configured printer queue.

## NepalHms Configuration

```env
POS_PRINT_ENABLED=true
POS_PRINT_URL=http://127.0.0.1:9111/print/
POS_PRINT_TOKEN=copy-the-generated-config-token-here
POS_PRINT_TIMEOUT_MS=3000
```

If the status probe cannot reach the tray service, NepalHms opens its normal
browser print dialog. Once a native print request has been submitted, it does
not automatically print a browser copy when confirmation is uncertain.
