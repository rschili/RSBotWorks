using Markdig;

namespace RSBotWorks.Tests;

public class MarkdownToHtmlTests
{
    [Test]
    public async Task ConvertMatrixBodyToHtml()
    {
        var input = @"""        [Wisst ihr, was wir jetzt brauchen? Digitale Souveränität.](https://www.reddit.com/r/fefe_blog_interim/comments/1ojj4ja/wisst_ihr_was_wir_jetzt_brauchen_digitale/)
Und wo kauft man die am besten? Natürlich bei Microsoft. Souveräner geht es nicht.

> Geplant sei ein zentraler Rahmenvertrag, der ohne echte Ausschreibung auskommt und damit bayerische Anbieter faktisch ausschließt. Zudem sei dafür über mehrere Jahre ein Budget im hohen dreistelligen Millionenbereich bis nahe an eine Milliarde Euro vorgesehen – Geld, das primär in ein einziges US-Unternehmen fließen soll, statt in lokale Wertschöpfung.

Die besten Demokratie, die man für Geld kaufen kann.
[Bayern kauft digitale Souveränität bei Microsoft und nennt das Fortschritt.](https://www.pandolin.io/bayern-kauft-digitale-souveranitat-bei-microsoft-und-nennt-das-fortschritt/)
        """;
        var result = Markdown.ToHtml(input);

        // Assert the result contains the two hyperlinks
        await Assert.That(result).Contains("<a href=\"https://www.reddit.com/r/fefe_blog_interim/comments/1ojj4ja/wisst_ihr_was_wir_jetzt_brauchen_digitale/\">Wisst ihr, was wir jetzt brauchen? Digitale Souveränität.</a>");
        await Assert.That(result).Contains("<a href=\"https://www.pandolin.io/bayern-kauft-digitale-souveranitat-bei-microsoft-und-nennt-das-fortschritt/\">Bayern kauft digitale Souveränität bei Microsoft und nennt das Fortschritt.</a>");
        
        // Assert the result contains a blockquote
        await Assert.That(result).Contains("<blockquote>");
    }
}