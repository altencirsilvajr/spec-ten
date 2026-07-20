# Development workflow

Follow `docs/development-process.md` for every state-changing change.

Before each substantive commit, run:

```powershell
dotnet test SpecTen.sln
git diff --check
```

Every substantive non-merge commit must include exactly one new Journal in
`docs/journal/` and declare its ADR status. Create ADRs in `docs/adr/` only
for durable architectural trade-offs.
