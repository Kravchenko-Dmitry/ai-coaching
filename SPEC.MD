# Architecture Knowledge Agent — MVP Specification

## 1. Purpose and Scope

A .NET 8 service that maintains a read-only, searchable index of architecture documentation (ADRs, guidelines, standards) that lives in external systems, and serves this knowledge through two channels:

1. **Direct Q&A** — architects chat with the agent to ask questions or sanity-check their understanding against the documented architecture.
2. **Guardrails** — the agent plugs into an AI coding workflow (Claude Code) via MCP to constrain and steer what the AI produces, keeping generated artifacts aligned with architectural intent.

The agent is an index, not a source of truth. Documents remain owned by their external source systems; the agent's store is disposable and rebuildable at any time via a full re-sync.

## 2. High-Level Structure

```
External Sources ──> Connectors (IKnowledgeSource) ──> Sync Engine ──> Knowledge Store (IKnowledgeStore)
                                                                             │
                                              ┌──────────────────────────────┤
                                              ▼                              ▼
                                       Query Pipeline ◄──────────── Sync State
                                              │
                              ┌───────────────┴───────────────┐
                              ▼                               ▼
                       REST API (/ask, /sync)          MCP Server (2 tools)
                              │                               │
                         Architects                      Claude Code
```

Three internal seams (interfaces) define the architecture:

| Seam | Interface | MVP implementation | Future implementations |
|---|---|---|---|
| Knowledge in | `IKnowledgeSource` | File-drop (markdown folder) | Confluence, SharePoint, Git/ADR repo |
| Storage/retrieval | `IKnowledgeStore` | In-memory + JSON file persistence | PostgreSQL FTS, vector DB (RAG) |
| Answer generation | `IAnswerService` | LLM (Anthropic API) with retrieved docs in context | unchanged; only retrieval improves |

## 3. Domain Model

```csharp
public record KnowledgeDocument
{
    public required string Id { get; init; }            // stable per source, e.g. "filedrop:adr-007.md"
    public required string SourceName { get; init; }    // connector instance name, e.g. "FileDrop"
    public required string Title { get; init; }
    public required string Content { get; init; }       // normalized markdown
    public required string ContentHash { get; init; }   // SHA-256 of Content
    public required DateTimeOffset LastModified { get; init; }  // from the source system
    public DocumentType Type { get; init; }             // Adr | Guideline | Standard | Diagram | Other
    public IReadOnlyList<DocumentSection> Sections { get; init; } // split by markdown headings
}

public record DocumentSection(string Heading, string Content, int Order);

public record SyncState
{
    public required string SourceName { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public SyncStatus Status { get; init; }             // Ok | Failed | InProgress | Never
    public string? LastError { get; init; }
    public Dictionary<string, string> DocumentHashes { get; init; } = new(); // docId -> hash
}
```

`DocumentType` is inferred heuristically in the MVP (filename/first-heading conventions, e.g. files starting with `adr-` → `Adr`).

## 4. Component Specifications

### 4.1 Source Connectors

```csharp
public interface IKnowledgeSource
{
    string Name { get; }  // unique instance name, used in doc Ids and sync state

    /// Documents created/changed since 'since' (null = full listing).
    /// May over-report; the sync engine deduplicates by ContentHash.
    Task<IReadOnlyList<SourceDocument>> GetChangesAsync(DateTimeOffset? since, CancellationToken ct);

    Task<SourceDocument?> GetDocumentAsync(string id, CancellationToken ct);

    /// Ids currently existing in the source (used to detect deletions).
    Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken ct);

    Task<HealthStatus> HealthCheckAsync(CancellationToken ct);
}

public record SourceDocument(string Id, string Title, string RawContent,
                             DateTimeOffset LastModified, string? ContentType);
```

**MVP implementation — `FileDropKnowledgeSource`:**
- Constructor parameter: folder path (from configuration).
- `GetChangesAsync`: enumerates `*.md` files with file-system `LastWriteTimeUtc > since`.
- `GetAllIdsAsync`: all `*.md` filenames.
- `HealthCheckAsync`: directory exists and is readable.
- Document Id: `"{Name}:{relativeFilePath}"`.

