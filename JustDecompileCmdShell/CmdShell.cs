using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using JustDecompile.Tools.MSBuildProjectBuilder;
using JustDecompileCmd;
using Telerik.JustDecompiler.External;
using Telerik.JustDecompiler.Languages.CSharp;
using Telerik.JustDecompiler.Languages;
using Telerik.JustDecompiler.External.Interfaces;
using Mono.Cecil;
using Mono.Cecil.AssemblyResolver;
using JustDecompile.EngineInfrastructure;
using Telerik.JustDecompiler.Languages.VisualBasic;

namespace JustDecompileCmdShell
{
    public class CmdShell
    {
        private static readonly string description = "[--- Copyright (c) 2011-2015 Telerik AD. All rights reserved ---]";
        private static uint count = 0;

        public virtual void Run(GeneratorProjectInfo projectInfo)
        {
            //"lang:scharp", 
            //@"/target C:\Work\Behemoth\Trunk\Source\Binaries\Telerik.Windows.Controls.dll",
            //string[] testArgs = new string[] {"/target" @"C:\Work\Behemoth\Trunk\Source\Binaries\Telerik.Windows.Controls.dll", @"/out", "c:\\dir" };

#if ENGINEONLYBUILD
            if (projectInfo == null)
            {
                projectInfo = new GeneratorProjectInfo(new CommandLineHelpError());
            }
#endif

            if (projectInfo == null)
            {
                CommandLineManager.WriteLineColor(ConsoleColor.Red, "args = null");
                CommandLineManager.ResetColor();

                return;
            }

            try
            {
                // If there is an error, there is no need to create output directory, because there will not be any project generation at all.
                if (projectInfo.Error == null)
                {
                    try
                    {
                        this.CreateOutputDirectory(projectInfo);
                    }
                    catch (Exception ex)
                    {
                        if (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                            ex is PathTooLongException || ex is DirectoryNotFoundException || ex is NotSupportedException)
                        {
                            projectInfo.Error = new CommandLineError(CommandLineManager.InvalidDirectoryPathError + ex.Message);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                if (projectInfo.Error == null)
                {
                    CommandLineManager.WriteLine();

                    CommandLineManager.WriteLineColor(ConsoleColor.White, description);
                    CommandLineManager.WriteLineColor(ConsoleColor.White, new string(Enumerable.Range(0, description.Length).Select(i => '=').ToArray()));
                    Console.WriteLine();
                    Console.WriteLine("Generates MS Visual Studio(r) Project from .NET assembly.");
                    CommandLineManager.WriteLine();

                    if (!CLRHelper.IsValidClrFile(projectInfo.Target))
                    {
                        CommandLineManager.WriteLineColor(ConsoleColor.Red, "The target assembly is not a valid CLR assembly.");
                        return;
                    }

                    AssemblyDefinition assembly = Telerik.JustDecompiler.Decompiler.Utilities.GetAssembly(projectInfo.Target);
                    ProjectGenerationSettings settings = this.GetSettings(projectInfo);
                    if (!settings.VisualStudioSupportedProjectType)
                    {
                        CommandLineManager.WriteLineColor(ConsoleColor.Red, this.ReplaceRegisteredTrademarkSymbol(settings.ErrorMessage));
                        return;
                    }
                    else if (!settings.JustDecompileSupportedProjectType)
                    {
                        CommandLineManager.WriteLineColor(ConsoleColor.Yellow, this.ReplaceRegisteredTrademarkSymbol(settings.ErrorMessage));
                        CommandLineManager.WriteLine();
                    }
                    else if (!projectInfo.IsDefaultFrameworkVersion)
                    {
                        AssemblyInfo assemblyInfo = NoCacheAssemblyInfoService.Instance.GetAssemblyInfo(assembly, new ConsoleFrameworkResolver(projectInfo.FrameworkVersion));
                        if (assemblyInfo.ModulesFrameworkVersions[assembly.MainModule] != projectInfo.FrameworkVersion)
                        {
                            CommandLineManager.WriteLineColor(ConsoleColor.Yellow, "JustDecompile managed to determine the target assembly framework. The fallback target framework version command line option is ignored.");
                            CommandLineManager.WriteLine();
                        }
                    }

                    CommandLineManager.WriteLineColor(ConsoleColor.White, "Project Creation:");
                    CommandLineManager.WriteLineColor(ConsoleColor.White, "============================");

                    TimeSpan projectGenerationTime = RunInternal(assembly, projectInfo, settings);

                    CommandLineManager.WriteLine();
                    CommandLineManager.WriteLineColor(ConsoleColor.White, "============================");
                    CommandLineManager.WriteLineColor(ConsoleColor.White, string.Format("Finished in {0:0.0} seconds.", projectGenerationTime.TotalSeconds));

                    count = 0;
                }
                else
                {
                    projectInfo.Error.PrintError();
                }
            }
            catch (Exception ex)
            {
                CommandLineManager.WriteLine();
                CommandLineManager.WriteLineColor(ConsoleColor.Red, ex.Message);
            }
            finally
            {
                CommandLineManager.WriteLine();
                CommandLineManager.ResetColor();
            }
        }

        protected ProjectGenerationSettings GetSettings(GeneratorProjectInfo projectInfo)
        {
            return ProjectGenerationSettingsProvider.GetProjectGenerationSettings(projectInfo.Target, NoCacheAssemblyInfoService.Instance,
                new ConsoleFrameworkResolver(projectInfo.FrameworkVersion), projectInfo.VisualStudioVersion, projectInfo.Language);
        }

        protected TimeSpan RunInternal(AssemblyDefinition assembly, GeneratorProjectInfo projectInfo, ProjectGenerationSettings settings)
        {
            string projFilePath = Path.Combine(projectInfo.Out, Path.GetFileNameWithoutExtension(projectInfo.Target) + projectInfo.Language.VSProjectFileExtension + (settings.JustDecompileSupportedProjectType ? string.Empty : MSBuildProjectBuilder.ErrorFileExtension));

            DecompilationPreferences preferences = new DecompilationPreferences();
            preferences.WriteFullNames = false;
            preferences.WriteDocumentation = projectInfo.AddDocumentation;
            preferences.RenameInvalidMembers = projectInfo.RenameInvalidMembers;

            IFrameworkResolver frameworkResolver = new ConsoleFrameworkResolver(projectInfo.FrameworkVersion);
            MSBuildProjectBuilder projectBuilder = GetProjectBuilder(assembly, projectInfo, settings, projectInfo.Language, projFilePath, preferences, frameworkResolver);
            ConfigurateProjectBuilder(projectBuilder);

            Stopwatch stopwatch = Stopwatch.StartNew();
            projectBuilder.BuildProject();
            TimeSpan projectGenerationTime = stopwatch.Elapsed;
            return projectGenerationTime;
        }

        protected void CreateOutputDirectory(GeneratorProjectInfo projectInfo)
        {
            Directory.CreateDirectory(projectInfo.Out);
        }

        private MSBuildProjectBuilder GetProjectBuilder(AssemblyDefinition assembly, GeneratorProjectInfo projectInfo, ProjectGenerationSettings settings, ILanguage language, string projFilePath, DecompilationPreferences preferences, IFrameworkResolver frameworkResolver)
        {
            TargetPlatform targetPlatform = assembly.MainModule.AssemblyResolver.GetTargetPlatform(assembly.MainModule.FilePath);
            if (targetPlatform == TargetPlatform.WinRT)
            {
                return new WinRTProjectBuilder(projectInfo.Target, projFilePath, language, preferences, null, NoCacheAssemblyInfoService.Instance, projectInfo.VisualStudioVersion, settings);
            }
            else
            {
                return new MSBuildProjectBuilder(projectInfo.Target, projFilePath, language, frameworkResolver, preferences, null, NoCacheAssemblyInfoService.Instance, projectInfo.VisualStudioVersion, settings);
            }
        }

        protected virtual void ConfigurateProjectBuilder(MSBuildProjectBuilder projectBuilder)
        {
            projectBuilder.ProjectFileCreated += OnProjectFileCreated;
            projectBuilder.ProjectGenerationFailure += OnProjectGenerationFailure;
            projectBuilder.ResourceWritingFailure += OnResourceWritingFailure;
        }

        private void OnResourceWritingFailure(object sender, string resourceName, Exception ex)
        {
            CommandLineManager.WriteLine();
            CommandLineManager.WriteLineColor(ConsoleColor.Red, string.Format("Error extracting resource: {0} - {1}", resourceName, ex.Message));
        }

        private void OnProjectGenerationFailure(object sender, Exception ex)
        {
            CommandLineManager.WriteLine();
            CommandLineManager.WriteLineColor(ConsoleColor.Red, ex.Message);
        }

        static void OnProjectFileCreated(object sender, ProjectFileCreatedEvent e)
        {
            count++;
            CommandLineManager.SetForegroundColor(ConsoleColor.DarkGray);

            string generationErrors = "";
            if (e.HasErrors)
            {
                generationErrors = " ... error generating.";
                Console.ForegroundColor = ConsoleColor.Red;
            }
            CommandLineManager.WriteLine(" " + count + "." + e.Name + generationErrors);
            CommandLineManager.ResetColor();
        }

        public bool TryGetProjectInfo(string[] args, out GeneratorProjectInfo projectInfo)
        {
            projectInfo = CommandLineManager.Parse(args);

            return projectInfo != null;
        }

        private string ReplaceRegisteredTrademarkSymbol(string str)
        {
            return str.Replace("\u00AE", "(r)");
        }
    }
}
