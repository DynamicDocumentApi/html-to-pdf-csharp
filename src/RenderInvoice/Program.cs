// The invoice from the Jinja template: one POST with HttpClient, the PDF comes back in the response body.
//
//   export DYNAMIC_DOCUMENT_API_KEY="dda_test_..."
//   dotnet run --project src/RenderInvoice

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Invoices;

var apiKey = Environment.GetEnvironmentVariable("DYNAMIC_DOCUMENT_API_KEY")
    ?? throw new InvalidOperationException("Set DYNAMIC_DOCUMENT_API_KEY to your API key.");

using var http = new HttpClient
{
    BaseAddress = new Uri("https://api-eu.dynamicdocumentapi.com/v1/"),
    // A little longer than the API ever waits for a render (up to 120 s). A very slow render then
    // comes back as 202, still running, instead of failing here with a timeout.
    Timeout = TimeSpan.FromSeconds(130),
};
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

var request = new
{
    html = await File.ReadAllTextAsync(Invoice.TemplatePath),
    data = Invoice.Load(),
    pdf = new
    {
        paper = new { size = "Letter" },
        margin = new { top = "16mm", right = "16mm", bottom = "20mm", left = "16mm" },
        footer = new { enabled = true, right = "Page {{page}} of {{pages}}", height = "12mm" },
    },
    delivery = "binary",
};

using var response = await http.PostAsJsonAsync("pdf/from-html", request, Invoice.JsonOptions);
if (response.StatusCode != HttpStatusCode.OK)
{
    throw new HttpRequestException(
        $"Render failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
}

await using (var file = File.Create("invoice.pdf"))
{
    await response.Content.CopyToAsync(file);
}

Console.WriteLine($"Wrote invoice.pdf ({response.Headers.GetValues("X-Pages").Single()} pages)");
