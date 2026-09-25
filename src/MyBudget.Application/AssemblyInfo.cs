using System.Runtime.CompilerServices;

// The parsing stages are internal so the public surface stays small, but they are meant to be
// unit tested in isolation: a defect in separator handling should be findable without going
// through the whole pipeline.
[assembly: InternalsVisibleTo("MyBudget.Application.Tests")]
