using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Budgets;
using MyBudget.Application.Categories;
using MyBudget.Application.Configuration;
using MyBudget.Application.Dates;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Matching;
using MyBudget.Application.Money;
using MyBudget.Application.Recurring;
using MyBudget.Application.Reporting;
using MyBudget.Application.Users;
using MyBudget.Domain.Users;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.RateLimiting;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The real dispatcher, router and onboarding conversation wired to in-memory stores.
/// <para>
/// Only the boundaries are doubled. Everything that decides what a user sees and what gets
/// persisted is the production code path.
/// </para>
/// </summary>
internal sealed class TelegramHarness
{
    private TelegramHarness(User user, TelegramOptions options, TimeProvider clock, CategoryMatchingOptions matching)
    {
        User = user;
        Options = options;
        Clock = clock;

        var localization = Microsoft.Extensions.Options.Options.Create(new LocalizationOptions());
        Messages = new ResourceUserMessages(localization);
        Menu = new MainMenu(Messages);
        Matcher = new CategoryMatcher(Microsoft.Extensions.Options.Options.Create(matching));

        Users = Substitute.For<IUserService>();
        Users.GetOrCreateAsync(Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(user);

        Onboarding = new StartConversation(Messages, Menu, UnitOfWork);

        CategoryService = Substitute.For<ICategoryService>();
        BudgetService = Substitute.For<IBudgetService>();
        ExpenseService = Substitute.For<IExpenseService>();
        RecurringService = Substitute.For<IRecurringExpenseService>();
        ReportService = Substitute.For<IReportService>();
        PendingActions = Substitute.For<IPendingActionStore>();
        Eraser = Substitute.For<IUserDataEraser>();

        var currencies = new CurrencyRegistry();
        var formatter = new MoneyFormatter(currencies);
        var moneyParser = new MoneyParser(currencies, formatter);
        var dateParser = new DateParser();
        var localDate = new UserLocalDate(clock);
        var compactParser = new CompactExpenseParser(currencies, moneyParser);
        var telegramOptions = Microsoft.Extensions.Options.Options.Create(options);
        RateLimiter = new SlidingWindowUserRateLimiter(telegramOptions, clock);

        Categories = new CategoriesConversation(
            Messages,
            CategoryService,
            BudgetService,
            moneyParser,
            formatter,
            localDate,
            Menu);

        Expenses = new ExpenseConversation(
            Messages,
            ExpenseService,
            CategoryService,
            PendingActions,
            moneyParser,
            formatter,
            dateParser,
            Matcher,
            localDate,
            clock,
            telegramOptions,
            Menu);

        UndoHandler = new ExpenseUndoHandler(ExpenseService, Messages, Menu);

        ExpenseList = new ExpensesConversation(
            Messages, ExpenseService, ReportService, CategoryService, moneyParser, formatter, dateParser,
            localDate, Menu);

        Summary = new SummaryConversation(Messages, ReportService, formatter, localDate);
        CategoryDetail = new CategoryDetailConversation(Messages, ReportService, formatter, localDate);
        Statistics = new StatisticsConversation(Messages, ReportService, formatter, localDate);
        Settings = new SettingsConversation(Messages, Menu, Eraser, UnitOfWork);
        Recurring = new RecurringConversation(
            Messages, RecurringService, CategoryService, moneyParser, formatter, localDate, Menu);

        Router = new ConversationRouter(
            Conversations,
            Messages,
            Menu,
            [Onboarding, Categories, Expenses, ExpenseList, Recurring, Summary, CategoryDetail, Statistics, Settings],
            [UndoHandler],
            compactParser,
            telegramOptions,
            clock);

        Dispatcher = new TelegramUpdateDispatcher(
            Inbox,
            Users,
            new PassThroughUserWorkLock(),
            Conversations,
            Router,
            Sender,
            Messages,
            RateLimiter,
            Microsoft.Extensions.Options.Options.Create(options),
            clock,
            NullLogger<TelegramUpdateDispatcher>.Instance);
    }

    public User User { get; }

    public TelegramOptions Options { get; }

    public TimeProvider Clock { get; }

    public IUserMessages Messages { get; }

    public MainMenu Menu { get; }

    public ICategoryMatcher Matcher { get; }

    public IUserService Users { get; }

    public StartConversation Onboarding { get; }

    public ICategoryService CategoryService { get; }

    public IBudgetService BudgetService { get; }

    public IReportService ReportService { get; }

    public CategoriesConversation Categories { get; }

    public IExpenseService ExpenseService { get; }

    public IRecurringExpenseService RecurringService { get; }

    public RecurringConversation Recurring { get; }

    public IPendingActionStore PendingActions { get; }

    public ExpenseConversation Expenses { get; }

    public ExpensesConversation ExpenseList { get; }

    public SummaryConversation Summary { get; }

    public CategoryDetailConversation CategoryDetail { get; }

    public StatisticsConversation Statistics { get; }

    public SettingsConversation Settings { get; }

    public IUserDataEraser Eraser { get; }

    public ExpenseUndoHandler UndoHandler { get; }

    public ConversationRouter Router { get; }

    public TelegramUpdateDispatcher Dispatcher { get; }

    public InMemoryUpdateInbox Inbox { get; } = new();

    public InMemoryConversationStore Conversations { get; } = new();

    public RecordingTelegramSender Sender { get; } = new();

    public IUserRateLimiter RateLimiter { get; }

    public RecordingUnitOfWork UnitOfWork { get; } = new();

    public static TelegramHarness Build(
        long telegramUserId = 999,
        TelegramOptions? options = null,
        TimeProvider? clock = null,
        CategoryMatchingOptions? matching = null)
    {
        var settings = options ?? new TelegramOptions
        {
            BotToken = "test-token",
            WebhookSecret = "test-webhook-secret-value",
            WebhookPath = "test-webhook-path-value",
            PublicBaseUrl = "https://example.test",
            AllowedUserIds = telegramUserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var user = new User(telegramUserId, "tester", "Test User");

        return new TelegramHarness(user, settings, clock ?? FixedClock.At(TestClock.Now), matching ?? new CategoryMatchingOptions());
    }
}
