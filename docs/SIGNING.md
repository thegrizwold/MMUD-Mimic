# Code signing & the SmartScreen flag

## Why the beta gets flagged

Nothing in the code trips a malware detector — the audit found no P/Invoke,
no hooks, no network, no registry, no self-modification; the only "sensitive"
APIs are the WPF clipboard and `Process.Start` on a folder path. The flag is
**Microsoft Defender SmartScreen reputation**, and the beta hit every input
that scores badly:

| Signal SmartScreen weighs | Beta ≤ 31 | Beta 32 |
|---|---|---|
| Authenticode signature | none → "Unknown publisher" | Azure Artifact Signing (public-trust cert, Microsoft HSM, timestamped) |
| Publisher reputation | none — every beta is a brand-new hash nobody has run | inherits the signer's reputation; every build signed with the same cert *shares* it |
| VERSIONINFO resource | empty (no Company/FileVersion/Description) | fully populated in `Mme.App.csproj` |
| Single-file bundle | self-extracting, native lib dropped beside exe | native libs bundled in-process, **compression off** (compressed bundles look like packers); framework-dependent by default (~5 MB, what every beta shipped as), `-SelfContained` for a runtime-free 148 MB exe |
| Download count of that exact file | ~0 per beta | irrelevant once signed |

A self-signed certificate does **nothing** for SmartScreen — reputation is
only tracked for certificates chaining to a Microsoft-trusted CA. The two
routes that work are an OV/EV certificate from a CA (~$200–400/yr, hardware
token for EV) or **Azure Artifact Signing** (formerly *Trusted Signing*):
$9.99/month, 5,000 signatures, key never leaves Microsoft's HSM, and a
signature carries base SmartScreen reputation from day one (equivalent to
EV). We use Artifact Signing.

## One-time Azure setup (owner)

1. **Azure subscription** → create an *Artifact Signing* account
   (portal: "Artifact Signing accounts", pick a region — the region fixes
   the endpoint, e.g. East US → `https://eus.codesigning.azure.net/`,
   West US 2 → `https://wus2.codesigning.azure.net/`). Basic SKU.
2. **Identity validation** → *Individual* (government ID + selfie through
   the portal; typically 1–3 business days) or *Organization* (needs a
   verifiable legal entity, 3+ years of history for public trust). The
   validated name becomes the certificate's Subject — that is the
   "Publisher" Windows shows, so pick the one you want players to see.
3. **Certificate profile** → type *Public Trust*, bound to the identity
   validation. Note the profile name (case-sensitive).
4. **Access** →
   * Your own user: role **Trusted Signing Certificate Profile Signer** on
     the account (for local `build/publish.ps1` runs).
   * CI: an Entra *App registration* (service principal) with a client
     secret and the same role. Put tenant id / client id / secret in the
     GitHub secrets listed at the top of `.github/workflows/release.yml`,
     plus `ACS_ENDPOINT`, `ACS_ACCOUNT`, `ACS_PROFILE`.

## Signing a release

**CI (preferred):** bump `<Version>` in `src/Mme.App/Mme.App.csproj` and
the window `Title` in `MainWindow.xaml`, tag `vX.Y.Z`, push. The `release`
workflow runs the logic suite, publishes the single-file exe on a Windows
runner, signs it with `azure/artifact-signing-action@v2`, verifies the
signature, and attaches `MMUD-Mimic-<version>-win-x64.zip` to a GitHub
Release.

**Local:**

```powershell
az login --use-device-code --scope "https://codesigning.azure.net/.default"
.\build\publish.ps1 -Endpoint https://eus.codesigning.azure.net -Account <account> -Profile <profile>
```

The script publishes with `Properties/PublishProfiles/win-x64-single.pubxml`,
signs with the `sign` .NET tool (`sign code trusted-signing …`, installed on
first use), runs `Get-AuthenticodeSignature`/`signtool verify /pa`, and zips
the exe into `artifacts/`. `-NoSign` gives an `-UNSIGNED` smoke build that
must not be distributed.

## What players will see

First launch of a freshly signed build: SmartScreen shows the **publisher
name** instead of "Unknown publisher" and, because Artifact Signing
certificates carry base reputation, normally no blue "Windows protected your
PC" wall at all. Every later beta signed with the same certificate profile
keeps that reputation even though the file hash changes. Certificates from
Artifact Signing rotate every 3 days — that is by design; the timestamp
counter-signature keeps old builds valid.

## Checklist before shipping a beta

- [ ] `dotnet test tests/Mme.Core.Tests` green
- [ ] `<Version>` / `<FileVersion>` / `<InformationalVersion>` and window
      title bumped together
- [ ] `publish.ps1` output is exactly one file (`MMUD-Mimic.exe`)
- [ ] `Get-AuthenticodeSignature` → `Valid`, signer = your validated name
- [ ] zip from `artifacts/`, not from `bin/`
