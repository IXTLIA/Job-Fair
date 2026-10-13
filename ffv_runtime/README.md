# Job Fair runtime

Current runtime: **0.9.5**. See [CONTRIBUTING.md](../CONTRIBUTING.md) for native prerequisites and build commands, and [TESTER_GUIDE.md](../TESTER_GUIDE.md) for current behavior and acceptance status.

The build contains 55 C# source files and 51 Harmony targets. It validates the supported native game hashes and approved startup override before installing hooks. Generated compatibility data is committed so a checkout can compile without the private research workspace. Native dependencies come from your own supported FFV installation.
