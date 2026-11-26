// The same invoice from a Razor component: .NET renders the HTML, the API only turns it into a PDF.
// Without `data` the API doesn't run its template language, so the HTML goes through as it is.
//
//   export DYNAMIC_DOCUMENT_API_KEY="dda_test_..."
//   dotnet run --project src/RenderRazorInvoice

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Invoices;

var apiKey = Environment.GetEnvironmentVariable("DYNAMIC_DOCUMENT_API_KEY")
    ?? throw new InvalidOperationException("Set DYNAMIC_DOCUMENT_API_KEY to your API key.");

var html = await InvoiceHtml.RenderAsync(Invoice.Load());

using var http = new HttpClient
{
    BaseAddress = new Uri("https://api-eu.dynamicdocumentapi.com/v1/"),
    Timeout = TimeSpan.FromSeconds(130),
};
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

using var response = await http.PostAsJsonAsync("pdf/from-html", new
{
    html,
    pdf = new
    {
        paper = new { size = "Letter" },
        margin = new { top = "16mm", right = "16mm", bottom = "20mm", left = "16mm" },
        footer = new { enabled = true, right = "Page {{page}} of {{pages}}", height = "12mm" },
    },
    delivery = "binary",
});
if (response.StatusCode != HttpStatusCode.OK)
{
    throw new HttpRequestException(
        $"Render failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
}

await using (var file = File.Create("invoice-razor.pdf"))
{
    await response.Content.CopyToAsync(file);
}

Console.WriteLine($"Wrote invoice-razor.pdf ({response.Headers.GetValues("X-Pages").Single()} pages)");
