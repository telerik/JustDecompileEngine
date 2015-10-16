using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using System.Xml.Serialization;

#if !NET35
using System.Threading.Tasks;
#endif

using JustDecompile.Tools.MSBuildProjectBuilder.ProjectItemFileWriters;
using Mono.Cecil;
using Mono.Cecil.AssemblyResolver;
using Mono.Cecil.Extensions;
#if !ENGINEONLYBUILD
using Telerik.Baml;
#endif
using Telerik.JustDecompiler.Common;
using Telerik.JustDecompiler.Common.NamespaceHierarchy;
using Telerik.JustDecompiler;
using Telerik.JustDecompiler.External;
using Telerik.JustDecompiler.External.Interfaces;
using Telerik.JustDecompiler.Languages;
using Telerik.JustDecompiler.Decompiler.WriterContextServices;
using Telerik.JustDecompiler.Decompiler.Caching;
using JustDecompile.Tools.MSBuildProjectBuilder.FilePathsServices;
using JustDecompile.SmartAssembly.Attributes;
using System.Collections;
using JustDecompile.EngineInfrastructure;
using Telerik.JustDecompiler.Languages.CSharp;
using Telerik.JustDecompiler.Languages.VisualBasic;

namespace JustDecompile.Tools.MSBuildProjectBuilder
{
	[DoNotPrune]
    [DoNotObfuscateType]
    public class MSBuildProjectBuilder
	{
        public const string ErrorFileExtension = ".error";

		protected IFileGenerationNotifier fileGeneratedNotifier;
		protected readonly ILanguage language;
        protected readonly AssemblyDefinition assembly;
        protected readonly TargetPlatform platform;
        protected readonly string assemblyPath;
		protected readonly string targetDir;
        private readonly Dictionary<string, ICollection<string>> xamlGeneratedFields = new Dictionary<string, ICollection<string>>();
        protected FileGenerationContext fileGenContext;
        protected readonly IAssemblyResolver currentAssemblyResolver;
        private Dictionary<ModuleDefinition, Mono.Collections.Generic.Collection<TypeDefinition>> userDefinedTypes;
		private Dictionary<ModuleDefinition, Mono.Collections.Generic.Collection<Resource>> resources;
        private readonly IFrameworkResolver frameworkResolver;
        protected IFilePathsService filePathsService;
		protected readonly Dictionary<ModuleDefinition, string> modulesToProjectsFilePathsMap;
        protected readonly Dictionary<Resource, string> resourcesToPathsMap;
        protected readonly Dictionary<string, string> xamlResourcesToPathsMap;
        protected IExceptionFormatter exceptionFormater = SimpleExceptionFormatter.Instance;
		private readonly NamespaceHierarchyTree namespaceHierarchyTree;
		protected readonly IDecompilationPreferences decompilationPreferences;
		protected Dictionary<ModuleDefinition, Guid> modulesProjectsGuids;
        protected VisualStudioVersion visualStudioVersion;
        private IAssemblyInfoService assemblyInfoService;
        private AssemblyInfo assemblyInfo;
        private ProjectGenerationSettings projectGenerationSettings;
		private IProjectGenerationNotifier projectNotifier;

        public delegate void TypeWritingFailureEventHandler(object sender, string typeName, Exception ex);
        public delegate void ResourceWritingFailureEventHandler(object sender, string resourceName, Exception ex);

        public event TypeWritingFailureEventHandler TypeWritingFailure;
        public event ResourceWritingFailureEventHandler ResourceWritingFailure;

        public delegate void ProjectGenerationFailureEventHandler(object sender, Exception ex);
        public event ProjectGenerationFailureEventHandler ProjectGenerationFailure;

        public event EventHandler ProjectGenerationFinished;

        public const int MaxPathLength = 259; // 259 + NULL == 260

        public MSBuildProjectBuilder(string assemblyPath, AssemblyDefinition assembly,
            Dictionary<ModuleDefinition, Mono.Collections.Generic.Collection<TypeDefinition>> userDefinedTypes,
 			Dictionary<ModuleDefinition, Mono.Collections.Generic.Collection<Resource>> resources,
			string targetPath, ILanguage language, IFrameworkResolver frameworkResolver,
            IDecompilationPreferences preferences, IAssemblyInfoService assemblyInfoService,
			VisualStudioVersion visualStudioVersion = VisualStudioVersion.VS2010, ProjectGenerationSettings projectGenerationSettings = null,
			IProjectGenerationNotifier projectNotifier = null)
        {
            this.assemblyPath = assemblyPath;
            this.assembly = assembly;
            this.userDefinedTypes = userDefinedTypes;
			this.resources = resources;
            this.TargetPath = targetPath;
			this.targetDir = Path.GetDirectoryName(targetPath);
            this.language = language;
            this.frameworkResolver = frameworkResolver;
            this.assemblyInfoService = assemblyInfoService;
            this.visualStudioVersion = visualStudioVersion;
            this.projectGenerationSettings = projectGenerationSettings;

            this.currentAssemblyResolver = assembly.MainModule.AssemblyResolver;
			this.decompilationPreferences = preferences;

            platform = currentAssemblyResolver.GetTargetPlatform(assembly.MainModule.FilePath);
            namespaceHierarchyTree = assembly.BuildNamespaceHierarchyTree();

            filePathsService =
                new DefaultFilePathsService(
					this.assembly,
                    this.assemblyPath,
					Path.GetFileName(this.TargetPath),
                    this.UserDefinedTypes,
                    this.Resources,
                    namespaceHierarchyTree,
                    this.language,
                    Utilities.GetMaxRelativePathLength(targetPath));

			this.modulesToProjectsFilePathsMap = this.filePathsService.GetModulesToProjectsFilePathsMap();
			this.resourcesToPathsMap = this.filePathsService.GetResourcesToFilePathsMap();
			this.xamlResourcesToPathsMap = this.filePathsService.GetXamlResourcesToFilePathsMap();

			this.assemblyInfo = GetAssemblyInfo();
			this.projectNotifier = projectNotifier;
        }

        public MSBuildProjectBuilder(string assemblyPath, string targetPath, ILanguage language,
            IFrameworkResolver frameworkResolver,IDecompilationPreferences preferences, IFileGenerationNotifier notifier,
            IAssemblyInfoService assemblyInfoService, VisualStudioVersion visualStudioVersion = VisualStudioVersion.VS2010,
			ProjectGenerationSettings projectGenerationSettings = null, IProjectGenerationNotifier projectNotifier = null)
        {
            this.assemblyPath = assemblyPath;
            this.TargetPath = targetPath;
			this.targetDir = Path.GetDirectoryName(targetPath);
            this.language = language;

            this.frameworkResolver = frameworkResolver;
            this.decompilationPreferences = preferences;
            this.assemblyInfoService = assemblyInfoService;
            this.visualStudioVersion = visualStudioVersion;
            this.projectGenerationSettings = projectGenerationSettings;

            this.currentAssemblyResolver = new WeakAssemblyResolver(GlobalAssemblyResolver.CurrentAssemblyPathCache);

            var readerParameters = new ReaderParameters(currentAssemblyResolver);
            assembly = currentAssemblyResolver.LoadAssemblyDefinition(assemblyPath, readerParameters, loadPdb: true);

            platform = currentAssemblyResolver.GetTargetPlatform(assemblyPath);
            namespaceHierarchyTree = assembly.BuildNamespaceHierarchyTree();

            filePathsService =
                new DefaultFilePathsService(
					this.assembly,
                    this.assemblyPath,
					Path.GetFileName(this.TargetPath),
                    this.UserDefinedTypes,
                    this.Resources,
                    namespaceHierarchyTree,
                    this.language,
                    Utilities.GetMaxRelativePathLength(targetPath));

			this.modulesToProjectsFilePathsMap = this.filePathsService.GetModulesToProjectsFilePathsMap();
			this.resourcesToPathsMap = this.filePathsService.GetResourcesToFilePathsMap();
			this.xamlResourcesToPathsMap = this.filePathsService.GetXamlResourcesToFilePathsMap();
            this.fileGeneratedNotifier = notifier;

			this.assemblyInfo = GetAssemblyInfo();
			this.projectNotifier = projectNotifier;
        }

