
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Telerik.JustDecompiler.Languages;
using Telerik.JustDecompiler.Languages.CSharp;
using Telerik.JustDecompiler.Languages.VisualBasic;
using JustDecompile.Tools.MSBuildProjectBuilder;
using Mono.Cecil.AssemblyResolver;

namespace JustDecompileCmdShell
{
    internal static class CommandLineManager
    {
        internal static readonly string CSharpLanguage = "csharp";
        internal static readonly string VisualBasicLanguage = "visualbasic";

        internal static readonly string RequiredParameterErrorPattern = "The /{0} parameter is required.";
        internal static readonly string InvalidDirectoryPathError = "Invalid output directory path: ";
        internal static readonly string InvalidAssemblyPathError = "Invalid target assembly path.";
        internal static readonly string InvalidVisualStudioVersionError = "Invalid Visual Studio(r) version. Supported versions: 2010 and later.";
        internal static readonly string MultipleFrameworkVersionsError = "You cannot choose more than one fallback .NET Framework version.";
        internal static readonly string InvalidLanguageError = "Invalid language.";
        internal static readonly string SupportedLanguagesMessage = "Supported languages: " + CSharpLanguage + ", " + VisualBasicLanguage + ".";
        internal static readonly string UnsupportedParametersPresentedError = "One or more unsupported parameters found.";
        internal static readonly string UseQuotesHintMessage = "Hint: For paths containing spaces you must use quotes.";

        private static readonly HashSet<char> InvalidDirectoryNameChars;
        private static readonly Dictionary<string, FrameworkVersion> FrameworkVersions;

        static CommandLineManager()
        {
            InvalidDirectoryNameChars = new HashSet<char>(Path.GetInvalidFileNameChars());
            FrameworkVersions = new Dictionary<string, FrameworkVersion>()
            {
                { "/net4.0", FrameworkVersion.v4_0 },
                { "/net4.5", FrameworkVersion.v4_5 },
                { "/net4.5.1", FrameworkVersion.v4_5_1 },
                { "/net4.5.2", FrameworkVersion.v4_5_2 },
                { "/net4.6", FrameworkVersion.v4_6 },
                { "/net4.6.1", FrameworkVersion.v4_6_1 },
                { "/net4.6.2", FrameworkVersion.v4_6_2 },
                { "/net4.7", FrameworkVersion.v4_7 },
				{ "/net4.7.1", FrameworkVersion.v4_7_1 }
			};
        }

        internal static void WriteLineColor(ConsoleColor consoleColor, string description)
        {
            SetForegroundColor(consoleColor);
            WriteLine(description);
        }

