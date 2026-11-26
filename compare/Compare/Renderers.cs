using DinkToPdf;
using iText.Html2pdf;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using TheArtOfDev.HtmlRenderer.PdfSharp;

namespace Compare;

/// <summary>
/// One method per library. Each prints US Letter with 16 mm margins (20 mm at the bottom) and a footer with
/// the seller on the left and "Page 1 of 2" on the right, the way that library's documentation describes.
/// </summary>
static class Renderers
{
    const string FooterLeft = "Northwind Outdoor Supply · Invoice INV-2026-0418";

    public const string JavaScriptTest = """
        <!DOCTYPE html><html><body style="font: 16pt sans-serif">
        <p>JavaScript ran: <b id="js">no</b></p>
        <script>document.getElementById("js").textContent = "yes";</script>
        </body></html>
        """;

    // PuppeteerSharp: headless Chrome. Page numbers come from Chrome's footer template.

    static string? chromePath;

    public static async Task<Dictionary<string, object?>> DownloadChromeAsync()
    {
        var folder = Path.GetFullPath(Path.Combine("compare", ".chrome"));
        var installed = await new BrowserFetcher(new BrowserFetcherOptions { Path = folder }).DownloadAsync();
        chromePath = installed.GetExecutablePath();
        var bytes = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        return new() { ["chrome"] = installed.BuildId, ["chrome_download_mb"] = Math.Round(bytes / 1_000_000.0, 1) };
    }

    public static async Task PuppeteerSharpAsync(string html, string target)
    {
        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true, ExecutablePath = chromePath });
        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html); // waits for the load event: stylesheets, the web font, images
        await page.PdfAsync(target, new PdfOptions
        {
            Format = PaperFormat.Letter,
            MarginOptions = new MarginOptions { Top = "16mm", Right = "16mm", Bottom = "20mm", Left = "16mm" },
            PrintBackground = true,
            DisplayHeaderFooter = true,
            HeaderTemplate = "<span></span>",
            FooterTemplate =
                """<div style="font-size: 7pt; width: 100%; margin: 0 16mm; display: flex; justify-content: space-between">"""
                + $"""<span>{FooterLeft}</span><span>Page <span class="pageNumber"></span> of <span class="totalPages"></span></span></div>""",
        });
    }

    // iText pdfHTML: page size, margins and page numbers through CSS paged media (@page margin boxes).
    // The footer uses Helvetica: iText knows the standard PDF fonts out of the box, not the system's Arial.

    public static Task ITextAsync(string html, string target)
    {
        const string pageCss = $$"""
            <style>
              @page { size: Letter; margin: 16mm 16mm 20mm 16mm;
                @bottom-left { content: "{{FooterLeft}}"; font-family: Helvetica, sans-serif; font-size: 7pt; }
                @bottom-right { content: "Page " counter(page) " of " counter(pages); font-family: Helvetica, sans-serif; font-size: 7pt; } }
            </style>
            """;
        using var file = File.Create(target);
        HtmlConverter.ConvertToPdf(html.Replace("</head>", pageCss + "</head>"), file);
        return Task.CompletedTask;
    }

    // HtmlRenderer.PdfSharp: renders the HTML with its own layout engine onto PDFsharp pages. It has no
    // header or footer, so the page numbers are drawn with PDFsharp afterwards.

    public static Task HtmlRendererAsync(string html, string target)
    {
        GlobalFontSettings.FontResolver ??= new SansFontResolver();

        var config = new PdfGenerateConfig { PageSize = PdfSharp.PageSize.Letter };
        config.MarginTop = config.MarginRight = config.MarginLeft = Points(16);
        config.MarginBottom = Points(20);
        using var document = PdfGenerator.GeneratePdf(html, config);

        var font = new XFont("Arial", 7);
        for (var i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            using var graphics = XGraphics.FromPdfPage(page);
            var line = new XRect(Points(16), page.Height.Point - Points(12), page.Width.Point - 2 * Points(16), 10);
            graphics.DrawString(FooterLeft, font, XBrushes.Black, line, XStringFormats.CenterLeft);
            graphics.DrawString($"Page {i + 1} of {document.PageCount}", font, XBrushes.Black, line, XStringFormats.CenterRight);
        }
        document.Save(target);
        return Task.CompletedTask;
    }

    static int Points(double millimeters) => (int)Math.Round(millimeters / 25.4 * 72);

    // DinkToPdf: a .NET wrapper around wkhtmltopdf's libwkhtmltox. Page numbers through wkhtmltopdf's footer
    // variables. wkhtmltopdf can be loaded once per process, so DinkToPdf wants one converter for the app.

    static SynchronizedConverter? dinkToPdf;

    public static Task DinkToPdfAsync(string html, string target)
    {
        dinkToPdf ??= new SynchronizedConverter(new PdfTools());
        dinkToPdf.Convert(new HtmlToPdfDocument
        {
            GlobalSettings =
            {
                PaperSize = PaperKind.Letter,
                Margins = new MarginSettings { Top = 16, Right = 16, Bottom = 20, Left = 16, Unit = Unit.Millimeters },
                Out = target,
            },
            Objects =
            {
                new ObjectSettings
                {
                    HtmlContent = html,
                    WebSettings = { DefaultEncoding = "utf-8" },
                    FooterSettings = { FontSize = 7, Left = FooterLeft, Right = "Page [page] of [topage]" },
                },
            },
        });
        return Task.CompletedTask;
    }

    public static string WkhtmltopdfVersion()
    {
        try
        {
            return new PdfTools().GetLibraryVersion();
        }
        catch (Exception error)
        {
            return $"not loaded: {error.Message}";
        }
    }
}

/// <summary>
/// PDFsharp finds installed fonts by itself only on Windows. Everywhere else it needs a font resolver:
/// this one maps every font the HTML asks for to Liberation Sans (Linux) or Arial (macOS).
/// </summary>
sealed class SansFontResolver : IFontResolver
{
    static readonly Dictionary<string, string[]> Files = new()
    {
        ["Regular"] = ["/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf"],
        ["Bold"] = ["/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf"],
        ["Italic"] = ["/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf", "/System/Library/Fonts/Supplemental/Arial Italic.ttf"],
        ["BoldItalic"] = ["/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf", "/System/Library/Fonts/Supplemental/Arial Bold Italic.ttf"],
    };

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new((bold, italic) switch { (true, true) => "BoldItalic", (true, false) => "Bold", (false, true) => "Italic", _ => "Regular" });

    public byte[] GetFont(string faceName) =>
        File.ReadAllBytes(Files[faceName].FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"No font file for {faceName}; install fonts-liberation."));
}
