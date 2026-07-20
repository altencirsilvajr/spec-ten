# 003 - Restore configured exact-search hydration

## Commit

`fix: restore configured exact-search hydration`

## Objetivo

Restabelecer a consolidacao da ficha quando a cobertura sob demanda esta
explicitamente habilitada e alinhar os testes de integracao aos dois fluxos:
hidratacao configurada e resposta publica sem bloqueio.

## Implementacao

- A busca e as sugestoes voltam a recarregar o catalogo apos uma hidratacao
  exata autorizada pela configuracao de cobertura.
- O ambiente principal de integracao habilita essa capacidade para exercitar a
  ficha completa.
- Os dois testes que verificam resposta sem bloqueio criam ambientes isolados
  com hidratacao explicitamente desligada.
- Atualizadas as assercoes de rotulos e anchors que tinham ficado obsoletos na
  interface publica.

## Rastreabilidade ADR

- `Decisao local sem ADR novo: a hidratacao continua condicionada a uma configuracao existente e os contratos de teste apenas tornam os dois modos explicitos.`

## Verificacao

- `dotnet test SpecTen.sln --no-restore --filter "FullyQualifiedName~ApiSmokeTests.SearchApi_PrefersExactCompactModelMatch_OverNumericSuperset"` - aprovado: 1 teste.
- `dotnet test SpecTen.sln --no-restore` - aprovado: 172 testes.

## Alternativas e trade-offs

- Descartar os testes antigos de hidratacao esconderia a diferenca entre os
  modos configuraveis. Os testes agora isolam explicitamente o modo sem
  bloqueio e mantem cobertura para a consolidacao habilitada.

## Proximo passo

Publicar a branch, abrir PR, aguardar CI e promover somente com os checks
verdes.
