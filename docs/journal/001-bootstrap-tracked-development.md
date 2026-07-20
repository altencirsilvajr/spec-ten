# 001 - Bootstrap tracked development

## Commit

`docs: bootstrap tracked development records`

## Objetivo

Estabelecer registros de processo e verificacao para que as proximas mudancas
do SpecTen permaneçam auditaveis por incremento.

## Implementacao

- Definidas regras locais de branch, verificacao, Journal e ADR.
- Criadas convencoes minimas para Journals, ADRs e especificacoes duraveis.

## Rastreabilidade ADR

- `Decisao local sem ADR novo: o formato de registro e processo do repositorio pode evoluir sem alterar a arquitetura do produto.`

## Verificacao

- `Get-Content -Raw C:\Users\alten\.codex\skills\tracked-development\SKILL.md` - instrucoes de desenvolvimento rastreado lidas.
- `git status --short --branch` - arvore inicial limpa na branch `main` antes deste incremento.

## Alternativas e trade-offs

- Manter apenas a orientacao externa impediria que revisores do repositorio encontrassem as regras e os registros futuros. A convencao local e curta e aponta os comandos reais do projeto.

## Proximo passo

Corrigir a prioridade da busca por modelo exato e o layout de busca/comparacao em telas mobile.
