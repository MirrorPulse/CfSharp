# Contributing to CfSharp

CfSharp values correctness, reviewability, and a clear project history. Contributions must follow the rules below.

## Language

Use English for source code, identifiers, comments, documentation, tests, examples, commit messages, and project metadata.

Project-owned copyright, package author, and primary attribution metadata must use `Quaternion8192`. Individual contributors remain identified by their Git commit authorship.

## Atomic Changes

- Keep each commit limited to one logical change or one fix.
- Do not combine unrelated fixes, refactoring, formatting, dependency updates, or documentation changes in one commit.
- Split preparatory refactoring from behavior changes when each can be reviewed and tested independently.
- Keep every commit buildable and testable whenever technically possible.
- Add or update tests in the same commit as the behavior they verify.

## Commit Messages

Use Conventional Commits:

```text
<type>[optional scope][!]: <description>
```

Allowed types include:

- `feat`: a user-visible capability;
- `fix`: a defect correction;
- `docs`: documentation-only changes;
- `test`: test-only changes;
- `refactor`: behavior-preserving code restructuring;
- `perf`: a performance improvement;
- `build`: build system or dependency changes;
- `ci`: continuous-integration changes;
- `chore`: repository maintenance;
- `revert`: a prior commit reversal.

Use an imperative, concise description without a trailing period. Add a body when the reason, tradeoff, compatibility impact, or verification is not obvious. Mark breaking changes with `!` and a `BREAKING CHANGE:` footer.

Examples:

```text
feat(items): add range hydration
fix(native): correct CF_OPERATION_INFO layout on ARM64
docs: explain local and remote change flows
```

## Verification

- Run the narrowest relevant tests while developing and the complete required suite before submission.
- Treat native ABI size, offset, calling-convention, and constant checks as mandatory for interop changes.
- Include Windows integration coverage for changes that alter sync roots, placeholders, callbacks, or file-system state.
- Document tests that cannot be run and explain the remaining risk.

## Scope And Generated Content

- Do not commit local research, downloaded documentation, credentials, build output, or IDE state.
- Keep generated changes reproducible and commit the generator or source-of-truth update with the output when required.
- Avoid drive-by cleanup outside the contribution's stated purpose.
