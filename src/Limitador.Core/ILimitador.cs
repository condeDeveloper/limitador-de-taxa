namespace Limitador.Core;

/// <summary>
/// O contrato de um limitador de taxa.
/// </summary>
/// <remarks>
/// Todos os algoritmos respondem a mesma pergunta — "esta requisição pode
/// passar?" — e diferem só em como contam. Manter o contrato igual é o que
/// permite trocar de algoritmo sem tocar em quem usa.
/// </remarks>
public interface ILimitador
{
    /// <summary>Nome do algoritmo.</summary>
    string Nome { get; }

    /// <summary>A política em vigor.</summary>
    Politica Politica { get; }

    /// <summary>
    /// Decide sobre uma requisição da chave informada, já contabilizando o
    /// consumo quando ela é permitida.
    /// </summary>
    Decisao Tentar(string chave, int custo = 1);

    /// <summary>Esquece o que foi contado para uma chave.</summary>
    void Esquecer(string chave);

    /// <summary>Esquece tudo.</summary>
    void EsquecerTudo();
}

/// <summary>Atalhos comuns a qualquer limitador.</summary>
public static class Limitadores
{
    /// <summary>Indica se a requisição passaria, sem consumir cota.</summary>
    public static bool Permite(this ILimitador limitador, string chave)
        => limitador.Tentar(chave, custo: 0).Permitida;

    /// <summary>
    /// Consome a cota de várias requisições de uma vez, parando na primeira
    /// recusa. Devolve quantas passaram.
    /// </summary>
    public static int TentarVarias(this ILimitador limitador, string chave, int quantas)
    {
        var passaram = 0;

        for (var i = 0; i < quantas; i++)
        {
            if (!limitador.Tentar(chave).Permitida)
            {
                break;
            }

            passaram++;
        }

        return passaram;
    }
}
