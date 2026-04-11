# Contributing to CC2UMAConverter

First off, thank you for considering contributing to CC2UMAConverter! It's people like you that make this tool better for everyone.

## Getting Started

1. Fork the repository on GitHub.
2. Clone your fork locally.
3. Check out the `main` branch and create a new feature branch from it.
4. If you're working on the Blender addons (`UMAConverterBlender` or `DazUMAConverterBlender`), you can test them directly in Blender by installing them from your local clone.
5. If you're working on the Unity package (`UmaConverterUnity`), you can test it by creating a new Unity project and importing the package from your local clone folder.

## Coding Guidelines

- Please ensure your code follows the existing style of the repository.
- Use descriptive commit messages.
- Always run impact analysis using `gitnexus_impact` before modifying functions or classes, as documented in `AGENTS.md`.
- Be mindful of the shared nature of the Unity post-processor; changes there affect both CC4 and Daz pipelines.

## Making a Pull Request

1. Ensure any new features are well documented.
2. Check that your changes don't break existing functionality.
3. Push your branch to your fork on GitHub.
4. Open a Pull Request against the `main` branch of this repository.
5. Provide a clear and descriptive title for your PR.
6. Fill out the Pull Request template provided.

## Reporting Bugs / Requesting Features

Please use the issue tracker to report bugs or request new features. We provide templates for both to help you gather the necessary information.
