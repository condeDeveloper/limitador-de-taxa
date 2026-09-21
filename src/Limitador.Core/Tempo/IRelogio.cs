namespace Limitador.Core.Tempo;

/// <summary>
/// A fonte de tempo do limitador.
/// </summary>
/// <remarks>
/// Testar limitação de taxa contra o relógio de verdade significa dormir, e
/// teste que dorme é teste lento e instável. Com o relógio na mão, "um minuto
/// depois" é uma linha de código.
/// </remarks>
public interface IRelogio
{
    /// <summary>Instante atual em UTC.</summary>
    DateTimeOffset Agora { get; }
}

/// <summary>Relógio ligado ao do sistema.</summary>
public sealed class RelogioDoSistema : IRelogio
{
    /// <summary>Instância única, já que não guarda estado.</summary>
    public static RelogioDoSistema Instancia { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}

/// <summary>Relógio controlado manualmente, para uso em teste.</summary>
public sealed class RelogioFixo : IRelogio
{
    private DateTimeOffset agora;

    /// <summary>Cria o relógio parado no instante informado.</summary>
    public RelogioFixo(DateTimeOffset? inicio = null)
    {
        agora = inicio ?? new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    }

    /// <inheritdoc />
    public DateTimeOffset Agora => agora;

    /// <summary>Avança o relógio.</summary>
    public RelogioFixo Avancar(TimeSpan quanto)
    {
        if (quanto < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quanto), "O relógio não anda para trás.");
        }

        agora = agora.Add(quanto);
        return this;
    }

    /// <summary>Avança o relógio em segundos.</summary>
    public RelogioFixo AvancarSegundos(double segundos) => Avancar(TimeSpan.FromSeconds(segundos));
}
