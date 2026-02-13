using ChatbotApi.Models;
using ChatbotApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChatbotApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService chatService, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message is required" });
        }

        _logger.LogInformation("Chat request received: {Message}", request.Message);

        var response = await _chatService.ChatAsync(request);
        return Ok(response);
    }
}
