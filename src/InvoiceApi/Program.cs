// GET /invoices/INV-2026-0418.pdf returns the invoice as a PDF, streamed from the API to the browser.
//
//   export DYNAMIC_DOCUMENT_API_KEY="dda_test_..."
//   dotnet run --project src/InvoiceApi
//   open http://localhost:5000/invoices/INV-2026-0418.pdf

using System.Net.Http.Headers;
using Invoices;

var builder = WebApplication.CreateBuilder(args);

var apiKey = builder.Configuration["DYNAMIC_DOCUMENT_API_KEY"]
    ?? throw new InvalidOperationException("Set DYNAMIC_DOCUMENT_API_KEY to your API key.");

builder.Services
    .AddHttpClient<DocumentApiClient>(client =>
    {
        client.BaseAddress = new Uri("https://api-eu.dynamicdocumentapi.com/v1/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.Timeout = Timeout.InfiniteTimeSpan; // the resilience handler below sets the timeouts
    })
    .AddStandardResilienceHandler(options =>
    {
        // The defaults (10 s per attempt, 30 s in total) are made for quick JSON calls and would cancel
        // a render that is still running. A sync render waits up to 120 s on the largest plans.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(130);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5); // at least twice the attempt timeout
    });

var app = builder.Build();

// The sample data has one invoice. In your app this is a database lookup.
var invoices = new[] { Invoice.Load() }.ToDictionary(invoice => invoice.Number);

app.MapGet("/invoices/{number}.pdf", async (string number, DocumentApiClient api, CancellationToken cancellationToken) =>
{
    if (!invoices.TryGetValue(number, out var invoice))
    {
        return Results.NotFound();
    }

    var pdf = await api.RenderInvoiceAsync(invoice, cancellationToken);
    return Results.Stream(pdf, "application/pdf", fileDownloadName: $"{number}.pdf");
});

app.Run();

public partial class Program;
