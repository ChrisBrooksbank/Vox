using Moq;
using Vox.Core.Braille;
using Vox.Core.Input;
using Vox.Core.Tests.TestSupport;
using Xunit;

namespace Vox.Core.Tests.Braille;

public class BrailleInputTests
{
    private static readonly byte A = BrailleDots.FromDots("1");
    private static readonly byte B = BrailleDots.FromDots("12");
    private static readonly byte C = BrailleDots.FromDots("14");
    private static readonly byte The = BrailleDots.FromDots("2346");

    private readonly Mock<IKeyboardOutput> _output = new();
    private readonly List<string> _typed = [];
    private readonly List<OutputKey> _keys = [];
    private readonly FakeBrailleTranslator _translator = new();
    private bool _contracted;

    public BrailleInputTests()
    {
        _output.Setup(o => o.TypeText(It.IsAny<string>())).Callback<string>(_typed.Add);
        _output.Setup(o => o.SendKey(It.IsAny<OutputKey>())).Callback<OutputKey>(_keys.Add);
    }

    private BrailleInput Input() => new(() => _translator, () => _contracted, _output.Object);

    [Fact]
    public void Uncontracted_TypesEachCellAtOnce()
    {
        var input = Input();

        input.Cell(A);
        input.Cell(B);

        Assert.Equal(["a", "b"], _typed);
        Assert.Empty(input.Pending);
    }

    [Fact]
    public void Uncontracted_Space_TypesASpace()
    {
        var input = Input();

        input.Cell(A);
        input.Space();

        Assert.Equal(["a", " "], _typed);
    }

    [Fact]
    public void Contracted_KeepsCellsUntilSpace()
    {
        _contracted = true;
        _translator.Contracted = true;
        var input = Input();

        input.Cell(C);
        input.Cell(A);
        Assert.Empty(_typed);
        Assert.Equal([C, A], input.Pending);

        input.Space();

        Assert.Equal(["ca", " "], _typed);
        Assert.Empty(input.Pending);
    }

    [Fact]
    public void Contracted_WordIsBackTranslatedWhole()
    {
        _contracted = true;
        _translator.Contracted = true;
        var input = Input();

        input.Cell(The);
        input.Enter();

        Assert.Equal(["the"], _typed);
        Assert.Equal([new OutputKey(0x0D)], _keys);
    }

    [Fact]
    public void Enter_WithNothingPending_OnlySendsEnter()
    {
        var input = Input();

        input.Enter();

        Assert.Empty(_typed);
        Assert.Equal([new OutputKey(0x0D)], _keys);
    }

    [Fact]
    public void Backspace_RemovesThePendingCell()
    {
        _contracted = true;
        var input = Input();
        input.Cell(A);
        input.Cell(B);

        input.Backspace();

        Assert.Equal([A], input.Pending);
        Assert.Empty(_keys);
        input.Space();
        Assert.Equal(["a", " "], _typed);
    }

    [Fact]
    public void Backspace_WithNothingPending_SendsBackspace()
    {
        var input = Input();

        input.Backspace();

        Assert.Equal([new OutputKey(0x08)], _keys);
    }

    [Fact]
    public void Flush_TypesThePendingCells()
    {
        _contracted = true;
        var input = Input();
        input.Cell(A);

        input.Flush();
        input.Flush();

        Assert.Equal(["a"], _typed);
        Assert.Empty(input.Pending);
    }

    [Fact]
    public void Discard_ForgetsThePendingCells()
    {
        _contracted = true;
        var input = Input();
        input.Cell(A);
        input.Cell(B);

        input.Discard();
        input.Space();

        Assert.Empty(input.Pending);
        Assert.Equal([" "], _typed);
    }

    [Fact]
    public void TranslatorAndContractionAreReadAtUse()
    {
        var input = Input();
        input.Cell(A);

        _contracted = true;
        input.Cell(B);

        Assert.Equal(["a"], _typed);
        Assert.Equal([B], input.Pending);
    }
}
