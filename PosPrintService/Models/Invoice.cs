using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosPrintService.Models
{
    /// <summary>
    /// Comprehensive structured invoice model tailored for Nepali hospital billing receipts
    /// (e.g. Family Care Hospital, NepalHMS) with full support for BS dates, VAT exemption, and 4-column items.
    /// </summary>
    public class Invoice
    {
        // --- Header Information ---
        [JsonPropertyName("HospitalName")]
        public string? HospitalName { get; set; }

        [JsonPropertyName("Title")]
        public string? Title { get; set; }

        [JsonPropertyName("Address")]
        public string? Address { get; set; }

        [JsonPropertyName("Phone")]
        public string? Phone { get; set; }

        [JsonPropertyName("PanNumber")]
        public string? PanNumber { get; set; }

        [JsonPropertyName("InvoiceType")]
        public string? InvoiceType { get; set; } // e.g. "NON-VAT INVOICE" or "TAX INVOICE"

        [JsonPropertyName("CopyType")]
        public string? CopyType { get; set; } // e.g. "COPY OF ORIGINAL" or "ORIGINAL"

        // --- Bill & Transaction Metadata ---
        [JsonPropertyName("BillNo")]
        public string? BillNo { get; set; }

        [JsonPropertyName("InvoiceNumber")]
        public string? InvoiceNumber { get; set; } // Fallback/alias for BillNo

        [JsonPropertyName("TxnDate")]
        public string? TxnDate { get; set; } // e.g. "2026-07-23 13:25"

        [JsonPropertyName("Date")]
        public string? Date { get; set; } // Fallback/alias for TxnDate

        [JsonPropertyName("IssueDate")]
        public string? IssueDate { get; set; } // e.g. "2026-07-23"

        [JsonPropertyName("DateBS")]
        public string? DateBS { get; set; } // e.g. "20830407"

        [JsonPropertyName("Counter")]
        public string? Counter { get; set; } // e.g. "Billing-01"

        [JsonPropertyName("Payment")]
        public string? Payment { get; set; } // e.g. "Cash" or "Fonepay"

        [JsonPropertyName("PaymentMethod")]
        public string? PaymentMethod { get; set; } // Fallback/alias for Payment

        // --- Patient & Demographics ---
        [JsonPropertyName("PatientName")]
        public string? PatientName { get; set; }

        [JsonPropertyName("HospitalNo")]
        public string? HospitalNo { get; set; } // e.g. "HN-2026-000001"

        [JsonPropertyName("AgeSex")]
        public string? AgeSex { get; set; } // e.g. "23 / unknown"

        [JsonPropertyName("DoctorName")]
        public string? DoctorName { get; set; }

        // --- Line Items (Item | Qty | Rate | Amt) ---
        [JsonPropertyName("Items")]
        public List<InvoiceItem> Items { get; set; } = new();

        // --- Totals & Financials ---
        [JsonPropertyName("CurrencyPrefix")]
        public string CurrencyPrefix { get; set; } = "Rs.";

        [JsonPropertyName("SubTotal")]
        public decimal? SubTotal { get; set; }

        [JsonPropertyName("VatExempt")]
        public decimal? VatExempt { get; set; }

        [JsonPropertyName("VatAmount")]
        public decimal? VatAmount { get; set; }

        [JsonPropertyName("Discount")]
        public decimal? Discount { get; set; }

        [JsonPropertyName("Tax")]
        public decimal? Tax { get; set; }

        [JsonPropertyName("Total")]
        public decimal? Total { get; set; }

        [JsonPropertyName("GrandTotal")]
        public decimal GrandTotal { get; set; } // Fallback for Total

        [JsonPropertyName("PaidByPatient")]
        public decimal? PaidByPatient { get; set; }

        [JsonPropertyName("Paid")]
        public decimal? Paid { get; set; }

        [JsonPropertyName("Change")]
        public decimal? Change { get; set; }

        // --- Footer & Words ---
        [JsonPropertyName("InWords")]
        public string? InWords { get; set; } // e.g. "In words: NPR One Hundred Fifty Rupees Only"

        [JsonPropertyName("FooterNotes")]
        public string? FooterNotes { get; set; } // e.g. "Computer-generated receipt\nThank you"

        [JsonPropertyName("FooterMessage")]
        public string? FooterMessage { get; set; } // Fallback/alias for FooterNotes

        // Hardware overrides
        [JsonPropertyName("OpenCashDrawer")]
        public bool? OpenCashDrawer { get; set; }
    }

    /// <summary>
    /// Represents a billing line item with 4 columns: Item, Qty, Rate, and Amt.
    /// </summary>
    public class InvoiceItem
    {
        [JsonPropertyName("Item")]
        public string? Item { get; set; }

        [JsonPropertyName("Name")]
        public string? Name { get; set; } // Fallback/alias for Item

        [JsonPropertyName("Qty")]
        public string? Qty { get; set; } // e.g. "1.00" or "1"

        [JsonPropertyName("Rate")]
        public decimal? Rate { get; set; }

        [JsonPropertyName("Amt")]
        public decimal? Amt { get; set; }

        [JsonPropertyName("Total")]
        public decimal Total { get; set; } // Fallback/alias for Amt
    }
}
