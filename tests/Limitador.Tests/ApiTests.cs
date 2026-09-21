using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Limitador.Api;
using Limitador.Core;
using Limitador.Core.Tempo;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Limitador.Tests;

public class SimuladorTests
{
    [Fact]
    public void A_rajada_mostra_onde_cada_algoritmo_corta()
    {
        // Vinte requisições instantâneas com limite de cinco: todos cortam em
        // cinco, mas por motivos diferentes.
        foreach (var algoritmo in Simulador.Algoritmos)
        {
            var resultado = Simulador.Rodar(new PedidoDeSimulacao(algoritmo, 5, 1, 20, 0));

            resultado.Permitidas.Should().Be(5, $"o algoritmo {algoritmo} deveria cortar em 5");
            resultado.Recusadas.Should().Be(15);
            resultado.Linha.Should().StartWith(".....x");
        }
    }

    [Fact]
    public void Com_intervalo_folgado_tudo_passa()
    {
        foreach (var algoritmo in Simulador.Algoritmos)
        {
            var resultado = Simulador.Rodar(new PedidoDeSimulacao(algoritmo, 5, 1, 10, 500));

            resultado.Recusadas.Should().Be(0, $"o algoritmo {algoritmo} não deveria recusar nada");
        }
    }

    [Fact]
    public void A_linha_desenha_o_que_aconteceu()
    {
        var resultado = Simulador.Rodar(new PedidoDeSimulacao("janela-fixa", 2, 10, 5, 0));

        resultado.Linha.Should().Be("..xxx");
        resultado.Politica.Should().Be("2 por 10s");
    }

    [Fact]
    public void Algoritmo_desconhecido_reclama()
    {
        var acao = () => Simulador.Rodar(new PedidoDeSimulacao("inventado", 5, 1, 10, 0));

        acao.Should().Throw<ArgumentException>().WithMessage("*desconhecido*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Quantidade_fora_da_faixa_reclama(int requisicoes)
    {
        var acao = () => Simulador.Rodar(new PedidoDeSimulacao("janela-fixa", 5, 1, requisicoes, 0));

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Intervalo_negativo_reclama()
    {
        var acao = () => Simulador.Rodar(new PedidoDeSimulacao("janela-fixa", 5, 1, 10, -1));

        acao.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Monta_cada_algoritmo_pelo_nome()
    {
        foreach (var nome in Simulador.Algoritmos)
        {
            Simulador.Montar(nome, Politica.PorSegundo(1), new RelogioFixo()).Should().NotBeNull();
        }
    }
}

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> fabrica;

    public ApiTests(WebApplicationFactory<Program> fabrica)
    {
        this.fabrica = fabrica;
    }

    private HttpClient ClienteChamado(string nome)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Cliente", nome);
        return cliente;
    }

    [Fact]
    public async Task A_rota_de_saude_passa_sem_limite()
    {
        var cliente = ClienteChamado($"saude-{Guid.NewGuid():N}");

        for (var i = 0; i < 20; i++)
        {
            (await cliente.GetAsync("/saude")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_resposta_traz_os_cabecalhos_de_limite()
    {
        var resposta = await ClienteChamado($"cab-{Guid.NewGuid():N}").GetAsync("/recurso");

        resposta.Headers.Should().ContainKey("X-RateLimit-Limit");
        resposta.Headers.Should().ContainKey("X-RateLimit-Remaining");
        resposta.Headers.Should().ContainKey("X-RateLimit-Reset");
    }

    [Fact]
    public async Task O_restante_diminui_a_cada_requisicao()
    {
        var cliente = ClienteChamado($"conta-{Guid.NewGuid():N}");

        var primeira = await cliente.GetAsync("/recurso");
        var segunda = await cliente.GetAsync("/recurso");

        var antes = int.Parse(primeira.Headers.GetValues("X-RateLimit-Remaining").First());
        var depois = int.Parse(segunda.Headers.GetValues("X-RateLimit-Remaining").First());

        depois.Should().BeLessThan(antes);
    }

    [Fact]
    public async Task Passar_do_limite_devolve_429_com_retry_after()
    {
        var cliente = ClienteChamado($"excede-{Guid.NewGuid():N}");
        HttpResponseMessage? recusada = null;

        for (var i = 0; i < 12; i++)
        {
            var resposta = await cliente.GetAsync("/recurso");

            if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
            {
                recusada = resposta;
                break;
            }
        }

        recusada.Should().NotBeNull();
        recusada!.Headers.Should().ContainKey("Retry-After");

        var corpo = await recusada.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("titulo").GetString().Should().Contain("Limite");
    }

    [Fact]
    public async Task Clientes_diferentes_nao_gastam_a_cota_um_do_outro()
    {
        var um = ClienteChamado($"um-{Guid.NewGuid():N}");
        var outro = ClienteChamado($"outro-{Guid.NewGuid():N}");

        for (var i = 0; i < 6; i++)
        {
            await um.GetAsync("/recurso");
        }

        (await outro.GetAsync("/recurso")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lista_os_algoritmos_com_as_caracteristicas()
    {
        var corpo = await fabrica.CreateClient().GetFromJsonAsync<JsonElement>("/algoritmos");

        corpo.GetArrayLength().Should().Be(5);
        corpo.EnumerateArray().Select(a => a.GetProperty("nome").GetString())
            .Should().Contain("balde de fichas");
    }

    [Fact]
    public async Task Simula_pela_api()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync("/simulacoes", new
        {
            algoritmo = "balde-de-fichas",
            permitidas = 3,
            janelaEmSegundos = 1.0,
            requisicoes = 6,
            intervaloEmMilissegundos = 0.0,
        });

        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        corpo.GetProperty("permitidas").GetInt32().Should().Be(3);
        corpo.GetProperty("linha").GetString().Should().Be("...xxx");
    }

    [Fact]
    public async Task Simulacao_invalida_reclama()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync("/simulacoes", new
        {
            algoritmo = "inventado",
            permitidas = 3,
            janelaEmSegundos = 1.0,
            requisicoes = 6,
            intervaloEmMilissegundos = 0.0,
        });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
