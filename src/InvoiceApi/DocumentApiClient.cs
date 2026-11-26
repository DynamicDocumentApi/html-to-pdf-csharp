using System.Net;
using System.Net.Http.Json;
using Invoices;

/// <summary>
/// Typed HttpClient for the Dynamic Document API. <see cref="RenderInvoiceAsync"/> returns the PDF as a
/// stream that is still being read from the network, so the caller can pass it on without buffering it.
/// </summary>
public sealed class DocumentApiClient(HttpClient http)
{
    static readonly string Template = File.ReadAllText(Invoice.TemplatePath);

    public async Task<Stream> RenderInvoiceAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "pdf/from-html")
        {
            Content = JsonContent.Create(new
            {
                html = Template,
                data = invoice,
                pdf = new
                {
                    paper = new { size = "Letter" },
                    margin = new { top = "16mm", right = "16mm", bottom = "20mm", left = "16mm" },
                    footer = new { enabled = true, right = "Page {{page}} of {{pages}}", height = "12mm" },
                },
                delivery = "binary",
            }, options: Invoice.JsonOptions),
        };
        // One key per invoice request. The resilience handler resends this same message on a retry,
        // key included, so a retry after a timeout can never produce a second document.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // ResponseHeadersRead: return as soon as the headers are in, the body streams afterwards.
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var requestId = response.Headers.TryGetValues("X-Request-Id", out var ids) ? ids.First() : "none";
                throw new HttpRequestException(
                    $"Render failed with {(int)response.StatusCode} (request {requestId}): {body}",
                    inner: null,
                    response.StatusCode);
            }
        }

        // Disposing this stream (Results.Stream does) releases the response and its connection.
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }
}
