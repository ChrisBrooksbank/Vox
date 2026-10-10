using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class PdfDocumentsTests
{
    private sealed class Element(int id, string controlType, string name = "", string value = "", string ariaRole = "", int headingLevel = 0) : IVBufferElement
    {
        private readonly List<IVBufferElement> _children = [];
        public int[] RuntimeId => [id];
        public string Name => name;
        public string ControlType => controlType;
        public string AriaRole => ariaRole;
        public string AriaProperties => string.Empty;
        public bool IsFocusable => false;
        public string Value => value;
        public int HeadingLevel => headingLevel;
        public IReadOnlyList<IVBufferElement> GetChildren() => _children;
        public Element Add(Element child) { _children.Add(child); return this; }
    }

    private static VBufferDocument Document(string url, params Element[] children)
    {
        var root = new Element(1, "Document", "Report", url);
        foreach (var child in children)
            root.Add(child);
        return new VBufferBuilder().Build(root);
    }

    [Theory]
    [InlineData("https://example.com/files/report.pdf", true)]
    [InlineData("file:///C:/Users/me/Report.PDF#page=2", true)]
    [InlineData("https://example.com/report.pdf?download=1", true)]
    [InlineData("https://example.com/pdf-guide.html", false)]
    [InlineData("", false)]
    public void IsPdf_FromTheDocumentAddress(string url, bool expected) =>
        Assert.Equal(expected, PdfDocuments.IsPdf(Document(url, new Element(2, "Text", "Some text"))));

    [Fact]
    public void TaggedPdf_IsJustAPdf()
    {
        var document = Document("https://example.com/a.pdf",
            new Element(2, "Text", "Summary", ariaRole: "heading", headingLevel: 1),
            new Element(3, "Text", "The year in review."));

        Assert.Equal("PDF document", PdfDocuments.LoadAnnouncement(document));
    }

    [Fact]
    public void UntaggedPdf_IsSaidToHaveNoStructure()
    {
        var document = Document("https://example.com/a.pdf", new Element(2, "Text", "Summary"), new Element(3, "Text", "The year in review."));

        Assert.Equal("Untagged PDF document: no headings or structure", PdfDocuments.LoadAnnouncement(document));
    }

    [Fact]
    public void ScannedPdf_HasNoText()
    {
        var document = Document("https://example.com/scan.pdf", new Element(2, "Image"));

        Assert.StartsWith("PDF document with no text", PdfDocuments.LoadAnnouncement(document));
    }

    [Fact]
    public void WebPage_SaysNothing() =>
        Assert.Null(PdfDocuments.LoadAnnouncement(Document("https://example.com/", new Element(2, "Text", "Hello"))));
}
