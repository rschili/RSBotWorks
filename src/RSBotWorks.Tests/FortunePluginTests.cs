using NSubstitute;
using DotNetEnv.Extensions;
using TUnit.Core.Logging;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using RSBotWorks.UniversalAI;
using RSBotWorks.Plugins;
using System.Text.Json;
using TUnit.Assertions.Extensions;

namespace RSBotWorks.Tests;

public class FortunePluginTests
{
    [Test]
    public async Task DrawSingleCard_ShouldReturnValidCard()
    {
        // Arrange
        var plugin = new FortunePlugin();

        // Act
        var result = await plugin.DrawCardAsync();

        // Assert
        await Assert.That(result).IsNotNullOrWhiteSpace();
        await Assert.That(result).Contains("("); // Should contain German name in parentheses
        await Assert.That(result).Contains(")");
        await Assert.That(result).Contains(" - "); // Should contain orientation separator
        
        // Should contain either "Upright" or "Reversed"
        await Assert.That(result.Contains("Upright") || result.Contains("Reversed")).IsTrue();
        
        TestContext.Current?.GetDefaultLogger()?.LogInformation($"Drawn card: {result}");
    }

    [Test]
    public async Task DrawThreeCards_ShouldReturnThreeCards()
    {
        // Arrange
        var plugin = new FortunePlugin();

        // Act
        var result = await plugin.DrawThreeCardsAsync();

        // Assert
        await Assert.That(result).IsNotNullOrWhiteSpace();
        
        // Should contain three cards (numbered 1, 2, 3)
        await Assert.That(result).Contains("1. ");
        await Assert.That(result).Contains("2. ");
        await Assert.That(result).Contains("3. ");
        
        TestContext.Current?.GetDefaultLogger()?.LogInformation($"Three card reading: {result}");
    }

    [Test]
    public async Task TarotDeck_ShouldContain78Cards()
    {
        // Act
        var totalCards = TarotDeck.AllCards.Count;

        // Assert
        await Assert.That(totalCards).IsEqualTo(78); // Standard tarot deck has 78 cards
    }

    [Test]
    public async Task TarotDeck_ShouldHaveUniqueCards()
    {
        // Act
        var uniqueCardNames = TarotDeck.AllCards.Select(c => c.EnglishName).Distinct().Count();
        var totalCards = TarotDeck.AllCards.Count;

        // Assert
        await Assert.That(uniqueCardNames).IsEqualTo(totalCards); // All cards should have unique English names
    }

    [Test]
    public async Task TarotDeck_ShouldHaveBothEnglishAndGermanNames()
    {
        // Act & Assert
        foreach (var card in TarotDeck.AllCards)
        {
            await Assert.That(card.EnglishName).IsNotNullOrWhiteSpace();
            await Assert.That(card.GermanName).IsNotNullOrWhiteSpace();
            await Assert.That(card.EnglishName).IsNotEqualTo(card.GermanName); // Should be different languages
        }
    }

    [Test]
    public async Task DrawCard_MultipleDraws_ShouldProduceDifferentResults()
    {
        // Arrange
        var plugin = new FortunePlugin();
        var results = new HashSet<string>();

        // Act - Draw 20 cards to check for some randomness
        for (int i = 0; i < 20; i++)
        {
            var result = await plugin.DrawCardAsync();
            results.Add(result);
        }

        // Assert - Should have some variety (not all the same card)
        await Assert.That(results.Count).IsGreaterThan(5);
    }
}