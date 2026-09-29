using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Users;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(MyBudgetDbContext dbContext) : IUserRepository
{
    public Task<User?> FindByTelegramUserIdAsync(
        long telegramUserId, CancellationToken cancellationToken = default)
        => dbContext.Users.FirstOrDefaultAsync(
            user => user.TelegramUserId == telegramUserId, cancellationToken);

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => dbContext.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.Users.AsNoTracking().OrderBy(user => user.Id).ToListAsync(cancellationToken);

    public void Add(User user) => dbContext.Users.Add(user);
}
