"""Reads compare/out/run.json and the PDFs compare/Compare wrote, and adds what the PDFs show.

For every library: the page count, whether "Page 1 of" is printed, which fonts page 1 uses, whether the
JavaScript test page says "yes", and one WebP image per page. Writes compare/out/results.json.

    pip install -r compare/requirements.txt
    python compare/analyze.py
"""

import ctypes
import json
from pathlib import Path

import pypdfium2 as pdfium
import pypdfium2.raw as pdfium_c

OUT = Path(__file__).resolve().parent / "out"


def pdf_text(doc: pdfium.PdfDocument) -> str:
    return " ".join(" ".join(page.get_textpage().get_text_range() for page in doc).split())


def fonts(page: pdfium.PdfPage) -> list[str]:
    textpage = page.get_textpage()
    buffer = ctypes.create_string_buffer(256)
    flags = ctypes.c_int()
    names = set()
    for i in range(textpage.count_chars()):
        if pdfium_c.FPDFText_GetFontInfo(textpage.raw, i, buffer, len(buffer), ctypes.byref(flags)):
            names.add(buffer.value.decode("utf-8", "replace").split("+")[-1])  # drop the subset prefix
    return sorted(names)


def save_page_images(doc: pdfium.PdfDocument, stem: str) -> list[str]:
    names = []
    for i, page in enumerate(doc, 1):
        name = f"{stem}-page-{i}.webp"
        page.render(scale=1.5).to_pil().save(OUT / name, "WEBP", quality=82, method=6)
        names.append(name)
    return names


def main() -> None:
    results = json.loads((OUT / "run.json").read_text(encoding="utf-8"))
    for name, entry in results["libraries"].items():
        pdf = OUT / f"{name}.pdf"
        if "error" not in entry and pdf.exists():
            doc = pdfium.PdfDocument(str(pdf))
            entry["pages"] = len(doc)
            entry["has_page_numbers"] = "Page 1 of" in pdf_text(doc)
            entry["fonts"] = fonts(doc[0])
            entry["images"] = save_page_images(doc, name)
        js = OUT / f"{name}-js.pdf"
        if "js_error" not in entry and js.exists():
            entry["runs_javascript"] = "JavaScript ran: yes" in pdf_text(pdfium.PdfDocument(str(js)))
        print(name, {k: v for k, v in entry.items() if k != "exception"})
    (OUT / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
