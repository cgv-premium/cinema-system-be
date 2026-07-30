using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Features.AI;
using CinemaBooking.Application.Features.AI.DTOs;

namespace CinemaBooking.Infrastructure.Services;

public sealed class RecommendationEngine : IRecommendationEngine
{
    private readonly IMovieRepository _movieRepository;
    private readonly IProductRepository _productRepository;
    private readonly IGenreRepository _genreRepository;
    private readonly IMovieReviewRepository _reviewRepository;

    public RecommendationEngine(
        IMovieRepository movieRepository,
        IProductRepository productRepository,
        IGenreRepository genreRepository,
        IMovieReviewRepository reviewRepository)
    {
        _movieRepository = movieRepository;
        _productRepository = productRepository;
        _genreRepository = genreRepository;
        _reviewRepository = reviewRepository;
    }

    public async Task<IReadOnlyList<MovieRecommendation>> GetMovieRecommendationsAsync(
        IntentResult intent,
        IReadOnlyList<int> preferredGenreIds,
        IReadOnlyList<string> recentlyWatchedTitles,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        var movies = await _movieRepository.GetMoviesAsync(null, [], null, cancellationToken);

        var movieIds = movies.Select(m => m.MovieID).ToList();
        var statsMap = await _reviewRepository.GetVisibleStatsForMoviesAsync(movieIds, cancellationToken);

        var scored = new List<(Domain.Entities.Movie Movie, decimal Score, List<string> Reasons)>();

        foreach (var movie in movies)
        {
            var score = 0m;
            var reasons = new List<string>();

            // Genre matching
            if (preferredGenreIds.Count > 0 && movie.MovieGenres.Count > 0)
            {
                var movieGenreIds = movie.MovieGenres
                    .Where(mg => mg.Genre is not null)
                    .Select(mg => mg.Genre!.GenreID)
                    .ToList();

                var matchingGenres = movieGenreIds.Intersect(preferredGenreIds).ToList();
                if (matchingGenres.Count > 0)
                {
                    var genreNames = movie.MovieGenres
                        .Where(mg => matchingGenres.Contains(mg.GenreID))
                        .Select(mg => mg.Genre?.GenreName ?? "Unknown")
                        .ToList();
                    score += matchingGenres.Count * 2.0m;
                    reasons.Add($"Matches your preferred genres: {string.Join(", ", genreNames)}");
                }
            }

            // Genre extraction from intent
            if (intent.ExtractedGenre is not null && movie.MovieGenres.Count > 0)
            {
                var hasGenre = movie.MovieGenres.Any(mg =>
                    mg.Genre?.GenreName.Equals(intent.ExtractedGenre, StringComparison.OrdinalIgnoreCase) == true);
                if (hasGenre)
                {
                    score += 1.5m;
                    reasons.Add($"Matches requested genre: {intent.ExtractedGenre}");
                }
            }

            // Review sentiment (higher rating = better)
            var stats = statsMap[movie.MovieID];
            if (stats.TotalReviews > 0 && stats.AverageRating.HasValue)
            {
                var avgRating = stats.AverageRating.Value;
                // Rating contributes 0-2 points based on average
                score += (decimal)avgRating / 5.0m * 2.0m;
                if (avgRating >= 4.0)
                    reasons.Add($"Highly rated ({avgRating:F1}/5 from {stats.TotalReviews} reviews)");
                else if (avgRating >= 3.0)
                    reasons.Add($"Well rated ({avgRating:F1}/5 from {stats.TotalReviews} reviews)");
            }

            // Avoid recently watched (negative score)
            if (recentlyWatchedTitles.Any(t =>
                t.Equals(movie.Title, StringComparison.OrdinalIgnoreCase)))
            {
                score -= 5.0m;
                reasons.Add("You recently watched this movie");
            }

            // Prefer movies with more reviews (social proof)
            if (stats.TotalReviews > 5)
                score += 0.5m;

            // Duration preference: prefer shorter for general, longer for specific genre requests
            if (intent.Intent == "movie" && intent.ExtractedGenre is not null)
            {
                // For specific genre requests, longer movies are fine
                if (movie.DurationMin >= 120)
                    score += 0.3m;
            }
            else
            {
                // For general recommendations, prefer standard length
                if (movie.DurationMin is >= 90 and <= 120)
                    score += 0.3m;
            }

            scored.Add((movie, score, reasons));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => MapToRecommendation(x.Movie, x.Score, x.Reasons))
            .ToList();
    }

    public async Task<IReadOnlyList<FnBRecommendation>> GetFnBRecommendationsAsync(
        IntentResult intent,
        IReadOnlyList<string> userVoucherCodes,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAvailableProductsAsync(cancellationToken);

        var scored = new List<(Domain.Entities.Product Product, decimal Score, List<string> Reasons)>();

        foreach (var product in products)
        {
            var score = 0m;
            var reasons = new List<string>();

            // Keyword matching from intent
            if (intent.ExtractedKeyword is not null)
            {
                if (product.ItemName.Contains(intent.ExtractedKeyword, StringComparison.OrdinalIgnoreCase) ||
                    (product.Description?.Contains(intent.ExtractedKeyword, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    score += 2.0m;
                    reasons.Add($"Matches your search: \"{intent.ExtractedKeyword}\"");
                }
            }

            // Combo items get a boost (popular choices)
            if (product.ItemType.Equals("combo", StringComparison.OrdinalIgnoreCase))
            {
                score += 1.0m;
                reasons.Add("Popular combo option");
            }

            // Loyalty eligible items get a boost for members
            if (product.IsLoyaltyEligible)
            {
                score += 0.5m;
                reasons.Add("Loyalty point eligible");
            }

            // Beverage items alongside movie recommendations
            if (intent.Intent == "fb")
            {
                if (product.ItemType.Equals("drink", StringComparison.OrdinalIgnoreCase) ||
                    product.ItemType.Equals("beverage", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.5m;
                }
                if (product.ItemType.Equals("snack", StringComparison.OrdinalIgnoreCase) ||
                    product.ItemType.Equals("food", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.5m;
                }
            }

            scored.Add((product, score, reasons));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => new FnBRecommendation
            {
                ItemId = x.Product.ItemID,
                ItemName = x.Product.ItemName,
                ItemType = x.Product.ItemType,
                Price = x.Product.Price,
                Description = x.Product.Description,
                Reasons = x.Reasons,
            })
            .ToList();
    }

    private static MovieRecommendation MapToRecommendation(
        Domain.Entities.Movie movie,
        decimal score,
        List<string> reasons)
    {
        var genres = movie.MovieGenres
            .Where(mg => mg.Genre is not null)
            .Select(mg => mg.Genre!.GenreName)
            .ToList();

        var actors = movie.MoviePersons
            .Where(mp => mp.Role == "actor" && mp.Person is not null)
            .OrderBy(mp => mp.DisplayOrder)
            .Select(mp => mp.Person!.Name)
            .ToList();

        var directors = movie.MoviePersons
            .Where(mp => mp.Role == "director" && mp.Person is not null)
            .OrderBy(mp => mp.DisplayOrder)
            .Select(mp => mp.Person!.Name)
            .ToList();

        return new MovieRecommendation
        {
            MovieId = movie.MovieID,
            Title = movie.Title,
            AgeRating = movie.AgeRating,
            DurationMin = movie.DurationMin,
            Description = movie.Description,
            ShowingFrom = movie.ShowingFrom,
            ShowingTo = movie.ShowingTo,
            Genres = genres,
            Actors = actors,
            Directors = directors,
            Reasons = reasons,
        };
    }
}
