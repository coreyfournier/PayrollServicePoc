namespace ChatbotApi.Models;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public List<ChatMessage>? ConversationHistory { get; set; }
}
