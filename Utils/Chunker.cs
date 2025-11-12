using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartStudyFunc.Utils
{
    /// <summary>
    /// Utility class for chunking text into semantic segments for embedding and processing.
    /// </summary>
    public static class Chunker
    {
        private const int MaxChunkSize = 1000;
        private const int MinChunkSize = 100;

        /// <summary>
        /// Creates semantic chunks from text by splitting on paragraphs.
        /// Each chunk is limited to approximately MaxChunkSize characters.
        /// </summary>
        /// <param name="text">Text to chunk</param>
        /// <returns>List of text chunks</returns>
        public static List<string> CreateSemanticChunks(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            var chunks = new List<string>();

            // Remove page break markers for cleaner chunking
            text = text.Replace("---PAGE_BREAK---", "\n\n");

            // Split on double newlines (paragraphs)
            var paragraphs = text.Split(
                new[] { "\n\n", "\r\n\r\n" },
                StringSplitOptions.RemoveEmptyEntries);

            string currentChunk = string.Empty;

            foreach (var paragraph in paragraphs)
            {
                var trimmedParagraph = paragraph.Trim();

                if (string.IsNullOrEmpty(trimmedParagraph))
                {
                    continue;
                }

                // Check if adding this paragraph exceeds the limit
                int potentialLength = currentChunk.Length + trimmedParagraph.Length + 2; // +2 for "\n\n"

                if (potentialLength > MaxChunkSize)
                {
                    // Save current chunk if it has content
                    if (!string.IsNullOrEmpty(currentChunk))
                    {
                        chunks.Add(currentChunk.Trim());
                        currentChunk = string.Empty;
                    }

                    // If single paragraph is too large, split it further
                    if (trimmedParagraph.Length > MaxChunkSize)
                    {
                        chunks.AddRange(SplitLargeParagraph(trimmedParagraph));
                    }
                    else
                    {
                        currentChunk = trimmedParagraph;
                    }
                }
                else
                {
                    // Add paragraph to current chunk
                    if (string.IsNullOrEmpty(currentChunk))
                    {
                        currentChunk = trimmedParagraph;
                    }
                    else
                    {
                        currentChunk += "\n\n" + trimmedParagraph;
                    }
                }
            }

            // Add remaining chunk
            if (!string.IsNullOrWhiteSpace(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
            }

            // If no chunks were created (no paragraph breaks), split by character limit
            if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(text))
            {
                chunks.AddRange(SplitByCharacterLimit(text.Trim()));
            }

            // Filter out very small chunks
            return chunks.Where(c => c.Length >= MinChunkSize).ToList();
        }

        /// <summary>
        /// Splits a large paragraph into smaller chunks by trying to split on sentences first.
        /// </summary>
        private static List<string> SplitLargeParagraph(string paragraph)
        {
            var chunks = new List<string>();

            // Try to split on sentence boundaries
            var sentenceDelimiters = new[] { ". ", "! ", "? ", ".\n", "!\n", "?\n" };
            var sentences = new List<string>();
            int lastIndex = 0;

            for (int i = 0; i < paragraph.Length; i++)
            {
                foreach (var delimiter in sentenceDelimiters)
                {
                    if (i + delimiter.Length <= paragraph.Length &&
                        paragraph.Substring(i, delimiter.Length) == delimiter)
                    {
                        sentences.Add(paragraph.Substring(lastIndex, i - lastIndex + 1).Trim());
                        lastIndex = i + delimiter.Length;
                        i += delimiter.Length - 1;
                        break;
                    }
                }
            }

            // Add remaining text as last sentence
            if (lastIndex < paragraph.Length)
            {
                sentences.Add(paragraph.Substring(lastIndex).Trim());
            }

            // Combine sentences into chunks
            string currentChunk = string.Empty;

            foreach (var sentence in sentences.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (currentChunk.Length + sentence.Length + 1 > MaxChunkSize)
                {
                    if (!string.IsNullOrEmpty(currentChunk))
                    {
                        chunks.Add(currentChunk.Trim());
                    }

                    // If single sentence is still too large, force split by characters
                    if (sentence.Length > MaxChunkSize)
                    {
                        chunks.AddRange(SplitByCharacterLimit(sentence));
                        currentChunk = string.Empty;
                    }
                    else
                    {
                        currentChunk = sentence;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(currentChunk))
                    {
                        currentChunk = sentence;
                    }
                    else
                    {
                        currentChunk += " " + sentence;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentChunk))
            {
                chunks.Add(currentChunk.Trim());
            }

            return chunks;
        }

        /// <summary>
        /// Splits text by character limit as a last resort.
        /// </summary>
        private static List<string> SplitByCharacterLimit(string text)
        {
            var chunks = new List<string>();

            for (int i = 0; i < text.Length; i += MaxChunkSize)
            {
                int length = Math.Min(MaxChunkSize, text.Length - i);
                var chunk = text.Substring(i, length).Trim();

                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    chunks.Add(chunk);
                }
            }

            return chunks;
        }
    }
}
