using System.ComponentModel.DataAnnotations;

namespace JobPortal.Areas.Shared.Options
{
    public sealed class OpenAIOptions
    {
        public const string SectionName = "OpenAI";

        [Required] public string ApiKey { get; init; } = string.Empty;
        [Required] public string ModelText { get; init; } = "gpt-4o-mini";          // concise reasoning + explanations
        [Required] public string ModelEmbed { get; init; } = "text-embedding-3-small"; // low-cost, good quality
        public string? BaseUrl { get; init; } // keep null for default https://api.openai.com
        public int TimeoutSeconds { get; init; } = 30;
    }
}