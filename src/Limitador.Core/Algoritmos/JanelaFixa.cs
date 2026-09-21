using System.Collections.Concurrent;
using Limitador.Core.Tempo;

namespace Limitador.Core.Algoritmos;

/// <summary>
/// Janela fixa: um contador por intervalo de relógio, zerado na virada.
/// </summary>
/// <remarks>
/// É o mais simples e o mais usado — e tem um defeito conhecido que quase
/// ninguém percebe até acontecer. Com 100 por minuto, um cliente pode mandar
/// 100 requisições às 12:00:59 e mais 100 às 12:01:00: são duas janelas
/// diferentes, cada uma dentro do limite, mas 200 requisições em um segundo.
/// É o pico de borda, e é por isso que a janela deslizante existe.
/// </remarks>
public sealed class JanelaFixa : ILimitador
{
    private sealed class Contador
    {
        public long Janela;
        public int Usadas;
    }

    private readonly ConcurrentDictionary<string, Contador> contadores = new(StringComparer.Ordinal);
    private readonly IRelogio relogio;

    /// <summary>Cria o limitador.</summary>
    public JanelaFixa(Politica politica, IRelogio? relogio = null)
    {
        Politica = politica.Validar();
        this.relogio = relogio ?? RelogioDoSistema.Instancia;
    }

    /// <inheritdoc />
    public string Nome => "janela fixa";

    /// <inheritdoc />
    public Politica Politica { get; }

    /// <inheritdoc />
    public Decisao Tentar(string chave, int custo = 1)
    {
        ArgumentNullException.ThrowIfNull(chave);

        var agora = relogio.Agora;
        var janela = agora.ToUnixTimeMilliseconds() / (long)Politica.Janela.TotalMilliseconds;
        var contador = contadores.GetOrAdd(chave, _ => new Contador());

        lock (contador)
        {
            if (contador.Janela != janela)
            {
                contador.Janela = janela;
                contador.Usadas = 0;
            }

            var reinicio = Reinicio(janela);

            if (contador.Usadas + custo > Politica.Permitidas)
            {
                return Decisao.Recusada(reinicio - agora, reinicio);
            }

            contador.Usadas += custo;
            return Decisao.Aceita(Politica.Permitidas - contador.Usadas, reinicio);
        }
    }

    /// <inheritdoc />
    public void Esquecer(string chave) => contadores.TryRemove(chave, out _);

    /// <inheritdoc />
    public void EsquecerTudo() => contadores.Clear();

    /// <summary>Quantas chaves estão sendo contadas.</summary>
    public int Chaves => contadores.Count;

    private DateTimeOffset Reinicio(long janela)
        => DateTimeOffset.FromUnixTimeMilliseconds((janela + 1) * (long)Politica.Janela.TotalMilliseconds);
}
