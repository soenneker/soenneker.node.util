[![](https://img.shields.io/nuget/v/soenneker.node.util.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.node.util/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.node.util/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.node.util/actions/workflows/publish-package.yml)
[![](https://img.shields.io/nuget/dt/soenneker.node.util.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.node.util/)
[![](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.node.util/codeql.yml?label=CodeQL&style=for-the-badge)](https://github.com/soenneker/soenneker.node.util/actions/workflows/codeql.yml)

# Soenneker.Node.Util

Provides helpers for locating, verifying, and installing Node.js and for running common npm operations.

## Install

```bash
dotnet add package Soenneker.Node.Util
```

## Quick start

```csharp
using Soenneker.Node.Util.Registrars;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
var result = services.AddNodeUtilAsSingleton();
```

Adds `INodeUtil` as a singleton service.

## What you get

- `INodeUtil` — Provides helpers for locating, verifying, and installing Node.js and for running common npm operations.
- `NodeUtilRegistrar` — A utility library for Node related operations.

## API at a glance

| API | What it does | Result / important behavior |
| --- | --- | --- |
| `INodeUtil.GetNodePath(nodeCommand, cancellationToken)` | Gets the full path to the Node.js executable by executing a small Node script that prints `process.execPath`. | The resolved Node.js executable path. |
| `INodeUtil.TryLocate(minVersion, cancellationToken)` | Attempts to locate Node.js and (optionally) verify it meets a minimum version requirement. | The resolved Node.js executable path if found and compatible; otherwise `null`. |
| `INodeUtil.TryLocateAny(cancellationToken)` | Attempts to locate any Node.js installation. | The resolved Node.js executable path if found; otherwise `null`. |
| `INodeUtil.EnsureInstalled(minVersion, installIfMissing, cancellationToken)` | Ensures Node.js is installed and (optionally) meets a minimum version. | The resolved Node.js executable path. |
| `INodeUtil.TryInstall(version, cancellationToken)` | Attempts to install Node.js. | A task that completes when the try install operation is complete. |
| `INodeUtil.NpmInstall(directory, cleanInstall, omitDevDependencies, ignoreScripts, noAudit, noFund, skipIfUpToDate, cancellationToken)` | Runs `npm install` or `npm ci` in the specified directory. | The captured stdout/stderr output from the npm command. |
| `INodeUtil.InstallPnpm(force, cancellationToken)` | Installs pnpm. | A task whose result is the text returned by install Pnpm. |
| `NodeUtilRegistrar.AddNodeUtilAsSingleton(services)` | Adds `INodeUtil` as a singleton service. | The same service collection, so additional registrations can be chained. |
| `NodeUtilRegistrar.AddNodeUtilAsScoped(services)` | Adds `INodeUtil` as a scoped service. | The same service collection, so additional registrations can be chained. |

## Important behavior

- `INodeUtil.EnsureInstalled(minVersion, installIfMissing, cancellationToken)`: Thrown when `minVersion` cannot be parsed. Thrown when Node.js cannot be found (or installed when enabled).
- `INodeUtil.TryInstall(version, cancellationToken)`: Installation strategy is OS-specific (for example: apt-get on Linux, winget/choco on Windows, brew on macOS). This method may require elevated privileges depending on the environment.
- `INodeUtil.NpmInstall(directory, cleanInstall, omitDevDependencies, ignoreScripts, noAudit, noFund, skipIfUpToDate, cancellationToken)`: Thrown when `directory` is empty or invalid. Thrown when `directory` does not exist.

## Practical notes

- Cancellation stops pending work; it does not undo work that has already completed.
