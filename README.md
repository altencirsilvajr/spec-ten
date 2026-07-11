# SpecTen

Blazor Web App em `.NET 9` para catalogar celulares com specs, fontes, score de confianca, classificador por benchmark/chipset e comparacao publica.

## Stack

- ASP.NET Core / Blazor Web App `net9.0`
- EF Core + PostgreSQL via Npgsql
- Minimal APIs para busca, comparacao e reportes
- BackgroundService opcional para importacao diaria
- Dockerfile pronto para Railway

## Rodar localmente

Configure um PostgreSQL local:

```text
Host=localhost;Port=5432;Database=specten;Username=postgres;Password=postgres
```

Depois:

```bash
dotnet restore
dotnet test
dotnet run --project src/SpecTen.Web
```

O ambiente de desenvolvimento pode usar adapters de fixture. O perfil de producao os mantem desligados e usa o seed versionado, cobertura sob demanda e fontes oficiais habilitadas.

## Railway

O Railway precisa do `Dockerfile` porque o deploy .NET usa container. O `railway.json` na raiz seleciona o Dockerfile correto e configura `/health`. Adicione um PostgreSQL ao projeto e defina:

```text
DATABASE_URL=postgresql://...
Scraping__Enabled=false
Scraping__UseFixtureAdapters=false
Scraping__DailyUtcHour=6
Scraping__UserAgent=SpecTenBot/1.0 (+https://seu-dominio/robots.txt)
Coverage__Enabled=true
Coverage__OnDemandHydrationEnabled=true
Coverage__MakerPageLimit=24
Coverage__MakerPageDelayMilliseconds=250
Coverage__CatalogEntryRefreshHours=168
```

Mantenha `Scraping__Enabled=false` ate registrar e revisar permissao, robots e limites das fontes que serao ativadas. O app le `PORT` automaticamente e expoe `/health`.

## Busca sob demanda e cache

O fluxo publico recomendado para o SpecTen fica assim:

- `PostgreSQL` continua sendo a fonte de verdade do catalogo publicado.
- A busca publica tenta o banco primeiro e usa cobertura remota quando o modelo nao existe ou quando a ficha local esta velha, incompleta ou suspeita.
- Quando a cobertura remota encontra um aparelho valido, a ficha entra no banco e passa a responder como catalogo persistido nas proximas consultas.
- O cache local so acelera leitura. Ele nao decide verdade de dados e eh limpo apos importacoes e hidratacoes relevantes.

Redis faz sentido quando voce tiver mais de uma instancia web ou muito trafego de busca. Nessa fase ele entra para compartilhar cache quente entre instancias e reduzir repeticao de consultas externas. Ele nao substitui o banco nem a logica de proveniencia.

## Fontes

O catalogo usa cobertura versionada para descoberta e provedores oficiais para enriquecimento sob demanda. Adapters de terceiros permanecem como fixtures enquanto permissao de uso, robots, limite de taxa e politica de imagens nao estiverem aprovados para execucao automatica.

## Nota de versao

O projeto fixa `.NET 9` por compatibilidade com o ambiente atual. Planeje migrar para `.NET 10 LTS` antes do fim de suporte do .NET 9.
