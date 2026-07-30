namespace CinemaBooking.Application.Features.AI.DTOs;

public sealed class ConversationSession
{
    public string SessionId { get; init; } = string.Empty;
    public int? UserId { get; set; }
    public string CurrentTopic { get; set; } = string.Empty;
    public string? PreferredGenre { get; set; }
    public List<string> RecommendedMovieTitles { get; set; } = [];
    public List<ConversationTurn> Turns { get; set; } = [];
    public List<string> SuggestedFollowUps { get; set; } = [];
    public int MessageCount { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public sealed record ConversationTurn(string UserMessage, string AiReply);
