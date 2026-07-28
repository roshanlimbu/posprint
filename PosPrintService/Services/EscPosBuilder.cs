using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PosPrintService.Models;

namespace PosPrintService.Services
{
    /// <summary>
    /// Translates structured Invoice data into raw binary ESC/POS command byte streams.
    /// Perfectly calibrated for POS-76 80mm thermal printers (42 columns standard)
    /// with accurate 4-column Item | Qty | Rate | Amt hospital receipt layout.
    /// </summary>
    public static class EscPosBuilder
    {
        // Standard ESC/POS Hardware Command Constants
        private static readonly byte[] ESC_INIT = { 0x1B, 0x40 };                 // ESC @ (Initialize printer)
        private static readonly byte[] ALIGN_LEFT = { 0x1B, 0x61, 0x00 };          // ESC a 0
        private static readonly byte[] ALIGN_CENTER = { 0x1B, 0x61, 0x01 };        // ESC a 1
        private static readonly byte[] ALIGN_RIGHT = { 0x1B, 0x61, 0x02 };         // ESC a 2
        private static readonly byte[] BOLD_ON = { 0x1B, 0x45, 0x01 };             // ESC E 1
        private static readonly byte[] BOLD_OFF = { 0x1B, 0x45, 0x00 };            // ESC E 0
        private static readonly byte[] SIZE_NORMAL = { 0x1B, 0x21, 0x00 };         // ESC ! 0 (Normal font)
        private static readonly byte[] SIZE_DOUBLE_WIDTH = { 0x1B, 0x21, 0x20 };   // ESC ! 0x20 (Double width)
        private static readonly byte[] SIZE_DOUBLE_HEIGHT = { 0x1B, 0x21, 0x10 };  // ESC ! 0x10 (Double height)
        private static readonly byte[] CUT_PAPER = { 0x1D, 0x56, 0x42, 0x00 };     // GS V 66 0 (Partial cut)
        private static readonly byte[] DRAWER_KICK = { 0x1B, 0x70, 0x00, 0x32, 0x32 }; // ESC p 0 50 50 (Cash drawer kick)

