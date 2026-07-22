# Connecting a repo to the Architecture Knowledge Agent via MCP

These files are templates for a *consumer* repo — the one where Claude Code is doing
development work and should be guarded by this agent's architecture knowledge. Do not copy them
into this repo (ak-agent already runs its own REST API and MCP server).

## Setup

1. Make sure the AkAgent REST API is running (`dotnet run --project src/AkAgent.Api` from this
   repo, or a deployed instance) and note its base URL.
2. Copy `.mcp.json` into the root of the consumer repo. Update:
   - `args`: the absolute path to `src/AkAgent.Mcp` in this repo (or, for a published build, point
     `command` at the built `AkAgent.Mcp` executable instead of `dotnet run`).
   - `env.Api__BaseUrl`: the REST API's base URL, if it isn't running on the default
     `http://localhost:5024`.
3. Append the contents of `CLAUDE.md-guardrail-block.md` to the consumer repo's `CLAUDE.md`.
4. Restart Claude Code in the consumer repo so it picks up the new MCP server.
