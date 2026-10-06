using MovingHeadSequencer.Application;
using MovingHeadSequencer.Cli;

namespace MovingHeadSequencer;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = CommandLineOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(CommandLineOptions.HelpText);
                return 0;
            }

            return new SequencerApplication(Console.Out).Run(options);
        }
        catch (CommandLineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLineOptions.HelpText);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
