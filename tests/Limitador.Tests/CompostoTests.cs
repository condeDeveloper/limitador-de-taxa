using Limitador.Core;
using Limitador.Core.Algoritmos;
using Limitador.Core.Armazenamento;
using Limitador.Core.Tempo;

namespace Limitador.Tests;

public class CompostoTests
{
    private readonly RelogioFixo relogio = new();

    [Fact]
    public void A_requisicao_precisa_passar_por_todos()
    {
        var composto = new LimitadorComposto(
            new BaldeDeFichas(Politica.PorSegundo(5), relogio),
            new JanelaPonderada(Politica.PorMinuto(10), relogio));

        composto.TentarVarias("a", 5).Should().Be(5);

        // O limite por segundo permitiria mais depois de um segundo, mas o
        // limite por minuto ainda tem cota; passam mais cinco e só.
        relogio.AvancarSegundos(1);

        composto.TentarVarias("a", 10).Should().Be(5);
    }

    [Fact]
    public void O_mais_apertado_manda()
    {
        var composto = new LimitadorComposto(
            new BaldeDeFichas(Politica.PorSegundo(100), relogio),
            new JanelaFixa(Politica.PorSegundo(2), relogio));

        composto.TentarVarias("a", 10).Should().Be(2);
    }

    [Fact]
    public void A_recusa_nao_gasta_a_cota_de_quem_passou()
    {
        // Este é o cuidado que separa um composto correto de um ingênuo: se o
        // primeiro consumisse antes de o segundo recusar, a requisição
        // recusada ainda assim teria gasto orçamento.
        var frouxo = new JanelaFixa(Politica.PorSegundo(10), relogio);
        var apertado = new JanelaFixa(Politica.PorSegundo(1), relogio);
        var composto = new LimitadorComposto(frouxo, apertado);

        composto.Tentar("a");

        for (var i = 0; i < 5; i++)
        {
            composto.Tentar("a").Permitida.Should().BeFalse();
        }

        relogio.AvancarSegundos(1.1);

        // Se as recusas tivessem consumido, o frouxo já teria gasto 6 de 10.
        frouxo.Tentar("a").Restantes.Should().Be(9);
    }

    [Fact]
    public void A_politica_exposta_e_a_mais_apertada()
    {
        var composto = new LimitadorComposto(
            new JanelaFixa(Politica.PorMinuto(600), relogio),
            new JanelaFixa(Politica.PorSegundo(2), relogio));

        composto.Politica.Permitidas.Should().Be(2);
    }

    [Fact]
    public void O_nome_junta_os_algoritmos()
    {
        var composto = new LimitadorComposto(
            new BaldeDeFichas(Politica.PorSegundo(1), relogio),
            new JanelaFixa(Politica.PorSegundo(1), relogio));

        composto.Nome.Should().Be("balde de fichas + janela fixa");
        composto.Encadeados.Should().HaveCount(2);
    }

    [Fact]
    public void Esquecer_alcanca_toda_a_cadeia()
    {
        var composto = new LimitadorComposto(
            new JanelaFixa(Politica.PorSegundo(1), relogio),
            new JanelaFixa(Politica.PorSegundo(1), relogio));

        composto.Tentar("a");
        composto.Esquecer("a");

        composto.Tentar("a").Permitida.Should().BeTrue();
    }

    [Fact]
    public void Esquecer_tudo_alcanca_toda_a_cadeia()
    {
        var composto = new LimitadorComposto(new JanelaFixa(Politica.PorSegundo(1), relogio));

        composto.Tentar("a");
        composto.EsquecerTudo();

        composto.Tentar("a").Permitida.Should().BeTrue();
    }

    [Fact]
    public void Composto_sem_limitador_reclama()
    {
        var acao = () => new LimitadorComposto();

        acao.Should().Throw<ArgumentException>();
    }
}

public class PorGrupoTests
{
    private readonly RelogioFixo relogio = new();

    private LimitadorPorGrupo Montar() => new(
        chave => chave.StartsWith("pago:", StringComparison.Ordinal) ? "pago" : "gratis",
        new Dictionary<string, ILimitador>
        {
            ["pago"] = new JanelaFixa(Politica.PorSegundo(100), relogio),
        },
        new JanelaFixa(Politica.PorSegundo(2), relogio));

    [Fact]
    public void O_grupo_com_cota_propria_usa_a_dele()
    {
        Montar().TentarVarias("pago:123", 50).Should().Be(50);
    }

    [Fact]
    public void Quem_nao_tem_grupo_cai_no_padrao()
    {
        Montar().TentarVarias("gratis:123", 10).Should().Be(2);
    }

    [Fact]
    public void Expoe_o_limitador_de_uma_chave()
    {
        var limitador = Montar();

        limitador.Para("pago:1").Politica.Permitidas.Should().Be(100);
        limitador.Para("outro").Politica.Permitidas.Should().Be(2);
    }

    [Fact]
    public void Esquecer_tudo_alcanca_todos_os_grupos()
    {
        var limitador = Montar();
        limitador.TentarVarias("gratis:1", 2);

        limitador.EsquecerTudo();

        limitador.Tentar("gratis:1").Permitida.Should().BeTrue();
    }

    [Fact]
    public void Argumento_nulo_reclama()
    {
        var acao = () => new LimitadorPorGrupo(null!, new Dictionary<string, ILimitador>(), new JanelaFixa(Politica.PorSegundo(1)));

        acao.Should().Throw<ArgumentNullException>();
    }
}
