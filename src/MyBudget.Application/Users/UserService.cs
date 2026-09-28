using Microsoft.Extensions.Options;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Configuration;
using MyBudget.Domain.Users;

namespace MyBudget.Application.Users;

public interface IUserService
{
    /// <summary>Resolves the user behind a Telegram identity, creating them on first contact.</summary>
    Task<User> GetOrCreateAsync(
        long telegramUserId,
        string? username,
        string? displayName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identity resolution.
/// <para>
/// Telegram can deliver several updates at once when somebody opens the bot for the first
/// time, so "read, then insert" is not safe. The insert is attempted and the unique constraint
/// decides the winner; the loser re-reads. That keeps first contact correct without making
/// every message wait on a lock it does not need.
/// </para>
/// </summary>
public sealed class UserService(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IOptions<LocalizationOptions> localization) : IUserService
{
    public async Task<User> GetOrCreateAsync(
        long telegramUserId,
        string? username,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(telegramUserId), telegramUserId, "Telegram user id must be positive.");
        }

        var existing = await users.FindByTelegramUserIdAsync(telegramUserId, cancellationToken);
        if (existing is not null)
        {
            // The username is a display snapshot and can change at any time. Keep it fresh,
            // but do not write on every single message.
            if (existing.UpdateProfile(username, displayName))
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return existing;
        }

        var defaults = localization.Value;
        var created = new User(telegramUserId, username, displayName);
        created.ChangeLanguage(defaults.DefaultLanguage);
        created.ChangeTimeZone(defaults.DefaultTimeZone);
        users.Add(created);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (UniqueConstraintViolationException)
        {
            unitOfWork.Detach(created);

            return await users.FindByTelegramUserIdAsync(telegramUserId, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"User {telegramUserId} could not be resolved after a concurrent insert.");
        }
    }
}
