using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Invoices;

/// <summary>
/// The invoice the templates print. <see cref="Load"/> reads invoice/invoice.json and adds the line
/// amounts and totals; money is <see cref="decimal"/>, rounded to cents.
/// </summary>
public sealed record Invoice(
    string Number,
    string Issued,
    string Due,
    string? PoNumber,
    Party Seller,
    Customer Customer,
    string Notes,
    IReadOnlyList<LineItem> Items,
    Totals Totals)
{
    /// <summary>snake_case keys (unit_price, po_number), the names invoice.html uses.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string TemplatePath => Path.Combine(AppContext.BaseDirectory, "invoice", "invoice.html");

    public static Invoice Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "invoice", "invoice.json");
        var source = JsonSerializer.Deserialize<InvoiceSource>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"{path} is empty");

        var items = source.Items
            .Select(item => new LineItem(
                item.Sku,
                item.Description,
                item.Qty,
                Cents(item.UnitPrice),
                Cents(item.UnitPrice * item.Qty),
                item.Backorder))
            .ToList();

        var subtotal = items.Sum(item => item.Amount);
        var tax = Cents(subtotal * source.TaxRate);
        var taxPercent = decimal.Parse((source.TaxRate * 100).ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); // 8.25, not 8.2500
        var totals = new Totals(subtotal, taxPercent, tax, source.Shipping, subtotal + tax + source.Shipping);

        return new Invoice(
            source.Number,
            LongDate(source.Issued),
            LongDate(source.Due),
            source.PoNumber,
            source.Seller,
            source.Customer,
            source.Notes,
            items,
            totals);
    }

    static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // "2026-11-02" -> "November 2, 2026"
    static string LongDate(string iso) =>
        DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));

    sealed record InvoiceSource(
        string Number,
        string Issued,
        string Due,
        string? PoNumber,
        Party Seller,
        Customer Customer,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal TaxRate,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal Shipping,
        string Notes,
        IReadOnlyList<ItemSource> Items);

    sealed record ItemSource(
        string Sku,
        string Description,
        int Qty,
        [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal UnitPrice,
        bool Backorder = false);
}

public sealed record Party(string Name, IReadOnlyList<string> Address, string Email);

public sealed record Customer(string Name, string Contact, IReadOnlyList<string> Address);

public sealed record LineItem(string Sku, string Description, int Qty, decimal UnitPrice, decimal Amount, bool Backorder);

public sealed record Totals(decimal Subtotal, decimal TaxPercent, decimal Tax, decimal Shipping, decimal Total);
