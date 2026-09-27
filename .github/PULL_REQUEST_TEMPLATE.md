## Change type

- [ ] `breaking` — public or package compatibility break
- [ ] `feature` — backward-compatible functionality
- [ ] `fix` — backward-compatible correction

Main-bound pull requests must originate from `develop` and carry exactly one of
the labels above. The branch-policy check enforces this rule.

## Verification

- [ ] The change is limited to one logical atomic commit or a cohesive atomic series.
- [ ] Release build and relevant tests pass locally.
- [ ] Public API, package, or workflow changes include their documentation and evidence.
- [ ] No secrets, user data, generated binaries, or ignored artifacts are included.

## Summary

<!-- Explain the behavior change, compatibility impact, and verification evidence. -->
