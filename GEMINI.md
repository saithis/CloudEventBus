
# Tests
## Running all tests
`dotnet run --project tests/Ratatoskr.Tests/Ratatoskr.Tests.csproj`

## Running filtered tests
`dotnet run --project tests/Ratatoskr.Tests/Ratatoskr.Tests.csproj -- --treenode-filter "/<Assembly>/<Namespace>/<Class name>/<Test name>"` (without the angled brackets)

### Filter Operators

TUnit supports several operators for building complex filters:

* Wildcard matching: Use `*` for pattern matching (e.g., `LoginTests*` matches `LoginTests`, `LoginTestsSuite`, etc.)
* Equality: Use `=` for exact match (e.g., `[Category=Unit]`)
* Negation: Use `!=` for excluding values (e.g., `[Category!=Performance]`)
* AND operator: Use `&` to combine conditions (e.g., `[Category=Unit]&[Priority=High]`)
* OR operator: Use `|` to match either condition within a single path segment - requires parentheses (e.g., `/*/*/(Class1)|(Class2)/*`)
