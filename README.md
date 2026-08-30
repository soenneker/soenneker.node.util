# Soenneker.Node.Util
[![](https://img.shields.io/nuget/v/soenneker.node.util.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.node.util/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.node.util/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.node.util/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.node.util.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.node.util/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.node.util/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.node.util/actions/workflows/codeql.yml)

Locates Node.js tooling, optionally installs Node.js or pnpm, and runs repeatable npm dependency installs from .NET.

## Installation

```bash
dotnet add package Soenneker.Node.Util
```

## Registration

```csharp
using Soenneker.Node.Util.Registrars;

builder.Services.AddNodeUtilAsSingleton();
// or: builder.Services.AddNodeUtilAsScoped();
```

## Locate Node.js without modifying the machine

```csharp
using Soenneker.Node.Util.Abstract;

string nodePath = await node.EnsureInstalled(
    minVersion: "20.11.1",
    installIfMissing: false,
    cancellationToken);
```

`TryLocate(minVersion)` returns `null` when no installation meets the minimum. `TryLocateAny()` accepts any version. Discovery checks the hosted-tool cache on Windows and then probes common `node` command names. Probe failures return `null`, while caller cancellation is propagated.

`GetNodePath(command)` executes the supplied Node executable and returns `process.execPath`. `GetNpmPath` and `GetNpxPath` search `PATH` and common platform locations, but may return the bare command name when no file is resolved. `GetGlobalToolPath` accepts a single tool file name and confines lookup to npm's global binary directory.

## Install project dependencies

```csharp
string output = await node.NpmInstall(
    directory: repositoryPath,
    cleanInstall: true,
    omitDevDependencies: false,
    ignoreScripts: true,
    noAudit: true,
    noFund: true,
    skipIfUpToDate: true,
    cancellationToken);
```

`cleanInstall: true` runs `npm ci` and therefore requires `package-lock.json` or `npm-shrinkwrap.json`. Otherwise the utility runs `npm install`.

After a successful install, the utility writes `npm-install.lockhash` in the project directory. A later call skips npm only when `node_modules` exists and the marker matches:

- `package.json`;
- the shrinkwrap or lock file, when present;
- the resolved Node.js and npm versions;
- `cleanInstall`, `omitDevDependencies`, and `ignoreScripts`.

Install calls for the same resolved directory are serialized within the process. A missing, stale, or unreadable marker runs npm again. Add the marker to `.gitignore` if it is only a local build artifact.

`ignoreScripts` defaults to `false`, which allows dependency lifecycle scripts to execute with the current account's permissions. Use `true` for untrusted dependency trees unless the build explicitly requires those scripts. `noAudit: true` suppresses npm's audit request; it is not a security scan.

## Machine-wide installation

`EnsureInstalled` defaults to `installIfMissing: true`. Depending on the OS, installation invokes `sudo apt-get`, winget or Chocolatey, or Homebrew. Linux package installation uses the configured apt repository and does not select the requested Node major. Windows and macOS installers select a major where their package manager supports it. `EnsureInstalled` always probes again and fails if the installed result does not satisfy the requested minimum.

`InstallPnpm` runs a global `npm install -g pnpm`. `RunNpmCommand` executes the provided raw npm arguments. Treat both as privileged operations and pass only application-controlled arguments. Package-manager changes are not rolled back when cancellation or a later step fails.
