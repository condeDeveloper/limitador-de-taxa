using System.Collections.Concurrent;
using Limitador.Core.Tempo;

namespace Limitador.Core.Algoritmos;

/// <summary>
/// Balde de fichas: o balde enche a uma taxa constante e cada requisição tira
/// uma ficha.
/// </summary>
/// <remarks>
/// É o único da família que aceita rajada de propósito. Quem ficou parado
/// acumula fichas até o balde encher, e pode gastá-las de uma vez — o que é
/// exatamente o comportamento certo para um cliente que sincroniza de hora em
/// hora. A capacidade do balde controla o tamanho da rajada; a taxa controla a
/// média no longo prazo.
/// </remarks>
public sealed class BaldeDeFichas : ILimitador
{
    private sealed class Balde
    {
        public double Fichas;
        public DateTimeOffset Atualizado;
    }

    private readonly ConcurrentDictionary<string, Balde> baldes = new(StringComparer.Ordinal);
    private readonly IRelogio relogio;

    /// <summary>Cria o limitador. A capacidade padrão é o próprio limite.</summary>
    public BaldeDeFichas(Politica politica, IRelogio? relogio = null, int? capacidade = null)
    {
        Politica = politica.Validar();
        Capacidade = capacidade ?? politica.Permitidas;
        this.relogio = relogio ?? RelogioDoSistema.Instancia;

        if (Capacidade <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacidade), "A capacidade precisa ser positiva.");
        }
    }

    /// <inheritdoc />
    public string Nome => "balde de fichas";

    /// <inheritdoc />
    public Politica Politica { get; }

    /// <summary>Quantas fichas o balde comporta, e portanto o tamanho da rajada.</summary>
    public int Capacidade { get; }

    /// <summary>Quantas fichas entram por segundo.</summary>
    public double TaxaPorSegundo => Politica.PorSegundoMedio;

    /// <inheritdoc />
    public Decisao Tentar(string chave, int custo = 1)
    {
        ArgumentNullException.ThrowIfNull(chave);

        var agora = relogio.Agora;
        var balde = baldes.GetOrAdd(chave, _ => new Balde { Fichas = Capacidade, Atualizado = agora });

        lock (balde)
        {
            // O enchimento é calculado na hora do uso, e não por um
            // temporizador: um balde que ninguém consulta não custa nada.
            var decorrido = (agora - balde.Atualizado).TotalSeconds;

            if (decorrido > 0)
            {
                balde.Fichas = Math.Min(Capacidade, balde.Fichas + (decorrido * TaxaPorSegundo));
                balde.Atualizado = agora;
            }

            if (balde.Fichas < custo)
            {
                var faltam = custo - balde.Fichas;
                var espera = TimeSpan.FromSeconds(faltam / TaxaPorSegundo);
                return Decisao.Recusada(espera, agora + espera);
            }

            balde.Fichas -= custo;
            var ateEncher = TimeSpan.FromSeconds((Capacidade - balde.Fichas) / TaxaPorSegundo);

            return Decisao.Aceita((int)Math.Floor(balde.Fichas), agora + ateEncher);
        }
    }

    /// <summary>Quantas fichas a chave tem agora.</summary>
    public double Fichas(string chave)
    {
        if (!baldes.TryGetValue(chave, out var balde))
        {
            return Capacidade;
        }

        lock (balde)
        {
            var decorrido = (relogio.Agora - balde.Atualizado).TotalSeconds;
            return Math.Min(Capacidade, balde.Fichas + (decorrido * TaxaPorSegundo));
        }
    }

    /// <inheritdoc />
    public void Esquecer(string chave) => baldes.TryRemove(chave, out _);

    /// <inheritdoc />
    public void EsquecerTudo() => baldes.Clear();
}

/// <summary>
/// Balde furado: a saída é constante, doa a quem doer.
/// </summary>
/// <remarks>
/// A diferença para o balde de fichas está em qual ponta o balde controla. Lá,
/// o balde acumula permissão e libera rajada; aqui, ele acumula requisição e a
/// libera em ritmo fixo. É o que se quer quando o gargalo é do outro lado — um
/// serviço externo que aceita dez por segundo e não aceita onze, nem que seja
/// uma vez só.
/// </remarks>
public sealed class BaldeFurado : ILimitador
{
    private sealed class Balde
    {
        public double Nivel;
        public DateTimeOffset Atualizado;
    }

    private readonly ConcurrentDictionary<string, Balde> baldes = new(StringComparer.Ordinal);
    private readonly IRelogio relogio;

    /// <summary>Cria o limitador.</summary>
    public BaldeFurado(Politica politica, IRelogio? relogio = null, int? capacidade = null)
    {
        Politica = politica.Validar();
        Capacidade = capacidade ?? politica.Permitidas;
        this.relogio = relogio ?? RelogioDoSistema.Instancia;

        if (Capacidade <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacidade), "A capacidade precisa ser positiva.");
        }
    }

    /// <inheritdoc />
    public string Nome => "balde furado";

    /// <inheritdoc />
    public Politica Politica { get; }

    /// <summary>Quantas requisições cabem na fila.</summary>
    public int Capacidade { get; }

    /// <summary>Quantas requisições vazam por segundo.</summary>
    public double VazaoPorSegundo => Politica.PorSegundoMedio;

    /// <inheritdoc />
    public Decisao Tentar(string chave, int custo = 1)
    {
        ArgumentNullException.ThrowIfNull(chave);

        var agora = relogio.Agora;
        var balde = baldes.GetOrAdd(chave, _ => new Balde { Nivel = 0, Atualizado = agora });

        lock (balde)
        {
            var decorrido = (agora - balde.Atualizado).TotalSeconds;

            if (decorrido > 0)
            {
                balde.Nivel = Math.Max(0, balde.Nivel - (decorrido * VazaoPorSegundo));
                balde.Atualizado = agora;
            }

            if (balde.Nivel + custo > Capacidade)
            {
                var excesso = balde.Nivel + custo - Capacidade;
                var espera = TimeSpan.FromSeconds(excesso / VazaoPorSegundo);
                return Decisao.Recusada(espera, agora + espera);
            }

            balde.Nivel += custo;
            var ateEsvaziar = TimeSpan.FromSeconds(balde.Nivel / VazaoPorSegundo);

            return Decisao.Aceita((int)Math.Floor(Capacidade - balde.Nivel), agora + ateEsvaziar);
        }
    }

    /// <summary>Quanto a fila da chave tem agora.</summary>
    public double Nivel(string chave)
    {
        if (!baldes.TryGetValue(chave, out var balde))
        {
            return 0;
        }

        lock (balde)
        {
            var decorrido = (relogio.Agora - balde.Atualizado).TotalSeconds;
            return Math.Max(0, balde.Nivel - (decorrido * VazaoPorSegundo));
        }
    }

    /// <inheritdoc />
    public void Esquecer(string chave) => baldes.TryRemove(chave, out _);

    /// <inheritdoc />
    public void EsquecerTudo() => baldes.Clear();
}
