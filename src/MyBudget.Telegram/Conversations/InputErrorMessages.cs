using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// Maps a parse failure to the message that explains it. Shared so the entry and edit flows
/// never drift into telling the user different things about the same input.
/// </summary>
internal static class InputErrorMessages
{
    public static string ForAmount(MoneyParseError reason) => reason switch
    {
        MoneyParseError.Negative => MessageKeys.AmountNegative,
        MoneyParseError.FractionNotAllowed => MessageKeys.AmountFractionNotAllowed,
        MoneyParseError.TooLarge => MessageKeys.AmountTooLarge,
        _ => MessageKeys.AmountInvalid,
    };

    public static string ForDate(DateParseError reason) => reason switch
    {
        DateParseError.Future => MessageKeys.DateFuture,
        DateParseError.TooOld => MessageKeys.DateTooOld,
        _ => MessageKeys.DateInvalid,
    };
}
