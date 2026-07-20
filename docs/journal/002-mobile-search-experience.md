# 002 - Correct mobile search and exact-model discovery

## Commit

`fix: improve mobile search and exact model discovery`

## Objetivo

Fazer a busca global caber no fluxo mobile sem cobrir o conteudo e assegurar
que um modelo digitado exatamente seja consultado antes de resultados apenas
relacionados, como Redmi quando a busca pede Xiaomi.

## Implementacao

- A busca do cabecalho passa a abrir como uma linha propria no grid mobile,
  mantendo navegacao e conteudo visiveis.
- A navegacao mobile usa tres colunas consistentes para Catalogo, Comparar e
  Metodologia.
- A cobertura ampla consulta a busca direta e a pagina da marca enquanto nao
  houver correspondencia exata no indice; uma correspondencia relacionada nao
  encerra mais a descoberta.
- Adicionado teste de regressao para um snapshot com `Redmi 12` e resultado
  direto para `Xiaomi 12`.

## Rastreabilidade ADR

- `Decisao local sem ADR novo: a prioridade de descoberta e o posicionamento responsivo sao regras locais reversiveis.`

## Verificacao

- `dotnet build SpecTen.sln --no-restore` - aprovado, sem avisos ou erros.
- `dotnet test SpecTen.sln --no-restore --filter "FullyQualifiedName~DeviceCoverageSnapshotTests"` - aprovado: 9 testes.
- Navegador real em 425 x 1127 - a busca aberta nao sobrepoe a home; a selecao de Xiaomi 15T e Xiaomi 15T Pro abriu a comparacao com os dois modelos corretos.
- `dotnet test SpecTen.sln --no-restore` - bloqueado por 23 falhas preexistentes em `OfficialCoverageFallbackTests` e `ApiSmokeTests`; o total ficou em 149 aprovados e 23 falhos. As falhas ja estavam presentes antes deste incremento e nao pertencem ao escopo mobile/exato.

## Alternativas e trade-offs

- Esconder resultados relacionados reduziria descoberta. Em vez disso, a busca direta apenas e acionada quando falta a correspondencia exata, preservando sugestoes proximas depois dela.

## Proximo passo

Investigar separadamente as falhas existentes de fixtures/cobertura oficial antes de promover a branch.