        internal static GeneratorProjectInfo Parse(string[] args)
        {
            int numberOfFoundValidArguments = 0;

            bool isOutParamPresented = false;
            string outParam = string.Empty;
            bool isTargetParamPresented = false;
            string targetParam = string.Empty;

            bool isInvalidLanguage = false;
            ILanguage languageParam = null;

            bool? addDocumentation = null;
            bool? renameInvalidMembers = null;
            bool? writeLargeNumbersInHex = null;
            bool? decompileDangerousResources = null;

            bool isInvalidVisualStudioVersion = false;
            VisualStudioVersion visualStudioVersion = VisualStudioVersion.Unknown;

            int numberOfFrameworkVersionArguments = 0;
            FrameworkVersion frameworkVersion = FrameworkVersion.Unknown;

            bool isProjectGenerationRequested = false;

            if (args.Length == 0)
            {
                return null;
            }

            if (TryGetHelpText(args))
            {
                return new GeneratorProjectInfo(new CommandLineHelpError());
            }

            if (TryGetOutParam(args, out outParam))
            {
                isOutParamPresented = true;
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            if (TryGetTargetParam(args, out targetParam))
            {
                isTargetParamPresented = true;
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            if (TryGetLanguageParam(args, out languageParam, ref isInvalidLanguage))
            {
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            if (TryGetNodocArgument(args))
            {
                addDocumentation = false;
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            if (TryGetNoRenameArgument(args))
            {
                renameInvalidMembers = false;
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            if (TryGetNoHexArgument(args))
            {
                writeLargeNumbersInHex = false;
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            if (TryGetDecompileDangerousResourcesArgument(args))
            {
                decompileDangerousResources = true;
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            if (TryGetVisualStudioVersionParam(args, out visualStudioVersion, ref isInvalidVisualStudioVersion))
            {
                isProjectGenerationRequested = true;
                numberOfFoundValidArguments++;
            }

            foreach (KeyValuePair<string, FrameworkVersion> pair in FrameworkVersions)
            {
                if (TryGetFrameworkVersion(args, pair.Key))
                {
                    frameworkVersion = pair.Value;
                    numberOfFrameworkVersionArguments++;
                    isProjectGenerationRequested = true;
                    numberOfFoundValidArguments++;
                }
            }

            if (!isProjectGenerationRequested)
            {
                return null;
            }

            if (!isOutParamPresented || string.IsNullOrWhiteSpace(outParam))
            {
                return new GeneratorProjectInfo(new CommandLineError(string.Format(RequiredParameterErrorPattern, "out")));
            }

            if (!isTargetParamPresented || string.IsNullOrWhiteSpace(targetParam))
            {
                return new GeneratorProjectInfo(new CommandLineError(string.Format(RequiredParameterErrorPattern, "target")));
            }
            else if (!IsValidFilePath(targetParam))
            {
                return new GeneratorProjectInfo(new CommandLineError(InvalidAssemblyPathError));
            }

            if (isInvalidVisualStudioVersion)
            {
                return new GeneratorProjectInfo(new CommandLineError(InvalidVisualStudioVersionError));
            }

            if (numberOfFrameworkVersionArguments > 1)
            {
                return new GeneratorProjectInfo(new CommandLineError(MultipleFrameworkVersionsError));
            }

            if (isInvalidLanguage)
            {
                return new GeneratorProjectInfo(new CommandLineError(string.Format("{0} {1}", InvalidLanguageError, SupportedLanguagesMessage)));
            }

            // The following check is disabled because it is valid with this type of parameters: /target:SomeAssembly.dll.
            // Until now the help message wasn't written very good and it doesn't point out clearly that out and target
            // parameters must be followed by : and then the path to the assembly, and some users may used it with this type
            // of parameters: /target SomeAssembly.dll, which we parse correctly. If we add it now it would be braking change.
            // Consider to enable it after some amount of time, an year or two.
            //if (numberOfFoundValidArguments != args.Length)
            //{
            //    return new GeneratorProjectInfo(new CommandLineError(string.Format("{0} {1}", UnsupportedParametersPresentedError, UseQuotesHintMessage)));
            //}

            GeneratorProjectInfo result = new GeneratorProjectInfo(targetParam, outParam.EndsWith("\\") ? outParam : outParam + "\\");

            if (languageParam != null)
            {
                result.Language = languageParam;
            }

            if (addDocumentation.HasValue)
            {
                result.AddDocumentation = addDocumentation.Value;
            }

            if (renameInvalidMembers.HasValue)
            {
                result.RenameInvalidMembers = renameInvalidMembers.Value;
            }

            if (writeLargeNumbersInHex.HasValue)
            {
                result.WriteLargeNumbersInHex = writeLargeNumbersInHex.Value;
            }

            if (decompileDangerousResources.HasValue)
            {
                result.DecompileDangerousResources = decompileDangerousResources.Value;
            }

            if (visualStudioVersion != VisualStudioVersion.Unknown)
            {
                result.VisualStudioVersion = visualStudioVersion;
            }

            if (frameworkVersion != FrameworkVersion.Unknown)
            {
                result.FrameworkVersion = frameworkVersion;
            }

            return result;
        }

        private static bool IsValidFilePath(string filePath)
        {
            if (File.Exists(filePath))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetVisualStudioVersionParam(string[] args, out VisualStudioVersion visualStudioVersion, ref bool isInvalidVisualStudioVersion)
        {
            string visualStudioVersionAsString;
            bool visualStudioVersionParamPresented = TryGetParam(args, "/vs", out visualStudioVersionAsString);
            if (visualStudioVersionParamPresented)
            {
                switch (visualStudioVersionAsString)
                {
                    case "2010":
                        visualStudioVersion = VisualStudioVersion.VS2010;
                        return true;
                    case "2012":
                        visualStudioVersion = VisualStudioVersion.VS2012;
                        return true;
                    case "2013":
                        visualStudioVersion = VisualStudioVersion.VS2013;
                        return true;
                    case "2015":
                        visualStudioVersion = VisualStudioVersion.VS2015;
                        return true;
                    case "2017":
                        visualStudioVersion = VisualStudioVersion.VS2017;
                        return true;
                    default:
                        isInvalidVisualStudioVersion = true;
                        break;
                }
            }

            visualStudioVersion = VisualStudioVersion.Unknown;
            return false;
        }

        private static bool TryGetHelpText(string[] args)
        {
            List<string> @params = args.ToList();
            if (@params.FirstOrDefault(s => (s == "?"
                                             || s == "/?"
                                             || s.ToLower() == "help"
                                             || s.ToLower() == "/help")) != null)
            {
                return true;
            }
            return false;
        }

        private static bool TryGetNodocArgument(string[] args)
        {
            List<string> @params = args.ToList();
            if (@params.FirstOrDefault(s => (s == "/nodoc")) != null)
            {
                return true;
            }
            return false;
        }

        private static bool TryGetNoRenameArgument(string[] args)
        {
            List<string> @params = args.ToList();
            if (@params.FirstOrDefault(p => (p == "/norename")) != null)
            {
                return true;
            }
            return false;
        }

        private static bool TryGetNoHexArgument(string[] args)
        {
            List<string> @params = args.ToList();
            if (@params.FirstOrDefault(p => (p == "/nohex")) != null)
            {
                return true;
            }
            return false;
        }

        private static bool TryGetDecompileDangerousResourcesArgument(string[] args)
        {
            List<string> @params = args.ToList();
            if (@params.FirstOrDefault(p => (p == "/decompileDangerousResources")) != null)
            {
                return true;
            }
            return false;
        }

        private static bool TryGetFrameworkVersion(string[] args, string targetFramework)
        {
            bool contains = args.Contains(targetFramework);
            return contains;
        }

        private static bool TryGetLanguageParam(string[] args, out ILanguage result, ref bool isInvalidLanguage)
        {
            string languageAsString;
            if (TryGetParam(args, "/lang", out languageAsString))
            {
                if (languageAsString == CommandLineManager.CSharpLanguage)
                {
                    result = LanguageFactory.GetLanguage(CSharpVersion.V7);
                    return true;
                }
                else if (languageAsString == CommandLineManager.VisualBasicLanguage)
                {
                    result = LanguageFactory.GetLanguage(VisualBasicVersion.V10);
                    return true;
                }
                else
                {
                    isInvalidLanguage = true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryGetTargetParam(string[] args, out string result)
        {
            return TryGetParam(args, "/target", out result);
        }

        private static bool TryGetOutParam(string[] args, out string result)
        {
            return TryGetParam(args, "/out", out result);
        }

        private static bool TryGetParam(string[] args, string paramName, out string result)
        {
            List<string> @params = args.ToList();
            result = @params.Where(s => s.ToLower().StartsWith(paramName)).FirstOrDefault();

            if (!string.IsNullOrEmpty(result))
            {
                bool hasSeparator = result.IndexOf(paramName + ":") > -1;
                if (hasSeparator)
                {
                    result = result.Remove(0, paramName.Length + 1).Trim(new char[] { ' ', '"' });
                    return true;
                }
                else
                {
                    int outParamIndex = @params.IndexOf(result);
                    if (outParamIndex < @params.Count - 1)
                    {
                        result = @params[@params.IndexOf(result) + 1].Trim(new char[] { ' ', '"' });
                        if (string.IsNullOrEmpty(result))
                        {
                            return false;
                        }
                        result = result.StartsWith(":") ? result.Remove(0, 1) : result;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static void PrintHelpText()
        {
            WriteLine();
            SetForegroundColor(ConsoleColor.White);
            WriteLine("JustDecompile /target: /out: [/lang:] [/vs:] [/net4.0] [/net4.5] [/net4.5.1]");
            WriteLine("[/net4.5.2] [/net4.6] [/net4.6.1] [/net4.6.2] [/net4.7] [/net4.7.1] [/nodoc] [/norename]");
            WriteLine("[/nohex] [/decompileDangerousResources] [/?]");
            WriteLine();

            WriteLine("[/?]        Display command line help.");

            WriteLine("[/target:]  The target assembly file path.");

            WriteLine("[/out:]     The output directory's parent. The generated project will be output in a sub-folder with the same name as the target assembly, without extension.");

            WriteLine("[/lang:]    [/lang:" + CSharpLanguage + "]. The language of the generated project.");
            Console.CursorLeft = 12;
            WriteLine(SupportedLanguagesMessage);

            WriteLine("[/vs:]      [/vs:2017]. The target Visual Studio(r) project version.");
            Console.CursorLeft = 12;
            WriteLine("Supported Visual Studio(r) versions: 2010 and later.");

            WriteLine("[/net4.0 /net4.5 /net4.5.1 /net4.5.2 /net4.6 /net4.6.1 /net4.6.2 /net4.7 /net4.7.1]");
            Console.CursorLeft = 12;
            WriteLine("Fallback .NET Framework version of the generated project. This");
            Console.CursorLeft = 12;
            WriteLine("option is ignored if JustDecompile manages to determine");
            Console.CursorLeft = 12;
            WriteLine("the target assembly framework version itself.");

            WriteLine("[/nodoc]    Documentation comments will not be added in the resulting project.");

            WriteLine("[/norename] Invalid characters in identifiers will not be replaced with");
            Console.CursorLeft = 12;
            WriteLine("their equivalent unicode character escape sequences.");
            Console.CursorLeft = 12;
            WriteLine("WARNING: Enabling this might result in code that fails to compile.");

            WriteLine("[/nohex]    Disable output of large numbers in HEX format.");
            
            WriteLine("[/decompileDangerousResources] Enable decompilation of dangerous resources,");
            Console.CursorLeft = 12;
            WriteLine("which may contain malicious code. Decompilation of such resources will result");
            Console.CursorLeft = 12;
            WriteLine("in execution of that malicious code. WARNING: Use with trusted assemblies only.");

            WriteLine();

            WriteLine(@"Example: JustDecompile.exe /target:C:\Assembly.dll /out:C:\Folder /lang:csharp");
            Console.CursorLeft = 9;
            WriteLine(@"/vs:2013 /net4.5 /nodoc");

            WriteLine();

            WriteLine(UseQuotesHintMessage);
        }

        internal static void WriteFinalLines()
        {
            WriteLineColor(ConsoleColor.White, "Press Enter to continue...");

            ResetColor();
        }

        internal static void WriteLine()
        {
            Console.WriteLine();
        }

        internal static void SetForegroundColor(ConsoleColor consoleColor)
        {
            Console.ForegroundColor = consoleColor;
        }

        internal static void WriteLine(string message)
        {
            Console.WriteLine(message);
        }

        internal static void ResetColor()
        {
            Console.ResetColor();
        }
    }
}
