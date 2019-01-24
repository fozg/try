using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WorkspaceServer;

namespace MLS.Agent
{
    public static class CommandLineParser
    {
        private static readonly TypeBinder _typeBinder = new TypeBinder(typeof(StartupOptions));

        public delegate void StartServer(StartupOptions options, InvocationContext context);
        public delegate Task TryGitHub(string repo, IConsole console);
        public delegate Task Pack(DirectoryInfo packTarget, IConsole console);
        public delegate Task Install(string packageName, DirectoryInfo addSource, IConsole console);
        public delegate Task<int> Verify(DirectoryInfo rootDirectory, IConsole console);

        public static Parser Create(
            StartServer start,
            TryGitHub tryGithub,
            Pack pack,
            Install install,
            Verify verify)
        {
            var startHandler = CommandHandler.Create<InvocationContext>(context =>
            {
                var options = (StartupOptions) _typeBinder.CreateInstance(context);

                start(options, context);
            });

            var rootCommand = StartInTryMode();
            rootCommand.Handler = startHandler;

            var startInHostedMode = StartInHostedMode();
            startInHostedMode.Handler = startHandler;

            rootCommand.AddCommand(startInHostedMode);
            rootCommand.AddCommand(ListPackages());
            rootCommand.AddCommand(GitHub());
            rootCommand.AddCommand(Pack());
            rootCommand.AddCommand(Install());
            rootCommand.AddCommand(Verify());

            return new CommandLineBuilder(rootCommand)
                   .UseDefaults()
                   .Build();

            RootCommand StartInTryMode()
            {
                var command = new RootCommand
                              {
                                  Description = "Try out a .NET project with interactive documentation in your browser",
                                  Argument = new Argument<DirectoryInfo>(() => new DirectoryInfo(Directory.GetCurrentDirectory()))
                                             {
                                                 Name = "rootDirectory",
                                                 Description = "Specify the path to the root directory"
                                             }.ExistingOnly()
                              };

                command.AddOption(new Option(
                                     "--add-source",
                                     "Specify an additional nuget package source",
                                     new Argument<DirectoryInfo>(new DirectoryInfo(Directory.GetCurrentDirectory())).ExistingOnly()));

                command.AddOption(new Option(
                     "--uri",
                     "Specify a URL to a markdown file",
                     new Argument<Uri>()));

                return command;
            }

            Command StartInHostedMode()
            {
                var command = new Command("hosted")
                              {
                                  Description = "Starts the Try .NET agent",
                                  IsHidden = true
                              };

                command.AddOption(new Option(
                                      "--id",
                                      "A unique id for the agent instance (e.g. its development environment id).",
                                      new Argument<string>(defaultValue: () => Environment.MachineName)));
                command.AddOption(new Option(
                                      "--production",
                                      "Specifies whether the agent is being run using production resources",
                                      new Argument<bool>()));
                command.AddOption(new Option(
                                      "--language-service",
                                      "Specifies whether the agent is being run in language service-only mode",
                                      new Argument<bool>()));
                command.AddOption(new Option(
                                      new[] { "-k", "--key" },
                                      "The encryption key",
                                      new Argument<string>()));
                command.AddOption(new Option(
                                      new[] { "--ai-key", "--application-insights-key" },
                                      "Application Insights key.",
                                      new Argument<string>()));
                command.AddOption(new Option(
                                      "--region-id",
                                      "A unique id for the agent region",
                                      new Argument<string>()));
                command.AddOption(new Option(
                                      "--log-to-file",
                                      "Writes a log file",
                                      new Argument<bool>()));

                return command;
            }

            Command ListPackages()
            {
                var run = new Command("list-packages", "Lists the installed Try .NET packages");

                run.Handler = CommandHandler.Create(async (IConsole console) =>
                {
                    var registry = PackageRegistry.CreateForHostedMode();

                    foreach (var package in registry)
                    {
                        console.Out.WriteLine((await package).PackageName);
                    }
                });

                return run;
            }

            Command GitHub()
            {
                var argument = new Argument<string>();

                // System.CommandLine parameter binding does lookup by name,
                // so name the argument after the github command's string param
                argument.Name = tryGithub.Method.GetParameters()
                                         .First(p => p.ParameterType == typeof(string))
                                         .Name;

                var github = new Command("github", "Try a GitHub repo", argument: argument);

                github.Handler = CommandHandler.Create<string, IConsole>((repo, console) => tryGithub(repo, console));

                return github;
            }

            Command Pack()
            {
                var packCommand = new Command("pack", "create a package");
                packCommand.Argument = new Argument<DirectoryInfo>();
                packCommand.Argument.Name = typeof(PackageCommand).GetMethods()
                                            .First(m => m.Name == nameof(PackageCommand.Do)).GetParameters()
                                         .First(p => p.ParameterType == typeof(DirectoryInfo))
                                         .Name;

                packCommand.Handler = CommandHandler.Create<DirectoryInfo, IConsole>(
                    (packTarget, console) => pack(packTarget, console));

                return packCommand;
            }

            Command Install()
            {
                var installCommand = new Command("install", "install a package");
                installCommand.Argument = new Argument<string>();
                installCommand.Argument.Name = typeof(InstallCommand).GetMethods()
                    .First(m => m.Name == nameof(InstallCommand.Do)).GetParameters()
                                         .First(p => p.ParameterType == typeof(string))
                                         .Name;

                var option = new Option("--add-source",
                                        argument: new Argument<DirectoryInfo>().ExistingOnly());

                installCommand.AddOption(option);

                installCommand.Handler = CommandHandler.Create<string, DirectoryInfo, IConsole>((packageName, addSource, console) => install(packageName, addSource, console));
                return installCommand;
            }

            Command Verify()
            {
                var verifyCommand = new Command("verify")
                                    {
                                        Argument = new Argument<DirectoryInfo>(() => new DirectoryInfo(Directory.GetCurrentDirectory()))
                                                   {
                                                       Name = "rootDirectory",
                                                       Description = "Specify the path to the root directory"
                                                   }.ExistingOnly()
                                    };

                verifyCommand.Handler = CommandHandler.Create<DirectoryInfo, IConsole>((rootDirectory, console) => verify(rootDirectory, console));

                return verifyCommand;
            }
        }
    }
}