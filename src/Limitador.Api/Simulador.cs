using Limitador.Core;
using Limitador.Core.Algoritmos;
using Limitador.Core.Tempo;

namespace Limitador.Api;

/// <summary>O que simular.</summary>
/// <param name="Algoritmo">Nome do algoritmo.</param>
/// <param name="Permitidas">Quantas requisições a política permite.</param>
/// <param name="JanelaEmSegundos">O tamanho da janela.</param>
/// <param name="Requisicoes">Quantas requisições disparar.</param>
/// <param name="IntervaloEmMilissegundos">Quanto tempo passa entre elas.</param>
public sealed record PedidoDeSimulacao(
    string Algoritmo,
    int Permitidas,
    double JanelaEmSegundos,
    int Requisicoes,
    double IntervaloEmMilissegundos);

/// <summary>O resultado da simulação.</summary>
/// <param name="Algoritmo">Nome do algoritmo usado.</param>
/// <param name="Politica">A política aplicada.</param>
/// <param name="Permitidas">Quantas passaram.</param>
/// <param name="Recusadas">Quantas foram recusadas.</param>
/// <param name="Linha">Um desenho do que aconteceu, uma marca por requisição.</param>
public sealed record ResultadoDaSimulacao(
    string Algoritmo,
    string Politica,
    int Permitidas,
    int Recusadas,
    string Linha);

/// <summary>
/// Roda um cenário contra um algoritmo e mostra o que aconteceu.
/// </summary>
/// <remarks>
/// Ler a descrição de cinco algoritmos não deixa claro a diferença entre eles.
/// Ver a mesma rajada passar por cada um, com um relógio controlado, deixa.
/// </remarks>
public static class Simulador
{
    /// <summary>Os nomes aceitos.</summary>
    public static IReadOnlyList<string> Algoritmos { get; } =
        ["janela-fixa", "janela-deslizante", "janela-ponderada", "balde-de-fichas", "balde-furado"];

    /// <summary>Roda a simulação.</summary>
    public static ResultadoDaSimulacao Rodar(PedidoDeSimulacao pedido)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        if (pedido.Requisicoes is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(pedido), "Simule de 1 a 500 requisições.");
        }

        if (pedido.IntervaloEmMilissegundos < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pedido), "O intervalo não pode ser negativo.");
        }

        var politica = new Politica(pedido.Permitidas, TimeSpan.FromSeconds(pedido.JanelaEmSegundos)).Validar();
        var relogio = new RelogioFixo();
        var limitador = Montar(pedido.Algoritmo, politica, relogio);

        var passaram = 0;
        var recusadas = 0;
        var linha = new System.Text.StringBuilder(pedido.Requisicoes);

        for (var i = 0; i < pedido.Requisicoes; i++)
        {
            if (i > 0)
            {
                relogio.Avancar(TimeSpan.FromMilliseconds(pedido.IntervaloEmMilissegundos));
            }

            if (limitador.Tentar("simulacao").Permitida)
            {
                passaram++;
                linha.Append('.');
            }
            else
            {
                recusadas++;
                linha.Append('x');
            }
        }

        return new ResultadoDaSimulacao(limitador.Nome, politica.ToString(), passaram, recusadas, linha.ToString());
    }

    /// <summary>Monta o limitador pelo nome.</summary>
    public static ILimitador Montar(string algoritmo, Politica politica, IRelogio relogio) => algoritmo switch
    {
        "janela-fixa" => new JanelaFixa(politica, relogio),
        "janela-deslizante" => new JanelaDeslizante(politica, relogio),
        "janela-ponderada" => new JanelaPonderada(politica, relogio),
        "balde-de-fichas" => new BaldeDeFichas(politica, relogio),
        "balde-furado" => new BaldeFurado(politica, relogio),
        _ => throw new ArgumentException(
            $"Algoritmo desconhecido: {algoritmo}. Conhecidos: {string.Join(", ", Algoritmos)}.",
            nameof(algoritmo)),
    };
}
