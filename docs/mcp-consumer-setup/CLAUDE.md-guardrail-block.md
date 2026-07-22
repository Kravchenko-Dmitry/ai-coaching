## Architecture guardrails (ak-agent MCP)

This repo is connected to the Architecture Knowledge Agent via MCP (`ak-agent`), which indexes
this organization's ADRs, guidelines, and standards. Use it as follows:

1. Before implementing an architecture-relevant change — a new dependency between services, a
   data storage choice, a communication pattern, or a tech stack addition — call
   `validate_against_architecture` with a short description of the planned approach.
2. For open questions that come up during implementation, call `query_architecture_knowledge`.
3. If a `validate_against_architecture` call returns `warning`, surface it to the developer along
   with the cited documents before proceeding. Either adjust the implementation to align with the
   cited architecture, or have the developer consciously confirm they want to override it.
4. A `not-covered` result is not a green light — it means the topic isn't documented, not that it's
   endorsed. Mention this to the developer; suggest creating an ADR if the decision is significant.
5. Every answer carries a "Knowledge last synchronized" timestamp. If it looks stale relative to
   the change being discussed, say so.
