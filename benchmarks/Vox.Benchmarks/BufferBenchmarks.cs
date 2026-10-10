using BenchmarkDotNet.Attributes;
using Vox.Core.Audio;
using Vox.Core.Buffer;

namespace Vox.Benchmarks;

/// <summary>Building the virtual buffer from a captured page (the budget is 500 ms for 10,000 elements).</summary>
[MemoryDiagnoser]
public class BufferBuildBenchmarks
{
    private SyntheticElement _page = null!;

    [Params(1_000, 10_000, 50_000)]
    public int Elements { get; set; }

    [GlobalSetup]
    public void Setup() => _page = SyntheticPage.Build(Elements);

    [Benchmark]
    public VBufferDocument Build() => new VBufferBuilder().Build(_page);
}

/// <summary>Moving the browse cursor through a 10,000-element page.</summary>
public class CursorBenchmarks
{
    private VBufferDocument _document = null!;

    private sealed class SilentCues : IAudioCuePlayer
    {
        public bool IsEnabled { get; set; }
        public void Play(string cueName) { }
    }

    [GlobalSetup]
    public void Setup() => _document = new VBufferBuilder().Build(SyntheticPage.Build(10_000));

    [Benchmark]
    public int ReadByLineToTheEnd()
    {
        var cursor = new VBufferCursor(_document, new SilentCues());
        int lines = 0;
        while (cursor.NextLine() is not null)
            lines++;
        return lines;
    }

    [Benchmark]
    public int ReadThousandWords()
    {
        var cursor = new VBufferCursor(_document, new SilentCues());
        int words = 0;
        for (int i = 0; i < 1000 && cursor.NextWord() is not null; i++)
            words++;
        return words;
    }

    [Benchmark]
    public int FindEveryHeading()
    {
        int found = 0;
        foreach (var heading in _document.Headings)
        {
            if (_document.FindByRuntimeId(heading.UIARuntimeId) is not null)
                found++;
        }
        return found;
    }
}
