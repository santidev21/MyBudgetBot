using System.Runtime.CompilerServices;

// Repositories are internal: the application layer depends on their interfaces only.
// Integration tests need to exercise the concrete implementations directly.
[assembly: InternalsVisibleTo("MyBudget.Infrastructure.Tests")]
