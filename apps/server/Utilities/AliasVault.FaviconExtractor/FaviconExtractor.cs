//-----------------------------------------------------------------------
// <copyright file="FaviconExtractor.cs" company="aliasvault">
// Copyright (c) aliasvault. All rights reserved.
// Licensed under the AGPLv3 license. See LICENSE.md file in the project root for full license information.
// </copyright>
//-----------------------------------------------------------------------

namespace AliasVault.FaviconExtractor;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using HtmlAgilityPack;
using SkiaSharp;

/// <summary>
/// Favicon service for extracting favicons from URLs.
/// </summary>
public static class FaviconExtractor
{
    private const int MaxSizeBytes = 20 * 1024; // 20KB max size; images above this are resized/re-encoded.
    private static readonly int[] _resizeWidths = [96, 64, 48, 32];
    private static readonly int[] _jpegFallbackQualities = [80, 65, 50];
    private static readonly string[] _allowedSchemes = ["http", "https"];

    /// <summary>
    /// Extracts the favicon from a URL with enhanced browser like behavior.
    /// </summary>
    /// <param name="url">The URL to extract the favicon for.</param>
    /// <returns>Byte array for favicon image.</returns>
    public static async Task<byte[]?> GetFaviconAsync(string url)
    {
        url = NormalizeUrl(url);
        Uri uri = new(url);

        if (!IsValidUri(uri))
        {
            return null;
        }

        using HttpClient client = CreateHttpClient();

        // Attempt the operation up to two times to handle common cookiewall redirects or transient issues.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var result = await TryGetFaviconAsync(client, uri);
            if (result != null)
            {
                return result;
            }
        }

