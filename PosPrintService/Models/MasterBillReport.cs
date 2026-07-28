using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PosPrintService.Models
{
    public class MasterBillReport : PrintRequestEnvelope
    {
        [JsonPropertyName("Title")]
        public string? Title { get; set; }

        [JsonPropertyName("GeneratedAt")]
        public string? GeneratedAt { get; set; }

        [JsonPropertyName("Period")]
        public string? Period { get; set; }

        [JsonPropertyName("CurrencyPrefix")]
        public string CurrencyPrefix { get; set; } = "Rs.";

        [JsonPropertyName("Bills")]
        public List<MasterBillEntry> Bills { get; set; } = new();

        [JsonPropertyName("Amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("Discount")]
        public decimal Discount { get; set; }

        [JsonPropertyName("Taxable")]
        public decimal Taxable { get; set; }

        [JsonPropertyName("Vat")]
        public decimal Vat { get; set; }

        [JsonPropertyName("Total")]
        public decimal Total { get; set; }
    }

    public class MasterBillEntry
    {
        [JsonPropertyName("BillNo")]
        public string? BillNo { get; set; }

        [JsonPropertyName("Customer")]
        public string? Customer { get; set; }

        [JsonPropertyName("Date")]
        public string? Date { get; set; }

        [JsonPropertyName("Amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("Discount")]
        public decimal Discount { get; set; }

        [JsonPropertyName("Taxable")]
        public decimal Taxable { get; set; }

        [JsonPropertyName("Vat")]
        public decimal Vat { get; set; }

        [JsonPropertyName("Total")]
        public decimal Total { get; set; }

        [JsonPropertyName("Status")]
        public string? Status { get; set; }

        [JsonPropertyName("Payment")]
        public string? Payment { get; set; }

        [JsonPropertyName("TransactionId")]
        public string? TransactionId { get; set; }
    }
}
