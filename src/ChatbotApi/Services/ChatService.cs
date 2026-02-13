using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ChatbotApi.Models;
using ChatbotApi.Tools;
using CommonTool = Anthropic.SDK.Common.Tool;

namespace ChatbotApi.Services;

public class ChatService : IChatService
{
    private readonly AnthropicClient _anthropicClient;
    private readonly ToolExecutor _toolExecutor;
    private readonly ILogger<ChatService> _logger;

    private const string SystemPrompt = @"You are a helpful payroll and Earned Wage Access (EWA) assistant with read-only access to a payroll system. You can look up employee information, time entries, tax information, deductions, and EWA balance/withdrawal availability.

When users ask about payroll data, use the available tools to fetch the information and then present it in a clear, natural language format.

Important context about the data:
- PayType enum: Hourly=1, Salary=2
- DeductionType enum: Health=1, Dental=2, Vision=3, Retirement401k=4, LifeInsurance=5, Other=99
- Employee IDs are GUIDs. If a user refers to an employee by name, first use get_all_employees to find their ID, then use specific tools with that ID.

Earned Wage Access (EWA) rules:
- When a user asks about their balance, available funds, how much they can withdraw, or anything about earned wages, use the get_ewa_balance tool.
- The balance shown is the NET earned wages (after estimated taxes and deductions are applied).
- Withdrawal limit per day: the LESSER of $200 or 70% of the net earned balance.
- Only 1 withdrawal is allowed per day.
- Always show BOTH the full available balance AND the withdrawal limit separately so employees understand the difference.
- Example: if net balance is $500, available to withdraw today is $200 (capped at daily max). If net balance is $250, available to withdraw is $175 (70% of $250).
- Employees without tax information will return a 422 error with code INSUFFICIENT_DATA. If this happens, explain that the balance cannot be calculated because tax information is missing.

You have READ-ONLY access. If a user asks you to create, update, or delete any data, politely explain that you can only view payroll information, not modify it.

Be concise and format data clearly. When showing monetary values, use dollar formatting.";

    private const int MaxToolRoundTrips = 10;

    public ChatService(AnthropicClient anthropicClient, ToolExecutor toolExecutor, ILogger<ChatService> logger)
    {
        _anthropicClient = anthropicClient;
        _toolExecutor = toolExecutor;
        _logger = logger;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        var messages = BuildMessages(request);
        IList<CommonTool> tools = ToolDefinitions.GetAllTools().Cast<CommonTool>().ToList();

        string? assistantText = null;

        for (int i = 0; i < MaxToolRoundTrips; i++)
        {
            var parameters = new MessageParameters
            {
                Messages = messages,
                Model = "claude-sonnet-4-5-20250929",
                MaxTokens = 4096,
                System = new List<SystemMessage> { new SystemMessage(SystemPrompt) },
                Tools = tools,
                Temperature = 0
            };

            _logger.LogDebug("Sending request to Claude (round {Round})", i + 1);
            var response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters);

            // Collect text and tool use blocks from the response
            var toolUseBlocks = new List<ToolUseContent>();
            var responseContent = new List<ContentBase>();

            foreach (var content in response.Content)
            {
                responseContent.Add(content);
                if (content is TextContent textContent)
                {
                    assistantText = textContent.Text;
                }
                else if (content is ToolUseContent toolUse)
                {
                    toolUseBlocks.Add(toolUse);
                }
            }

            // Add assistant message with all content blocks
            messages.Add(new Message
            {
                Role = RoleType.Assistant,
                Content = responseContent
            });

            if (response.StopReason == "end_turn" || toolUseBlocks.Count == 0)
            {
                break;
            }

            // Execute tools and add results as a user message
            var toolResults = new List<ContentBase>();
            foreach (var toolUse in toolUseBlocks)
            {
                _logger.LogInformation("Claude requested tool: {ToolName}", toolUse.Name);
                var toolInput = toolUse.Input?.ToString() ?? "{}";
                var result = await _toolExecutor.ExecuteToolAsync(toolUse.Name, toolInput);

                toolResults.Add(new ToolResultContent
                {
                    ToolUseId = toolUse.Id,
                    Content = new List<ContentBase> { new TextContent { Text = result } }
                });
            }

            messages.Add(new Message
            {
                Role = RoleType.User,
                Content = toolResults
            });
        }

        return BuildResponse(assistantText ?? "I'm sorry, I wasn't able to generate a response.", messages);
    }

    private static List<Message> BuildMessages(ChatRequest request)
    {
        var messages = new List<Message>();

        if (request.ConversationHistory != null)
        {
            foreach (var msg in request.ConversationHistory)
            {
                var role = msg.Role.ToLowerInvariant() == "assistant"
                    ? RoleType.Assistant
                    : RoleType.User;
                messages.Add(new Message
                {
                    Role = role,
                    Content = new List<ContentBase> { new TextContent { Text = msg.Content } }
                });
            }
        }

        messages.Add(new Message
        {
            Role = RoleType.User,
            Content = new List<ContentBase> { new TextContent { Text = request.Message } }
        });
        return messages;
    }

    private static ChatResponse BuildResponse(string responseText, List<Message> messages)
    {
        var history = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            var textParts = msg.Content?
                .OfType<TextContent>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (textParts != null && textParts.Count > 0)
            {
                history.Add(new ChatMessage
                {
                    Role = msg.Role == RoleType.Assistant ? "assistant" : "user",
                    Content = string.Join("\n", textParts)
                });
            }
        }

        return new ChatResponse
        {
            Response = responseText,
            ConversationHistory = history
        };
    }
}
