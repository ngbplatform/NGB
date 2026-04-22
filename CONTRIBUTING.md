# Contributing to NGB Platform

Thank you for your interest in contributing to **NGB Platform**.

NGB is a long-lived platform for building complex business applications. Contributions are welcome, but they should align with the platform's architectural principles, quality bar, and long-term maintainability goals.

## Before you contribute

Please make sure you:

- read the root `README.md`;
- review open issues and discussions before starting work;
- open an issue before making major changes, so the direction can be discussed first;
- keep changes focused and easy to review.

## What kinds of contributions are welcome

We welcome contributions such as:

- bug fixes;
- documentation improvements;
- test coverage improvements;
- performance improvements;
- developer experience improvements;
- carefully scoped platform enhancements.

For larger architectural, behavioral, or API-level changes, please open an issue first and describe the motivation, proposed design, trade-offs, and compatibility impact.

## Contribution principles

Contributions should follow these principles:

- **Keep the platform coherent.** Avoid changes that add special cases, inconsistent patterns, or domain-specific leakage into shared platform modules.
- **Prefer maintainability over cleverness.** The codebase should remain understandable and production-ready.
- **Preserve architectural boundaries.** Platform modules, infrastructure modules, and vertical solutions should remain properly separated.
- **Prefer explicitness.** Behavior, contracts, naming, and data flow should be clear.
- **Do not introduce breaking changes casually.** Compatibility matters.
- **Include tests where appropriate.** Behavioral changes and bug fixes should generally be covered by tests.

## Development workflow

1. Fork the repository.
2. Create a focused branch from `main`.
3. Make your changes.
4. Add or update tests as needed.
5. Run the relevant build and test commands locally.
6. Open a pull request with a clear explanation of the change.

## Pull request guidelines

Please keep pull requests:

- small enough to review effectively;
- clearly described;
- limited in scope;
- free of unrelated cleanup.

A good pull request usually includes:

- the problem being solved;
- the approach taken;
- important trade-offs or limitations;
- notes about tests, migrations, or compatibility impact.

## Commit guidance

Clear commit messages are encouraged.

Examples:

- `Add report definition validation for duplicate field aliases`
- `Fix PostgreSQL reader paging bug in account card report`
- `Clarify Property Management setup instructions`

## Reporting bugs

When reporting a bug, please include as much of the following as possible:

- what you expected to happen;
- what actually happened;
- steps to reproduce;
- relevant logs or screenshots;
- environment details such as OS, .NET version, PostgreSQL version, and browser if relevant.

## Suggesting features

Feature requests are welcome, especially when they include:

- the business or technical problem;
- why the change belongs in the platform rather than a single vertical solution;
- possible alternatives considered;
- expected compatibility impact.

## Code of Conduct

By participating in this project, you agree to follow the rules described in [`CODE_OF_CONDUCT.md`](./CODE_OF_CONDUCT.md).

## Security issues

Please do **not** report security issues publicly in GitHub issues.

If a security policy is present in the repository, follow the instructions in `SECURITY.md`.
Until then, please report sensitive security issues privately to the project maintainer.
