using System.Runtime.CompilerServices;

// The dispatcher, the router and the conversations are internal so the public surface stays
// small, but the pipeline is exactly what needs testing.
[assembly: InternalsVisibleTo("MyBudget.Telegram.Tests")]
