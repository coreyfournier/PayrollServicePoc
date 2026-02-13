namespace ChatbotApi.Models;

public class ChatResponse
{
    public string Response { get; set; } = string.Empty;
    public List<ChatMessage> ConversationHistory { get; set; } = new();
}