        // Return null if the favicon extraction failed.
        return null;
    }

    /// <summary>
    /// Extracts favicons for multiple URLs in parallel. Each URL is processed independently;
    /// individual failures are returned as null entries rather than throwing. The returned
    /// list lines up index-for-index with the input.
    /// </summary>
    /// <param name="urls">The URLs to extract favicons for.</param>
    /// <returns>A list of favicon byte arrays, in the same order as the input urls.</returns>
    public static async Task<IReadOnlyList<byte[]?>> GetFaviconsAsync(IReadOnlyList<string> urls)
    {
        if (urls.Count == 0)
        {
            return Array.Empty<byte[]?>();
        }

        var tasks = new Task<byte[]?>[urls.Count];
        for (int i = 0; i < urls.Count; i++)
        {
            // Wrap each call in a try/catch so one bad URL doesn't fail the whole batch.
            var url = urls[i];
            tasks[i] = SafeGetFaviconAsync(url);
        }

        return await Task.WhenAll(tasks);
    }

    private static async Task<byte[]?> SafeGetFaviconAsync(string url)
    {
        try
        {
            return await GetFaviconAsync(url);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to get the favicon from the URL.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="uri">The URI to get the favicon from.</param>
    /// <returns>The favicon bytes.</returns>
    private static async Task<byte[]?> TryGetFaviconAsync(HttpClient client, Uri uri)
    {
        var response = await FollowRedirectsAsync(client, uri);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        var faviconNodes = await GetFaviconNodesFromHtml(response, uri);
        return await TryExtractFaviconFromNodes(faviconNodes, client, uri);
    }

    /// <summary>
    /// Gets the favicon nodes from the HTML.
    /// </summary>
    /// <param name="response">The response to get the favicon nodes from.</param>
    /// <param name="uri">The URI to get the favicon nodes from.</param>
    /// <returns>The favicon nodes.</returns>
    private static async Task<HtmlNodeCollection[]> GetFaviconNodesFromHtml(HttpResponseMessage response, Uri uri)
    {
        string htmlContent = await response.Content.ReadAsStringAsync();
        HtmlDocument htmlDoc = new();
        htmlDoc.LoadHtml(htmlContent);

        var defaultFavicon = new HtmlNode(HtmlNodeType.Element, htmlDoc, 0);
        defaultFavicon.Attributes.Add("href", $"{uri.GetLeftPart(UriPartial.Authority)}/favicon.ico");

        // Get the favicon nodes from the HTML, in order of preference.
        HtmlNodeCollection?[] nodeArray =
        [
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='icon' and @type='image/svg+xml']"),
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='icon' and @sizes='96x96']"),
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='icon' and @sizes='128x128']"),
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='icon' and @sizes='48x48']"),
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='icon' and @sizes='32x32']"),
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='icon' and @sizes='192x192']"),
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='apple-touch-icon' or @rel='apple-touch-icon-precomposed']"),
            htmlDoc.DocumentNode.SelectNodes("//link[@rel='icon' or @rel='shortcut icon']"),
            new HtmlNodeCollection(htmlDoc.DocumentNode) { defaultFavicon },
        ];

        // Filter node array to only return non-null values and cast to non-nullable array
        return nodeArray.Where(x => x != null).Cast<HtmlNodeCollection>().ToArray();
    }

    /// <summary>
    /// Tries to extract the favicon from the nodes.
    /// </summary>
    /// <param name="faviconNodes">The favicon nodes.</param>
    /// <param name="client">The HTTP client.</param>
    /// <param name="baseUri">The base URI.</param>
    /// <returns>The favicon bytes.</returns>
    private static async Task<byte[]?> TryExtractFaviconFromNodes(HtmlNodeCollection[] faviconNodes, HttpClient client, Uri baseUri)
    {
        foreach (var nodeCollection in faviconNodes)
        {
            if (nodeCollection == null || nodeCollection.Count == 0)
            {
                continue;
            }

            foreach (var node in nodeCollection)
            {
                var faviconUrl = node.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(faviconUrl))
                {
                    continue;
                }

                if (!Uri.IsWellFormedUriString(faviconUrl, UriKind.Absolute))
                {
                    faviconUrl = new Uri(baseUri, faviconUrl).ToString();
                }

                var faviconBytes = await FetchAndProcessFaviconAsync(client, faviconUrl);
                if (faviconBytes != null)
                {
                    return faviconBytes;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fetches and processes the favicon.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="url">The URL to fetch the favicon from.</param>
    /// <returns>The favicon bytes.</returns>
    private static async Task<byte[]?> FetchAndProcessFaviconAsync(HttpClient client, string url)
    {
        try
        {
            // Validate the favicon URL before fetching
            if (!Uri.TryCreate(url, UriKind.Absolute, out var faviconUri) || !IsValidUri(faviconUri))
            {
                return null;
            }

            // Follow redirects with validation
            var response = await FollowRedirectsAsync(client, faviconUri);

            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("image/"))
            {
                return null;
            }

            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            if (imageBytes.Length == 0)
            {
                return null;
            }

            // If image is too large, attempt to resize/re-encode it down under the cap.
            if (imageBytes.Length > MaxSizeBytes)
            {
                return ResizeImage(imageBytes, contentType);
            }

            return imageBytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new HTTP client with enhanced browser-like configuration to handle bot protection.
    /// </summary>
    /// <returns>The HTTP client.</returns>
    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false, // Handle redirects manually
            UseCookies = true,         // Enable cookie handling for session management
            CookieContainer = new System.Net.CookieContainer(),
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5), // Keep original timeout
        };

        var random = new Random();
        var userAgents = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/121.0",
        };

        // Use random User-Agent
        client.DefaultRequestHeaders.Add("User-Agent", userAgents[random.Next(userAgents.Length)]);

        // More comprehensive Accept header with image types prioritized
        client.DefaultRequestHeaders.Add(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");

        // Additional browser-like headers
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.Add("DNT", "1");
        client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        client.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");

        // Add Sec-Fetch headers to mimic modern browsers
        if (random.Next(2) == 0)
        {
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        }

        // Add Chrome-specific headers randomly
        if (random.Next(3) == 0)
        {
            client.DefaultRequestHeaders.Add("Sec-CH-UA", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
            client.DefaultRequestHeaders.Add("Sec-CH-UA-Mobile", "?0");
            client.DefaultRequestHeaders.Add("Sec-CH-UA-Platform", "\"Windows\"");
        }

        return client;
    }

    /// <summary>
    /// Normalizes the URL by adding a scheme if it is missing.
    /// </summary>
    /// <param name="url">The URL to normalize.</param>
    /// <returns>The normalized URL.</returns>
    private static string NormalizeUrl(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + url;
        }

        return url;
    }

    /// <summary>
    /// Checks if the URI is valid and not pointing to internal/private IPs.
    /// </summary>
    /// <param name="uri">The URI to check.</param>
    /// <returns>True if the URI is valid and safe, false otherwise.</returns>
    private static bool IsValidUri(Uri uri)
    {
        // Check scheme and port
        if (!_allowedSchemes.Contains(uri.Scheme) || !uri.IsDefaultPort)
        {
            return false;
        }

        // Resolve hostname to IP and validate
        try
        {
            var addresses = Dns.GetHostAddresses(uri.Host);
            foreach (var address in addresses)
            {
                if (!IPAddressValidator.IsPublicIPAddress(address))
                {
                    return false;
                }
            }
        }
        catch
        {
            // If DNS resolution fails, block the request
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles HTTP redirects with validation to prevent SSRF attacks.
    /// </summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="uri">The initial URI to request.</param>
    /// <returns>The final HTTP response after following redirects, or null if blocked/failed.</returns>
    private static async Task<HttpResponseMessage?> FollowRedirectsAsync(HttpClient client, Uri uri)
    {
        var currentUri = uri;
        int redirectCount = 0;
        const int maxRedirects = 5;

        while (redirectCount < maxRedirects)
        {
            // Create request with referer header to appear more browser-like
            var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            if (redirectCount == 0)
            {
                // First request - add Google referer to appear like navigation
                request.Headers.Add("Referer", "https://www.google.com/");
            }
            else
            {
                // Subsequent redirects - use original URL as referer
                request.Headers.Add("Referer", uri.ToString());
            }

            var response = await client.SendAsync(request);

            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
            {
                var location = response.Headers.Location;
                if (location == null)
                {
                    return null;
                }

                // Resolve relative URLs
                if (!location.IsAbsoluteUri)
                {
                    location = new Uri(currentUri, location);
                }

                // Validate the redirect target
                if (!IsValidUri(location))
                {
                    return null; // Block redirect to internal IPs
                }

                currentUri = location;
                redirectCount++;
            }
            else
            {
                return response;
            }
        }

        return null; // Too many redirects
    }

    /// <summary>
    /// Iteratively shrinks the image until it fits in the size cap. Tries
    /// progressively smaller widths in PNG (lossless after downscale handles most cases), then
    /// falls back to JPEG at decreasing quality if PNG at the smallest width is still too large.
    /// </summary>
    /// <param name="imageBytes">The image bytes to resize.</param>
    /// <param name="contentType">The content type of the image.</param>
    /// <returns>The resized image bytes, or null if it could not be brought under the size cap.</returns>
    private static byte[]? ResizeImage(byte[] imageBytes, string contentType)
    {
        // SVG is a text format; we can't usefully raster-resize it here. Caller already rejects
        // anything over the cap, so just bail and let it return null.
        if (contentType == "image/svg+xml")
        {
            return null;
        }

        try
        {
            using var original = SKBitmap.Decode(imageBytes);
            if (original == null)
            {
                return null;
            }

            // Pass 1: PNG at progressively smaller widths. Preserves transparency.
            foreach (var width in _resizeWidths)
            {
                var encoded = EncodeAtWidth(original, width, SKEncodedImageFormat.Png, 100);
                if (encoded != null && encoded.Length <= MaxSizeBytes)
                {
                    return encoded;
                }
            }

            // Pass 2: JPEG at the smallest width with decreasing quality. Loses transparency but
            // gives much smaller files for photographic favicons that resist PNG compression.
            var fallbackWidth = _resizeWidths[^1];
            foreach (var quality in _jpegFallbackQualities)
            {
                var encoded = EncodeAtWidth(original, fallbackWidth, SKEncodedImageFormat.Jpeg, quality);
                if (encoded != null && encoded.Length <= MaxSizeBytes)
                {
                    return encoded;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resizes the bitmap to the target width (preserving aspect ratio, never upscaling) and
    /// encodes it in the given format.
    /// </summary>
    /// <param name="original">The decoded source bitmap.</param>
    /// <param name="targetWidth">Desired output width in pixels.</param>
    /// <param name="format">Encode format.</param>
    /// <param name="quality">Encoder quality (only meaningful for lossy formats).</param>
    /// <returns>Encoded bytes, or null if encoding failed.</returns>
    private static byte[]? EncodeAtWidth(SKBitmap original, int targetWidth, SKEncodedImageFormat format, int quality)
    {
        var width = Math.Min(targetWidth, original.Width);
        var scale = (float)width / original.Width;
        var height = Math.Max(1, (int)(original.Height * scale));

        using var resized = original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        if (resized == null)
        {
            return null;
        }

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(format, quality);
        return data?.ToArray();
    }
}
