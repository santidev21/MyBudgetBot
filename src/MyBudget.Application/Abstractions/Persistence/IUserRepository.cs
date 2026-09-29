using MyBudget.Domain.Users;

namespace MyBudget.Application.Abstractions.Persistence;

public interface IUserRepository
{
    /// <summary>Identity lookup. Never resolves a user by username.</summary>
    Task<User?> FindByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every user, for the scheduled pass that has to discover whose calendar day it is.
    /// <para>
    /// Deliberately not user-scoped: there is no single owner to scope by, exactly like
    /// <see cref="IRecurringExpenseRepository.ListActiveAsync"/>. Callers must still write
    /// through each user's own identifier.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<User>> ListAllAsync(CancellationToken cancellationToken = default);

    void Add(User user);
}