        private Project CreateProject()
        {
            Project project = new Project();
            if (this.visualStudioVersion == VisualStudioVersion.VS2010 || this.visualStudioVersion == VisualStudioVersion.VS2012)
            {
                project.ToolsVersion = 4.0M;
            }
            else if (this.visualStudioVersion == VisualStudioVersion.VS2013)
            {
                project.ToolsVersion = 12.0M;
            }
            else
            {
                throw new NotImplementedException();
            }

            project.DefaultTargets = "Build";

            return project;
        }

        public string TargetPath { get; private set; }

        public int ResourcesCount
        {
            get
            {
                return Utilities.GetResourcesCount(Resources);
            }
        }

        public Dictionary<ModuleDefinition, Mono.Collections.Generic.Collection<TypeDefinition>> UserDefinedTypes
        {
            get
            {
                return userDefinedTypes ?? (userDefinedTypes = Utilities.GetUserDefinedTypes(assembly));
            }
        }

		public Dictionary<ModuleDefinition, Mono.Collections.Generic.Collection<Resource>> Resources
		{
			get
			{
				return resources ?? (resources = Utilities.GetResources(assembly));
			}
		}

		public virtual uint NumberOfFilesToGenerate 
		{
			get 
			{
                if (this.projectGenerationSettings != null && !this.projectGenerationSettings.JustDecompileSupportedProjectType)
                {
                    // 1 for AssemblyInfo.cs
                    // + 1 proj file for the main module containing the error
                    // + 1 for solution file
                    return (uint)(this.NumberOfTypesInAssembly + this.ResourcesCount + 1 + 1 + 1);
                }
                else
                {
                    // 1 for AssemblyInfo.cs
                    // + proj file for every module
                    // + 1 for solution file
                    return (uint)(this.NumberOfTypesInAssembly + this.ResourcesCount + 1 + this.assembly.Modules.Count + 1);
                }
			}
		}

        public int NumberOfTypesInAssembly
        {
			get
			{
				int totalTypesCount = 0;

				foreach (Mono.Collections.Generic.Collection<TypeDefinition> moduleTypes in this.UserDefinedTypes.Values)
				{
					totalTypesCount += moduleTypes.Count;
				}

				return totalTypesCount;
			}
        }

        public event EventHandler<ProjectFileCreated> ProjectFileCreated;

		private ICollection<TypeReference> GetExpandedTypeDependanceList(ModuleDefinition module)
		{
			Mono.Collections.Generic.Collection<TypeDefinition> moduleTypes;
			if (!UserDefinedTypes.TryGetValue(module, out moduleTypes))
			{
				throw new Exception("Module types not found.");
			}

			HashSet<TypeReference> firstLevelDependanceTypes = new HashSet<TypeReference>();
			foreach (TypeReference type in moduleTypes)
			{
				if (!firstLevelDependanceTypes.Contains(type))
				{
					firstLevelDependanceTypes.Add(type);
				}
			}

			foreach (TypeReference type in module.GetTypeReferences())
			{
				if (!firstLevelDependanceTypes.Contains(type))
				{
					firstLevelDependanceTypes.Add(type);
				}
			}

			return Telerik.JustDecompiler.Decompiler.Utilities.GetExpandedTypeDependanceList(firstLevelDependanceTypes);
		}

		private ICollection<AssemblyNameReference> GetAssembliesDependingOn(ModuleDefinition module)
		{
			ICollection<AssemblyNameReference> result;

			ICollection<TypeReference> expadendTypeDependanceList = GetExpandedTypeDependanceList(module);
			result = Telerik.JustDecompiler.Decompiler.Utilities.GetAssembliesDependingOn(module, expadendTypeDependanceList);

			foreach (AssemblyNameReference assemblyReference in module.AssemblyReferences)
			{
				if (!result.Contains(assemblyReference))
				{
					result.Add(assemblyReference);
				}
			}

			return result;
		}

		private ICollection<ModuleReference> GetModulesDependingOn(ModuleDefinition module)
		{
			ICollection<ModuleReference> result;

			ICollection<TypeReference> expadendTypeDependanceList = GetExpandedTypeDependanceList(module);
			result = Telerik.JustDecompiler.Decompiler.Utilities.GetModulesDependingOn(expadendTypeDependanceList);

			return result;
		}
		
#if !NET35
        public void BuildProjectCancellable(CancellationToken cancellationToken)
        {
            BuildProjectInternal(() => { return cancellationToken.IsCancellationRequested; });
        }
#endif

        public void BuildProject()
        {
            BuildProjectInternal(() => { return false; });
        }

		public void BuildProjectComCancellable(Func<bool> shouldCancel)
		{
			BuildProjectInternal(shouldCancel);
		}

        private static object writeTypesLock = new object();

