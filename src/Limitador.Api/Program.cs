using Limitador.Api;
using Limitador.Core;
using Limitador.Core.Algoritmos;
using Limitador.Core.Armazenamento;
using Microsoft.AspNetCore.Mvc;

var construtor = WebApplication.CreateBuilder(args);

construtor.Services.AddEndpointsApiExplorer();
construtor.Services.AddSwaggerGen();

// Dois limites em série: o primeiro segura a rajada, o segundo o abuso
// continuado. É o arranjo mais comum em serviço de verdade.
construtor.Services.AddSingleton<ILimitador>(_ => new LimitadorComposto(
    new BaldeDeFichas(Politica.PorSegundo(5)),
    new JanelaPonderada(Politica.PorMinuto(60))));

var aplicacao = construtor.Build();

if (aplicacao.Environment.IsDevelopment())
{
    aplicacao.UseSwagger();
    aplicacao.UseSwaggerUI();
}

aplicacao.UsarLimiteDeTaxa(new OpcoesDoMiddleware
{
    Chave = OpcoesDoMiddleware.PorCabecalho("X-Cliente"),
    Liberadas = ["/saude", "/algoritmos"],
});

aplicacao.MapGet("/saude", () => Results.Ok(new { estado = "ok" }))
    .WithName("Saude")
    .WithTags("Serviço");

aplicacao.MapGet("/algoritmos", () => Results.Ok(new[]
    {
        new { nome = "janela fixa", aceitaRajada = false, memoria = "um contador por chave", defeito = "pico na borda da janela" },
        new { nome = "janela deslizante", aceitaRajada = false, memoria = "um instante por requisição", defeito = "memória cresce com o limite" },
        new { nome = "janela ponderada", aceitaRajada = false, memoria = "dois contadores por chave", defeito = "aproxima o tráfego da janela anterior" },
        new { nome = "balde de fichas", aceitaRajada = true, memoria = "um contador por chave", defeito = "permite rajada por definição" },
        new { nome = "balde furado", aceitaRajada = false, memoria = "um contador por chave", defeito = "não aproveita ociosidade" },
    }))
    .WithName("Algoritmos")
    .WithTags("Serviço");

aplicacao.MapGet("/recurso", (HttpContext contexto) => Results.Ok(new
    {
        mensagem = "passou pelo limitador",
        restantes = contexto.Response.Headers["X-RateLimit-Remaining"].ToString(),
    }))
    .WithName("Recurso")
    .WithTags("Exemplo");

aplicacao.MapPost("/simulacoes", ([FromBody] PedidoDeSimulacao pedido) =>
    {
        try
        {
            return Results.Ok(Simulador.Rodar(pedido));
        }
        catch (Exception erro) when (erro is ArgumentException or ArgumentOutOfRangeException)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Simulação inválida",
                Detail = erro.Message,
                Status = StatusCodes.Status400BadRequest,
            });
        }
    })
    .WithName("Simular")
    .WithTags("Exemplo");

aplicacao.Run();

/// <summary>Exposta para que os testes de integração possam subir a API.</summary>
public partial class Program;