**Registration:** connectors are registered in DI at startup; the sync engine receives `IEnumerable<IKnowledgeSource>`. MVP registers exactly one `FileDropKnowledgeSource`. Adding a source type = new class + DI registration. (Config-driven multi-instance registration is a documented extension, out of scope for MVP.)

### 4.2 Sync Engine

**Triggers:**
1. `BackgroundService` timer, interval from configuration (`Sync:IntervalMinutes`, default 60).
2. `POST /sync` (manual, returns the sync report).
3. Startup: if store is empty or persisted state is missing, run a full sync before serving queries (readiness endpoint reports "warming up" until done).

**Algorithm (per registered source, sources processed sequentially):**
1. Mark `SyncState.Status = InProgress`.
2. `changes = source.GetChangesAsync(state.LastSyncAt)`.
3. For each changed doc: compute `ContentHash`; if hash equals stored hash → skip; else normalize (markdown), split into `Sections` by headings (`#`–`###`), infer `DocumentType`, upsert into `IKnowledgeStore`, update hash map.
4. Deletion detection: `GetAllIdsAsync()`; ids present in hash map but absent in source → remove from store and hash map.
5. Update `SyncState` (`LastSyncAt = now`, `Status = Ok`) and persist it.
6. On exception: `Status = Failed`, `LastError` recorded; previous data remains served (stale-but-available beats unavailable). Failure does not abort other sources.

**Concurrency:** a single semaphore guarantees at most one sync run at a time; a manual trigger during a scheduled run returns `409 Conflict` with the running state.

### 4.3 Knowledge Store

```csharp
public interface IKnowledgeStore
{
    Task UpsertAsync(KnowledgeDocument doc, CancellationToken ct);
    Task RemoveAsync(string docId, CancellationToken ct);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct);
    Task<KnowledgeDocument?> GetAsync(string docId, CancellationToken ct);
    Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken ct); // id, title, type, lastModified
    Task<SyncState?> GetSyncStateAsync(string sourceName, CancellationToken ct);
    Task SaveSyncStateAsync(SyncState state, CancellationToken ct);
}

public record SearchHit(KnowledgeDocument Document, DocumentSection? BestSection, double Score);
```

**MVP implementation — `InMemoryKnowledgeStore`:**
- Documents in a `ConcurrentDictionary<string, KnowledgeDocument>`.
- Persistence: on every mutation, serialize documents and sync state to JSON files under a configured data folder (`Store:DataPath`); loaded at startup. This makes sync state survive restarts, so incremental sync works across runs.
- `SearchAsync` scoring (simple, deterministic):
  - tokenize query (lowercase, split on non-alphanumerics, drop stop-words);
  - score per document = weighted term-frequency: title match ×3, heading match ×2, body match ×1;
  - `BestSection` = highest-scoring section;
  - results ordered by score, cut to `maxResults` (default 5);
  - score normalized to 0..1 (score / max achievable for the query).
- **Relevance threshold:** hits with normalized score `< Store:MinScore` (default 0.15) are discarded. Empty result list ⇒ "not documented" behavior downstream.

The store is the designated replacement point for retrieval upgrades (PostgreSQL `tsvector` FTS, then embeddings/vector search). No consumer-visible contract changes are expected for these swaps.

### 4.4 Query Pipeline (Answer Service)

```csharp
public interface IAnswerService
{
    Task<AgentAnswer> AskAsync(string question, CancellationToken ct);
    Task<ValidationResult> ValidateAsync(string proposal, CancellationToken ct);
}

public record AgentAnswer(string Answer, IReadOnlyList<Citation> Citations,
                          DateTimeOffset? OldestSourceModified, DateTimeOffset LastSyncAt,
                          bool IsDocumented);

public record Citation(string DocumentId, string Title, string? Section, DateTimeOffset LastModified);

public record ValidationResult(ValidationDecision Decision,   // Aligned | Warning | NotCovered
                               string Explanation,
                               IReadOnlyList<Citation> Citations,
                               DateTimeOffset LastSyncAt);
```

