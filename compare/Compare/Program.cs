// Renders the same invoice with PuppeteerSharp, iText pdfHTML, HtmlRenderer.PdfSharp and DinkToPdf.
//
// Every library gets the same finished HTML (the Razor component in src/Invoices) and adds page numbers
// the way its documentation describes. A second, tiny page checks whether the library runs JavaScript.
// The PDFs and run.json go to compare/out; compare/analyze.py then counts the pages, checks the page
// numbers and the JavaScript test and draws the page images.
//
// Runs in GitHub Actions (.github/workflows/compare.yml). From the repository root:
//   dotnet run --project compare/Compare -c Release                  all four libraries
//   dotnet run --project compare/Compare -c Release -- itext         only the ones you name
// DinkToPdf needs wkhtmltopdf's libwkhtmltox installed; PuppeteerSharp downloads Chrome on the first run.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compare;
using Invoices;

var outDir = Path.GetFullPath(Path.Combine("compare", "out"));
Directory.CreateDirectory(outDir);

var html = await InvoiceHtml.RenderAsync(Invoice.Load());
await File.WriteAllTextAsync(Path.Combine(outDir, "invoice.html"), html);

Library[] libraries =
[
    new("puppeteersharp", "PuppeteerSharp", typeof(PuppeteerSharp.Puppeteer), Renderers.PuppeteerSharpAsync, Renderers.DownloadChromeAsync),
    new("itext", "itext.pdfhtml", typeof(iText.Html2pdf.HtmlConverter), Renderers.ITextAsync),
    new("htmlrenderer", "HtmlRenderer.PdfSharp", typeof(TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator), Renderers.HtmlRendererAsync),
    new("dinktopdf", "DinkToPdf", typeof(DinkToPdf.SynchronizedConverter), Renderers.DinkToPdfAsync),
];
var selected = args.Length == 0 ? libraries : libraries.Where(library => args.Contains(library.Name)).ToArray();

var run = new Dictionary<string, object?>
{
    ["dotnet"] = RuntimeInformation.FrameworkDescription,
    ["os"] = RuntimeInformation.OSDescription,
    ["libraries"] = new Dictionary<string, Dictionary<string, object?>>(),
};

foreach (var library in selected)
{
    var entry = new Dictionary<string, object?> { ["package"] = library.Package, ["version"] = Version(library.Marker) };
    try
    {
        // Setup that isn't part of a render (the Chrome download) runs first and is reported on its own.
        if (library.Setup is not null)
        {
            foreach (var (key, value) in await library.Setup())
            {
                run[key] = value;
            }
        }

        var started = Stopwatch.StartNew();
        await library.Render(html, Path.Combine(outDir, $"{library.Name}.pdf"));
        entry["seconds"] = Math.Round(started.Elapsed.TotalSeconds, 2);
    }
    catch (Exception error) // record the failure and keep going with the next library
    {
        entry["error"] = $"{error.GetType().Name}: {error.Message}";
        entry["exception"] = error.ToString();
    }

    try
    {
        await library.Render(Renderers.JavaScriptTest, Path.Combine(outDir, $"{library.Name}-js.pdf"));
    }
    catch (Exception error)
    {
        entry["js_error"] = $"{error.GetType().Name}: {error.Message}";
    }

    ((Dictionary<string, Dictionary<string, object?>>)run["libraries"]!)[library.Name] = entry;
    Console.WriteLine($"{library.Name}: {string.Join(", ", entry.Where(e => e.Key != "exception").Select(e => $"{e.Key}={e.Value}"))}");
}

if (selected.Any(library => library.Name == "dinktopdf"))
{
    run["wkhtmltopdf"] = Renderers.WkhtmltopdfVersion();
}

var json = JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(outDir, "run.json"), json);
Console.WriteLine($"Wrote {Path.Combine(outDir, "run.json")}");

// The package version, from the assembly ("25.18.1", without a commit hash).
static string Version(Type marker)
{
    var assembly = marker.Assembly;
    var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "unknown";
}

record Library(
    string Name,
    string Package,
    Type Marker,
    Func<string, string, Task> Render,
    Func<Task<Dictionary<string, object?>>>? Setup = null);
