using Vox.Core.MathSpeech;

namespace Vox.Core.Tests.TestSupport;

/// <summary>An <see cref="IMathSpeech"/> that records what it is given and answers with fixed text.</summary>
public sealed class FakeMathSpeech : IMathSpeech
{
    public bool IsAvailable { get; set; }
    public List<string> Expressions { get; } = [];
    public List<string> Commands { get; } = [];

    public string? SetExpression(string mathMl)
    {
        Expressions.Add(mathMl);
        return "x squared";
    }

    public string? Navigate(string command)
    {
        Commands.Add(command);
        return command == "ZoomIn" ? "x" : "squared";
    }
}