**`AskAsync` flow:**
1. `hits = store.SearchAsync(question, 5)`.
2. If `hits` empty → return `IsDocumented = false`, answer text: "This is not documented in the architecture knowledge base." No LLM call.
3. Otherwise build LLM prompt: system prompt (below) + retrieved documents (title, section, lastModified, content) + question.
4. LLM must answer **only** from provided documents and reference them; response is mapped to `AgentAnswer` with citations and freshness fields.

**System prompt (Q&A), normative content:**
- Answer only from the provided architecture documents.
- If the documents do not contain the answer, say so explicitly; never infer undocumented decisions.
- Cite every claim with document title and section.
- If documents conflict, present both positions and flag the conflict.

**`ValidateAsync` flow (guardrail):**
1. Retrieve with the proposal text as query (top 5).
2. No hits → `Decision = NotCovered` ("the architecture documentation does not cover this; consider creating an ADR").
3. Otherwise LLM prompt (validation variant): compare proposal against retrieved constraints; output structured JSON `{decision: "aligned"|"warning", explanation, referencedDocuments[]}`; parsed into `ValidationResult`.
- MVP guardrails are **advisory**: `Aligned | Warning | NotCovered`, never blocking. (Deterministic allow/block for CI is a future evolution, requiring structured rule extraction — out of scope.)

**LLM access:** Anthropic API (model configurable, default `claude-sonnet-4-6`), thin client behind `IAnswerService`'s implementation; API key via configuration/user-secrets, never in source.

### 4.5 REST API (architect-facing)

Base: `http://localhost:5xxx`, JSON. MVP runs without authentication on localhost; an API-key middleware hook is left in place (`Security:ApiKey`, disabled when empty).

| Method & Path | Request | Response | Notes |
|---|---|---|---|
| `POST /ask` | `{ "question": string }` | `AgentAnswer` (200) | 400 if question empty |
| `POST /validate` | `{ "proposal": string }` | `ValidationResult` (200) | same engine as MCP tool |
| `POST /sync` | – | `SyncReport` (200) | 409 if sync already running |
| `GET /status` | – | per-source: `SyncState` summary + doc count + health | also serves as readiness probe |
| `GET /documents` | – | `DocumentSummary[]` | browsing/debugging |
| `GET /documents/{id}` | – | `KnowledgeDocument` | 404 if unknown |

`SyncReport`: `{ startedAt, finishedAt, perSource: [{ sourceName, changed, removed, skipped, status, error? }] }`.

**Error contract:** problem-details (`application/problem+json`) for all non-2xx responses.

### 4.6 MCP Server (Claude Code–facing)

Transport: **stdio** (MVP), launched by Claude Code from `.mcp.json`; the MCP process calls the running REST API internally (or hosts the services in-process — implementation detail; stdio process + HTTP call to the service is the simplest and is the MVP choice).

**Tools:**

```
Tool: query_architecture_knowledge
Description: Ask a question about the documented software architecture
             (decisions, standards, patterns, constraints). Returns an answer
             with citations and freshness information.
Input schema: { "question": { "type": "string" } }   (required)
Output: text content — answer, followed by "Sources:" list
        (title, section, last modified) and "Knowledge last synchronized: <time>".
        If not documented: explicit statement that the topic is not covered.

Tool: validate_against_architecture
Description: Validate a planned implementation approach against the documented
             architecture BEFORE writing code. Returns aligned / warning /
             not-covered with explanation and sources.
Input schema: { "proposal": { "type": "string",
                "description": "Short description of the planned approach" } } (required)
Output: text content — "Decision: aligned|warning|not-covered", explanation,
        sources, sync freshness.
```

