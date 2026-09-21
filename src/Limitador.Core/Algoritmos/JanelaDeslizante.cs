using System.Collections.Concurrent;
using Limitador.Core.Tempo;

namespace Limitador.Core.Algoritmos;

/// <summary>
/// Janela deslizante por registro: guarda o instante de cada requisição.
/// </summary>
/// <remarks>
/// É o algoritmo exato — nenhum pico de borda passa, porque a janela acompanha
/// o relógio em vez de virar de uma vez. O preço é a memória: com um limite de
/// dez mil por hora, são dez mil instantes guardados por chave. Por isso ele
/// costuma ficar para limites pequenos, e a janela ponderada assume o resto.
/// </remarks>
public sealed class JanelaDeslizante : ILimitador
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> registros = new(StringComparer.Ordinal);
    private readonly IRelogio relogio;

    /// <summary>Cria o limitador.</summary>
    public JanelaDeslizante(Politica politica, IRelogio? relogio = null)
    {
        Politica = politica.Validar();
        this.relogio = relogio ?? RelogioDoSistema.Instancia;
    }

    /// <inheritdoc />
    public string Nome => "janela deslizante";

    /// <inheritdoc />
    public Politica Politica { get; }

    /// <inheritdoc />
    public Decisao Tentar(string chave, int custo = 1)
    {
        ArgumentNullException.ThrowIfNull(chave);

        var agora = relogio.Agora;
        var corte = agora - Politica.Janela;
        var fila = registros.GetOrAdd(chave, _ => new Queue<DateTimeOffset>());

        lock (fila)
        {
            // O que saiu da janela é descartado na hora do uso, e não por um
            // processo de limpeza: quem não é consultado não ocupa CPU.
            while (fila.Count > 0 && fila.Peek() <= corte)
            {
                fila.Dequeue();
            }

            var reinicio = fila.Count > 0 ? fila.Peek() + Politica.Janela : agora;

            if (fila.Count + custo > Politica.Permitidas)
            {
                return Decisao.Recusada(reinicio - agora, reinicio);
            }

            for (var i = 0; i < custo; i++)
            {
                fila.Enqueue(agora);
            }

            var proximoReinicio = fila.Count > 0 ? fila.Peek() + Politica.Janela : agora;
            return Decisao.Aceita(Politica.Permitidas - fila.Count, proximoReinicio);
        }
    }

    /// <inheritdoc />
    public void Esquecer(string chave) => registros.TryRemove(chave, out _);

    /// <inheritdoc />
    public void EsquecerTudo() => registros.Clear();

    /// <summary>Quantos instantes estão guardados para uma chave.</summary>
    public int Guardados(string chave) => registros.TryGetValue(chave, out var fila) ? fila.Count : 0;
}

/// <summary>
/// Janela deslizante ponderada: dois contadores e uma regra de três.
/// </summary>
/// <remarks>
/// O meio-termo que a maioria dos serviços usa de verdade. Ele mantém só o
/// contador da janela atual e o da anterior, e estima o consumo da janela
/// móvel pesando o contador antigo pela fração que ainda está dentro dela.
/// Custa dois inteiros por chave em vez de milhares de instantes, e erra
/// pouco: o pico de borda da janela fixa desaparece, ao custo de uma
/// aproximação que assume tráfego distribuído dentro da janela anterior.
/// </remarks>
public sealed class JanelaPonderada : ILimitador
{
    private sealed class Contadores
    {
        public long Janela;
        public int Atual;
        public int Anterior;
    }

    private readonly ConcurrentDictionary<string, Contadores> contadores = new(StringComparer.Ordinal);
    private readonly IRelogio relogio;

    /// <summary>Cria o limitador.</summary>
    public JanelaPonderada(Politica politica, IRelogio? relogio = null)
    {
        Politica = politica.Validar();
        this.relogio = relogio ?? RelogioDoSistema.Instancia;
    }

    /// <inheritdoc />
    public string Nome => "janela ponderada";

    /// <inheritdoc />
    public Politica Politica { get; }

    /// <inheritdoc />
    public Decisao Tentar(string chave, int custo = 1)
    {
        ArgumentNullException.ThrowIfNull(chave);

        var agora = relogio.Agora;
        var milissegundos = (long)Politica.Janela.TotalMilliseconds;
        var janela = agora.ToUnixTimeMilliseconds() / milissegundos;
        var decorrido = (agora.ToUnixTimeMilliseconds() % milissegundos) / (double)milissegundos;

        var estado = contadores.GetOrAdd(chave, _ => new Contadores { Janela = janela });

        lock (estado)
        {
            Alinhar(estado, janela);

            // A fração da janela anterior que ainda pesa: no começo da janela
            // nova ela conta quase inteira, e some conforme o tempo passa.
            var estimado = (estado.Anterior * (1 - decorrido)) + estado.Atual;
            var reinicio = DateTimeOffset.FromUnixTimeMilliseconds((janela + 1) * milissegundos);

            if (estimado + custo > Politica.Permitidas)
            {
                return Decisao.Recusada(reinicio - agora, reinicio);
            }

            estado.Atual += custo;
            return Decisao.Aceita((int)Math.Floor(Politica.Permitidas - estimado - custo), reinicio);
        }
    }

    private static void Alinhar(Contadores estado, long janela)
    {
        var distancia = janela - estado.Janela;

        if (distancia == 0)
        {
            return;
        }

        estado.Anterior = distancia == 1 ? estado.Atual : 0;
        estado.Atual = 0;
        estado.Janela = janela;
    }

    /// <inheritdoc />
    public void Esquecer(string chave) => contadores.TryRemove(chave, out _);

    /// <inheritdoc />
    public void EsquecerTudo() => contadores.Clear();
}
