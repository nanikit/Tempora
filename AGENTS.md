# Windows tests

See `mise tasks` for other commands.

```sh
# Selected native tests with desktop and settings isolation; replace <godot-executable> and the filter
dotnet run --project tools/StartupSmoke/StartupSmoke.csproj -- test <godot-executable> --native -- --filter FullyQualifiedName~TimingShortcutTests
```
