Audit the current codebase against SPEC.md.

Report only deviations, grouped as:
1. **Violations** — behavior contradicting SPEC.md (cite section)
2. **Missing** — specified but not implemented (ignore milestones not yet started)
3. **Scope creep** — implemented but listed in §7 Out of Scope or not in SPEC.md at all

For each item: file/location, SPEC.md section, one-line fix proposal. No code changes; report only.