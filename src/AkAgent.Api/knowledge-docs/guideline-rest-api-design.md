# Guideline: REST API Design

## Versioning
- APIs are versioned in the URL path: `/api/v1/...`.
- Breaking changes require a new major version; v(n-1) is supported for 6 months after
  v(n) goes live.

## Naming
- Resource names are plural nouns in kebab-case: `/api/v1/purchase-orders`.
- No verbs in paths; actions that don't map to CRUD use sub-resources
  (e.g. `POST /purchase-orders/{id}/cancellation`).

## Responses
- JSON only, camelCase property names.
- Errors use RFC 7807 problem details (`application/problem+json`).
- Collections are paginated with `page`/`pageSize` query parameters and return a
  `totalCount` header; default page size 50, maximum 200.

## Idempotency
- PUT and DELETE must be idempotent.
- POST endpoints that create resources accept an optional `Idempotency-Key` header.
