using System.ComponentModel;
using System.Security.Cryptography;
using RSBotWorks.UniversalAI;

namespace RSBotWorks.Plugins;

public enum TarotSuit
{
    MajorArcana,
    Swords,
    Pentacles,
    Wands,
    Cups
}

public record TarotCard(
    string EnglishName,
    string GermanName,
    TarotSuit Suit,
    int Number,
    string UprightDescription = "",
    string ReversedDescription = ""
);

public record DrawnTarotCard(
    TarotCard Card,
    bool IsReversed
);

public static class TarotDeck
{
    public static readonly List<TarotCard> AllCards = new()
    {
        // Major Arcana
        new("The Fool", "Der Narr", TarotSuit.MajorArcana, 0),
        new("The Magician", "Der Magier", TarotSuit.MajorArcana, 1),
        new("The High Priestess", "Die Hohepriesterin", TarotSuit.MajorArcana, 2),
        new("The Empress", "Die Herrscherin", TarotSuit.MajorArcana, 3),
        new("The Emperor", "Der Herrscher", TarotSuit.MajorArcana, 4),
        new("The Hierophant", "Der Hierophant", TarotSuit.MajorArcana, 5),
        new("The Lovers", "Die Liebenden", TarotSuit.MajorArcana, 6),
        new("The Chariot", "Der Wagen", TarotSuit.MajorArcana, 7),
        new("Strength", "Die Kraft", TarotSuit.MajorArcana, 8),
        new("The Hermit", "Der Eremit", TarotSuit.MajorArcana, 9),
        new("Wheel of Fortune", "Das Rad des Schicksals", TarotSuit.MajorArcana, 10),
        new("Justice", "Die Gerechtigkeit", TarotSuit.MajorArcana, 11),
        new("The Hanged Man", "Der Gehängte", TarotSuit.MajorArcana, 12),
        new("Death", "Der Tod", TarotSuit.MajorArcana, 13),
        new("Temperance", "Die Mäßigung", TarotSuit.MajorArcana, 14),
        new("The Devil", "Der Teufel", TarotSuit.MajorArcana, 15),
        new("The Tower", "Der Turm", TarotSuit.MajorArcana, 16),
        new("The Star", "Der Stern", TarotSuit.MajorArcana, 17),
        new("The Moon", "Der Mond", TarotSuit.MajorArcana, 18),
        new("The Sun", "Die Sonne", TarotSuit.MajorArcana, 19),
        new("Judgement", "Das Gericht", TarotSuit.MajorArcana, 20),
        new("The World", "Die Welt", TarotSuit.MajorArcana, 21),

        // Swords
        new("Ace of Swords", "Ass der Schwerter", TarotSuit.Swords, 1),
        new("Two of Swords", "Zwei der Schwerter", TarotSuit.Swords, 2),
        new("Three of Swords", "Drei der Schwerter", TarotSuit.Swords, 3),
        new("Four of Swords", "Vier der Schwerter", TarotSuit.Swords, 4),
        new("Five of Swords", "Fünf der Schwerter", TarotSuit.Swords, 5),
        new("Six of Swords", "Sechs der Schwerter", TarotSuit.Swords, 6),
        new("Seven of Swords", "Sieben der Schwerter", TarotSuit.Swords, 7),
        new("Eight of Swords", "Acht der Schwerter", TarotSuit.Swords, 8),
        new("Nine of Swords", "Neun der Schwerter", TarotSuit.Swords, 9),
        new("Ten of Swords", "Zehn der Schwerter", TarotSuit.Swords, 10),
        new("Page of Swords", "Bube der Schwerter", TarotSuit.Swords, 11),
        new("Knight of Swords", "Ritter der Schwerter", TarotSuit.Swords, 12),
        new("Queen of Swords", "Königin der Schwerter", TarotSuit.Swords, 13),
        new("King of Swords", "König der Schwerter", TarotSuit.Swords, 14),

        // Pentacles
        new("Ace of Pentacles", "Ass der Münzen", TarotSuit.Pentacles, 1),
        new("Two of Pentacles", "Zwei der Münzen", TarotSuit.Pentacles, 2),
        new("Three of Pentacles", "Drei der Münzen", TarotSuit.Pentacles, 3),
        new("Four of Pentacles", "Vier der Münzen", TarotSuit.Pentacles, 4),
        new("Five of Pentacles", "Fünf der Münzen", TarotSuit.Pentacles, 5),
        new("Six of Pentacles", "Sechs der Münzen", TarotSuit.Pentacles, 6),
        new("Seven of Pentacles", "Sieben der Münzen", TarotSuit.Pentacles, 7),
        new("Eight of Pentacles", "Acht der Münzen", TarotSuit.Pentacles, 8),
        new("Nine of Pentacles", "Neun der Münzen", TarotSuit.Pentacles, 9),
        new("Ten of Pentacles", "Zehn der Münzen", TarotSuit.Pentacles, 10),
        new("Page of Pentacles", "Bube der Münzen", TarotSuit.Pentacles, 11),
        new("Knight of Pentacles", "Ritter der Münzen", TarotSuit.Pentacles, 12),
        new("Queen of Pentacles", "Königin der Münzen", TarotSuit.Pentacles, 13),
        new("King of Pentacles", "König der Münzen", TarotSuit.Pentacles, 14),

        // Wands
        new("Ace of Wands", "Ass der Stäbe", TarotSuit.Wands, 1),
        new("Two of Wands", "Zwei der Stäbe", TarotSuit.Wands, 2),
        new("Three of Wands", "Drei der Stäbe", TarotSuit.Wands, 3),
        new("Four of Wands", "Vier der Stäbe", TarotSuit.Wands, 4),
        new("Five of Wands", "Fünf der Stäbe", TarotSuit.Wands, 5),
        new("Six of Wands", "Sechs der Stäbe", TarotSuit.Wands, 6),
        new("Seven of Wands", "Sieben der Stäbe", TarotSuit.Wands, 7),
        new("Eight of Wands", "Acht der Stäbe", TarotSuit.Wands, 8),
        new("Nine of Wands", "Neun der Stäbe", TarotSuit.Wands, 9),
        new("Ten of Wands", "Zehn der Stäbe", TarotSuit.Wands, 10),
        new("Page of Wands", "Bube der Stäbe", TarotSuit.Wands, 11),
        new("Knight of Wands", "Ritter der Stäbe", TarotSuit.Wands, 12),
        new("Queen of Wands", "Königin der Stäbe", TarotSuit.Wands, 13),
        new("King of Wands", "König der Stäbe", TarotSuit.Wands, 14),

        // Cups
        new("Ace of Cups", "Ass der Kelche", TarotSuit.Cups, 1),
        new("Two of Cups", "Zwei der Kelche", TarotSuit.Cups, 2),
        new("Three of Cups", "Drei der Kelche", TarotSuit.Cups, 3),
        new("Four of Cups", "Vier der Kelche", TarotSuit.Cups, 4),
        new("Five of Cups", "Fünf der Kelche", TarotSuit.Cups, 5),
        new("Six of Cups", "Sechs der Kelche", TarotSuit.Cups, 6),
        new("Seven of Cups", "Sieben der Kelche", TarotSuit.Cups, 7),
        new("Eight of Cups", "Acht der Kelche", TarotSuit.Cups, 8),
        new("Nine of Cups", "Neun der Kelche", TarotSuit.Cups, 9),
        new("Ten of Cups", "Zehn der Kelche", TarotSuit.Cups, 10),
        new("Page of Cups", "Bube der Kelche", TarotSuit.Cups, 11),
        new("Knight of Cups", "Ritter der Kelche", TarotSuit.Cups, 12),
        new("Queen of Cups", "Königin der Kelche", TarotSuit.Cups, 13),
        new("King of Cups", "König der Kelche", TarotSuit.Cups, 14),
    };
}

