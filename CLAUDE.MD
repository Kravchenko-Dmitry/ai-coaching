# Architecture Knowledge Agent

## Source of truth
- `SPEC.md` is the authoritative specification. Implement exactly what it defines.
- If SPEC.md is ambiguous or you want to deviate: STOP and ask before writing code.
- Anything listed in SPEC.md §7 (Out of Scope) must NOT be implemented, even if trivial.

## Stack & conventions
- .NET 8, C# 12, minimal APIs (no controllers), nullable enabled, implicit usings.
- Single solution `ArchKnowledgeAgent.sln`:
  - `src/AkAgent.Domain` — records, enums, interfaces (no dependencies)
  - `src/AkAgent.Infrastructure` — FileDrop connector, in-memory store + JSON persistence, sync engine, Anthropic client
  - `src/AkAgent.Api` — minimal API host, DI wiring, BackgroundService
  - `src/AkAgent.Mcp` — stdio MCP server (calls REST API via HttpClient)
  - `tests/AkAgent.Tests` — unit; `tests/AkAgent.IntegrationTests` — integration (WebApplicationFactory)
- Records over classes for models (as in SPEC.md §3 — use those definitions verbatim).
- `IOptions<T>` for config sections (Sync, Store, FileDrop, Llm, Security).
- Problem-details for all non-2xx responses (SPEC.md §4.5).
- Structured logging via ILogger; never log answer content or API keys.
- Anthropic API key: user-secrets / env var only. Never in source or appsettings.

## Testing
- NUnit + NSubstitute. Run `dotnet build && dotnet test` after every milestone; do not proceed on red.
- Test targets are defined in SPEC.md §5 "Testing targets" — treat them as the minimum.
- Integration tests use a fixture folder under `tests/fixtures/knowledge-docs/`.

## Workflow
- Work milestone by milestone (see Milestones). One commit per milestone, message: `M<n>: <summary>`.
- Before starting a milestone, restate in 2–3 sentences what you will build and which SPEC.md sections apply.
- LLM calls are mocked in all tests (`IAnswerService` boundary). No live API calls in tests.

## Milestones
1. Solution skeleton + Domain model (SPEC.md §3) + interfaces (§4.1, §4.3, §4.4 signatures)
2. InMemoryKnowledgeStore + JSON persistence + search scoring/threshold + unit tests
3. FileDropKnowledgeSource + unit tests (file discovery, since-filter)
4. Sync engine (diff/hash/delete logic, semaphore, BackgroundService, startup sync) + unit tests
5. AnswerService (Anthropic client, Q&A + validation prompts, empty-hits branch) + unit tests
6. REST API endpoints (§4.5) + problem-details + /status readiness + integration tests
7. MCP server (§4.6, stdio, 2 tools) + sample `.mcp.json` + CLAUDE.md guardrail block for consumer repos

## Definition of done (per milestone)
- Builds clean, all tests green, no warnings introduced.
- No TODOs left in code; unresolved questions go to me, not into comments.