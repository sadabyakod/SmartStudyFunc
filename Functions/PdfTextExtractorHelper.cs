using System;
using System.IO;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Microsoft.Extensions.Logging;

namespace SmartStudyFunc.Helpers
{
    /// <summary>
    /// Helper class for extracting text from PDF files using PdfPig library.
    /// </summary>
    public static class PdfTextExtractorHelper
    {
        /// <summary>
        /// Extracts all text content from a PDF file.
        /// </summary>
        /// <param name="pdfBytes">PDF file as byte array</param>
        /// <returns>Extracted text with page breaks preserved</returns>
        /// <exception cref="ArgumentException">Thrown when pdfBytes is null or empty</exception>
        /// <exception cref="InvalidOperationException">Thrown when PDF extraction fails</exception>
        public static string Extract(byte[] pdfBytes)
        {
            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                throw new ArgumentException("PDF bytes cannot be null or empty", nameof(pdfBytes));
            }

            // Safety check for extremely large PDFs
            const long MaxPdfSize = 100 * 1024 * 1024; // 100MB
            if (pdfBytes.Length > MaxPdfSize)
            {
                throw new ArgumentException(
                    $"PDF file too large: {pdfBytes.Length} bytes. Maximum: {MaxPdfSize} bytes",
                    nameof(pdfBytes));
            }

            try
            {
                using var pdf = PdfDocument.Open(pdfBytes);
                var textBuilder = new StringBuilder();
                int pageCount = pdf.NumberOfPages;
                int successfulPages = 0;
                int failedPages = 0;

                // Safety check for excessive page count
                const int MaxPages = 1000;
                if (pageCount > MaxPages)
                {
                    Console.WriteLine($"Warning: PDF has {pageCount} pages, limiting to first {MaxPages}");
                    pageCount = MaxPages;
                }

                for (int pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                {
                    try
                    {
                        // Add per-page timeout protection
                        var pageTask = Task.Run(() => 
                        {
                            var page = pdf.GetPage(pageNumber);
                            return page.Text;
                        });

                        // 30 second timeout per page
                        if (!pageTask.Wait(TimeSpan.FromSeconds(30)))
                        {
                            Console.WriteLine($"Warning: Timeout extracting page {pageNumber}, skipping...");
                            failedPages++;
                            continue;
                        }

                        var pageText = pageTask.Result;

                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            // Append page text
                            textBuilder.AppendLine(pageText.Trim());
                            
                            // Add page separator (except for last page)
                            if (pageNumber < pageCount)
                            {
                                textBuilder.AppendLine();
                                textBuilder.AppendLine("---PAGE_BREAK---");
                                textBuilder.AppendLine();
                            }
                            
                            successfulPages++;
                        }
                    }
                    catch (Exception pageEx)
                    {
                        // Log and continue to next page if one page fails
                        Console.WriteLine($"Warning: Failed to extract text from page {pageNumber}: {pageEx.Message}");
                        failedPages++;
                    }
                }

                var extractedText = textBuilder.ToString().Trim();

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    throw new InvalidOperationException(
                        $"No text could be extracted from the PDF. The PDF may be image-based or empty. "
                        + $"Pages processed: {successfulPages}, Failed: {failedPages}");
                }

                if (failedPages > 0)
                {
                    Console.WriteLine($"PDF extraction completed with warnings: {successfulPages} pages successful, {failedPages} pages failed");
                }

                return extractedText;
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is InvalidOperationException))
            {
                throw new InvalidOperationException($"Failed to extract text from PDF: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Extracts text from a PDF stream.
        /// </summary>
        /// <param name="pdfStream">PDF file stream</param>
        /// <returns>Extracted text with page breaks preserved</returns>
        public static string Extract(Stream pdfStream)
        {
            if (pdfStream == null)
            {
                throw new ArgumentNullException(nameof(pdfStream));
            }

            using var memoryStream = new MemoryStream();
            pdfStream.CopyTo(memoryStream);
            return Extract(memoryStream.ToArray());
        }
    }
}
