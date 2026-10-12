namespace Vox.FaultTestApp;

/// <summary>
/// Shows <see cref="FaultTestForm"/> and takes commands, one per line, on standard input:
/// <c>hang</c>, <c>throw</c>, <c>normal</c> / <c>release</c> (switch the faulty provider),
/// <c>text</c> (prints <c>text:&lt;what Notes holds&gt;</c>) and <c>quit</c>. Prints <c>ready</c>
/// once shown and <c>ok:&lt;command&gt;</c> after each command. Closing standard input quits too.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // An injected fault surfacing on the UI thread (WinForms reading its own provider) must
        // not stop the app with a crash dialog
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Console.WriteLine("error:" + e.Exception.Message);
        using var form = new FaultTestForm();
        var commands = new Thread(() => ReadCommands(form)) { IsBackground = true, Name = "Commands" };
        form.HandleCreated += (_, _) => commands.Start();
        Application.Run(form);
        Faults.Release();
    }

    private static void ReadCommands(FaultTestForm form)
    {
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            var command = line.Trim().ToLowerInvariant();
            switch (command)
            {
                case "hang":
                    Faults.Set(FaultMode.Hang);
                    break;
                case "throw":
                    Faults.Set(FaultMode.Throw);
                    break;
                case "normal":
                case "release":
                    Faults.Release();
                    break;
                case "text":
                    var text = (string)form.Invoke(() => form.NotesText);
                    Console.WriteLine("text:" + text);
                    break;
                case "quit":
                    Quit(form);
                    return;
                default:
                    Console.WriteLine("unknown:" + command);
                    continue;
            }
            Console.WriteLine("ok:" + command);
        }
        // The test has gone away
        Quit(form);
    }

    private static void Quit(FaultTestForm form)
    {
        Faults.Release();
        try
        {
            form.BeginInvoke(form.Close);
        }
        catch (InvalidOperationException)
        {
            // Already closed
        }
    }
}