**Intended Claude Code workflow** (documented in the repo's `CLAUDE.md`):
1. Developer describes a task; before implementing architecture-relevant changes (new dependencies between services, data storage choices, communication patterns, tech stack additions), Claude Code calls `validate_against_architecture` with its planned approach.
2. For open questions during implementation it calls `query_architecture_knowledge`.
3. `warning` results must be surfaced to the developer with the cited ADRs; Claude Code adjusts the implementation or the developer consciously overrides.

A sample `.mcp.json` and the `CLAUDE.md` instruction block are part of the deliverable.

## 5. Cross-Cutting Concerns

**Freshness transparency.** Every answer (REST and MCP) carries the last-sync timestamp and per-citation last-modified dates. Staleness is displayed, never hidden.

**Failure behavior.**
- Source unreachable during sync → serve existing (stale) knowledge; `/status` shows `Failed` + error.
- LLM unavailable → `/ask` and `/validate` return 503 with problem details; retrieval endpoints unaffected.
- Empty store (first run, before initial sync completes) → `/ask` returns 503 "knowledge base initializing".

**Configuration (appsettings.json):**
```json
{
  "Sync":  { "IntervalMinutes": 60 },
  "Store": { "DataPath": "./data", "MinScore": 0.15, "MaxResults": 5 },
  "FileDrop": { "Path": "./knowledge-docs" },
  "Llm":   { "Model": "claude-sonnet-4-6" },
  "Security": { "ApiKey": "" }
}
```

**Logging.** Structured logging (ILogger): per sync run (per-source counts, duration, errors), per query (question hash, hit count, top score, documented yes/no — no answer content at info level).

**Testing targets (MVP).**
- Unit: store search scoring/threshold; sync diff logic (changed/unchanged/deleted); connector file discovery; answer-service "empty hits → not documented" branch (LLM mocked).
- Integration: full sync from a fixture folder → `/ask` returns cited answer; modify a file → `/sync` → answer reflects change.

## 6. Key Architecture Decisions (with rationale)

1. **Source of truth stays external; the agent is a disposable read-only index.** Full re-sync rebuilds everything; no write path to sources.
2. **Hourly polling + manual `/sync`, no webhooks.** Architecture documentation changes on a scale of days; ≤1 h staleness is acceptable. Polling keeps connectors source-agnostic and simple; incremental hash-diff makes it cheap. Webhooks (Confluence events, Graph change notifications) would add per-source infrastructure for freshness nobody needs; the trigger-agnostic sync engine allows adding them later without redesign.
3. **Pluggability at the connector seam.** One interface (`IKnowledgeSource`), DI-registered; all downstream components operate on a normalized document model. Source type = interface implementation (MVP scope: one type, one instance).
4. **No vector DB / RAG in MVP.** Corpus is small; deterministic keyword retrieval behind `IKnowledgeStore` is sufficient and fully debuggable. Upgrade path — PostgreSQL FTS, then embeddings — is an internal implementation swap invisible to consumers.
5. **Trust over confidence.** Citations on every claim, freshness always displayed, sub-threshold retrieval yields explicit "not documented", conflicts are surfaced. A confidently wrong architecture answer is worse than none.
6. **MCP as the guardrail channel; advisory-only in MVP.** Live, interactive, validates intent before code exists, scales beyond context limits. Deterministic blocking checks (CI) require structured rule extraction and are deferred.
7. **Meet users where they work.** Architects: plain REST/chat. Developers: guardrails inside Claude Code via MCP + `CLAUDE.md` conventions. No new portal.

## 7. Out of Scope (MVP)

Permissions/ACL propagation from sources; multiple instances per source type (config-driven registration); webhooks; PostgreSQL/vector storage; semantic (embedding) retrieval; deterministic blocking guardrails / CI integration; dedicated web UI; multi-user auth; conversation memory in Q&A (each `/ask` is stateless).

## 8. Future Evolution (documented, not built)

1. Confluence/SharePoint connectors (delta APIs; permissions via `GetPermissionsAsync` extension).
2. Config-driven multi-instance connector registration.
3. PostgreSQL FTS store implementation → vector store + embeddings (true RAG); hybrid retrieval.
4. Structured rule extraction from ADRs → deterministic rule evaluator → CI PR check (allow/block).
5. Webhook receivers invoking the per-document sync routine.
6. Thin chat clients (Teams/Slack/Web) over `/ask`.
