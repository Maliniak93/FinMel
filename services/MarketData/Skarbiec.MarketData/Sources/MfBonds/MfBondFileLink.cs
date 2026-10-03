using System.Net;
using System.Text.RegularExpressions;

namespace Skarbiec.MarketData.Sources.MfBonds;

public static partial class MfBondFileLink
{
    // gov.pl breaks long file names with &#8203; so they wrap; those characters are not part of the name.
    private static readonly char[] ZeroWidthCharacters = ['​', '‌', '‍', '⁠', '﻿'];

    public static Uri? Find(string html, Uri pageUrl, string fileName)
    {
        foreach (Match anchor in FileDownloadAnchor().Matches(html))
        {
            var extension = ExtensionSpan().Match(anchor.Groups["body"].Value);
            if (!extension.Success)
            {
                continue;
            }

            var name = StripZeroWidth(WebUtility.HtmlDecode(extension.Groups["text"].Value)).Trim();
            if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = Href().Match(anchor.Groups["attributes"].Value);
            if (href.Success && Uri.TryCreate(pageUrl, WebUtility.HtmlDecode(href.Groups["href"].Value), out var url))
            {
                return url;
            }
        }

        return null;
    }

    private static string StripZeroWidth(string text) =>
        string.Concat(text.Where(c => Array.IndexOf(ZeroWidthCharacters, c) < 0));

    [GeneratedRegex("""<a\b(?<attributes>[^>]*\bclass\s*=\s*["'][^"']*\bfile-download\b[^"']*["'][^>]*)>(?<body>.*?)</a\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FileDownloadAnchor();

    [GeneratedRegex("""<span\b[^>]*\bclass\s*=\s*["'][^"']*\bextension\b[^"']*["'][^>]*>(?<text>.*?)</span\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ExtensionSpan();

    [GeneratedRegex("""\bhref\s*=\s*["'](?<href>[^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}
