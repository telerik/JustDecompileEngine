using System;
using System.IO;
using System.Linq;
using JustDecompile.Tools.MSBuildProjectBuilder;
using JustDecompileCmd;
using Telerik.JustDecompiler.External;
using Telerik.JustDecompiler.Languages;
using Telerik.JustDecompiler.External.Interfaces;
using Mono.Cecil;
using Mono.Cecil.AssemblyResolver;
using JustDecompile.EngineInfrastructure;
using JustDecompile.Tools.MSBuildProjectBuilder.NetCore;

namespace JustDecompileCmdShell
{
    public class CmdShell : ExceptionThrownNotifier, IExceptionThrownNotifier
    {
        private static readonly string description = "[--- Copyright (c) 2011-2019 Progress Software Corporation and/or one of its subsidiaries or affiliates. All rights reserved. ---]";
        private static uint count = 0;
        private string outputFolder;
        public event EventHandler<AssemblyDefinition> ProjectGenerationStarted;

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
                        // Setting the output folder to a sub-folder of the out parameter, named after the target assembly, as per issue #37
                        outputFolder = Path.Combine(projectInfo.Out, Path.GetFileNameWithoutExtension(projectInfo.Target));
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
                    CommandLineManager.WriteLineColor(ConsoleColor.White, new string(Enumerable.Repeat('=', description.Length).ToArray()));
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

                    if (!projectInfo.DecompileDangerousResources)
                    {
                        CommandLineManager.WriteLineColor(ConsoleColor.Yellow, "By default JustDecompile's command-line project generataion does not decompile dangerous resources, which may contain malicious code. Decompilation of such resources will result in execution of that malicious code. Check the help [/?] for information how to turn on decompilation of such resources. WARNING: Use with trusted assemblies only.");
                        CommandLineManager.WriteLine();
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

                OnExceptionThrown(ex);
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
                new ConsoleFrameworkResolver(projectInfo.FrameworkVersion), projectInfo.VisualStudioVersion, projectInfo.Language, TargetPlatformResolver.Instance);
        }

        protected TimeSpan RunInternal(AssemblyDefinition assembly, GeneratorProjectInfo projectInfo, ProjectGenerationSettings settings)
        {
            OnProjectGenerationStarted(assembly);

            string projFilePath = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(projectInfo.Target) + projectInfo.Language.VSProjectFileExtension + (settings.JustDecompileSupportedProjectType ? string.Empty : MSBuildProjectBuilder.ErrorFileExtension));

            DecompilationPreferences preferences = new DecompilationPreferences();
            preferences.WriteFullNames = false;
            preferences.WriteDocumentation = projectInfo.AddDocumentation;
            preferences.RenameInvalidMembers = projectInfo.RenameInvalidMembers;
            preferences.WriteLargeNumbersInHex = projectInfo.WriteLargeNumbersInHex;
            preferences.DecompileDangerousResources = projectInfo.DecompileDangerousResources;

            IFrameworkResolver frameworkResolver = new ConsoleFrameworkResolver(projectInfo.FrameworkVersion);
			ITargetPlatformResolver targetPlatformResolver = TargetPlatformResolver.Instance;

			BaseProjectBuilder projectBuilder = GetProjectBuilder(assembly, projectInfo, settings, projectInfo.Language, projFilePath, preferences, frameworkResolver, targetPlatformResolver);
            AttachProjectBuilderEventHandlers(projectBuilder);

            //As per https://github.com/telerik/JustDecompileEngine/pull/2
            DateTime startTime = DateTime.UtcNow;
            projectBuilder.BuildProject();
            TimeSpan projectGenerationTime = DateTime.UtcNow - startTime;

            DetachProjectBuilderEventHandlers(projectBuilder);

            return projectGenerationTime;
        }

        protected void CreateOutputDirectory(GeneratorProjectInfo projectInfo)
        {
            Directory.CreateDirectory(outputFolder);
        }

        private BaseProjectBuilder GetProjectBuilder(AssemblyDefinition assembly, GeneratorProjectInfo projectInfo, ProjectGenerationSettings settings, ILanguage language, string projFilePath, DecompilationPreferences preferences, IFrameworkResolver frameworkResolver, ITargetPlatformResolver targetPlatformResolver)
        {
            TargetPlatform targetPlatform = targetPlatformResolver.GetTargetPlatform(assembly.MainModule.FilePath, assembly.MainModule);
			BaseProjectBuilder projectBuilder = null;

			if (targetPlatform == TargetPlatform.NetCore)
			{
				projectBuilder = new NetCoreProjectBuilder(projectInfo.Target, projFilePath, language, preferences, null, NoCacheAssemblyInfoService.Instance, projectInfo.VisualStudioVersion, settings);
			}
			else if (targetPlatform == TargetPlatform.WinRT)
            {
				projectBuilder = new WinRTProjectBuilder(projectInfo.Target, projFilePath, language, preferences, null, NoCacheAssemblyInfoService.Instance, projectInfo.VisualStudioVersion, settings);
            }
            else
            {
				projectBuilder = new MSBuildProjectBuilder(projectInfo.Target, projFilePath, language, frameworkResolver, preferences, null, NoCacheAssemblyInfoService.Instance, projectInfo.VisualStudioVersion, settings);
            }

			return projectBuilder;
        }

        protected virtual void AttachProjectBuilderEventHandlers(BaseProjectBuilder projectBuilder)
        {
            projectBuilder.ProjectFileCreated += OnProjectFileCreated;
            projectBuilder.ProjectGenerationFailure += OnProjectGenerationFailure;
            projectBuilder.ResourceWritingFailure += OnResourceWritingFailure;
            projectBuilder.ExceptionThrown += OnExceptionThrown;
        }
        
        protected virtual void DetachProjectBuilderEventHandlers(BaseProjectBuilder projectBuilder)
        {
            projectBuilder.ProjectFileCreated -= OnProjectFileCreated;
            projectBuilder.ProjectGenerationFailure -= OnProjectGenerationFailure;
            projectBuilder.ResourceWritingFailure -= OnResourceWritingFailure;
            projectBuilder.ExceptionThrown -= OnExceptionThrown;
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

        static void OnProjectFileCreated(object sender, ProjectFileCreated e)
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

        private void OnProjectGenerationStarted(AssemblyDefinition assembly)
        {
            EventHandler<AssemblyDefinition> handler = this.ProjectGenerationStarted;
            if (handler != null)
            {
                handler(this, assembly);
            }
        }
    }
}
