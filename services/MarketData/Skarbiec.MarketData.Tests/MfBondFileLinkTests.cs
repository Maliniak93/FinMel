using Skarbiec.MarketData.Sources.MfBonds;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests;

public sealed class MfBondFileLinkTests
{
    private static readonly Uri PageUrl = new("https://www.gov.pl/web/finanse/obligacje-detaliczne1");

    private const string FileName = "Dane_dotyczace_obligacji_detalicznych.xls";

    [Fact]
    public void Find_FileNameSplitByZeroWidthSpaces_ResolvesHrefAgainstPageUrl()
    {
        var html = RecordedResponse.Read("mf-bond-page.html");

        var link = MfBondFileLink.Find(html, PageUrl, FileName);

        Assert.Equal(new Uri("https://www.gov.pl/attachment/addc8008-886b-469d-98d3-a63f0dd02c83"), link);
    }

    [Fact]
    public void Find_PageWithoutTheFile_ReturnsNull()
    {
        var html = RecordedResponse.Read("mf-bond-page-no-link.html");

        var link = MfBondFileLink.Find(html, PageUrl, FileName);

        Assert.Null(link);
    }
}
