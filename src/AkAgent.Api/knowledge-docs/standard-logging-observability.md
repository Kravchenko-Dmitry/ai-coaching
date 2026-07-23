# Standard: Logging and Observability

## Logging
- Structured logging only (JSON), via ILogger abstractions; no Console.WriteLine.
- Mandatory fields on every entry: timestamp, level, service name, correlation id.
- Log levels: Debug (local only), Information (business events), Warning (handled
  anomalies), Error (failed operations). Errors must include exception details.
- Never log: passwords, tokens, API keys, full personal data records.

## Tracing
- All services propagate W3C Trace Context headers (`traceparent`).
- Outbound HTTP calls and message publishes are instrumented with OpenTelemetry.

## Metrics
- Every service exposes `/metrics` in Prometheus format.
- Minimum set: request count, request duration (p50/p95/p99), error rate, and one
  domain-specific business metric per service.

## Retention
- Application logs: 30 days hot, 1 year archive.
- Audit-relevant events additionally go to the audit log store (7 years).