		private void WriteUserDefinedTypes(ModuleDefinition module, Func<bool> shouldCancel)
		{
			Mono.Collections.Generic.Collection<TypeDefinition> moduleTypes;
			if (!UserDefinedTypes.TryGetValue(module, out moduleTypes))
			{
				throw new Exception("Module types not found.");
			}

			ProjectItemWriterFactory writerFactory = new ProjectItemWriterFactory(assembly, moduleTypes, fileGenContext, filePathsService, language.VSCodeFileExtension);

#if !NET35
            CancellationTokenSource cts = new CancellationTokenSource();

            ParallelOptions parallelOptions = new ParallelOptions() { CancellationToken = cts.Token, MaxDegreeOfParallelism = Environment.ProcessorCount };

            try
            {
                Parallel.ForEach(
					moduleTypes,
                    parallelOptions,
                    (TypeDefinition type) =>
                    {
                        try
                        {
                            if (shouldCancel())
                            {
                                cts.Cancel();
                                return;
                            }

                            IProjectItemFileWriter itemWriter = writerFactory.GetProjectItemWriter(type);

							bool shouldBeXamlPartial = Utilities.ShouldBePartial(type) || fileGenContext.XamlFullNameToRelativePathMap.ContainsKey(type.FullName);

                            if (shouldBeXamlPartial)
                            {
                                string typeName = type.FullName;
                                if (xamlGeneratedFields.ContainsKey(typeName))
                                {
                                    xamlGeneratedFields[typeName].Add("_contentLoaded");//Always present, not bound to any XAML element
                                }
                                else
                                {
                                    xamlGeneratedFields.Add(typeName, new HashSet<string>(new string[1] { "_contentLoaded" }));//Always present, not bound to any XAML element
                                }
                            }

							List<WritingInfo> writingInfos;
							string theCodeString;
                            bool exceptionsWhileDecompiling = WriteTypeToFile(type, itemWriter, xamlGeneratedFields, shouldBeXamlPartial, 
																			language, out writingInfos, out theCodeString);
							bool exceptionsWhileWriting = HasExceptionsWhileWriting(writingInfos);
                            lock (writeTypesLock)
                            {
                                itemWriter.GenerateProjectItems();

								Dictionary<MemberIdentifier, CodeSpan> mapping = null;
								if (this.fileGeneratedNotifier != null)
								{
									/// Create the mapping only when it's needed for the internal API.
									mapping = GenerateMemberMapping(this.assemblyPath, theCodeString, writingInfos);
								}

								IUniqueMemberIdentifier uniqueMemberIdentifier = new UniqueMemberIdentifier(module.FilePath, type.MetadataToken.ToInt32());
								IFileGeneratedInfo args = new TypeGeneratedInfo(itemWriter.FullSourceFilePath, exceptionsWhileDecompiling, exceptionsWhileWriting, uniqueMemberIdentifier, mapping);
                                this.OnProjectFileCreated(args);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (TypeWritingFailure != null)
                            {
                                TypeWritingFailure(this, type.FullName, ex);
                            }

							if (this.projectNotifier != null)
							{
								this.projectNotifier.OnTypeWritingFailure(type.FullName, ex);
							}
                        }
                    });
            }
            catch (OperationCanceledException)
            {
            }
#else
			for (int typeIndex = 0; typeIndex < moduleTypes.Count; typeIndex++)
			{
				if (shouldCancel())
				{
					return;
				}
				TypeDefinition type = moduleTypes[typeIndex];
				try
				{
					IProjectItemFileWriter itemWriter = writerFactory.GetProjectItemWriter(type);
					bool shouldBeXamlPartial = Utilities.ShouldBePartial(type) || fileGenContext.XamlFullNameToRelativePathMap.ContainsKey(type.FullName);
					if (shouldBeXamlPartial)
					{
						string typeName = type.FullName;
						if (xamlGeneratedFields.ContainsKey(typeName))
						{
							xamlGeneratedFields[typeName].Add("_contentLoaded");//Always present, not bound to any XAML element
						}
						else
						{
							xamlGeneratedFields.Add(typeName, new HashSet<string>(new string[1] { "_contentLoaded" }));//Always present, not bound to any XAML element
						}
					}
					List<WritingInfo> writingInfos;
					string typeCode;
					bool exceptionsWhileDecompiling = WriteTypeToFile(type, itemWriter, xamlGeneratedFields, shouldBeXamlPartial, 
																		language, out writingInfos, out typeCode);
					bool exceptionsWhileWriting = HasExceptionsWhileWriting(writingInfos);
					itemWriter.GenerateProjectItems();
					Dictionary<MemberIdentifier, CodeSpan> mapping = GenerateMemberMapping(this.assemblyPath, typeCode, writingInfos);

					IUniqueMemberIdentifier uniqueMemberIdentifier = new UniqueMemberIdentifier(module.FilePath, type.MetadataToken.ToInt32());
					IFileGeneratedInfo args = new TypeGeneratedInfo(itemWriter.FullSourceFilePath, exceptionsWhileDecompiling, exceptionsWhileWriting, uniqueMemberIdentifier, mapping);
                    this.OnProjectFileCreated(args);
				}
				catch (Exception ex)
				{
					if (TypeWritingFailure != null)
					{
						TypeWritingFailure(this, type.FullName, ex);
					}
				}
			 }
#endif
		}
  
		private bool HasExceptionsWhileWriting(List<WritingInfo> writingInfos)
		{
			foreach (WritingInfo info in writingInfos)
			{
				if (info.ExceptionsWhileWriting.Count > 0)
				{
					return true;
				}
			}
			return false;
		}
  
		private Dictionary<MemberIdentifier, CodeSpan> GenerateMemberMapping(string assemblyFilePath, string theCodeString, List<WritingInfo> writingInfos)
		{
			StringBuilder sb = new StringBuilder();
			StringWriter strWriter = new StringWriter(sb);
			strWriter.Write(theCodeString);
			return ExternallyVisibleDecompilationUtilities.GenerateMemberMapping(assemblyFilePath, strWriter, writingInfos);
		}

        private bool BuildProjectInternal(Func<bool> shouldCancel)
        {

			if (this.fileGeneratedNotifier != null)
			{
				this.fileGeneratedNotifier.TotalFileCount = this.NumberOfFilesToGenerate;
			}

			this.modulesProjectsGuids = new Dictionary<ModuleDefinition, Guid>();

            try
            {
				foreach (ModuleDefinition module in assembly.Modules)
				{
					ModuleDefinition currentModule = module;

					fileGenContext = new FileGenerationContext(this.targetDir, namespaceHierarchyTree);

					CreateResources(module);
#if !NET35
					Task.Factory.StartNew(
						() => WriteUserDefinedTypes(currentModule, shouldCancel))
						.Wait();
#else
				    WriteUserDefinedTypes(currentModule, shouldCancel);
#endif

					if (shouldCancel())
					{
						return true;
					}

					bool isMainModule = Utilities.IsMainModule(module);

					if (isMainModule)
					{
						bool createdFile;
						string assemblyInfoRelativePath = WriteAssemblyInfo(assembly, out createdFile);
						if (createdFile)
						{
							ProjectItemGroupCompile assemblyInfoFileEntry = new ProjectItemGroupCompile();
							assemblyInfoFileEntry.Include = assemblyInfoRelativePath;
							fileGenContext.AssemblyInfoFileEntry = assemblyInfoFileEntry;
							IFileGeneratedInfo assemblyInfoArgs = new FileGeneratedInfo(Path.Combine(this.targetDir, assemblyInfoRelativePath), false);
							this.OnProjectFileCreated(assemblyInfoArgs);
						}
					}

                    CopyAppConfig(module);

                    if (this.projectGenerationSettings != null && !this.projectGenerationSettings.JustDecompileSupportedProjectType && module.IsMain)
                    {
                        StreamWriter writer;
                        if (this.TargetPath.EndsWith(language.VSProjectFileExtension + ErrorFileExtension))
                        {
                            writer = new StreamWriter(this.TargetPath);
                        }
                        else
                        {
                            writer = new StreamWriter(this.TargetPath + language.VSProjectFileExtension + ErrorFileExtension);
                        }

                        using (writer)
                        {
                            writer.Write("JustDecompile: " + this.projectGenerationSettings.ErrorMessage);
                        }

                        InformProjectFileCreated(module, language.VSProjectFileExtension + ErrorFileExtension, false);
                    }
                    else
                    {
                        bool exceptionsWhenCreatingProjectFile = false;
						bool projectFileCreated = false;
						try
                        {
                            if (isMainModule)
                            {
                                projectFileCreated = WriteMainModuleProjectFile(module);
                            }
                            else
                            {
								projectFileCreated = WriteNetModuleProjectFile(module);
                            }
                        }
                        catch (Exception)
                        {
                            exceptionsWhenCreatingProjectFile = true;
                        }

						if (projectFileCreated)
						{
							InformProjectFileCreated(module, this.language.VSProjectFileExtension, exceptionsWhenCreatingProjectFile);
						}
                    }
				}

                if (this.projectGenerationSettings == null || this.projectGenerationSettings.JustDecompileSupportedProjectType)
                {
                    // Write the solution file
                    bool exceptionWhileWritingSolutionFile = false;
					bool solutionFileCreated = false;
                    try
                    {
						solutionFileCreated = WriteSolutionFile();
                    }
                    catch (Exception)
                    {
                        exceptionWhileWritingSolutionFile = true;
                    }

					if (solutionFileCreated)
					{
						string solutionFilePath = Path.Combine(this.targetDir, this.filePathsService.GetSolutionRelativePath());
						IFileGeneratedInfo solutionArgs = new FileGeneratedInfo(solutionFilePath, exceptionWhileWritingSolutionFile);
						this.OnProjectFileCreated(solutionArgs);
					}
                }
            }
            catch (Exception ex)
            {
                if (ProjectGenerationFailure != null)
                {
                    ProjectGenerationFailure(this, ex);
                }

				if (this.projectNotifier != null)
				{
					this.projectNotifier.OnProjectGenerationFailure(ex);
				}
            }
            finally
            {
                OnProjectGenerationFinished();
            }
            if (decompilationPreferences.WriteDocumentation)
            {
                /// Clear the cached documentation
                Telerik.JustDecompiler.XmlDocumentationReaders.DocumentationManager.ClearCache();
            }

			ClearCaches();

            return false;
        }

        private void InformProjectFileCreated(ModuleDefinition module, string extension, bool hasErrors)
        {
            string projFileName;

            if (!modulesToProjectsFilePathsMap.TryGetValue(module, out projFileName))
            {
                throw new Exception("Module project file path not found in modules projects filepaths map.");
            }

            if (!projFileName.EndsWith(extension))
            {
                projFileName += extension;
            }

            string fullFilePath = Path.Combine(this.targetDir, projFileName);
            IFileGeneratedInfo csprojArgs = new FileGeneratedInfo(fullFilePath, hasErrors);
            this.OnProjectFileCreated(csprojArgs);
        }

        private void CopyAppConfig(ModuleDefinition module)
        {
            string originalAppConfigFilePath = module.FilePath + ".config";
            string targetAppConfigFilePath = Path.Combine(this.targetDir, "App.config");
            if (File.Exists(originalAppConfigFilePath))
            {
                bool hasErrors = false;
                if (targetAppConfigFilePath.Length <= MaxPathLength && this.assembly.Modules.Count == 1)
                {
                    try
                    {
                        File.Copy(originalAppConfigFilePath, targetAppConfigFilePath, true);
                    }
                    catch (Exception)
                    {
                        hasErrors = true;
                    }

                    if (!hasErrors)
                    {
                        ProjectItemGroupNone appConfigFileEntry = new ProjectItemGroupNone() { Include = "App.config" };
                        this.fileGenContext.AppConfigFileEntry = appConfigFileEntry;
                    }
                }
                else
                {
                    hasErrors = true;
                }

                IFileGeneratedInfo appConfigArgs = new FileGeneratedInfo(targetAppConfigFilePath, hasErrors);
                this.OnProjectFileCreated(appConfigArgs);
            }
        }

		protected virtual void ClearCaches()
		{
			ProjectGenerationDecompilationCacheService.ClearAssemblyContextsCache();
			this.currentAssemblyResolver.ClearCache();
			this.currentAssemblyResolver.ClearAssemblyFailedResolverCache();
		}

		protected virtual bool WriteSolutionFile()
		{
			SolutionWriter solutionWriter =
				new SolutionWriter(this.assembly, this.platform, this.targetDir, this.filePathsService.GetSolutionRelativePath(), this.modulesToProjectsFilePathsMap, this.modulesProjectsGuids, this.visualStudioVersion, this.language);
			solutionWriter.WriteSolutionFile();

			return true;
		}

		protected virtual IFormatter GetFormatter(StringWriter writer)
		{
			return new PlainTextFormatter(writer);
		}

        public bool WriteTypeToFile(TypeDefinition type, IProjectItemFileWriter itemWriter, Dictionary<string, ICollection<string>> membersToSkip, bool shouldBePartial,
            ILanguage language, out List<WritingInfo> writingInfos, out string theCodeString)
        {
			theCodeString = string.Empty;
			writingInfos = null;
            StringWriter theWriter = new StringWriter();

			IFormatter formatter = GetFormatter(theWriter);
            ILanguageWriter writer = language.GetWriter(formatter, this.exceptionFormater, true);

            IWriterContextService writerContextService = this.GetWriterContextService();

            bool exceptionOccurred = false;

            try
            {
				bool showCompilerGeneratedMembers = Utilities.IsVbInternalTypeWithoutRootNamespace(type) ||
													Utilities.IsVbInternalTypeWithRootNamespace(type);

                if (!(writer is INamespaceLanguageWriter))
                {
					writingInfos = writer.Write(type, writerContextService, decompilationPreferences.WriteDocumentation, showCompilerGeneratedMembers);
                }
                else
                {

                    if (shouldBePartial)
                    {
						writingInfos = (writer as INamespaceLanguageWriter).WritePartialTypeAndNamespaces(type, writerContextService, showCompilerGeneratedMembers,
							decompilationPreferences.WriteFullNames, decompilationPreferences.WriteDocumentation, membersToSkip);
                    }
                    else
                    {
						writingInfos = (writer as INamespaceLanguageWriter).WriteTypeAndNamespaces(type, writerContextService, decompilationPreferences.WriteDocumentation, showCompilerGeneratedMembers, decompilationPreferences.WriteFullNames);
                    }
                }

                RecordGeneratedFileData(type, itemWriter.FullSourceFilePath, theWriter, formatter, writerContextService, writingInfos);

                MemoryStream sourceFileStream = new MemoryStream(Encoding.UTF8.GetBytes(theWriter.ToString()));
                itemWriter.CreateProjectSourceFile(sourceFileStream);
                sourceFileStream.Close();
                theWriter.Close();
            }
            catch (Exception e)
            {
                exceptionOccurred = true;

                string[] exceptionMessageLines = exceptionFormater.Format(e, type.FullName, itemWriter.FullSourceFilePath);
                string exceptionMessage = string.Join(Environment.NewLine, exceptionMessageLines);
                string commentedExceptionMessage = language.CommentLines(exceptionMessage);
                itemWriter.CreateProjectSourceFile(new MemoryStream(Encoding.UTF8.GetBytes(commentedExceptionMessage)));
            }

			theCodeString = theWriter.ToString();
            return exceptionOccurred || writerContextService.ExceptionsWhileDecompiling.Any();
        }

        protected virtual void RecordGeneratedFileData(TypeDefinition type, string sourceFilePath, StringWriter theWriter, IFormatter formatter, IWriterContextService writerContextService, List<WritingInfo> writingInfos)
        {

        }

        private string WriteAssemblyInfo(AssemblyDefinition assembly, out bool createdNewFile)
        {
            string fileContent;
            IAssemblyAttributeWriter writer = null;
            using (StringWriter stringWriter = new StringWriter())
            {
                writer = language.GetAssemblyAttributeWriter(new PlainTextFormatter(stringWriter), this.exceptionFormater, true);
                IWriterContextService writerContextService = this.GetWriterContextService();

                // "Duplicate 'TargetFramework' attribute" when having it written in AssemblyInfo
                writer.WriteAssemblyInfo(assembly, writerContextService, true,
                    new string[1] { "System.Runtime.Versioning.TargetFrameworkAttribute" }, new string[1] { "System.Security.UnverifiableCodeAttribute" });

                fileContent = stringWriter.ToString();
            }

            string parentDirectory = Path.GetDirectoryName(this.TargetPath);
            string relativePath = filePathsService.GetAssemblyInfoRelativePath();
            string fullPath = Path.Combine(parentDirectory, relativePath);

            string dirPath = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            if (File.Exists(fullPath) && assembly.MainModule.Types.FirstOrDefault(x => x.FullName == "Properties.AssemblyInfo") != null)
            {
                createdNewFile = false;
                PushAssemblyAttributes(fullPath, fileContent, writer);
            }
            else
            {
                createdNewFile = true;
                using (StreamWriter fileWriter = new StreamWriter(fullPath, false, Encoding.UTF8))
                {
                    fileWriter.Write(fileContent);
                }
            }

            return relativePath;
        }

        protected virtual TypeCollisionWriterContextService GetWriterContextService()
        {
            return new TypeCollisionWriterContextService(new ProjectGenerationDecompilationCacheService(), decompilationPreferences.RenameInvalidMembers);
        }

        protected virtual List<string> GetConfigurationConstants(bool debugConfiguration)
        {
            List<string> result = new List<string>();
            if (this.language is CSharp)
            {
                if (debugConfiguration)
                {
                    result.Add("DEBUG");
                }

                result.Add("TRACE");
            }

            return result;
        }

        private void PushAssemblyAttributes(string fileName, string fileContent, IAssemblyAttributeWriter writer)
        {
            string usingsText, content;
            HashSet<string> usings = new HashSet<string>();
            using (TextReader reader = new StreamReader(fileName))
            {
                StringBuilder usingsBuilder = new StringBuilder();
                while (true)
                {
                    string line = reader.ReadLine();
                    char[] chars = { ' ', ';' };
                    string[] words = line.Split(chars, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 2)
                    {
                        if (words[0] == "using")// make this language independant!
                        {
                            if (!(writer as BaseAssemblyAttributeWriter).AssemblyInfoNamespacesUsed.Contains(words[1]))
                            {
                                usingsBuilder.AppendLine(line);
                            }
                            usings.Add(words[1]);
                        }
                        else
                        {
                            content = line + Environment.NewLine;
                            break;
                        }
                    }
                    else
                    {
                        usingsBuilder.Append(line);
                    }
                }

                usingsText = usingsBuilder.ToString();
                content += reader.ReadToEnd();
            }

            File.WriteAllText(fileName, usingsText + fileContent + content);

        }

		protected virtual bool WriteNetModuleProjectFile(ModuleDefinition module)
		{
			string moduleProjFilePath;
			if (!modulesToProjectsFilePathsMap.TryGetValue(module, out moduleProjFilePath))
			{
				throw new Exception("Module project file path not found in modules projects filepaths map.");
			}

			FileStream projectFile = new FileStream(Path.Combine(this.targetDir, moduleProjFilePath), FileMode.OpenOrCreate);

			try
			{
				Project project = CreateProject();

                ProjectPropertyGroup basicProjectProperties = GetNetmoduleBasicProjectProperties(module);

                project.Items = GetProjectItems(module, basicProjectProperties);

				XmlSerializer serializer = new XmlSerializer(typeof(Project));
				serializer.Serialize(projectFile, project);

			}
			catch (Exception e)
			{
				StreamWriter fileWriter = new StreamWriter(projectFile);
				string exceptionWithStackTrance = string.Format("{0}{1}{2}", e.Message, Environment.NewLine, e.StackTrace);

				fileWriter.Write(exceptionWithStackTrance);
				throw e;
			}
			finally
			{
				projectFile.Flush();
				projectFile.Close();
				projectFile.Dispose();
			}

			return true;
		}

        protected virtual object[] GetProjectItems(ModuleDefinition module, ProjectPropertyGroup basicProjectProperties)
        {
            bool isVisualBasic = this.language is VisualBasic;
            object[] items = isVisualBasic ? new object[7] : new object[6];
            int i = 0;
            if (this.visualStudioVersion == VisualStudioVersion.VS2012 || this.visualStudioVersion == VisualStudioVersion.VS2013)
            {
                items = new object[items.Length + 1];
                items[i++] = GenerateCommonPropsProjectImportProperty();
            }

            items[i++] = basicProjectProperties;
            items[i++] = CreateConfiguration(basicProjectProperties, true); //Debug
            items[i++] = CreateConfiguration(basicProjectProperties, false); //Release
            items[i++] = CreatePojectReferences(module, basicProjectProperties);
            items[i++] = fileGenContext.GetProjectItemGroup();
            if (isVisualBasic)
            {
                items[i++] = GetCompileOptions();
            }

            items[i++] = GenerateLanguageTargetsProjectImportProperty();

            return items;
        }

        protected object GetCompileOptions()
        {
            ProjectPropertyGroup compileOptions = new ProjectPropertyGroup();
            compileOptions.OptionExplicit = "On";
            compileOptions.OptionCompare = "Binary";
            compileOptions.OptionStrict = "Off";
            compileOptions.OptionInfer = "On";

            return compileOptions;
        }

		protected virtual bool WriteMainModuleProjectFile(ModuleDefinition module)
		{
            //FileStream projectFile = new FileStream(Path.GetDirectoryName(this.TestName) + Path.DirectorySeparatorChar + assembly.Name.Name + language.VSProjectFileExtension, FileMode.OpenOrCreate);
            FileStream projectFile = null;
            if (this.TargetPath.EndsWith(language.VSProjectFileExtension))
            {
                projectFile = new FileStream(this.TargetPath, FileMode.OpenOrCreate);
            }
            else
            {
                projectFile = new FileStream(this.TargetPath + language.VSProjectFileExtension, FileMode.OpenOrCreate);
            }
            try
            {
				Project project = CreateProject();

                ProjectPropertyGroup basicProjectProperties = GetMainModuleBasicProjectProperties();

                project.Items = GetProjectItems(module, basicProjectProperties);

                XmlSerializer serializer = new XmlSerializer(typeof(Project));
                serializer.Serialize(projectFile, project);

            }
            catch (Exception e)
            {
                StreamWriter fileWriter = new StreamWriter(projectFile);
                string exceptionWithStackTrance = string.Format("{0}{1}{2}", e.Message, Environment.NewLine, e.StackTrace);

                fileWriter.Write(exceptionWithStackTrance);
                throw e;
            }
            finally
            {
                projectFile.Flush();
                projectFile.Close();
                projectFile.Dispose();
            }

			return true;
		}
  
		protected virtual ProjectImport GenerateLanguageTargetsProjectImportProperty()
		{
			if (this.language is Telerik.JustDecompiler.Languages.CSharp.CSharp)
			{
				return new ProjectImport() { Project = @"$(MSBuildToolsPath)\Microsoft.CSharp.targets" };
			}
			else if (this.language is Telerik.JustDecompiler.Languages.VisualBasic.VisualBasic)
			{
				return new ProjectImport() { Project = @"$(MSBuildToolsPath)\Microsoft.VisualBasic.targets" };
			}
			throw new NotSupportedException("Project generation not supported in this language.");
		}

        protected ProjectImport GenerateCommonPropsProjectImportProperty()
        {
            return new ProjectImport()
            {
                Project = @"$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props",
                Condition = @"Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')"
            };
        }

        protected ProjectItemGroup CreatePojectReferences(ModuleDefinition module, ProjectPropertyGroup basicProjectProperties)
        {
            ProjectItemGroup result = new ProjectItemGroup();

			ICollection<AssemblyNameReference> dependingOnAssemblies = GetAssembliesDependingOn(module);
            result.Reference = new ProjectItemGroupReference[dependingOnAssemblies.Count];

            string copiedReferencesSubfolder = basicProjectProperties.AssemblyName + "References";
            string referencesPath = TargetPath.Remove(TargetPath.LastIndexOf(Path.DirectorySeparatorChar)) + Path.DirectorySeparatorChar + copiedReferencesSubfolder;

            int assemblyReferenceIndex = 0;
            foreach (AssemblyNameReference reference in dependingOnAssemblies)
            {
                if (reference.Name == "mscorlib" ||
                    reference.Name == "Windows" && reference.Version.Equals(new Version(255, 255, 255, 255)) ||
                    reference.Name == "System.Runtime")
                {
                    continue;
                }

                AssemblyName assemblyName = new AssemblyName(reference.Name,
                                                                reference.FullName,
                                                                reference.Version,
                                                                reference.PublicKeyToken);

                string currentReferenceInitialLocation = this.currentAssemblyResolver.FindAssemblyPath(assemblyName, null);
                AssemblyDefinition referencedAssembly = this.currentAssemblyResolver.Resolve(reference, "", assembly.MainModule.GetModuleArchitecture());
#if NET35
				if (!currentReferenceInitialLocation.IsNullOrWhiteSpace())
#else
                if (!string.IsNullOrWhiteSpace(currentReferenceInitialLocation))
#endif
                {
                    result.Reference[assemblyReferenceIndex] = new ProjectItemGroupReference();
                    if (IsInReferenceAssemblies(referencedAssembly))
                    {
                        //TODO: Consider doing additional check, to see if the assembly is resolved because it was pointed by the used/already in the tree
                        //		In this case, it might be better to copy it.
                        result.Reference[assemblyReferenceIndex].Include = reference.Name;
                    }
                    else // Copy the referenced assembly
                    {
                        if (!Directory.Exists(referencesPath))
                        {
                            Directory.CreateDirectory(referencesPath);
                        }

                        string currentReferenceFileName = Path.GetFileName(currentReferenceInitialLocation);
                        string currentReferenceFinalLocation = Path.Combine(referencesPath, currentReferenceFileName);
                        File.Copy(currentReferenceInitialLocation, currentReferenceFinalLocation, true);

                        // set to normal for testing purposes- to allow the test project to delete the coppied file between test runs
                        File.SetAttributes(currentReferenceFinalLocation, FileAttributes.Normal);

                        string relativePath = Path.Combine(".", copiedReferencesSubfolder);
                        relativePath = Path.Combine(relativePath, currentReferenceFileName);

                        result.Reference[assemblyReferenceIndex].Include = Path.GetFileNameWithoutExtension(currentReferenceFinalLocation);
                        result.Reference[assemblyReferenceIndex].Item = relativePath;
                    }
                }

                assemblyReferenceIndex++;
            }

			ICollection<ModuleReference> dependingOnModules = GetModulesDependingOn(module);
			result.AddModules = new ProjectItemGroupAddModules[dependingOnModules.Count * 2];

			int moduleReferenceIndex = 0;
			foreach (ModuleReference moduleRef in dependingOnModules)
			{
				result.AddModules[moduleReferenceIndex] = new ProjectItemGroupAddModules();
				result.AddModules[moduleReferenceIndex].Include = @"bin\Debug\" + Utilities.GetNetmoduleName(moduleRef) + ".netmodule";
				result.AddModules[moduleReferenceIndex].Condition = " '$(Configuration)' == 'Debug' ";
				moduleReferenceIndex++;
				result.AddModules[moduleReferenceIndex] = new ProjectItemGroupAddModules();
				result.AddModules[moduleReferenceIndex].Include = @"bin\Release\" + Utilities.GetNetmoduleName(moduleRef) + ".netmodule";
				result.AddModules[moduleReferenceIndex].Condition = " '$(Configuration)' == 'Release' ";
				moduleReferenceIndex++; 
			}

            return result;
        }

        private bool IsInReferenceAssemblies(AssemblyDefinition referencedAssembly)
        {
            //Search C:\Program Files\Reference Assemblies 
            //and 
            //       C:\Program Files (x86)\Reference Assemblies
            //for the dll. If it is found there, then just a simple name will be enough for VS to locate it
            // Otherwise, the file should be copied it to local project folder and the reference should be pointing there.
            // TODO: Implement this method
            /// Try x86

#if !NET35
            string programFilesX64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.None);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolderOption.None);
#else
            string programFilesX64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string programFilesX86 =  programFilesX64 + " (x86)";
#endif

