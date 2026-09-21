namespace Limitador.Core;

/// <summary>
/// Quanto se pode gastar e em quanto tempo.
/// </summary>
/// <param name="Permitidas">Quantas requisições cabem na janela.</param>
/// <param name="Janela">O tamanho da janela.</param>
public readonly record struct Politica(int Permitidas, TimeSpan Janela)
{
    /// <summary>Tantas requisições por segundo.</summary>
    public static Politica PorSegundo(int permitidas) => new(permitidas, TimeSpan.FromSeconds(1));

    /// <summary>Tantas requisições por minuto.</summary>
    public static Politica PorMinuto(int permitidas) => new(permitidas, TimeSpan.FromMinutes(1));

    /// <summary>Tantas requisições por hora.</summary>
    public static Politica PorHora(int permitidas) => new(permitidas, TimeSpan.FromHours(1));

    /// <summary>Tantas requisições por dia.</summary>
    public static Politica PorDia(int permitidas) => new(permitidas, TimeSpan.FromDays(1));

    /// <summary>Quantas requisições por segundo a política permite, em média.</summary>
    public double PorSegundoMedio => Permitidas / Janela.TotalSeconds;

    /// <summary>Quanto tempo se leva para recuperar uma unidade.</summary>
    public TimeSpan IntervaloEntreRequisicoes => Janela / Permitidas;

    /// <summary>Confere os valores e devolve a própria política.</summary>
    public Politica Validar()
    {
        if (Permitidas <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Permitidas), "A política precisa permitir ao menos uma requisição.");
        }

        if (Janela <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Janela), "A janela precisa ser positiva.");
        }

        return this;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Permitidas} por {Descrever(Janela)}";

    private static string Descrever(TimeSpan janela) => janela switch
    {
        { TotalDays: >= 1 } => $"{janela.TotalDays:0.##}d",
        { TotalHours: >= 1 } => $"{janela.TotalHours:0.##}h",
        { TotalMinutes: >= 1 } => $"{janela.TotalMinutes:0.##}min",
        _ => $"{janela.TotalSeconds:0.##}s",
    };
}

/// <summary>
/// O que o limitador respondeu.
/// </summary>
/// <param name="Permitida">Se a requisição pode seguir.</param>
/// <param name="Restantes">Quantas ainda cabem na janela.</param>
/// <param name="EsperarPor">Quanto esperar antes de tentar de novo.</param>
/// <param name="ReiniciaEm">Quando a cota volta ao cheio.</param>
public readonly record struct Decisao(bool Permitida, int Restantes, TimeSpan EsperarPor, DateTimeOffset ReiniciaEm)
{
    /// <summary>Monta uma decisão de aceite.</summary>
    public static Decisao Aceita(int restantes, DateTimeOffset reiniciaEm)
        => new(true, Math.Max(0, restantes), TimeSpan.Zero, reiniciaEm);

    /// <summary>Monta uma decisão de recusa.</summary>
    public static Decisao Recusada(TimeSpan esperarPor, DateTimeOffset reiniciaEm)
        => new(false, 0, esperarPor < TimeSpan.Zero ? TimeSpan.Zero : esperarPor, reiniciaEm);

    /// <summary>
    /// O valor do cabeçalho <c>Retry-After</c>, em segundos inteiros.
    /// </summary>
    /// <remarks>
    /// O arredondamento é para cima de propósito: mandar o cliente voltar em
    /// zero segundo quando ainda faltam 400 ms garante uma segunda recusa.
    /// </remarks>
    public int RetryAfter => (int)Math.Ceiling(EsperarPor.TotalSeconds);

    /// <inheritdoc />
    public override string ToString()
        => Permitida ? $"permitida, restam {Restantes}" : $"recusada, esperar {EsperarPor.TotalSeconds:0.###}s";
}
