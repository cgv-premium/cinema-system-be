using CinemaBooking.Application.Common.ImageFiles;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Common.Security;
using CinemaBooking.Application.Users;
using CinemaBooking.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace CinemaBooking.API.Tests;

public sealed class UserServicePasswordTests
{
    [Fact]
    public async Task ChangePasswordAsync_ValidPasswords_UpdatesPasswordHash()
    {
        var repository = new StubUserRepository("OldPassword1!");
        var service = CreateService(repository);

        var result = await service.ChangePasswordAsync(1, "OldPassword1!", "NewPassword2!", "NewPassword2!");

        Assert.True(result.Succeeded);
        Assert.NotNull(repository.NewPasswordHash);
        Assert.True(PasswordHasher.Verify("NewPassword2!", repository.NewPasswordHash, out _));
    }

    [Fact]
    public async Task ChangePasswordAsync_IncorrectCurrentPassword_DoesNotUpdatePassword()
    {
        var repository = new StubUserRepository("OldPassword1!");
        var service = CreateService(repository);

        var result = await service.ChangePasswordAsync(1, "WrongPassword1!", "NewPassword2!", "NewPassword2!");

        Assert.False(result.Succeeded);
        Assert.Equal("Current password is incorrect.", result.ErrorMessage);
        Assert.Null(repository.NewPasswordHash);
    }

    [Fact]
    public async Task ChangePasswordAsync_NewPasswordMatchesCurrent_DoesNotUpdatePassword()
    {
        var repository = new StubUserRepository("OldPassword1!");
        var service = CreateService(repository);

        var result = await service.ChangePasswordAsync(1, "OldPassword1!", "OldPassword1!", "OldPassword1!");

        Assert.False(result.Succeeded);
        Assert.Equal("New password must be different from the current password.", result.ErrorMessage);
        Assert.Null(repository.NewPasswordHash);
    }

    private static UserService CreateService(StubUserRepository repository) =>
        new(repository, new StubUserVoucherRepository(), new StubImageStorageService(), NullLogger<UserService>.Instance);

    private sealed class StubUserRepository : IUserRepository
    {
        private readonly User _user;
        public string? NewPasswordHash { get; private set; }

        public StubUserRepository(string password) => _user = new User
        {
            UserID = 1,
            PasswordHash = PasswordHasher.Hash(password)
        };

        public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == _user.UserID ? _user : null);
        public Task<bool> TryUpdatePasswordHashAsync(int userId, string expectedPasswordHash,
            string newPasswordHash, CancellationToken cancellationToken = default)
        {
            if (userId != _user.UserID || expectedPasswordHash != _user.PasswordHash) return Task.FromResult(false);
            NewPasswordHash = newPasswordHash;
            return Task.FromResult(true);
        }

        public Task<User?> GetProfileByIdAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> LookupCustomerAsync(string? email, string? phone, string? barcode, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> PhoneExistsAsync(string phone, int? excludingUserId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> UpdateProfileAsync(int userId, string fullName, string? phone, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<User?> UpdateAvatarAsync(int userId, string? avatarUrl, string? avatarPublicId, CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);
        public Task<Wallet?> GetWalletByUserIdAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult<Wallet?>(null);
        public Task<bool> DeleteAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddUserWithWalletAsync(User user, Wallet wallet, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddUserWithWalletAndVerificationTokenAsync(User user, Wallet wallet, EmailVerificationToken verificationToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddEmailVerificationTokenAsync(EmailVerificationToken verificationToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<EmailVerificationToken?> GetEmailVerificationTokenAsync(string token, CancellationToken cancellationToken = default) => Task.FromResult<EmailVerificationToken?>(null);
        public Task<EmailVerificationToken?> GetLatestEmailVerificationTokenAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult<EmailVerificationToken?>(null);
        public Task AddPasswordResetTokenAsync(PasswordResetToken resetToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PasswordResetToken?> GetLatestPasswordResetTokenAsync(int userId, CancellationToken cancellationToken = default) => Task.FromResult<PasswordResetToken?>(null);
        public Task ReplaceUnusedPasswordResetTokensAsync(int userId, PasswordResetToken resetToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReplaceUnverifiedEmailVerificationTokensAsync(int userId, EmailVerificationToken verificationToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteEmailVerificationTokenAsync(string token, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeletePasswordResetTokenAsync(string token, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryResetPasswordAsync(string token, string passwordHash, DateTime resetAt, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TryIncrementTokenVersionAsync(int userId, int expectedTokenVersion, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> BarCodeExistsAsync(string barcode, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<List<User>> GetUsersByRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken = default) => Task.FromResult(new List<User>());
    }

    private sealed class StubImageStorageService : IImageStorageService
    {
        public Task<StoredImageResult> UploadImageAsync(Stream imageStream, string fileName, string folder,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteImageAsync(string publicId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubUserVoucherRepository : IUserVoucherRepository
    {
        public Task<List<UserVoucher>> GetUserVouchersAsync(int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<UserVoucher>());
        public Task<UserVoucher?> GetUserVoucherByIdAsync(int userVoucherId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserVoucher?>(null);
        public Task<UserVoucher?> GetUserVoucherByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserVoucher?>(null);
        public Task AddUserVoucherAsync(UserVoucher userVoucher, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task UpdateUserVoucherAsync(UserVoucher userVoucher, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<bool> HasUsedVoucherCodeAsync(int userId, string voucherCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<UserVoucher?> GetByIdAsync(int userVoucherId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserVoucher?>(null);

        public Task<(bool Succeeded, string? Error)> RedeemVoucherAsync(
            UserVoucher userVoucher,
            LoyaltyPoints loyaltyPoint,
            int pointsToDeduct,
            int? maxUses,
            int? exchangeLimit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(bool, string?)>((false, "Not supported"));

        public Task<UserVoucher?> GetAvailableOwnedAsync(
            int userId,
            int voucherId,
            DateTime now,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UserVoucher?>(null);

        public Task<UserVoucher?> GetAvailableForUpdateAsync(
            int userId,
            int voucherId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UserVoucher?>(null);

        public Task MarkReservedAsUsedByBookingAsync(
            int bookingId,
            DateTime usedAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReleaseReservedByBookingAsync(
            int bookingId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