public class FortunePlugin
{
    [LocalFunction("draw_card")]
    [Description("Draws a single tarot fortune card")]
    public Task<string> DrawCardAsync()
    {
        var shuffledDeck = ShuffleDeck();
        var drawnCard = DrawCard(shuffledDeck.First());
        
        return Task.FromResult(FormatCard(drawnCard));
    }
    
    [LocalFunction("draw_three_cards")]
    [Description("Draws three tarot fortune cards")]
    public Task<string> DrawThreeCardsAsync()
    {
        var shuffledDeck = ShuffleDeck();
        var drawnCards = shuffledDeck.Take(3).Select(DrawCard).ToList();
        
        var result = string.Join(", ", drawnCards.Select((card, index) => $"{index + 1}. {FormatCard(card)}"));
        
        return Task.FromResult(result);
    }

    private static List<TarotCard> ShuffleDeck()
    {
        var deck = new List<TarotCard>(TarotDeck.AllCards);
        
        // Fisher-Yates shuffle algorithm
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        
        return deck;
    }

    private static DrawnTarotCard DrawCard(TarotCard card)
    {
        var isReversed = RandomNumberGenerator.GetInt32(2) == 1; // 50% chance of being reversed
        return new DrawnTarotCard(card, isReversed);
    }

    private static string FormatCard(DrawnTarotCard drawnCard)
    {
        var card = drawnCard.Card;
        var orientation = drawnCard.IsReversed ? "Reversed" : "Upright";

        return $"{card.EnglishName} ({card.GermanName}) - {orientation}";
    }
}