            //NOTE: programFilesX64 and programFilexX86 are expected to be equal on 32-bit machine

            string x86ReferenceAssemblies = Path.Combine(programFilesX86, "Reference Assemblies");
            string x64ReferenceAssemblies = Path.Combine(programFilesX64, "Reference Assemblies");

            if (!Directory.Exists(x86ReferenceAssemblies))
            {
                if (x86ReferenceAssemblies == x64ReferenceAssemblies)
                {
                    return false;
                }
            }
            else
            {
                bool returnValue = ContainsReferencedAssembly(x86ReferenceAssemblies, referencedAssembly);
                if (returnValue)
                {
                    return returnValue;
                }
            }

            if (x86ReferenceAssemblies != x64ReferenceAssemblies)
            {
                if (!Directory.Exists(x64ReferenceAssemblies))
                {
                    return false;
                }
                else
                {
                    return ContainsReferencedAssembly(x64ReferenceAssemblies, referencedAssembly);
                }
            }
            return false;
        }

        private bool ContainsReferencedAssembly(string directory, AssemblyDefinition referencedAssembly)
        {
            string assemblyName = Path.GetFileName(referencedAssembly.MainModule.FilePath);
#if !NET35
            IEnumerable<string> foundFiles = Directory.EnumerateFiles(directory, assemblyName, SearchOption.AllDirectories);
#else
            string[] foundFiles = Directory.GetFiles(directory, assemblyName, SearchOption.AllDirectories);
#endif
            foreach (string file in foundFiles)
            {
                ///Chech if all other assemblyName attributes match
                //AssemblyDefinition a = AssemblyDefinition.ReadAssembly(file);
                AssemblyDefinition a = currentAssemblyResolver.GetAssemblyDefinition(file);
                if (a.FullName == referencedAssembly.FullName)
                {
                    return true;
                }
            }
            return false;
        }

