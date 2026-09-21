namespace Limitador.Core.Armazenamento;

/// <summary>
/// Vários limitadores em série: a requisição precisa passar por todos.
/// </summary>
/// <remarks>
/// Serviço de verdade quase nunca tem um limite só. O comum é "dez por
/// segundo e mil por hora": o primeiro segura a rajada, o segundo segura o
/// abuso continuado. Encadear os dois resolve, com um cuidado — quem recusa
/// não pode deixar os anteriores já terem consumido cota, senão uma requisição
/// recusada ainda assim gasta o orçamento de quem passou.
/// </remarks>
public sealed class LimitadorComposto : ILimitador
{
    private readonly IReadOnlyList<ILimitador> limitadores;

    /// <summary>Encadeia os limitadores.</summary>
    public LimitadorComposto(params ILimitador[] limitadores)
    {
        ArgumentNullException.ThrowIfNull(limitadores);

        if (limitadores.Length == 0)
        {
            throw new ArgumentException("Informe ao menos um limitador.", nameof(limitadores));
        }

        this.limitadores = limitadores;
    }

    /// <inheritdoc />
    public string Nome => string.Join(" + ", limitadores.Select(limitador => limitador.Nome));

    /// <summary>A política mais apertada da cadeia, em requisições por segundo.</summary>
    public Politica Politica => limitadores.MinBy(limitador => limitador.Politica.PorSegundoMedio)!.Politica;

    /// <summary>Os limitadores encadeados.</summary>
    public IReadOnlyList<ILimitador> Encadeados => limitadores;

    /// <inheritdoc />
    public Decisao Tentar(string chave, int custo = 1)
    {
        // Primeiro pergunta a todos sem consumir nada. Só quando todos dizem
        // sim é que o consumo acontece — senão uma recusa no terceiro deixaria
        // os dois primeiros com cota gasta à toa.
        foreach (var limitador in limitadores)
        {
            var consulta = limitador.Tentar(chave, custo: 0);

            if (!consulta.Permitida)
            {
                return consulta;
            }
        }

        var pior = default(Decisao);
        var primeira = true;

        foreach (var limitador in limitadores)
        {
            var decisao = limitador.Tentar(chave, custo);

            if (!decisao.Permitida)
            {
                return decisao;
            }

            if (primeira || decisao.Restantes < pior.Restantes)
            {
                pior = decisao;
                primeira = false;
            }
        }

        return pior;
    }

    /// <inheritdoc />
    public void Esquecer(string chave)
    {
        foreach (var limitador in limitadores)
        {
            limitador.Esquecer(chave);
        }
    }

    /// <inheritdoc />
    public void EsquecerTudo()
    {
        foreach (var limitador in limitadores)
        {
            limitador.EsquecerTudo();
        }
    }
}

/// <summary>
/// Um limitador com cotas diferentes por grupo de chave.
/// </summary>
/// <remarks>
/// O caso clássico é plano de assinatura: o cliente grátis tem uma cota, o
/// pago tem outra. Em vez de espalhar `if` pelo código, a escolha do limitador
/// fica em um lugar só.
/// </remarks>
public sealed class LimitadorPorGrupo : ILimitador
{
    private readonly Func<string, string> classificar;
    private readonly IReadOnlyDictionary<string, ILimitador> porGrupo;
    private readonly ILimitador padrao;

    /// <summary>Monta o limitador com a função que classifica a chave.</summary>
    public LimitadorPorGrupo(
        Func<string, string> classificar,
        IReadOnlyDictionary<string, ILimitador> porGrupo,
        ILimitador padrao)
    {
        this.classificar = classificar ?? throw new ArgumentNullException(nameof(classificar));
        this.porGrupo = porGrupo ?? throw new ArgumentNullException(nameof(porGrupo));
        this.padrao = padrao ?? throw new ArgumentNullException(nameof(padrao));
    }

    /// <inheritdoc />
    public string Nome => "por grupo";

    /// <inheritdoc />
    public Politica Politica => padrao.Politica;

    /// <summary>O limitador que atende uma chave.</summary>
    public ILimitador Para(string chave)
        => porGrupo.TryGetValue(classificar(chave), out var limitador) ? limitador : padrao;

    /// <inheritdoc />
    public Decisao Tentar(string chave, int custo = 1) => Para(chave).Tentar(chave, custo);

    /// <inheritdoc />
    public void Esquecer(string chave) => Para(chave).Esquecer(chave);

    /// <inheritdoc />
    public void EsquecerTudo()
    {
        padrao.EsquecerTudo();

        foreach (var limitador in porGrupo.Values)
        {
            limitador.EsquecerTudo();
        }
    }
}
