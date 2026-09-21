# limitador-de-taxa

Cinco algoritmos de limitação de taxa em C# e .NET 8, com relógio injetável,
composição em série e middleware para ASP.NET Core.

```csharp
var limitador = new LimitadorComposto(
    new BaldeDeFichas(Politica.PorSegundo(5)),    // segura a rajada
    new JanelaPonderada(Politica.PorMinuto(60))); // segura o abuso continuado

var decisao = limitador.Tentar(chaveDoCliente);

if (!decisao.Permitida)
{
    return Results.StatusCode(429);   // decisao.RetryAfter diz quando voltar
}
```

## Por que existe

"Cem requisições por minuto" parece uma frase sem ambiguidade. Não é — e cada
algoritmo interpreta de um jeito, com consequências diferentes em produção.

### Os cinco, e onde cada um dói

| Algoritmo | Aceita rajada | Memória por chave | O problema dele |
| --- | --- | --- | --- |
| **Janela fixa** | não | um contador | pico na borda |
| **Janela deslizante** | não | um instante por requisição | memória cresce com o limite |
| **Janela ponderada** | não | dois contadores | aproxima o tráfego da janela anterior |
| **Balde de fichas** | **sim** | um contador | permite rajada por definição |
| **Balde furado** | não | um contador | não aproveita ociosidade |

**O pico de borda** é o defeito que pega todo mundo. Com janela fixa de 100 por
minuto, um cliente pode mandar 100 requisições às 12:00:59 e mais 100 às
12:01:00. São duas janelas, cada uma dentro do limite — e 200 requisições em um
segundo. Há um teste documentando isso de propósito, e outro provando que a
janela deslizante e a ponderada não deixam passar.

**A janela ponderada** é o meio-termo que a maioria dos serviços usa de verdade.
Ela guarda só o contador da janela atual e o da anterior, e estima o consumo da
janela móvel pesando o contador antigo pela fração que ainda está dentro dela.
Dois inteiros por chave em vez de milhares de instantes, e o pico de borda some.

**Balde de fichas e balde furado** parecem o mesmo e são opostos:

- O **balde de fichas** acumula *permissão*. Quem ficou parado junta fichas e
  pode gastá-las de uma vez — o que é o comportamento certo para um cliente que
  sincroniza de hora em hora, e não deveria ser punido por isso.
- O **balde furado** acumula *requisição* e a libera em ritmo fixo. É o que se
  quer quando o gargalo é do outro lado: um serviço externo que aceita dez por
  segundo e não aceita onze, nem que seja uma vez só.

## Vendo a diferença

```csharp
Simulador.Rodar(new PedidoDeSimulacao("balde-de-fichas", permitidas: 3,
                                      janelaEmSegundos: 1, requisicoes: 6,
                                      intervaloEmMilissegundos: 0));
// ...xxx
```

Ler a descrição de cinco algoritmos não deixa clara a diferença entre eles. Ver
a mesma rajada passar por cada um, com relógio controlado, deixa. O endpoint
`POST /simulacoes` faz isso pela API.

## Compondo

Serviço de verdade quase nunca tem um limite só:

```csharp
new LimitadorComposto(
    new BaldeDeFichas(Politica.PorSegundo(5)),
    new JanelaPonderada(Politica.PorMinuto(60)),
    new JanelaFixa(Politica.PorDia(10_000)));
```

Com um cuidado que separa um composto correto de um ingênuo: ele **pergunta a
todos antes de consumir de qualquer um**. Se o terceiro recusa depois de os dois
primeiros já terem descontado, a requisição recusada ainda assim gastou
orçamento — e há um teste guardando exatamente isso.

Para cota por plano:

```csharp
new LimitadorPorGrupo(
    chave => chave.StartsWith("pago:") ? "pago" : "gratis",
    new Dictionary<string, ILimitador> { ["pago"] = new JanelaPonderada(Politica.PorSegundo(100)) },
    padrao: new JanelaPonderada(Politica.PorSegundo(2)));
```

## Middleware

```csharp
aplicacao.UsarLimiteDeTaxa(new OpcoesDoMiddleware
{
    Chave = OpcoesDoMiddleware.PorCabecalho("X-Cliente"),
    Liberadas = ["/saude"],
});
```

Devolve 429 com `Retry-After`, `X-RateLimit-Limit`, `X-RateLimit-Remaining` e
`X-RateLimit-Reset`. Os cabeçalhos não são enfeite: sem `Retry-After` o cliente
educado não sabe quando voltar e fica tentando, e sem `Remaining` ele não tem
como se regular antes de bater no limite. Devolver 429 sem eles é tecnicamente
correto e praticamente inútil.

## Testando sem dormir

Todo algoritmo recebe um `IRelogio`. Teste de limitação de taxa contra o relógio
de verdade significa dormir, e teste que dorme é lento e instável:

```csharp
var relogio = new RelogioFixo();
var limitador = new BaldeDeFichas(Politica.PorMinuto(60), relogio);

limitador.TentarVarias("cliente", 60);   // esgota
relogio.AvancarSegundos(60);             // um minuto, instantâneo
limitador.TentarVarias("cliente", 60);   // 60 de novo
```

Os testes comuns a todos os algoritmos ficam em uma classe base: um contrato
quebrado por uma implementação aparece ali, não num teste específico dela.

## Estrutura

```
Politica.cs                      a cota, a decisão e o Retry-After
ILimitador.cs                    o contrato e os atalhos
Tempo/IRelogio.cs                relógio do sistema e relógio de teste
Algoritmos/JanelaFixa.cs         contador por intervalo de relógio
Algoritmos/JanelaDeslizante.cs   por registro e ponderada
Algoritmos/BaldeDeFichas.cs      fichas e balde furado
Armazenamento/Composto.cs        em série e por grupo
```

## Rodando

```bash
dotnet test
dotnet run --project src/Limitador.Api
```

128 testes. O núcleo não depende de nada além da BCL.

## Limites conhecidos

- **Só em memória.** Em mais de uma instância, cada uma conta a sua parte e o
  limite efetivo vira o limite vezes o número de instâncias. Um armazenamento
  compartilhado — Redis com script atômico — resolveria, mas é outro projeto.
- Sem limpeza automática de chaves ociosas: em um cenário com muitas chaves
  distintas, o dicionário cresce até alguém chamar `Esquecer`.
- O balde de fichas não enfileira quem foi recusado; ele recusa e diz quando
  voltar.
- Sem prioridade entre requisições, sem cota compartilhada entre chaves e sem
  limite por custo variável além do parâmetro `custo`.

## Licença

MIT.