        protected ProjectPropertyGroup CreateConfiguration(ProjectPropertyGroup basicProjectProperties, bool debugConfiguration)
        {
            return this.CreateConfiguration(basicProjectProperties.Platform.Value, debugConfiguration);
        }

        protected virtual ProjectPropertyGroup CreateConfiguration(string platform, bool debugConfiguration)
        {
            ProjectPropertyGroup result = new ProjectPropertyGroup();

            if (debugConfiguration)
            {
                result.Condition = " '$(Configuration)|$(Platform)' == 'Debug|" + platform + "' ";
                result.DebugSymbols = true;
                result.DebugType = "full";
                result.Optimize = false;
                result.OutputPath = GetOutputPath(platform, debugConfiguration);
            }
            else
            {
                result.Condition = " '$(Configuration)|$(Platform)' == 'Release|" + platform + "' ";
                result.DebugSymbols = false;
                result.DebugType = "pdbonly";
                result.Optimize = true;
                result.OutputPath = GetOutputPath(platform, debugConfiguration);
            }

            string separator = this.language is VisualBasic ? "," : ";";
            string defineConstants = string.Join(separator, GetConfigurationConstants(debugConfiguration));
            if (defineConstants != string.Empty)
            {
                result.DefineConstants = defineConstants;
            }

            if (this.language is CSharp)
            {
                result.ErrorReport = "prompt";
                result.WarningLevel = 4;
                result.WarningLevelSpecified = true;
            }
            else if (this.language is VisualBasic)
            {
                result.DefineDebug = debugConfiguration;
                result.DefineDebugSpecified = true;

                result.DefineTrace = true;
                result.DefineTraceSpecified = true;

                result.DocumentationFile = string.Format("{0}.xml", this.assembly.Name.Name);
                result.NoWarn = string.Join(",", this.GetWarningConfigurations());
            }
            else
            {
                throw new NotSupportedException();
            }

            result.PlatformTarget = platform;
            result.OptimizeSpecified = true;
            result.DebugSymbolsSpecified = true;

            return result;
        }

