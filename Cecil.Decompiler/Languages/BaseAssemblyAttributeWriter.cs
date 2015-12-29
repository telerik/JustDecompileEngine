using System.Collections.Generic;
using Mono.Cecil;
using Telerik.JustDecompiler.Decompiler;
using Telerik.JustDecompiler.Decompiler.WriterContextServices;

namespace Telerik.JustDecompiler.Languages
{
	public abstract class BaseAssemblyAttributeWriter : IAssemblyAttributeWriter
	{
		protected bool WriteExceptionsAsComments { get; private set; }
		protected IExceptionFormatter exceptionFormatter;

		public ILanguage Language { get; private set; }

		protected AttributeWriter attributeWriter = null;

		protected AttributeWriter AttributeWriter
		{
			get
			{
				if (attributeWriter == null)
				{
					attributeWriter = CreateAttributeWriter();
				}
				return attributeWriter;
			}
		}

		protected NamespaceImperativeLanguageWriter Writer
		{
			get
			{
				return GetLanguageWriter();
			}
		}

		public IEnumerable<string> AssemblyInfoNamespacesUsed
		{
			get
			{
				return Writer.AssemblyInfoNamespacesUsed;
			}
		}

		public BaseAssemblyAttributeWriter(ILanguage language, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
        {
            this.Language = language;
			this.exceptionFormatter = exceptionFormatter;
			this.WriteExceptionsAsComments = writeExceptionsAsComments;
		}

		protected abstract AttributeWriter CreateAttributeWriter();
		protected abstract NamespaceImperativeLanguageWriter GetLanguageWriter();

		protected abstract void SetAssemblyContext(AssemblySpecificContext assemblyContext);
		protected abstract void SetModuleContext(ModuleSpecificContext moduleContext);

		public virtual void WriteAssemblyAttributes(AssemblyDefinition assembly, IWriterContextService writerContextService, bool writeUsings = false, ICollection<string> attributesToIgnore = null)
		{
			AssemblySpecificContext assemblyContext = writerContextService.GetAssemblyContext(assembly, Language);
			SetAssemblyContext(assemblyContext);

			ModuleSpecificContext mainModuleContext = writerContextService.GetModuleContext(assembly.MainModule, Language);
			SetModuleContext(mainModuleContext);

			WriteAssemblyAttributesInternal(assembly, assemblyContext.AssemblyNamespaceUsings, mainModuleContext.ModuleNamespaceUsings, writeUsings, attributesToIgnore);
		}

		public virtual void WriteAssemblyAttributesInternal(AssemblyDefinition assembly, ICollection<string> assemblyNamespaceUsings,
			ICollection<string> mainModuleNamespaceUsings, bool writeUsings = false, ICollection<string> attributesToIgnore = null)
		{
			if (writeUsings && (assemblyNamespaceUsings.Count > 0 || mainModuleNamespaceUsings.Count > 0))
			{
				Writer.WriteAssemblyUsings();
				Writer.WriteLine();
				Writer.WriteLine();
			}

			AttributeWriter.WriteAssemblyAttributes(assembly, attributesToIgnore);
		}

		public virtual void WriteModuleAttributes(ModuleDefinition module, IWriterContextService writerContextService, bool writeUsings = false, ICollection<string> attributesToIgnore = null)
		{
			AssemblySpecificContext assemblyContext = new AssemblySpecificContext();
			SetAssemblyContext(assemblyContext);

			ModuleSpecificContext moduleContext = writerContextService.GetModuleContext(module, Language);
			SetModuleContext(moduleContext);


			WriteModuleAttributesInternal(module, moduleContext.ModuleNamespaceUsings, writeUsings, attributesToIgnore);
		}

		public virtual void WriteModuleAttributesInternal(ModuleDefinition module, ICollection<string> moduleNamespaceUsings, bool writeUsings = false, ICollection<string> attributesToIgnore = null)
		{
			if (writeUsings && moduleNamespaceUsings.Count > 0)
			{
				Writer.WriteModuleUsings();
				Writer.WriteLine();
				Writer.WriteLine();
			}

			AttributeWriter.WriteModuleAttributes(module, attributesToIgnore);
		}

		public void WriteAssemblyInfo(AssemblyDefinition assembly, IWriterContextService writerContextService, bool writeUsings = false, 
			ICollection<string> assemblyAttributesToIgnore = null, ICollection<string> moduleAttributesToIgnore = null)
		{

			AssemblySpecificContext assemblyContext = writerContextService.GetAssemblyContext(assembly, Language);
			SetAssemblyContext(assemblyContext);

			ModuleSpecificContext mainModuleContext = writerContextService.GetModuleContext(assembly.MainModule, Language);
			SetModuleContext(mainModuleContext);

			WriteAssemblyInfoInternal(assembly, assemblyContext.AssemblyNamespaceUsings, mainModuleContext.ModuleNamespaceUsings, writeUsings, assemblyAttributesToIgnore, moduleAttributesToIgnore);
		}

		private void WriteAssemblyInfoInternal(AssemblyDefinition assembly, ICollection<string> assemblyNamespaceUsings,
			ICollection<string> mainModuleNamespaceUsings, bool writeUsings = false, ICollection<string> assemblyAttributesToIgnore = null, ICollection<string> moduleAttributesToIgnore = null)
		{
			if (writeUsings && (assemblyNamespaceUsings.Count > 0 || mainModuleNamespaceUsings.Count > 0))
			{
				Writer.WriteAssemblyAndModuleUsings();
				Writer.WriteLine();
				Writer.WriteLine();
			}

			WriteAssemblyAttributesInternal(assembly, assemblyNamespaceUsings, mainModuleNamespaceUsings, false, assemblyAttributesToIgnore);
			WriteModuleAttributesInternal(assembly.MainModule, mainModuleNamespaceUsings, false, moduleAttributesToIgnore);
		}
	}
}
