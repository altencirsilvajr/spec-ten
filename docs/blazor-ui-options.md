# Opções de UI Blazor para o SpecTen

Pesquisa em 11 de julho de 2026, com fontes oficiais. O objetivo é dar ao catálogo público uma aparência profissional, consistente e confiável sem reescrever a lógica de catálogo, busca ou comparação.

## Contexto verificado

O `SpecTen.Web` é um Blazor Web App `net9.0`, com componentes em Razor, `InteractiveServer` e CSS próprio centralizado em `wwwroot/app.css`; não há biblioteca de UI no projeto hoje. Isso permite migrar visualmente por página e preservar rotas, serviços, DTOs e APIs.

Um template visual não corrige por si só a imagem ausente nem o campo de busca que perde caracteres. Esses são fluxos de dados/interatividade e devem continuar sendo tratados separadamente. A nova UI deve ser aplicada somente depois que o input puder manter seu valor local enquanto a consulta assíncrona acontece em segundo plano.

## Opções avaliadas

### 1. MudBlazor — recomendada

Biblioteca de componentes Material Design escrita para Blazor, com [layouts oficiais](https://mudblazor.com/getting-started/layouts), tema configurável, playground e [templates de projeto oficiais](https://github.com/MudBlazor/Templates). A linha atual suporta .NET 9 e exige interatividade — condição já atendida pelo SpecTen em `InteractiveServer` ([README oficial](https://github.com/MudBlazor/MudBlazor)).

- **Licença/custo:** [MIT](https://github.com/MudBlazor/MudBlazor/blob/dev/LICENSE); sem custo ou royalty de produção.
- **Maturidade visual:** design system Material, componentes de layout, navegação, formulário, cards, chips, skeletons e diálogos. É uma boa base para lista de aparelhos, filtros e página de especificações, mas não oferece um catálogo de celulares pronto: a identidade e os cards do SpecTen continuam sendo próprios.
- **Migração:** média e incremental. Seguir a [instalação oficial](https://mudblazor.com/getting-started/installation), criar primeiro um `MudTheme` com os tokens SpecTen e converter `MainLayout`, busca, filtros e cards gradualmente. Não substituir tudo de uma vez nem manter CSS concorrente para os mesmos elementos.

**Por que é a melhor escolha aqui:** entrega uma linguagem visual coerente e responsiva sem custo recorrente, encaixa no render mode atual e deixa espaço para o catálogo parecer uma marca própria, em vez de uma tela administrativa genérica.

### 2. Microsoft Fluent UI Blazor

Biblioteca Microsoft de componentes Razor baseada no Fluent Design System. Possui [demo e documentação oficiais](https://www.fluentui-blazor.net/) e [templates `dotnet new`](https://github.com/microsoft/fluentui-blazor/blob/main/README.md) já configurados; o projeto declara uso em Blazor .NET 8 e 9.

- **Licença/custo:** [MIT](https://github.com/microsoft/fluentui-blazor/blob/main/LICENSE); sem custo de runtime.
- **Maturidade visual:** design tokens e acessibilidade fazem parte do sistema; resulta naturalmente no aspecto de aplicações modernas da Microsoft.
- **Migração:** média. Adiciona o pacote, `AddFluentUIComponents`, imports e providers no layout. Os templates devem servir como referência para extrair shell e padrões, não como motivo para recriar o projeto. Exige validar a versão escolhida antes de adotar, pois a própria documentação registra mudanças incompatíveis entre versões.

**Quando escolher:** se a direção desejada for explicitamente um produto com estética Fluent/Microsoft. Para um catálogo de consumo, o visual pode parecer mais corporativo do que o necessário.

### 3. Radzen Blazor Components

Biblioteca nativa de Blazor com mais de 145 componentes e suporte declarado a .NET 9 e Blazor Server ([README oficial](https://github.com/radzenhq/radzen-blazor)). A documentação mostra a integração com render modes interativos, pacote, tema, serviço e script ([guia oficial](https://blazor.radzen.com/get-started)).

- **Licença/custo:** componentes sob [MIT](https://github.com/radzenhq/radzen-blazor/blob/master/LICENSE), inclusive para uso comercial. A oferta Pro adiciona suporte, ferramentas, temas e templates.
- **Maturidade visual:** muitos componentes, temas claro/escuro e variáveis CSS; os templates/produtividade mais avançados pertencem à assinatura Pro.
- **Migração:** média. Adiciona `Radzen.Blazor`, `AddRadzenComponents`, tema em `App.razor` e o JavaScript da biblioteca. É forte para CRUD e áreas internas; para o catálogo público, seria preciso desenhar e revisar cuidadosamente cards e hierarquia para não ficar com aparência de backoffice.

### 4. Telerik UI for Blazor

Opção comercial com componentes Blazor nativos, [templates de projeto](https://www.telerik.com/blazor-ui/documentation/installation/project-templates), temas e `ThemeBuilder`. É a opção que oferece mais material pronto diretamente aplicável: [Page Templates e Building Blocks](https://www.telerik.com/page-templates-and-ui-blocks/) incluem páginas de listagem, filtros, product cards e um exemplo de catálogo de moda.

- **Licença/custo:** comercial; a página de compra mostrava planos a partir de **US$ 749 por desenvolvedor/ano**, além de trial de 30 dias ([preços oficiais](https://www.telerik.com/purchase/blazor-ui)). Confirmar o valor no momento da compra.
- **Maturidade visual:** alta; possui temas completos, documentação de design e blocos responsivos já prontos para copiar e adaptar.
- **Migração:** média/alta. Além da conversão dos componentes e do layout, requer pacote/feed e gestão da licença em desenvolvimento e CI. Há maior lock-in e custo recorrente, mas é a alternativa indicada se houver orçamento para suporte comercial e se a prioridade for partir de templates de produto já produzidos.

## Decisão recomendada

Fazer um POC com **MudBlazor** antes de uma migração ampla:

1. Definir tokens de marca (cores, tipografia, espaçamento, raio, estados de foco e estados de carregamento) em um único tema.
2. Converter apenas `MainLayout` e a página de catálogo: barra de busca, filtros, skeleton, card de aparelho e estado vazio/erro.
3. Testar em desktop e mobile com busca lenta e imagens indisponíveis; o campo precisa continuar controlado localmente pelo navegador, sem ter o texto reescrito por uma resposta atrasada do servidor.
4. Só então migrar detalhes e comparação, removendo os trechos equivalentes do CSS próprio a cada etapa.

Se o POC não alcançar o nível visual desejado ou se o time aceitar uma licença comercial, avaliar Telerik como segunda opção — especificamente pelos blocos de catálogo, filtros e cards. Não recomendo substituir o site inteiro por um starter template: aproveitar o design system e refazer a hierarquia visual do SpecTen preserva melhor a marca e reduz risco de regressão.