        protected virtual string GetOutputPath(string platform, bool debugConfiguration)
        {
            if (debugConfiguration)
            {
                return @"bin\Debug\";
            }
            else
            {
                return @"bin\Release\";
            }
        }

        protected virtual IList<string> GetWarningConfigurations()
        {
            return new List<string>() { "42016", "41999", "42017", "42018", "42019", "42032", "42036", "42020", "42021", "42022" };
        }

		protected virtual ProjectPropertyGroup GetNetmoduleBasicProjectProperties(ModuleDefinition module)
		{
			if (module.Kind != ModuleKind.NetModule)
			{
				throw new Exception("Unexpected type of module.");
			}

			ProjectPropertyGroup basicProjectProperties = new ProjectPropertyGroup();

            basicProjectProperties.TargetFrameworkVersion = this.GetTargetFrameworkVersion(module);

			basicProjectProperties.AssemblyName = Utilities.GetNetmoduleName(module);
			basicProjectProperties.OutputType = GetOutputType(module);

			basicProjectProperties.Platform = new ProjectPropertyGroupPlatform() { Condition = " '$(Platform)' == '' " };
			basicProjectProperties.Platform.Value = Utilities.GetModuleArchitecturePropertyValue(module);

			basicProjectProperties.Configuration = new ProjectPropertyGroupConfiguration() { Condition = " '$(Configuration)' == '' ", Value = "Debug" };

			Guid guid = Guid.NewGuid();
			modulesProjectsGuids.Add(module, guid);
			basicProjectProperties.ProjectGuid = "{" + guid.ToString() + "}";

            if (this.visualStudioVersion == VisualStudioVersion.VS2010)
            {
                basicProjectProperties.SchemaVersion = (decimal)2.0; //constant in VS 2010
                basicProjectProperties.SchemaVersionSpecified = true;

                //constant in VS 2010roperties.ProjectType
                //basicProjectProperties.SchemaVersionSpecified

                //basicProjectProperties.ProductVersion - the version of the project template VS used to create the project file
            }

			if (!(this.language is Telerik.JustDecompiler.Languages.VisualBasic.VisualBasic))
			{
				basicProjectProperties.RootNamespace = fileGenContext.NamespacesTree.RootNamespace;
			}

			return basicProjectProperties;
		}

