using Microsoft.AspNetCore.Mvc;
using JobPortal.Services;

namespace JobPortal.Controllers
{
    public class ChatController : Controller
    {
        private readonly ChatbotService _chatbot;

        public ChatController(ChatbotService chatbot)
        {
            _chatbot = chatbot;
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return Json(new { response = "Please enter a message." });

            var reply = await _chatbot.AskAsync(message);
            return Json(new { response = reply });
        }
    }
}
