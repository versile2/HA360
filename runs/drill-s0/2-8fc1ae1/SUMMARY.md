# CI summary: FAILURE

- result: failure
- branch: drill/S0
- sha: 8fc1ae16b0837519a7abad659f123d0059d54f4e
- run: 2
- url: https://github.com/versile2/HA360/actions/runs/36898570465
- why: job dotnet: failure; 1 distinct compiler error(s)

## Jobs

| job | result |
|---|---|
| dotnet | failure |

## Compiler errors (1 distinct)

```text
src/Realm.Domain/DrillBroken.cs(5,32): CS0103 The name 'undefinedSymbol' does not exist in the current context
```

## Tests

No .trx files were found.