		protected virtual ProjectPropertyGroup GetMainModuleBasicProjectProperties()
        {
			ProjectPropertyGroup basicProjectProperties = new ProjectPropertyGroup();

            basicProjectProperties.TargetFrameworkVersion = GetTargetFrameworkVersion(this.assembly.MainModule);

            basicProjectProperties.AssemblyName = assembly.Name.Name;

            basicProjectProperties.OutputType = GetOutputType(this.assembly.MainModule);

            basicProjectProperties.Platform = new ProjectPropertyGroupPlatform() { Condition = " '$(Platform)' == '' " };
			basicProjectProperties.Platform.Value = Utilities.GetModuleArchitecturePropertyValue(assembly.MainModule);

            basicProjectProperties.Configuration = new ProjectPropertyGroupConfiguration() { Condition = " '$(Configuration)' == '' ", Value = "Debug" };

			Guid guid = Guid.NewGuid();
			modulesProjectsGuids.Add(assembly.MainModule, guid);
            basicProjectProperties.ProjectGuid = "{" + guid.ToString() + "}";

            if (this.visualStudioVersion == VisualStudioVersion.VS2010)
            {
                basicProjectProperties.SchemaVersion = (decimal)2.0; //constant in VS 2010
                basicProjectProperties.SchemaVersionSpecified = true;

                //constant in VS 2010roperties.ProjectType
                //basicProjectProperties.SchemaVersionSpecified

                //basicProjectProperties.ProductVersion - the version of the project template VS used to create the project file
            }

			// VB compiler injects RootNamespace in all types
			// so we let it stay in the source code and remove it from the project settings
			if (!(this.language is Telerik.JustDecompiler.Languages.VisualBasic.VisualBasic))
			{
				basicProjectProperties.RootNamespace = fileGenContext.NamespacesTree.RootNamespace;
			}

			return basicProjectProperties;
        }

        protected virtual string GetTargetFrameworkVersion(ModuleDefinition module)
        {
            //TODO: handle Silverlight/WinPhone projects
            FrameworkVersion frameworkVersion = this.assemblyInfo.ModulesFrameworkVersions[module];
            if (frameworkVersion == FrameworkVersion.Unknown || frameworkVersion == FrameworkVersion.Silverlight)
            {
                return null;
            }

            return frameworkVersion.ToString(includeVersionSign: true);
        }

        protected virtual string GetOutputType(ModuleDefinition module)
        {
            switch (module.Kind)
            {
                case ModuleKind.Dll:
                    return "Library";
                case ModuleKind.Console:
                    return "Exe";
                case ModuleKind.Windows:
                    return "WinExe";
                case ModuleKind.NetModule:
                    return "module";
                default:
                    throw new NotImplementedException();
            }
        }

