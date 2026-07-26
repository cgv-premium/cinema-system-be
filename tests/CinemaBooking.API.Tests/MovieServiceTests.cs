using CinemaBooking.Application.Common.ImageFiles;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Movie;
using CinemaBooking.Domain.Entities;

namespace CinemaBooking.API.Tests;

public sealed class MovieServiceTests
{
    [Theory]
    [InlineData(1, 2, "coming_soon")]
    [InlineData(-1, 1, "now_showing")]
    [InlineData(-2, -1, "ended")]
    public async Task CreateMovieAsync_Dates_CalculatesStatus(
        int startOffsetDays, int endOffsetDays, string expectedStatus)
    {
        var repository = new StubMovieRepository();
        var service = new MovieService(repository, new StubImageStorageService());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await service.CreateMovieAsync(
            "Movie", null, "P", new[] { 1 }, Array.Empty<int>(), null, 120,
            today.AddDays(startOffsetDays), today.AddDays(endOffsetDays),
            null, null, null);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedStatus, result.Movie!.Status);
    }

    [Fact]
    public async Task UpdateMovieAsync_StatusIsNull_PreservesExistingStatus()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repository = new StubMovieRepository
        {
            ExistingMovie = new Movie
            {
                MovieID = 1, Status = "ended", PosterPublicId = null
            }
        };
        var service = new MovieService(repository, new StubImageStorageService());

        var result = await service.UpdateMovieAsync(
            1, "Movie", null, "P", new[] { 1 }, Array.Empty<int>(), null, 120,
            today, today.AddDays(1), null, null, null, null);

        Assert.True(result.Succeeded);
        Assert.Equal("ended", repository.UpdatedStatus);
    }

    [Fact]
    public async Task UpdateMovieAsync_ManualStatus_UsesProvidedStatus()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repository = new StubMovieRepository
        {
            ExistingMovie = new Movie
            {
                MovieID = 1, Status = "ended", PosterPublicId = null
            }
        };
        var service = new MovieService(repository, new StubImageStorageService());

        var result = await service.UpdateMovieAsync(
            1, "Movie", null, "P", new[] { 1 }, Array.Empty<int>(), null, 120,
            today, today.AddDays(1), null, null, null, "NOW_SHOWING");

        Assert.True(result.Succeeded);
        Assert.Equal("now_showing", repository.UpdatedStatus);
    }

    [Fact]
    public async Task GetMoviesAsync_GenreIds_ForwardsOrFilterWithStatus()
    {
        var repository = new StubMovieRepository();
        var service = new MovieService(repository, new StubImageStorageService());

        await service.GetMoviesAsync("now_showing", new[] { 1, 2 }, cinemaId: null);

        Assert.Equal("now_showing", repository.Status);
        Assert.Equal(new[] { 1, 2 }, repository.GenreIds);
    }

    [Fact]
    public async Task GetMovieSalesAsync_RanksGloballyAndMarksOnlyTopFive()
    {
        var repository = new StubMovieRepository
        {
            TicketSales =
            [
                new(6, "Zulu", 10),
                new(2, "Beta", 30),
                new(1, "Alpha", 30),
                new(3, "Gamma", 20),
                new(4, "Delta", 15),
                new(5, "Epsilon", 12),
                new(7, "No Sales", 0)
            ]
        };
        var service = new MovieService(repository, new StubImageStorageService());

        var result = await service.GetMovieSalesAsync();

        Assert.Equal(1, result[1].SalesRank);
        Assert.Equal(2, result[2].SalesRank);
        Assert.True(result[5].IsTopSelling);
        Assert.False(result[6].IsTopSelling);
        Assert.Equal(6, result[6].SalesRank);
        Assert.False(result.ContainsKey(7));
    }

    private sealed class StubMovieRepository : IMovieRepository
    {
        public string? Status { get; private set; }
        public IReadOnlyCollection<int> GenreIds { get; private set; } = Array.Empty<int>();
        public Movie? ExistingMovie { get; init; }
        public string? UpdatedStatus { get; private set; }
        public List<MovieTicketSales> TicketSales { get; init; } = [];

        public Task<List<Movie>> GetMoviesAsync(
            string? status, IReadOnlyCollection<int> genreIds, int? cinemaId,
            CancellationToken cancellationToken = default)
        {
            Status = status;
            GenreIds = genreIds;
            return Task.FromResult(new List<Movie>());
        }

        public Task<bool> CinemaExistsAsync(int cinemaId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> TitleExistsAsync(
            string title,
            int? excludingMovieId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<List<MovieTicketSales>> GetMovieTicketSalesAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(TicketSales);

        public Task<Movie?> GetByIdAsync(int movieId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingMovie?.MovieID == movieId ? ExistingMovie : null);
        public Task<Movie?> GetMovieByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Movie?>(null);
        public Task<List<Movie>> SearchMoviesAsync(string keyword, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Movie>());
        public Task<(Movie? Movie, IReadOnlyList<int> MissingPersonIds)> AddMovieAsync(
            Movie movie, IReadOnlyCollection<string> genreNames,
            IReadOnlyList<int> directorIds, IReadOnlyList<int> actorIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(Movie? Movie, IReadOnlyList<int> MissingPersonIds)>((movie, Array.Empty<int>()));
        public Task<(Movie? Movie, IReadOnlyList<int> MissingPersonIds)> UpdateMovieAsync(
            int movieId, string title,
            IReadOnlyCollection<string>? genreNames, string ageRating,
            IReadOnlyList<int> directorIds, IReadOnlyList<int> actorIds,
            string? synopsis, int durationMinutes, DateOnly? showingFromDate, DateOnly? showingToDate,
            string? posterUrl, string? posterPublicId, string? trailerUrl, string status, DateTime updatedAt,
            CancellationToken cancellationToken = default)
        {
            UpdatedStatus = status;
            if (ExistingMovie is not null) ExistingMovie.Status = status;
            return Task.FromResult<(Movie? Movie, IReadOnlyList<int> MissingPersonIds)>((ExistingMovie, Array.Empty<int>()));
        }
        public Task<Movie?> UpdatePosterAsync(int movieId, string posterUrl, string posterPublicId,
            DateTime updatedAt, CancellationToken cancellationToken = default) => Task.FromResult<Movie?>(null);
        public Task<bool> HasActiveOrUpcomingShowtimesAsync(int movieId, DateTime now,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> DeleteMovieAsync(int movieId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<List<Movie>> GetMoviesByIdsAsync(List<int> movieIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Movie>());
    }

    private sealed class StubImageStorageService : IImageStorageService
    {
        public Task<StoredImageResult> UploadImageAsync(Stream imageStream, string fileName, string folder,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteImageAsync(string publicId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