        /// <summary>
        /// Generates the raw byte stream representing the styled thermal hospital receipt.
        /// </summary>
        public static byte[] BuildReceipt(Invoice invoice, Config config)
        {
            using var ms = new MemoryStream();
            Encoding encoding = GetEncoding(config.CharacterEncoding);
            int width = Math.Clamp(config.ReceiptWidth, 24, 64);
            string separator = new string('-', width);
            string prefix = !string.IsNullOrWhiteSpace(invoice.CurrencyPrefix) ? invoice.CurrencyPrefix.Trim() + " " : "Rs. ";

            // 1. Initialize hardware & optional Cash Drawer kick
            ms.Write(ESC_INIT);
            
            if (config.OpenCashDrawer)
            {
                ms.Write(DRAWER_KICK);
            }

            // 2. Header Section (Center Aligned)
            ms.Write(ALIGN_CENTER);
            
            string hospitalTitle = !string.IsNullOrWhiteSpace(invoice.HospitalName) ? invoice.HospitalName :
                                   !string.IsNullOrWhiteSpace(invoice.Title) ? invoice.Title : "FAMILY CARE HOSPITAL";

            ms.Write(BOLD_ON);
            WriteText(ms, encoding, $"{hospitalTitle.Trim()}\n");
            ms.Write(BOLD_OFF);

            if (!string.IsNullOrWhiteSpace(invoice.Address))
                WriteText(ms, encoding, $"{invoice.Address.Trim()}\n");
            if (!string.IsNullOrWhiteSpace(invoice.PanNumber))
                WriteText(ms, encoding, $"PAN: {invoice.PanNumber.Trim()}\n");
            else if (!string.IsNullOrWhiteSpace(invoice.HospitalName))
                WriteText(ms, encoding, "PAN: N/A\n");

            // Invoice Type (e.g., NON-VAT INVOICE or TAX INVOICE)
            string invType = !string.IsNullOrWhiteSpace(invoice.InvoiceType) ? invoice.InvoiceType.ToUpper() : "NON-VAT INVOICE";
            ms.Write(BOLD_ON);
            WriteText(ms, encoding, $"{invType}\n");
            ms.Write(BOLD_OFF);

            // Copy Label (e.g., [ COPY OF ORIGINAL ])
            if (!string.IsNullOrWhiteSpace(invoice.CopyType) &&
                !string.Equals(invoice.CopyType.Trim(), "ORIGINAL", StringComparison.OrdinalIgnoreCase))
            {
                string copyText = invoice.CopyType.Trim();
                if (!copyText.StartsWith("[")) copyText = $"[ {copyText} ]";
                ms.Write(BOLD_ON);
                WriteText(ms, encoding, $"{copyText}\n");
                ms.Write(BOLD_OFF);
            }
            WriteText(ms, encoding, $"{separator}\n");

            // 3. Billing & Transaction Metadata (Left-Right Aligned)
            ms.Write(ALIGN_LEFT);
            
            string billNo = !string.IsNullOrWhiteSpace(invoice.BillNo) ? invoice.BillNo : 
                            !string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? invoice.InvoiceNumber : "N/A";
            WriteTwoColumnWrap(ms, encoding, "Bill No", billNo, width);
            
            string txnDate = !string.IsNullOrWhiteSpace(invoice.TxnDate) ? invoice.TxnDate : 
                             !string.IsNullOrWhiteSpace(invoice.Date) ? invoice.Date : DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            WriteTwoColumnWrap(ms, encoding, "Txn Date", txnDate, width);

            if (!string.IsNullOrWhiteSpace(invoice.IssueDate))
                WriteTwoColumnWrap(ms, encoding, "Issue Date", invoice.IssueDate, width);

            if (!string.IsNullOrWhiteSpace(invoice.DateBS))
                WriteTwoColumnWrap(ms, encoding, "Date BS", invoice.DateBS, width);

            if (!string.IsNullOrWhiteSpace(invoice.Counter))
                WriteTwoColumnWrap(ms, encoding, "Counter", invoice.Counter, width);

            string payment = !string.IsNullOrWhiteSpace(invoice.Payment) ? invoice.Payment : 
                             !string.IsNullOrWhiteSpace(invoice.PaymentMethod) ? invoice.PaymentMethod : "Cash";
            WriteTwoColumnWrap(ms, encoding, "Payment", payment, width);

            if (!string.IsNullOrWhiteSpace(invoice.PaymentTransactionId))
                WriteTwoColumnWrap(ms, encoding, "Txn ID", invoice.PaymentTransactionId, width);

            WriteText(ms, encoding, $"{separator}\n");

            // 4. Patient Demographics
            if (!string.IsNullOrWhiteSpace(invoice.PatientName))
            {
                ms.Write(BOLD_ON);
                WriteText(ms, encoding, $"{invoice.PatientName.Trim()}\n");
                ms.Write(BOLD_OFF);
            }

            if (!string.IsNullOrWhiteSpace(invoice.HospitalNo))
            {
                string hNo = invoice.HospitalNo.Trim();
                if (!hNo.StartsWith("Hospital No", StringComparison.OrdinalIgnoreCase))
                    hNo = $"Hospital No: {hNo}";
                WriteText(ms, encoding, $"{hNo}\n");
            }

            if (!string.IsNullOrWhiteSpace(invoice.BuyerMobile))
                WriteWrappedLabel(ms, encoding, "Mobile", invoice.BuyerMobile, width);

            if (!string.IsNullOrWhiteSpace(invoice.BuyerPan))
                WriteWrappedLabel(ms, encoding, "Buyer PAN", invoice.BuyerPan, width);

            if (!string.IsNullOrWhiteSpace(invoice.BuyerAddress))
                WriteWrappedLabel(ms, encoding, "Address", invoice.BuyerAddress, width);

            if (!string.IsNullOrWhiteSpace(invoice.AgeSex))
            {
                string ageSex = invoice.AgeSex.Trim();
                if (!ageSex.StartsWith("Age/Sex", StringComparison.OrdinalIgnoreCase))
                    ageSex = $"Age/Sex: {ageSex}";
                WriteText(ms, encoding, $"{ageSex}\n");
            }

            if (!string.IsNullOrWhiteSpace(invoice.DoctorName))
            {
                string doc = invoice.DoctorName.Trim();
                if (!doc.StartsWith("Dr", StringComparison.OrdinalIgnoreCase) && !doc.StartsWith("Doctor", StringComparison.OrdinalIgnoreCase))
                    doc = $"Doctor: Dr. {doc}";
                WriteText(ms, encoding, $"{doc}\n");
            }

            WriteText(ms, encoding, $"{separator}\n");

            // 5. Item Table Header (4 Columns: Item | Qty | Rate | Amt)
            ms.Write(BOLD_ON);
            WriteText(ms, encoding, FormatFourColumnHeader(width) + "\n");
            ms.Write(BOLD_OFF);
            WriteText(ms, encoding, $"{separator}\n");

            // 6. Line Items (Item | Qty | Rate | Amt)
            if (invoice.Items != null && invoice.Items.Count > 0)
            {
                foreach (var item in invoice.Items)
                {
                    string name = item.Item ?? item.Name ?? "Service";
                    string qty = !string.IsNullOrWhiteSpace(item.Qty) ? item.Qty : "1.00";
                    
                    decimal rateVal = item.Rate ?? item.Amt ?? item.Total;
                    decimal amtVal = item.Amt ?? item.Total;

                    string rateStr = rateVal.ToString("0.00");
                    string amtStr = amtVal.ToString("0.00");

                    // Handle intelligent word wrapping across the 4-column structure
                    var rows = FormatFourColumnRow(name, qty, rateStr, amtStr, width);
                    foreach (string row in rows)
                    {
                        WriteText(ms, encoding, $"{row}\n");
                    }

                    if (!string.IsNullOrWhiteSpace(item.HsCode))
                        WriteWrappedLabel(ms, encoding, "HS", item.HsCode, width);
                }
            }
            else
            {
                WriteText(ms, encoding, "No services listed.\n");
            }

            WriteText(ms, encoding, "\n");

            // 7. Totals & Tax Section
            if (invoice.SubTotal.HasValue)
                WriteTwoColumnWrap(ms, encoding, "Subtotal", $"{prefix}{invoice.SubTotal.Value:0.00}", width);
            
            if (invoice.Discount.HasValue && invoice.Discount.Value > 0)
                WriteTwoColumnWrap(ms, encoding, "Discount", $"-{prefix}{invoice.Discount.Value:0.00}", width);

            if (invoice.Taxable.HasValue && invoice.Taxable.Value > 0)
                WriteTwoColumnWrap(ms, encoding, "Taxable", $"{prefix}{invoice.Taxable.Value:0.00}", width);

            if (invoice.VatExempt.HasValue && invoice.VatExempt.Value > 0)
                WriteTwoColumnWrap(ms, encoding, "VAT Exempt", $"{prefix}{invoice.VatExempt.Value:0.00}", width);

            if (invoice.VatAmount.HasValue && invoice.VatAmount.Value > 0)
                WriteTwoColumnWrap(ms, encoding, "VAT", $"{prefix}{invoice.VatAmount.Value:0.00}", width);

            if (invoice.Tax.HasValue && invoice.Tax.Value > 0 && !invoice.VatAmount.HasValue)
                WriteTwoColumnWrap(ms, encoding, "Tax", $"{prefix}{invoice.Tax.Value:0.00}", width);

            WriteText(ms, encoding, $"{separator}\n");

            // Grand Total (Bold)
            decimal grandTotal = invoice.Total ?? invoice.GrandTotal;
            ms.Write(BOLD_ON);
            WriteTwoColumnWrap(ms, encoding, "Total", $"{prefix}{grandTotal:0.00}", width);
            ms.Write(BOLD_OFF);

            if (invoice.SchemePaid.HasValue && invoice.SchemePaid.Value > 0)
                WriteTwoColumnWrap(ms, encoding, "Scheme Paid", $"{prefix}{invoice.SchemePaid.Value:0.00}", width);

            if (invoice.PaidByPatient.HasValue)
                WriteTwoColumnWrap(ms, encoding, "Paid by patient", $"{prefix}{invoice.PaidByPatient.Value:0.00}", width);

            if (invoice.Paid.HasValue)
                WriteTwoColumnWrap(ms, encoding, "Paid", $"{prefix}{invoice.Paid.Value:0.00}", width);

            if (invoice.Change.HasValue && invoice.Change.Value > 0)
                WriteTwoColumnWrap(ms, encoding, "Change / Return", $"{prefix}{invoice.Change.Value:0.00}", width);

            if (invoice.Balance.HasValue && invoice.Balance.Value > 0)
                WriteTwoColumnWrap(ms, encoding, "Balance", $"{prefix}{invoice.Balance.Value:0.00}", width);

            WriteText(ms, encoding, $"{separator}\n");

            // 8. Amount In Words
            if (!string.IsNullOrWhiteSpace(invoice.InWords))
            {
                string words = invoice.InWords.Trim();
                if (!words.StartsWith("In words", StringComparison.OrdinalIgnoreCase))
                    words = $"In words: {words}";
                
                var wordLines = WordWrapText(words, width);
                foreach (var wl in wordLines)
                {
                    WriteText(ms, encoding, $"{wl}\n");
                }
                WriteText(ms, encoding, "\n");
            }
            else
            {
                WriteText(ms, encoding, "\n");
            }

            // 9. Footer Section (Center Aligned)
            ms.Write(ALIGN_CENTER);
            string footer = !string.IsNullOrWhiteSpace(invoice.FooterNotes) ? invoice.FooterNotes :
                            !string.IsNullOrWhiteSpace(invoice.FooterMessage) ? invoice.FooterMessage : 
                            "Computer-generated receipt\nThank you";
            
            WriteText(ms, encoding, $"{footer.Trim()}\n");

            // 10. Paper Feed and Cut Command
            // Feed 4 blank lines before cutting so knife cuts beneath receipt footer
            WriteText(ms, encoding, "\n\n\n\n");
            
            if (config.AutoCut)
            {
                ms.Write(CUT_PAPER);
            }

            return ms.ToArray();
        }

