namespace MyBudget.Application.Money;

/// <summary>Raised when an amount is requested for a currency that is not registered.</summary>
public sealed class UnsupportedCurrencyException(string currencyCode)
    : InvalidOperationException($"Currency '{currencyCode}' is not registered.")
{
    public string CurrencyCode { get; } = currencyCode;
}

public interface ICurrencyRegistry
{
    IReadOnlyCollection<CurrencyDefinition> All { get; }

    bool TryGet(string currencyCode, out CurrencyDefinition definition);

    /// <summary>Throws <see cref="UnsupportedCurrencyException"/> for an unknown code.</summary>
    CurrencyDefinition Get(string currencyCode);
}

/// <summary>
/// The currencies the application knows about. Spanish and COP ship first; the registry
/// exists so a second currency is a registration, not a rewrite.
/// </summary>
public sealed class CurrencyRegistry : ICurrencyRegistry
{
    private readonly Dictionary<string, CurrencyDefinition> _byCode;

    public CurrencyRegistry()
        : this([CurrencyDefinition.ColombianPeso])
    {
    }

    public CurrencyRegistry(IEnumerable<CurrencyDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _byCode = definitions.ToDictionary(
            definition => definition.Code, StringComparer.OrdinalIgnoreCase);

        if (_byCode.Count == 0)
        {
            throw new ArgumentException("At least one currency must be registered.", nameof(definitions));
        }
    }

    public IReadOnlyCollection<CurrencyDefinition> All => _byCode.Values;

    public bool TryGet(string currencyCode, out CurrencyDefinition definition) =>
        _byCode.TryGetValue(currencyCode?.Trim() ?? string.Empty, out definition!);

    public CurrencyDefinition Get(string currencyCode) =>
        TryGet(currencyCode, out var definition)
            ? definition
            : throw new UnsupportedCurrencyException(currencyCode);
}
