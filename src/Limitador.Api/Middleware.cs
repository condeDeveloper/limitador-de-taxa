using Limitador.Core;

namespace Limitador.Api;

/// <summary>Como a chave de limitação é extraída da requisição.</summary>
public sealed record OpcoesDoMiddleware
{
    /// <summary>De onde sai a chave. O padrão é o IP de origem.</summary>
    public Func<HttpContext, string> Chave { get; init; } = PorIp;

    /// <summary>Rotas que passam sem limitação.</summary>
    public IReadOnlyCollection<string> Liberadas { get; init; } = ["/saude"];

    /// <summary>A chave pelo IP de origem.</summary>
    public static string PorIp(HttpContext contexto)
        => contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

    /// <summary>A chave por um cabeçalho, caindo para o IP quando ele falta.</summary>
    public static Func<HttpContext, string> PorCabecalho(string nome) => contexto =>
        contexto.Request.Headers.TryGetValue(nome, out var valor) && !string.IsNullOrWhiteSpace(valor)
            ? valor.ToString()
            : PorIp(contexto);
}

/// <summary>
/// Aplica o limitador a cada requisição e devolve 429 quando a cota acaba.
/// </summary>
/// <remarks>
/// Os cabeçalhos não são enfeite: sem <c>Retry-After</c> o cliente educado não
/// sabe quando voltar e fica tentando, e sem <c>X-RateLimit-Remaining</c> ele
/// não tem como se regular antes de bater no limite. Devolver 429 sem eles é
/// tecnicamente correto e praticamente inútil.
/// </remarks>
public sealed class MiddlewareDeLimite
{
    private readonly RequestDelegate proximo;
    private readonly ILimitador limitador;
    private readonly OpcoesDoMiddleware opcoes;

    /// <summary>Monta o middleware.</summary>
    public MiddlewareDeLimite(RequestDelegate proximo, ILimitador limitador, OpcoesDoMiddleware? opcoes = null)
    {
        this.proximo = proximo;
        this.limitador = limitador;
        this.opcoes = opcoes ?? new OpcoesDoMiddleware();
    }

    /// <summary>Trata a requisição.</summary>
    public async Task InvokeAsync(HttpContext contexto)
    {
        if (opcoes.Liberadas.Contains(contexto.Request.Path.Value ?? string.Empty))
        {
            await proximo(contexto);
            return;
        }

        var decisao = limitador.Tentar(opcoes.Chave(contexto));

        contexto.Response.Headers["X-RateLimit-Limit"] = limitador.Politica.Permitidas.ToString();
        contexto.Response.Headers["X-RateLimit-Remaining"] = decisao.Restantes.ToString();
        contexto.Response.Headers["X-RateLimit-Reset"] = decisao.ReiniciaEm.ToUnixTimeSeconds().ToString();

        if (decisao.Permitida)
        {
            await proximo(contexto);
            return;
        }

        contexto.Response.Headers["Retry-After"] = decisao.RetryAfter.ToString();
        contexto.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        await contexto.Response.WriteAsJsonAsync(new
        {
            titulo = "Limite de requisições excedido",
            detalhe = $"Tente de novo em {decisao.RetryAfter} segundo(s).",
            limite = limitador.Politica.ToString(),
            algoritmo = limitador.Nome,
        });
    }
}

/// <summary>Atalho para registrar o middleware.</summary>
public static class Extensoes
{
    /// <summary>Liga a limitação de taxa no pipeline.</summary>
    public static IApplicationBuilder UsarLimiteDeTaxa(
        this IApplicationBuilder aplicacao,
        OpcoesDoMiddleware? opcoes = null)
        => aplicacao.UseMiddleware<MiddlewareDeLimite>(opcoes ?? new OpcoesDoMiddleware());
}
