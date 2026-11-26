using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Invoices;

/// <summary>Renders <see cref="InvoiceDocument"/> to an HTML string with .NET's HtmlRenderer, no browser and no web server.</summary>
public static class InvoiceHtml
{
    public static async Task<string> RenderAsync(Invoice invoice)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Invoice"] = invoice });
            var output = await renderer.RenderComponentAsync<InvoiceDocument>(parameters);
            return output.ToHtmlString();
        });
    }
}