        private void CreateResources(ModuleDefinition module)
        {
			string targetDir = Path.GetDirectoryName(this.TargetPath);
			foreach (Resource resource in module.Resources)
			{
				if (resource.ResourceType != ResourceType.Embedded)
				{
					continue;
				}

				EmbeddedResource embeddedResource = (EmbeddedResource)resource;
				IFileGeneratedInfo args;
				if (resource.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
				{
					if (!embeddedResource.Name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
					{
						string resourceName = embeddedResource.Name.Substring(0, embeddedResource.Name.Length - 10); //".resources".Length == 10
						string relativeResourcePath = resourcesToPathsMap[resource];
						string fullResourcePath = Path.Combine(targetDir, relativeResourcePath);
						if (TryCreateResXFile(embeddedResource, fullResourcePath))
						{
							fileGenContext.ResourceDesignerMap.Add(resourceName, relativeResourcePath);
							args = new FileGeneratedInfo(fullResourcePath, false);
							OnProjectFileCreated(args);
						}
					}
					else
					{
						ProcessXamlResources(embeddedResource, module);
					}
				}
				else
				{
					string resourceLegalName = resourcesToPathsMap[resource];
					string resourceFullPath = Path.Combine(targetDir, resourceLegalName);
					using (FileStream fileStream = new FileStream(resourceFullPath, FileMode.Create, FileAccess.Write))
					{
						embeddedResource.GetResourceStream().CopyTo(fileStream);
					}

					fileGenContext.OtherEmbeddedResources.Add(new ProjectItemGroupEmbeddedResource() { Include = resourceLegalName });

					args = new FileGeneratedInfo(resourceFullPath, false);
					OnProjectFileCreated(args);
				}
			}
        }

        private bool TryCreateResXFile(EmbeddedResource embeddedResource, string resourceFilePath)
        {
            List<System.Collections.DictionaryEntry> resourceEntries = new List<System.Collections.DictionaryEntry>();

            using (ResourceReader resourceReader = new ResourceReader(embeddedResource.GetResourceStream()))
            {
                IDictionaryEnumerator enumerator = resourceReader.GetEnumerator();

                while (enumerator.MoveNext())
                {
                    try
                    {
                        resourceEntries.Add(enumerator.Entry);
                    }
                    catch (Exception ex)
                    {
                        if (ResourceWritingFailure != null)
                        {
                            ResourceWritingFailure(this, embeddedResource.Name, ex);
                        }

						if (this.projectNotifier != null)
						{
							this.projectNotifier.OnResourceWritingFailure(embeddedResource.Name, ex);
						}
                    }
                }
            }

            string dirPath = Path.GetDirectoryName(resourceFilePath);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

#if !NET35
            using (ResXResourceWriter resXWriter = new ResXResourceWriter(resourceFilePath, ResourceTypeNameConverter))
#else
            using (ResXResourceWriter resXWriter = new ResXResourceWriter(resourceFilePath))
#endif
            {
                foreach (System.Collections.DictionaryEntry resourceEntry in resourceEntries)
                {
                    resXWriter.AddResource((string)resourceEntry.Key, resourceEntry.Value);
                }
            }

            return true;
        }

        private void ProcessXamlResources(EmbeddedResource resource, ModuleDefinition module)
        {
            string targetDir = Path.GetDirectoryName(TargetPath);
            using (ResourceReader resourceReader = new ResourceReader(resource.GetResourceStream()))
            {
                foreach (System.Collections.DictionaryEntry resourceEntry in resourceReader)
                {
                    string xamlResourceKey = Utilities.GetXamlResourceKey(resourceEntry, module);

					bool isBamlResource = ((string)resourceEntry.Key).EndsWith(".baml", StringComparison.OrdinalIgnoreCase);

                    string xamlResourceRelativePath = xamlResourcesToPathsMap[xamlResourceKey];
                    string fullPath = Path.Combine(targetDir, xamlResourceRelativePath);

                    string fullClassName = TryWriteBamlResource(fullPath, isBamlResource, resourceEntry.Value as UnmanagedMemoryStream);
                    if (fullClassName != null)
                    {
                        fileGenContext.XamlFullNameToRelativePathMap.Add(fullClassName, xamlResourceRelativePath);
                    }
                    else
                    {
                        fileGenContext.OtherXamlResources.Add(new ProjectItemGroupResource() { Include = xamlResourceRelativePath });
                    }

					IFileGeneratedInfo args = new FileGeneratedInfo(fullPath, false);
                    OnProjectFileCreated(args);
                }
            }
        }

        private string TryWriteBamlResource(string resourcePath, bool isBamlResource, UnmanagedMemoryStream unmanagedStream)
        {
            if (unmanagedStream == null)
            {
                return null;
            }

            string resourceDir = Path.GetDirectoryName(resourcePath);
            if (!Directory.Exists(resourceDir))
            {
                Directory.CreateDirectory(resourceDir);
            }

            Stream sourceStream;
            string fullClassName = null;
#if ENGINEONLYBUILD
            sourceStream = unmanagedStream;
#else
            XDocument xamlDoc = null;

            bool exceptionOccurred = false;
            //TODO: refactor
            if (isBamlResource)
            {
                sourceStream = new MemoryStream();
                try
                {
                    unmanagedStream.Seek(0, SeekOrigin.Begin);
                    xamlDoc = BamlToXamlConverter.DecompileToDocument(unmanagedStream, currentAssemblyResolver, assemblyPath);
#if !NET35
                    xamlDoc.Save(sourceStream);
#else
                    xamlDoc.Save(new StreamWriter(sourceStream));
#endif
                }
                catch
                {
                    exceptionOccurred = true;
                    unmanagedStream.Seek(0, SeekOrigin.Begin);
                    sourceStream = unmanagedStream;
                }
            }
            else
            {
                sourceStream = unmanagedStream;
            }

            if (isBamlResource && !exceptionOccurred)
            {
                fullClassName = Utilities.GetXamlTypeFullName(xamlDoc);
                if (fullClassName != null)
                {
                    xamlGeneratedFields.Add(fullClassName, Utilities.GetXamlGeneratedFields(xamlDoc));
                }
            }
#endif

            using (FileStream fileStream = new FileStream(resourcePath, FileMode.Create, FileAccess.Write))
            {
                using (sourceStream)
                {
                    sourceStream.Seek(0, SeekOrigin.Begin);
                    sourceStream.CopyTo(fileStream);
                }
            }

            return fullClassName;
        }

        private string ResourceTypeNameConverter(Type type)
        {
            return type.AssemblyQualifiedName;
        }

		private void OnProjectFileCreated(IFileGeneratedInfo projectFileGeneratedCallbackArgs)
        {
            if (ProjectFileCreated != null)
            {
                ProjectFileCreated(this, new ProjectFileCreated(projectFileGeneratedCallbackArgs.FullPath, projectFileGeneratedCallbackArgs.HasErrors));
            }
			if (fileGeneratedNotifier != null)
			{
				fileGeneratedNotifier.OnProjectFileGenerated(projectFileGeneratedCallbackArgs);
			}
        }

        private void OnProjectGenerationFinished()
        {
            EventHandler handler = ProjectGenerationFinished;
            if (handler != null)
            {
                handler(this, new EventArgs());
            }

			if (this.projectNotifier != null)
			{
				this.projectNotifier.OnProjectGenerationFinished();
			}
        }

		protected virtual AssemblyInfo GetAssemblyInfo()
		{
			return this.assemblyInfoService.GetAssemblyInfo(this.assembly, this.frameworkResolver);
		}
    }
}