        public static byte[] BuildMasterBill(MasterBillReport report, Config config)
        {
            using var ms = new MemoryStream();
            Encoding encoding = GetEncoding(config.CharacterEncoding);
            int width = Math.Clamp(config.ReceiptWidth, 24, 64);
            string separator = new string('-', width);
            string prefix = !string.IsNullOrWhiteSpace(report.CurrencyPrefix)
                ? report.CurrencyPrefix.Trim() + " "
                : "Rs. ";

            ms.Write(ESC_INIT);
            ms.Write(ALIGN_CENTER);
            ms.Write(BOLD_ON);
            WriteText(ms, encoding, $"{(report.Title ?? "MASTER BILL REPORT").Trim()}\n");
            ms.Write(BOLD_OFF);

            if (!string.IsNullOrWhiteSpace(report.Period))
                WriteText(ms, encoding, $"{report.Period.Trim()}\n");

            if (!string.IsNullOrWhiteSpace(report.GeneratedAt))
                WriteText(ms, encoding, $"{report.GeneratedAt.Trim()}\n");

            ms.Write(ALIGN_LEFT);
            WriteText(ms, encoding, $"{separator}\n");
            WriteTwoColumnWrap(ms, encoding, "Bills", report.Bills.Count.ToString(), width);
            WriteText(ms, encoding, $"{separator}\n");

            foreach (MasterBillEntry bill in report.Bills)
            {
                WriteTwoColumnWrap(ms, encoding, "Bill No", bill.BillNo ?? "N/A", width);
                WriteWrappedLabel(ms, encoding, "Customer", bill.Customer ?? "N/A", width);
                WriteTwoColumnWrap(ms, encoding, "Date", bill.Date ?? "N/A", width);
                WriteTwoColumnWrap(ms, encoding, "Amount", $"{prefix}{bill.Amount:0.00}", width);

                if (bill.Discount > 0)
                    WriteTwoColumnWrap(ms, encoding, "Discount", $"{prefix}{bill.Discount:0.00}", width);

                if (bill.Taxable > 0)
                    WriteTwoColumnWrap(ms, encoding, "Taxable", $"{prefix}{bill.Taxable:0.00}", width);

                if (bill.Vat > 0)
                    WriteTwoColumnWrap(ms, encoding, "VAT", $"{prefix}{bill.Vat:0.00}", width);

                ms.Write(BOLD_ON);
                WriteTwoColumnWrap(ms, encoding, "Total", $"{prefix}{bill.Total:0.00}", width);
                ms.Write(BOLD_OFF);

                WriteTwoColumnWrap(ms, encoding, "Status", bill.Status ?? "N/A", width);
                WriteTwoColumnWrap(ms, encoding, "Payment", bill.Payment ?? "N/A", width);

                if (!string.IsNullOrWhiteSpace(bill.TransactionId))
                    WriteWrappedLabel(ms, encoding, "Txn ID", bill.TransactionId, width);

                WriteText(ms, encoding, $"{separator}\n");
            }

            WriteTwoColumnWrap(ms, encoding, "Amount", $"{prefix}{report.Amount:0.00}", width);
            WriteTwoColumnWrap(ms, encoding, "Discount", $"{prefix}{report.Discount:0.00}", width);
            WriteTwoColumnWrap(ms, encoding, "Taxable", $"{prefix}{report.Taxable:0.00}", width);
            WriteTwoColumnWrap(ms, encoding, "VAT", $"{prefix}{report.Vat:0.00}", width);
            ms.Write(BOLD_ON);
            WriteTwoColumnWrap(ms, encoding, "Report Total", $"{prefix}{report.Total:0.00}", width);
            ms.Write(BOLD_OFF);
            WriteText(ms, encoding, "\n\n\n\n");

            if (config.AutoCut)
                ms.Write(CUT_PAPER);

            return ms.ToArray();
        }

