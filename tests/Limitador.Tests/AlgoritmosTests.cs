using Limitador.Core;
using Limitador.Core.Algoritmos;
using Limitador.Core.Tempo;

namespace Limitador.Tests;

public class PoliticaTests
{
    [Fact]
    public void Os_atalhos_montam_a_janela_certa()
    {
        Politica.PorSegundo(10).Janela.Should().Be(TimeSpan.FromSeconds(1));
        Politica.PorMinuto(60).Janela.Should().Be(TimeSpan.FromMinutes(1));
        Politica.PorHora(1000).Janela.Should().Be(TimeSpan.FromHours(1));
        Politica.PorDia(10_000).Janela.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void Calcula_a_taxa_media_por_segundo()
    {
        Politica.PorMinuto(60).PorSegundoMedio.Should().Be(1);
        Politica.PorSegundo(10).PorSegundoMedio.Should().Be(10);
    }

    [Fact]
    public void Calcula_o_intervalo_entre_requisicoes()
    {
        Politica.PorSegundo(4).IntervaloEntreRequisicoes.Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(10, 0)]
    public void Politica_invalida_reclama(int permitidas, int segundos)
    {
        var acao = () => new Politica(permitidas, TimeSpan.FromSeconds(segundos)).Validar();

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_politica_se_descreve_de_volta()
    {
        Politica.PorMinuto(60).ToString().Should().Be("60 por 1min");
        Politica.PorSegundo(5).ToString().Should().Be("5 por 1s");
    }

    [Fact]
    public void O_retry_after_arredonda_para_cima()
    {
        // Mandar o cliente voltar em zero segundo quando faltam 400 ms garante
        // uma segunda recusa.
        var decisao = Decisao.Recusada(TimeSpan.FromMilliseconds(400), DateTimeOffset.UnixEpoch);

        decisao.RetryAfter.Should().Be(1);
    }

    [Fact]
    public void A_decisao_se_descreve_de_volta()
    {
        Decisao.Aceita(3, DateTimeOffset.UnixEpoch).ToString().Should().Contain("restam 3");
        Decisao.Recusada(TimeSpan.FromSeconds(2), DateTimeOffset.UnixEpoch).ToString().Should().Contain("recusada");
    }

    [Fact]
    public void A_espera_nunca_e_negativa()
    {
        Decisao.Recusada(TimeSpan.FromSeconds(-5), DateTimeOffset.UnixEpoch).EsperarPor.Should().Be(TimeSpan.Zero);
    }
}

public class RelogioTests
{
    [Fact]
    public void O_relogio_fixo_so_anda_quando_mandam()
    {
        var relogio = new RelogioFixo();
        var inicio = relogio.Agora;

        relogio.AvancarSegundos(30);

        (relogio.Agora - inicio).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void O_relogio_nao_anda_para_tras()
    {
        var acao = () => new RelogioFixo().Avancar(TimeSpan.FromSeconds(-1));

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }
}

/// <summary>
/// Os testes que valem para qualquer algoritmo. Um contrato quebrado por uma
/// implementação só aparece aqui, e não num teste específico dela.
/// </summary>
public abstract class ContratoDoLimitador
{
    protected readonly RelogioFixo Relogio = new();

    protected abstract ILimitador Criar(Politica politica);

    [Fact]
    public void Deixa_passar_ate_o_limite()
    {
        var limitador = Criar(Politica.PorSegundo(3));

        limitador.TentarVarias("a", 3).Should().Be(3);
    }

    [Fact]
    public void Recusa_o_que_passa_do_limite()
    {
        var limitador = Criar(Politica.PorSegundo(3));
        limitador.TentarVarias("a", 3);

        limitador.Tentar("a").Permitida.Should().BeFalse();
    }

    [Fact]
    public void Cada_chave_tem_a_propria_cota()
    {
        var limitador = Criar(Politica.PorSegundo(2));

        limitador.TentarVarias("a", 2);

        limitador.Tentar("b").Permitida.Should().BeTrue();
    }

    [Fact]
    public void A_cota_volta_depois_de_duas_janelas_paradas()
    {
        // Duas janelas, e não uma: a janela ponderada devolve a cota aos
        // poucos de propósito, então exigir a cota cheia logo depois de uma
        // janela seria exigir que ela tivesse o defeito da janela fixa.
        var limitador = Criar(Politica.PorSegundo(2));
        limitador.TentarVarias("a", 2);

        Relogio.AvancarSegundos(2.1);

        limitador.Tentar("a").Permitida.Should().BeTrue();
    }

    [Fact]
    public void A_recusa_informa_quanto_esperar()
    {
        var limitador = Criar(Politica.PorSegundo(1));
        limitador.Tentar("a");

        var recusa = limitador.Tentar("a");

        recusa.Permitida.Should().BeFalse();
        recusa.EsperarPor.Should().BeGreaterThan(TimeSpan.Zero);
        recusa.RetryAfter.Should().BeGreaterThan(0);
    }

    [Fact]
    public void O_custo_zero_consulta_sem_consumir()
    {
        var limitador = Criar(Politica.PorSegundo(1));

        limitador.Permite("a").Should().BeTrue();
        limitador.Permite("a").Should().BeTrue();
        limitador.Tentar("a").Permitida.Should().BeTrue();
    }

    [Fact]
    public void O_custo_maior_consome_mais()
    {
        var limitador = Criar(Politica.PorSegundo(10));

        limitador.Tentar("a", custo: 10).Permitida.Should().BeTrue();
        limitador.Tentar("a").Permitida.Should().BeFalse();
    }

    [Fact]
    public void O_custo_acima_do_limite_nunca_passa()
    {
        var limitador = Criar(Politica.PorSegundo(5));

        limitador.Tentar("a", custo: 6).Permitida.Should().BeFalse();
    }

    [Fact]
    public void Esquecer_devolve_a_cota()
    {
        var limitador = Criar(Politica.PorSegundo(1));
        limitador.Tentar("a");

        limitador.Esquecer("a");

        limitador.Tentar("a").Permitida.Should().BeTrue();
    }

    [Fact]
    public void Esquecer_tudo_devolve_a_cota_de_todos()
    {
        var limitador = Criar(Politica.PorSegundo(1));
        limitador.Tentar("a");
        limitador.Tentar("b");

        limitador.EsquecerTudo();

        limitador.Tentar("a").Permitida.Should().BeTrue();
        limitador.Tentar("b").Permitida.Should().BeTrue();
    }

    [Fact]
    public void Chave_nula_reclama()
    {
        var limitador = Criar(Politica.PorSegundo(1));

        var acao = () => limitador.Tentar(null!);

        acao.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Politica_invalida_reclama_na_criacao()
    {
        var acao = () => Criar(new Politica(0, TimeSpan.FromSeconds(1)));

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void O_algoritmo_tem_nome()
    {
        Criar(Politica.PorSegundo(1)).Nome.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Ao_longo_do_tempo_a_media_respeita_a_politica()
    {
        // Uma requisição a cada 100 ms com limite de 5 por segundo: o
        // algoritmo pode variar no curto prazo, mas em dez segundos nenhum
        // pode deixar passar muito mais do que a política permite.
        var limitador = Criar(Politica.PorSegundo(5));
        var passaram = 0;

        for (var i = 0; i < 100; i++)
        {
            if (limitador.Tentar("a").Permitida)
            {
                passaram++;
            }

            Relogio.Avancar(TimeSpan.FromMilliseconds(100));
        }

        passaram.Should().BeLessThanOrEqualTo(60);
    }
}

public class JanelaFixaTests : ContratoDoLimitador
{
    protected override ILimitador Criar(Politica politica) => new JanelaFixa(politica, Relogio);

    [Fact]
    public void O_pico_de_borda_passa_de_propósito()
    {
        // Este é o defeito conhecido do algoritmo, e o teste existe para
        // documentá-lo: 2 por segundo deixa passar 4 em torno da virada.
        var limitador = new JanelaFixa(Politica.PorSegundo(2), Relogio);

        var inicio = Relogio.Agora.ToUnixTimeMilliseconds();
        Relogio.Avancar(TimeSpan.FromMilliseconds(999 - (inicio % 1000)));

        limitador.TentarVarias("a", 2).Should().Be(2);

        Relogio.Avancar(TimeSpan.FromMilliseconds(2));

        limitador.TentarVarias("a", 2).Should().Be(2);
    }

    [Fact]
    public void Conta_as_chaves_ativas()
    {
        var limitador = new JanelaFixa(Politica.PorSegundo(1), Relogio);
        limitador.Tentar("a");
        limitador.Tentar("b");

        limitador.Chaves.Should().Be(2);
    }
}

public class JanelaDeslizanteTests : ContratoDoLimitador
{
    protected override ILimitador Criar(Politica politica) => new JanelaDeslizante(politica, Relogio);

    [Fact]
    public void O_pico_de_borda_nao_passa()
    {
        // O mesmo cenário que a janela fixa deixa passar: aqui não passa.
        var limitador = new JanelaDeslizante(Politica.PorSegundo(2), Relogio);

        limitador.TentarVarias("a", 2).Should().Be(2);
        Relogio.Avancar(TimeSpan.FromMilliseconds(2));

        limitador.Tentar("a").Permitida.Should().BeFalse();
    }

    [Fact]
    public void A_cota_volta_aos_poucos_e_nao_de_uma_vez()
    {
        var limitador = new JanelaDeslizante(Politica.PorSegundo(2), Relogio);
        limitador.Tentar("a");
        Relogio.AvancarSegundos(0.5);
        limitador.Tentar("a");

        // Meio segundo depois, só a primeira saiu da janela.
        Relogio.AvancarSegundos(0.6);

        limitador.Tentar("a").Permitida.Should().BeTrue();
        limitador.Tentar("a").Permitida.Should().BeFalse();
    }

    [Fact]
    public void Os_instantes_velhos_sao_descartados()
    {
        var limitador = new JanelaDeslizante(Politica.PorSegundo(5), Relogio);
        limitador.TentarVarias("a", 5);

        limitador.Guardados("a").Should().Be(5);

        Relogio.AvancarSegundos(2);
        limitador.Tentar("a");

        limitador.Guardados("a").Should().Be(1);
    }
}

public class JanelaPonderadaTests : ContratoDoLimitador
{
    protected override ILimitador Criar(Politica politica) => new JanelaPonderada(politica, Relogio);

    [Fact]
    public void Segura_o_pico_de_borda_que_a_janela_fixa_deixa_passar()
    {
        var limitador = new JanelaPonderada(Politica.PorSegundo(4), Relogio);

        var inicio = Relogio.Agora.ToUnixTimeMilliseconds();
        Relogio.Avancar(TimeSpan.FromMilliseconds(999 - (inicio % 1000)));

        limitador.TentarVarias("a", 4).Should().Be(4);
        Relogio.Avancar(TimeSpan.FromMilliseconds(2));

        // Logo depois da virada, a janela anterior ainda pesa quase inteira.
        limitador.Tentar("a").Permitida.Should().BeFalse();
    }

    [Fact]
    public void Depois_de_duas_janelas_paradas_a_cota_esta_cheia()
    {
        var limitador = new JanelaPonderada(Politica.PorSegundo(3), Relogio);
        limitador.TentarVarias("a", 3);

        Relogio.AvancarSegundos(3);

        limitador.TentarVarias("a", 3).Should().Be(3);
    }

    [Fact]
    public void Logo_depois_da_janela_a_cota_volta_so_em_parte()
    {
        // É a diferença que justifica o algoritmo: a janela fixa devolveria
        // tudo na virada, e o cliente emendaria duas rajadas cheias.
        var ponderada = new JanelaPonderada(Politica.PorSegundo(4), Relogio);
        var fixa = new JanelaFixa(Politica.PorSegundo(4), Relogio);

        ponderada.TentarVarias("a", 4);
        fixa.TentarVarias("a", 4);

        Relogio.AvancarSegundos(1.1);

        ponderada.TentarVarias("a", 4).Should().BeLessThan(4);
        fixa.TentarVarias("a", 4).Should().Be(4);
    }
}

public class BaldeDeFichasTests : ContratoDoLimitador
{
    protected override ILimitador Criar(Politica politica) => new BaldeDeFichas(politica, Relogio);

    [Fact]
    public void O_balde_comeca_cheio()
    {
        var limitador = new BaldeDeFichas(Politica.PorSegundo(5), Relogio);

        limitador.Fichas("a").Should().Be(5);
    }

    [Fact]
    public void Quem_ficou_parado_pode_gastar_de_uma_vez()
    {
        // É o recurso, não o defeito: o cliente que sincroniza de hora em hora
        // não deveria ser punido por isso.
        var limitador = new BaldeDeFichas(Politica.PorMinuto(60), Relogio);
        limitador.TentarVarias("a", 60);

        Relogio.AvancarSegundos(60);

        limitador.TentarVarias("a", 60).Should().Be(60);
    }

    [Fact]
    public void As_fichas_voltam_aos_poucos()
    {
        var limitador = new BaldeDeFichas(Politica.PorSegundo(10), Relogio);
        limitador.TentarVarias("a", 10);

        Relogio.AvancarSegundos(0.5);

        limitador.Fichas("a").Should().BeApproximately(5, 0.01);
    }

    [Fact]
    public void O_balde_nao_passa_da_capacidade()
    {
        var limitador = new BaldeDeFichas(Politica.PorSegundo(5), Relogio);

        Relogio.AvancarSegundos(100);

        limitador.Fichas("a").Should().Be(5);
    }

    [Fact]
    public void A_capacidade_pode_ser_maior_que_o_limite()
    {
        // Rajada de 20 com média de 5 por segundo.
        var limitador = new BaldeDeFichas(Politica.PorSegundo(5), Relogio, capacidade: 20);

        limitador.TentarVarias("a", 20).Should().Be(20);
    }

    [Fact]
    public void Capacidade_invalida_reclama()
    {
        var acao = () => new BaldeDeFichas(Politica.PorSegundo(5), Relogio, capacidade: 0);

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class BaldeFuradoTests : ContratoDoLimitador
{
    protected override ILimitador Criar(Politica politica) => new BaldeFurado(politica, Relogio);

    [Fact]
    public void O_balde_comeca_vazio()
    {
        new BaldeFurado(Politica.PorSegundo(5), Relogio).Nivel("a").Should().Be(0);
    }

    [Fact]
    public void A_fila_vaza_em_ritmo_constante()
    {
        var limitador = new BaldeFurado(Politica.PorSegundo(10), Relogio);
        limitador.TentarVarias("a", 10);

        Relogio.AvancarSegundos(0.5);

        limitador.Nivel("a").Should().BeApproximately(5, 0.01);
    }

    [Fact]
    public void Ficar_parado_nao_acumula_credito()
    {
        // A diferença para o balde de fichas: aqui a ociosidade não vira
        // permissão de rajada, só esvazia a fila.
        var limitador = new BaldeFurado(Politica.PorSegundo(5), Relogio);

        Relogio.AvancarSegundos(100);

        limitador.TentarVarias("a", 5).Should().Be(5);
        limitador.Tentar("a").Permitida.Should().BeFalse();
    }
}
