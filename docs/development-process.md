# Development process

- Work on a dedicated branch in atomic vertical slices.
- Every substantive non-merge commit includes exactly one Journal.
- Every Journal declares whether it creates an ADR, applies one or needs no new ADR.
- Create ADRs only for durable, surprising trade-offs.
- Keep the active spec and roadmap truthful when they exist.
- Test through public seams; record only commands that were actually run.
- Preserve atomic commits in pull requests.
- Release only after CI, configuration readiness and smoke validation.
- Backfill published-history gaps explicitly; never represent retrospective files as original.

## Records

- `docs/journal/`: one numbered, dated increment record per substantive non-merge commit.
- `docs/adr/`: durable architectural decisions. Use `ADR-NNNN-title.md`.
- `docs/sdd/`: active implementation specifications when a change needs durable product detail.
