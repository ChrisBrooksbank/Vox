using BenchmarkDotNet.Running;

// dotnet run -c Release --project benchmarks/Vox.Benchmarks -- --filter "*"   (docs/benchmarks.md)
BenchmarkSwitcher.FromAssembly(typeof(Vox.Benchmarks.SyntheticPage).Assembly).Run(args);
