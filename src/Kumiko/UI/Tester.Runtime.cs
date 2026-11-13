using Kumiko.Runtime;

namespace Kumiko.UI;

public sealed partial class Tester
{
    readonly IProgram? program;

    public Tester(IProgram program, TesterOptions options = default) : this(program.View, options) => this.program = program;

    public void Replay(string path)
    {
        (program ?? throw new InvalidOperationException("Construct Tester with a Kumiko program to replay.")).Replay(path);
        Frame(0);
    }

    public void Step(int step)
    {
        (program ?? throw new InvalidOperationException("Construct Tester with a Kumiko program to step.")).Step(step);
        Frame(0);
    }
}