        private static Encoding GetEncoding(int codePage)
        {
            try
            {
                return Encoding.GetEncoding(codePage);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        private static void WriteText(MemoryStream ms, Encoding encoding, string text)
        {
            byte[] bytes = encoding.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }

        private static void WriteTwoColumnWrap(MemoryStream ms, Encoding encoding, string left, string right, int width)
        {
            left = left.Trim();
            right = right.Trim();

            if (left.Length + right.Length + 1 <= width)
            {
                int padding = width - left.Length - right.Length;
                string line = left + new string(' ', Math.Max(1, padding)) + right;
                WriteText(ms, encoding, $"{line}\n");
                return;
            }

            WriteText(ms, encoding, $"{left}\n");

            foreach (string rightLine in WordWrapText(right, width))
            {
                int padding = Math.Max(0, width - rightLine.Length);
                WriteText(ms, encoding, $"{new string(' ', padding)}{rightLine}\n");
            }
        }

        private static void WriteWrappedLabel(MemoryStream ms, Encoding encoding, string label, string value, int width)
        {
            foreach (string line in WordWrapText($"{label}: {value.Trim()}", width))
            {
                WriteText(ms, encoding, $"{line}\n");
            }
        }

        /// <summary>
        /// Formats 4-column header (Item | Qty | Rate | Amt) calibrated for 42 columns.
        /// </summary>
        private static string FormatFourColumnHeader(int totalWidth)
        {
            // For 42 columns: Qty = 5, Rate = 7, Amt = 8 -> Item = 42 - 20 = 22
            int qtyWidth = 5;
            int rateWidth = 7;
            int amtWidth = 8;
            int itemWidth = Math.Max(10, totalWidth - qtyWidth - rateWidth - amtWidth);

            string item = "Item".PadRight(itemWidth);
            string qty = "Qty".PadLeft(qtyWidth);
            string rate = "Rate".PadLeft(rateWidth);
            string amt = "Amt".PadLeft(amtWidth);

            return item + qty + rate + amt;
        }

        /// <summary>
        /// Formats item rows with smart word-wrapping for descriptions over 4 columns.
        /// </summary>
        private static List<string> FormatFourColumnRow(string name, string qty, string rate, string amt, int totalWidth)
        {
            int qtyWidth = 5;
            int rateWidth = 7;
            int amtWidth = 8;
            int itemWidth = Math.Max(10, totalWidth - qtyWidth - rateWidth - amtWidth);

            var result = new List<string>();

            if (qty.Length > qtyWidth || rate.Length > rateWidth || amt.Length > amtWidth)
            {
                result.AddRange(WordWrapText(name.Trim(), totalWidth));
                AddLabeledValueRows(result, "Qty", qty, totalWidth);
                AddLabeledValueRows(result, "Rate", rate, totalWidth);
                AddLabeledValueRows(result, "Amt", amt, totalWidth);

                return result;
            }

            var nameLines = WordWrapText(name.Trim(), itemWidth);
            string qtyCol = qty.PadLeft(qtyWidth);
            string rateCol = rate.PadLeft(rateWidth);
            string amtCol = amt.PadLeft(amtWidth);

            for (int i = 0; i < nameLines.Count; i++)
            {
                string nCol = nameLines[i].PadRight(itemWidth);
                if (i == 0)
                {
                    result.Add(nCol + qtyCol + rateCol + amtCol);
                }
                else
                {
                    // Additional word-wrapped lines leave Qty/Rate/Amt blank
                    result.Add(nCol + new string(' ', qtyWidth + rateWidth + amtWidth));
                }
            }

            if (nameLines.Count == 0)
            {
                result.Add(new string(' ', itemWidth) + qtyCol + rateCol + amtCol);
            }

            return result;
        }

        private static void AddLabeledValueRows(List<string> rows, string label, string value, int width)
        {
            if (label.Length + value.Length + 1 <= width)
            {
                rows.Add(label + new string(' ', width - label.Length - value.Length) + value);
                return;
            }

            rows.Add(label);
            rows.AddRange(WordWrapText(value, width));
        }

        /// <summary>
        /// Performs natural word-wrapping so whole words are preserved when breaking lines.
        /// </summary>
        private static List<string> WordWrapText(string text, int maxChars)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return lines;

            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var curLine = new StringBuilder();

            foreach (string word in words)
            {
                if (curLine.Length + word.Length + (curLine.Length > 0 ? 1 : 0) <= maxChars)
                {
                    if (curLine.Length > 0) curLine.Append(' ');
                    curLine.Append(word);
                }
                else
                {
                    if (curLine.Length > 0)
                    {
                        lines.Add(curLine.ToString());
                        curLine.Clear();
                    }
                    
                    // If a single word is longer than maxChars, slice it directly
                    if (word.Length > maxChars)
                    {
                        string remaining = word;
                        while (remaining.Length > maxChars)
                        {
                            lines.Add(remaining.Substring(0, maxChars));
                            remaining = remaining.Substring(maxChars);
                        }
                        curLine.Append(remaining);
                    }
                    else
                    {
                        curLine.Append(word);
                    }
                }
            }

            if (curLine.Length > 0)
            {
                lines.Add(curLine.ToString());
            }

            return lines;
        }
    }
}
