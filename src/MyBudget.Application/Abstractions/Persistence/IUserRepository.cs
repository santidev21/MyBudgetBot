using MyBudget.Domain.Users;

namespace MyBudget.Application.Abstractions.Persistence;

public interface IUserRepository
{
    /// <summary>Identity lookup. Never resolves a user by username.</summary>
    Task<User?> FindByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    void Add(User user);
}
