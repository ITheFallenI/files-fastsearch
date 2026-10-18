# Files FastSearch privacy policy

The FMF service reads local NTFS filename and filesystem metadata and stores its
index under `%ProgramData%\find-my-files`. Files communicates with it through a
local named pipe restricted to the installing user and authorized administrators.
The index and filename queries are not uploaded by the FastSearch integration.

Local diagnostic logs may contain paths, filenames, account identifiers or other
personal information. Review and redact logs and screenshots before attaching them
to a public issue. Sharing diagnostics is optional.

Files has network features independent of the index. Sideload builds check GitHub
for updates at startup and periodically, and may display online release notes.
GitHub and any content providers receive normal request metadata such as the public
IP address. Cloud-drive access, downloading updates, and opening websites or remote
locations use the relevant provider's services and privacy policy.

FastSearch release workflows do not inject a Sentry DSN, Bing Maps key or GitHub
OAuth client ID. They do not enable a fork-operated analytics endpoint. A custom
build that supplies credentials for these services must disclose that configuration.

Provider policies: [GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement),
[Microsoft](https://privacy.microsoft.com/privacystatement), and the policy of any
cloud provider the user connects. This fork does not claim that all upstream
features operate offline.
