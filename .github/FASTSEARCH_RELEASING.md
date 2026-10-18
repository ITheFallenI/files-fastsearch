# Releasing Files FastSearch

Releases are built by the **FastSearch Release** workflow (`.github/workflows/fastsearch-release.yml`), which only runs when a maintainer starts it. It attaches everything to a **draft** release. Nothing becomes public until a maintainer publishes that draft by hand.

## One-time setup

1. Apply to the [SignPath Foundation](https://signpath.org/) for free open-source code signing. They review the public repository and its build.
2. In SignPath, create a project with two artifact configurations:
   - `fmf-service`: an `.exe` (Authenticode). The input is the zip of the uploaded artifact, which contains `fmf-service.exe` and `fmf-service-THIRD-PARTY.txt`.
   - `msixbundle`: the `.msixbundle` in the packages artifact.
3. Add these repository variables: `FASTSEARCH_PUBLISHER` (the signing certificate's exact subject, for example `CN=SignPath Foundation, O=SignPath Foundation, ...`), `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG`.
4. Add one repository secret, `SIGNPATH_API_TOKEN`.

Until signing is configured, the workflow still builds and uploads unsigned packages as a workflow artifact for testing. It never creates a release from them.

## Why the service is pinned

`Files.App.FastSearch` only starts the bundled `fmf-service.exe` elevated when two things hold:

- The image's Authenticode-stable SHA-256 matches `FmfServiceImageSha256`, which the release workflow computes with `.github/scripts/Get-PeImageDigest.ps1` and passes to MSBuild.
- The image carries a trusted Authenticode signature.

Local and CI Debug builds are `UNPINNED`. There, the **Fast search** settings page says it can't install the service, and developers run `fmf-service setup --owner-sid=<SID>` from an administrator terminal instead.

## Before publishing a draft

- [ ] CI is green on the release commit, including the Find My Files engine tests.
- [ ] The Find My Files submodule points at a commit that is reachable from a public repository.
- [ ] Install the `.msixbundle` (through `FilesFastSearch.appinstaller`) on a clean Windows 11 x64 machine or VM, beside official Files.
- [ ] **Settings → Advanced → Fast search → Turn on fast search** shows one UAC prompt, and the service starts and indexes.
- [ ] Filename searches are answered from the index. `tag:`, `kind:`, zip and network searches still use the regular search.
- [ ] Create, rename and delete a file, and check that searches reflect each change.
- [ ] **Remove indexing service** with "Also delete the saved index", and check that `sc query fmf-engine` reports 1060 and `C:\ProgramData\find-my-files` is gone.
- [ ] Uninstall Files FastSearch from Settings → Apps. Check that official Files still opens, keeps its settings, and still owns `files:` links and its own aliases.
- [ ] `Licenses\find-my-files-LICENSE.txt` and `Licenses\fmf-service-THIRD-PARTY.txt` are in the installed package, and NOTICE.md is current.
- [ ] Write the release notes. Publishing the draft also makes it the `latest` release the in-app updater reads.
