# Contributing documentation and code

CfSharp follows the repository rules in `AGENTS.md` and `CONTRIBUTING.md`. The short version is:

- use English for source, public documentation, tests, and examples;
- keep one logical behavior or fix per commit;
- include tests and documentation with the behavior they describe;
- preserve the dependency direction and explicit ownership semantics;
- run the smallest relevant local gates, then the full CI checks required by the change.

## Editing a guide

Use sentence-case headings and direct language. Start with prerequisites and scope. Code examples
should use Windows paths and include cancellation where the public API requires it. Explain what the
host owns, what CfSharp owns, and what happens after a failure or restart.

Run link and spelling checks locally when available. A guide is not complete if it describes an
unsupported architecture, silently invents a remote service, or hides an irreversible unregister,
delete, or migration step.

## API comments

Public APIs require XML documentation covering ownership, lifetime, thread-safety, platform
requirements, failure modes, and relevant native behavior. The generated reference is built from
Release assemblies and XML files, so missing comments are a documentation defect as well as an API
quality issue.
