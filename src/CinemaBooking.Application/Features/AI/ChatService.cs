using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Features.AI.DTOs;
using CinemaBooking.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace CinemaBooking.Application.Features.AI;

public sealed class ChatService : IChatService
{
    private readonly IAIService _aiService;
    private readonly IIntentRouter _intentRouter;
    private readonly ISafetyFilter _safetyFilter;
    private readonly IRecommendationEngine _recommendationEngine;
    private readonly IConversationStore _conversationStore;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IUserRepository _userRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IUserVoucherRepository _userVoucherRepository;
    private readonly IVoucherRepository _voucherRepository;
    private readonly IGenreRepository _genreRepository;
    private readonly ICinemaRepository _cinemaRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly IProductRepository _productRepository;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IAIService aiService,
        IIntentRouter intentRouter,
        ISafetyFilter safetyFilter,
        IRecommendationEngine recommendationEngine,
        IConversationStore conversationStore,
        IPromptBuilder promptBuilder,
        IUserRepository userRepository,
        IBookingRepository bookingRepository,
        IUserVoucherRepository userVoucherRepository,
        IVoucherRepository voucherRepository,
        IGenreRepository genreRepository,
        ICinemaRepository cinemaRepository,
        IMovieRepository movieRepository,
        IProductRepository productRepository,
        ILogger<ChatService> logger)
    {
        _aiService = aiService;
        _intentRouter = intentRouter;
        _safetyFilter = safetyFilter;
        _recommendationEngine = recommendationEngine;
        _conversationStore = conversationStore;
        _promptBuilder = promptBuilder;
        _userRepository = userRepository;
        _bookingRepository = bookingRepository;
        _userVoucherRepository = userVoucherRepository;
        _voucherRepository = voucherRepository;
        _genreRepository = genreRepository;
        _cinemaRepository = cinemaRepository;
        _movieRepository = movieRepository;
        _productRepository = productRepository;
        _logger = logger;
    }

    public async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        var isAuth = userId.HasValue;

        // Step 1: Safety check
        var safetyCheck = _safetyFilter.Check(request.Message);
        if (!safetyCheck.IsSafe)
        {
            _logger.LogWarning("Safety filter blocked message: {Reason}", safetyCheck.Reason);
            var session = _conversationStore.GetOrCreate(request.SessionId);
            return new ChatResponse
            {
                Reply = safetyCheck.SuggestedResponse ?? "I can't process that request.",
                SessionId = session.SessionId,
            };
        }

        // Step 2: Intent classification (keyword-based, no LLM call)
        var intent = await _intentRouter.ClassifyIntentAsync(request.Message, cancellationToken);

        // Step 3: Get or create conversation session
        var conversationSession = _conversationStore.GetOrCreate(request.SessionId);

        // Step 4: Build user profile context
        string? userProfileContext = null;
        if (isAuth)
        {
            conversationSession.UserId = userId;
            userProfileContext = await BuildUserContextAsync(userId!.Value, cancellationToken);
        }

        // Step 5: Build prompt based on intent
        var prompt = intent.Intent switch
        {
            "movie" => await BuildMoviePromptAsync(intent, conversationSession, request.Message, isAuth, userProfileContext, cancellationToken),
            "fb" => await BuildFnBPromptAsync(intent, conversationSession, request.Message, isAuth, userProfileContext, cancellationToken),
            "promotion" => await BuildPromotionPromptAsync(conversationSession, request.Message, isAuth, userProfileContext, cancellationToken),
            "support" => await BuildSupportPromptAsync(conversationSession, request.Message, isAuth, userProfileContext, cancellationToken),
            _ => await BuildGeneralPromptAsync(conversationSession, request.Message, isAuth, userProfileContext, cancellationToken),
        };

        // Step 6: Call Gemini
        try
        {
            var rawReply = await _aiService.GenerateResponseAsync(prompt, cancellationToken);
            var (reply, followUps) = ParseGeminiResponse(rawReply, conversationSession);

            // Step 7: Update conversation state
            UpdateConversationState(conversationSession, intent, request.Message, reply, followUps);

            return new ChatResponse
            {
                Reply = reply,
                SessionId = conversationSession.SessionId,
                FollowUpQuestions = followUps,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI service failed for chat message");
            return new ChatResponse
            {
                Reply = "Sorry, I'm having trouble right now. Please try again later.",
                SessionId = conversationSession.SessionId,
            };
        }
    }

    private async Task<string> BuildMoviePromptAsync(
        IntentResult intent,
        ConversationSession session,
        string userMessage,
        bool isAuth,
        string? userProfileContext,
        CancellationToken ct)
    {
        // Extract preferred genre IDs from user history or intent
        var preferredGenreIds = new List<int>();
        var recentlyWatched = new List<string>();

        if (isAuth)
        {
            var userId = GetUserIdFromSession(session);
            if (userId.HasValue)
            {
                var bookings = await _bookingRepository.GetBookingsByUserAsync(userId.Value, ct);
                recentlyWatched = bookings
                    .Where(b => b.Showtime?.Movie is not null)
                    .Select(b => b.Showtime!.Movie!.Title)
                    .Distinct()
                    .ToList();
            }
        }

        if (intent.ExtractedGenre is not null)
        {
            var genres = await _genreRepository.GetGenresAsync(ct);
            var matchedGenre = genres.FirstOrDefault(g =>
                g.GenreName.Equals(intent.ExtractedGenre, StringComparison.OrdinalIgnoreCase));
            if (matchedGenre is not null)
                preferredGenreIds.Add(matchedGenre.GenreID);
        }

        var recommendations = await _recommendationEngine.GetMovieRecommendationsAsync(
            intent, preferredGenreIds, recentlyWatched, maxResults: 5, ct);

        return _promptBuilder.BuildMoviePrompt(intent, recommendations, session, userMessage, isAuth, userProfileContext);
    }

    private async Task<string> BuildFnBPromptAsync(
        IntentResult intent,
        ConversationSession session,
        string userMessage,
        bool isAuth,
        string? userProfileContext,
        CancellationToken ct)
    {
        var userVoucherCodes = new List<string>();
        if (isAuth)
        {
            var userId = GetUserIdFromSession(session);
            if (userId.HasValue)
            {
                var vouchers = await _userVoucherRepository.GetUserVouchersAsync(userId.Value, ct);
                userVoucherCodes = vouchers
                    .Where(uv => uv.Status == "available" && uv.ExpiredAt > DateTime.UtcNow && uv.Voucher is not null)
                    .Select(uv => uv.Voucher!.VoucherCode)
                    .ToList();
            }
        }

        var recommendations = await _recommendationEngine.GetFnBRecommendationsAsync(
            intent, userVoucherCodes, maxResults: 5, ct);

        return _promptBuilder.BuildFnBPrompt(intent, recommendations, session, userMessage, isAuth, userProfileContext);
    }

    private async Task<string> BuildPromotionPromptAsync(
        ConversationSession session,
        string userMessage,
        bool isAuth,
        string? userProfileContext,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var activeVouchers = await _voucherRepository.GetActiveVouchersAsync(ct);

        sb.AppendLine("--- ACTIVE VOUCHERS & PROMOTIONS (complete list) ---");
        if (activeVouchers.Count == 0)
        {
            sb.AppendLine("No active promotions at this time.");
        }
        else
        {
            foreach (var v in activeVouchers)
            {
                sb.AppendLine($"- Code: {v.VoucherCode}");
                sb.AppendLine($"  Discount: {(v.DiscountType == "percent" ? $"{v.DiscountValue}% off" : $"{v.DiscountValue:N0} VND off")}");
                if (v.MinOrderValue.HasValue)
                    sb.AppendLine($"  Min order: {v.MinOrderValue:N0} VND");
                sb.AppendLine($"  Valid: {v.ValidFrom:yyyy-MM-dd} to {v.ValidUntil:yyyy-MM-dd}");
                if (v.Description is not null)
                    sb.AppendLine($"  Description: {v.Description}");
                sb.AppendLine();
            }
        }

        return _promptBuilder.BuildPromotionPrompt(sb.ToString(), session, userMessage, isAuth, userProfileContext);
    }

    private async Task<string> BuildSupportPromptAsync(
        ConversationSession session,
        string userMessage,
        bool isAuth,
        string? userProfileContext,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var cinemas = await _cinemaRepository.GetActiveCinemasAsync(ct);

        sb.AppendLine("--- THEATER LOCATIONS (complete list) ---");
        foreach (var c in cinemas)
        {
            sb.AppendLine($"- {c.CinemaName}: {c.Address}");
        }
        sb.AppendLine();
        sb.AppendLine($"[END OF LOCATIONS — {cinemas.Count} location(s) total]");
        sb.AppendLine();
        sb.AppendLine("--- GENERAL POLICIES ---");
        sb.AppendLine("- Booking can be done through the website or mobile app.");
        sb.AppendLine("- Tickets can be cancelled before showtime according to our refund policy.");
        sb.AppendLine("- For immediate assistance, please contact the front desk at your nearest cinema.");

        return _promptBuilder.BuildSupportPrompt(sb.ToString(), session, userMessage, isAuth, userProfileContext);
    }

    private async Task<string> BuildGeneralPromptAsync(
        ConversationSession session,
        string userMessage,
        bool isAuth,
        string? userProfileContext,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var cinemas = await _cinemaRepository.GetActiveCinemasAsync(ct);

        sb.AppendLine("--- CINEMA LOCATIONS (complete list) ---");
        foreach (var c in cinemas)
        {
            sb.AppendLine($"- {c.CinemaName}: {c.Address}");
        }
        sb.AppendLine($"[END OF LOCATIONS — {cinemas.Count} location(s)]");
        sb.AppendLine();
        sb.AppendLine("--- NOW SHOWING MOVIES (complete list) ---");
        var nowShowingMovies = await _movieRepository.GetMoviesAsync("now_showing", [], null, ct);
        foreach (var m in nowShowingMovies)
        {
            sb.AppendLine($"- {m.Title} | {m.DurationMin} min | {m.AgeRating} | {string.Join(", ", m.MovieGenres.Select(mg => mg.Genre?.GenreName ?? ""))}");
        }
        if (nowShowingMovies.Count == 0)
            sb.AppendLine("No movies currently showing.");

        return _promptBuilder.BuildGeneralPrompt(sb.ToString(), session, userMessage, isAuth, userProfileContext);
    }

    private static int? GetUserId(ClaimsPrincipal? user)
    {
        if (user is null) return null;
        var claim = user.FindFirst(ClaimTypes.NameIdentifier);
        return claim is not null && int.TryParse(claim.Value, out var userId) ? userId : null;
    }

    private static int? GetUserIdFromSession(ConversationSession session)
    {
        return session.UserId;
    }

    private async Task<string> BuildUserContextAsync(int userId, CancellationToken ct)
    {
        var sb = new StringBuilder();

        var userProfile = await _userRepository.GetByIdAsync(userId, ct);
        if (userProfile is not null)
        {
            if (userProfile.LoyaltyTier is not null)
                sb.AppendLine($"Loyalty Tier: {userProfile.LoyaltyTier.TierName}");
            sb.AppendLine($"Total Points: {userProfile.TotalPoints}");
        }

        var bookings = await _bookingRepository.GetBookingsByUserAsync(userId, ct);
        if (bookings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Booking history (recent movies watched):");
            foreach (var b in bookings.Take(10))
            {
                if (b.Showtime?.Movie is not null)
                    sb.AppendLine($"- {b.Showtime.Movie.Title} (booked {b.BookingDate:yyyy-MM-dd})");
            }
        }

        var userVouchers = await _userVoucherRepository.GetUserVouchersAsync(userId, ct);
        var availableVouchers = userVouchers
            .Where(uv => uv.Status == "available" && uv.ExpiredAt > DateTime.UtcNow)
            .ToList();
        if (availableVouchers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Available vouchers:");
            foreach (var uv in availableVouchers.Take(5))
                sb.AppendLine($"- Voucher code: {uv.Voucher?.VoucherCode ?? "N/A"}");
        }

        return sb.ToString();
    }

    private static void UpdateConversationState(
        ConversationSession session,
        IntentResult intent,
        string userMessage,
        string reply,
        IReadOnlyList<string> followUps)
    {
        session.CurrentTopic = intent.Intent;
        session.MessageCount++;

        if (intent.ExtractedGenre is not null)
            session.PreferredGenre = intent.ExtractedGenre;

        // Track recommended movie titles from reply (simple extraction)
        if (intent.Intent == "movie")
        {
            var lines = reply.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("**", StringComparison.Ordinal))
                {
                    var title = line.Replace("Title:", "").Replace("**", "").Trim();
                    if (title.Length > 0 && title.Length < 100)
                    {
                        session.RecommendedMovieTitles.Add(title);
                        if (session.RecommendedMovieTitles.Count > 10)
                            session.RecommendedMovieTitles.RemoveAt(0);
                    }
                }
            }
        }

        // Track suggested follow-ups for deduplication
        foreach (var q in followUps)
        {
            if (!session.SuggestedFollowUps.Contains(q, StringComparer.OrdinalIgnoreCase))
                session.SuggestedFollowUps.Add(q);
        }
        if (session.SuggestedFollowUps.Count > 20)
            session.SuggestedFollowUps.RemoveRange(0, session.SuggestedFollowUps.Count - 20);

        // Store conversation turn (user message + AI reply)
        session.Turns.Add(new ConversationTurn(userMessage, reply));
        if (session.Turns.Count > 6)
            session.Turns.RemoveAt(0);
    }

    private (string Reply, IReadOnlyList<string> FollowUps) ParseGeminiResponse(
        string rawResponse,
        ConversationSession session)
    {
        var trimmed = rawResponse.Trim();

        // Strip markdown code fences if present
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].TrimEnd();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiChatResponse>(trimmed, JsonOptions);
            if (parsed is not null
                && !string.IsNullOrWhiteSpace(parsed.Reply)
                && parsed.FollowUpQuestions.Count > 0)
            {
                // Deduplicate follow-ups against session history
                var deduped = parsed.FollowUpQuestions
                    .Where(q => !session.SuggestedFollowUps.Contains(q, StringComparer.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();

                // If all were duplicates, keep the parsed ones anyway (better than empty)
                var finalFollowUps = deduped.Count > 0
                    ? deduped
                    : parsed.FollowUpQuestions.Take(3).ToList();

                return (parsed.Reply, finalFollowUps);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini response as JSON, falling back to plain text");
        }

        // Fallback: treat entire response as the reply, no follow-ups
        return (trimmed, []);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
