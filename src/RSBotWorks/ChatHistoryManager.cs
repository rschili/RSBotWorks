// Chat-Historie Manager
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Memory;

namespace RSBotWorks;

public class ChatHistoryManager
{
    private readonly ISemanticTextMemory _memory;
    private readonly List<ChatMessageContent> _shortTermHistory;
    private const int MAX_SHORT_TERM_MESSAGES = 10;
    private const string MEMORY_COLLECTION = "chat_history";

    public ChatHistoryManager(ISemanticTextMemory memory)
    {
        _memory = memory;
        _shortTermHistory = new List<ChatMessageContent>();
    }

    public async Task AddMessageAsync(ChatMessageContent message, string sessionId)
    {
        // Zur Kurzzeithistorie hinzufügen
        _shortTermHistory.Add(message);

        // Zu RAG-Memory hinzufügen für Langzeithistorie
        var memoryId = $"{sessionId}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var messageText = $"{message.Role}: {message.Content}";

        await _memory.SaveInformationAsync(
            MEMORY_COLLECTION,
            messageText,
            memoryId,
            additionalMetadata: $"SessionId: {sessionId}, Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
        );

        // Kurzzeithistorie begrenzen
        if (_shortTermHistory.Count > MAX_SHORT_TERM_MESSAGES)
        {
            _shortTermHistory.RemoveAt(0);
        }
    }

    public List<ChatMessageContent> GetShortTermHistory()
    {
        return new List<ChatMessageContent>(_shortTermHistory);
    }

    public async Task<string> SearchRelevantHistoryAsync(string query, int maxResults = 5)
    {
        var searchResults = _memory.SearchAsync(
            MEMORY_COLLECTION,
            query,
            limit: maxResults,
            minRelevanceScore: 0.7
        );

        var relevantHistory = new List<string>();
        await foreach (var result in searchResults)
        {
            relevantHistory.Add($"[{result.Metadata.AdditionalMetadata}] {result.Metadata.Text}");
        }

        return relevantHistory.Any()
            ? $"Relevante frühere Unterhaltungen:\n{string.Join("\n", relevantHistory)}\n\n"
            : "";
    }
}