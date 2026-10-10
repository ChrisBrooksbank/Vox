# Benchmarks

`benchmarks/Vox.Benchmarks` measures the parts of Vox that decide how quickly it responds, with BenchmarkDotNet:

- `BufferBuildBenchmarks`: building the virtual buffer from a captured page of 1,000, 10,000 and 50,000 elements (synthetic article sections: headings, paragraphs with links, lists, tables). The budget is 500 ms for 10,000 elements; `BufferBuildBudgetTests` enforces it in the unit tests.
- `CursorBenchmarks`: reading a 10,000-element page line by line to the end, a thousand words, finding every heading by runtime id.
- `TextProcessorBenchmarks`: the text rules every utterance passes through (capitals, pronunciation, punctuation at Some and All, numbers) on a sentence full of symbols and on a long paragraph.

## Running

```powershell
dotnet run -c Release --project benchmarks/Vox.Benchmarks -- --filter "*"
dotnet run -c Release --project benchmarks/Vox.Benchmarks -- --filter "*BufferBuild*"
```

Results go to `BenchmarkDotNet.Artifacts/` (not committed). The `Benchmarks` workflow (run it from the Actions tab) runs them all on `windows-latest` and keeps the results as an artifact of that commit, so numbers can be compared between commits and published with a release.
