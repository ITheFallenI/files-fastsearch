# Code signing policy

## Current status

SignPath Foundation signing is planned and has not been approved or configured.
There is no publicly trusted signed Files FastSearch release yet. Local test
packages use a self-signed certificate whose non-exportable private key remains in
the maintainer's Windows certificate store. Private keys and PFX files are never
stored in Git or attached to releases. A local test certificate is not a substitute
for a publicly trusted release certificate.

## Responsibilities

- Maintainer, committer and reviewer: [ITheFallenI](https://github.com/ITheFallenI).
- Release/signing approver: ITheFallenI. Each public signing request requires
  maintainer review of the source, workflow and generated artifacts.
- External contributions require review before entering a signing branch.

## Planned release process

Builds must use the exact reviewed Files commit and publicly reachable FMF submodule
commit. The workflow signs the FMF service, pins its Authenticode-stable image digest
into the app, and signs the MSIX bundle. Unsigned runs produce test artifacts only.
The workflow creates draft releases; publishing a draft is a separate action.

If the Foundation accepts the project, its required attribution will be added after
approval. No SignPath sponsorship or certificate availability is claimed today.
See the [privacy policy](FASTSEARCH_PRIVACY.md) and
[release checklist](FASTSEARCH_RELEASING.md).
