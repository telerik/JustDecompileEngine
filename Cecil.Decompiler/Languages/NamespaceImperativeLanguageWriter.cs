using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Extensions;
using Telerik.JustDecompiler.Decompiler.WriterContextServices;
using Telerik.JustDecompiler.Decompiler;

namespace Telerik.JustDecompiler.Languages
{
	public abstract class NamespaceImperativeLanguageWriter : BaseImperativeLanguageWriter, INamespaceLanguageWriter
	{
        protected const string CastToObjectResolvementError = "The cast to object might be unnecessary. Please, locate the assembly where \"{0}\" is defined.";

		protected bool writeFullyQualifiedNames;
		protected string currentNamespace;
		protected bool writeNamespacesandUsings;

		public IEnumerable<string> AssemblyInfoNamespacesUsed
		{
			get
			{
				return AssemblyContext.AssemblyNamespaceUsings.Union(ModuleContext.ModuleNamespaceUsings);
			}
		}

		public NamespaceImperativeLanguageWriter(ILanguage language, IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
			: base(language, formatter, exceptionFormatter, writeExceptionsAsComments)
		{
			writeNamespacesandUsings = false;
			writeFullyQualifiedNames = false;
		}

		public List<WritingInfo> WriteTypeAndNamespaces(TypeDefinition type, IWriterContextService writerContextService, bool writeDocumentation, bool showCompilerGeneratedMembers, bool writeFullyQualifiedNames)
		{
			this.writerContextService = writerContextService;
			this.writerContext = writerContextService.GetWriterContext(type, Language);
			this.CurrentType = type;
			this.currentNamespace = GetCurrentNamespace(type);
			this.currentWritingInfo = new WritingInfo(type);
			UpdateWritingInfo(this.writerContext, this.currentWritingInfo);
			this.writingInfos = new List<WritingInfo>();
			this.writingInfos.Add(this.currentWritingInfo);
			WriteTypeAndNamespacesInternal(type, writeDocumentation, showCompilerGeneratedMembers, writeFullyQualifiedNames);
            return writingInfos;
		}

		public override void WriteNamespaceIfTypeInCollision(TypeReference reference)
		{
			if (this.writeFullyQualifiedNames)
			{
				return;
			}

			base.WriteNamespaceIfTypeInCollision(reference);
		}

		protected void WriteTypeAndNamespacesInternal(TypeDefinition type, bool writeDocumentation, bool showCompilerGeneratedMembers, bool writeFullyQualifiedNames)
		{
			this.writeNamespacesandUsings = true;
			this.writeFullyQualifiedNames = writeFullyQualifiedNames;
			this.currentNamespace = GetCurrentNamespace(type);

			if (TypeContext.UsedNamespaces.Count > 0)
			{
				WriteUsings(TypeContext.UsedNamespaces);
				WriteLine();
				WriteLine();
			}

			WriteTypeNamespaceStart(type);
			WriteInternal(type, writeDocumentation, showCompilerGeneratedMembers);
			WriteTypeNamespaceEnd(type);
		}

		private string GetCurrentNamespace(TypeDefinition type)
		{
			TypeDefinition outerMostDeclaringType = Utilities.GetOuterMostDeclaringType(type);
			return outerMostDeclaringType.Namespace;
		}

		public List<WritingInfo> WriteType(TypeDefinition type, IWriterContextService writerContextService, bool writeDocumentation, bool showCompilerGeneratedMembers, bool writeFullyQualifiedNames)
		{
			this.writeNamespacesandUsings = false;
			this.writeFullyQualifiedNames = writeFullyQualifiedNames;
			this.currentNamespace = GetCurrentNamespace(type);
			return base.Write(type, writerContextService, writeDocumentation, showCompilerGeneratedMembers);
		}

		public List<WritingInfo> WritePartialTypeAndNamespaces(TypeDefinition type, IWriterContextService writerContextService, bool showCompilerGeneratedMembers, bool writeFullyQualifiedNames, bool writeDocumentation, Dictionary<string, ICollection<string>> fieldsToSkip = null)
		{
			this.writerContextService = writerContextService;
			this.writerContext = writerContextService.GetWriterContext(type, Language);
			this.CurrentType = type;
			this.currentWritingInfo = new WritingInfo(type);
			UpdateWritingInfo(this.writerContext, this.currentWritingInfo);
			this.writingInfos = new List<WritingInfo>();
			this.writingInfos.Add(this.currentWritingInfo);
			WritePartialTypeAndNamespacesInternal(type, showCompilerGeneratedMembers, writeFullyQualifiedNames, writeDocumentation, fieldsToSkip);
            return writingInfos;
		}

		public void WritePartialTypeAndNamespacesInternal(TypeDefinition type, bool showCompilerGeneratedMembers, bool writeFullyQualifiedNames, bool writeDocumentation, Dictionary<string, ICollection<string>> fieldsToSkip = null)
		{ 			
			this.writeNamespacesandUsings = true;
			this.writeFullyQualifiedNames = writeFullyQualifiedNames;
			currentNamespace = type.Namespace;
			ICollection<string> typeFieldsToSkip = null;
			if (fieldsToSkip.ContainsKey(type.FullName))
			{
				typeFieldsToSkip = fieldsToSkip[type.FullName];
			}

			if (TypeContext.UsedNamespaces.Count > 0)
			{
				WriteUsings(TypeContext.UsedNamespaces);
				WriteLine();
				WriteLine();
			}

			WriteTypeNamespaceStart(type);
			WritePartialType(type, writeDocumentation,showCompilerGeneratedMembers, fieldsToSkip: typeFieldsToSkip);
			WriteTypeNamespaceEnd(type);
		}

		private void WriteNamespaceDeclaration(string @namespace)
		{
			if (ModuleContext.RenamedNamespacesMap.ContainsKey(@namespace))
			{
				WriteComment(@namespace);
				WriteLine();
				@namespace = ModuleContext.RenamedNamespacesMap[@namespace];
			}

			WriteKeyword(KeyWordWriter.Namespace);
			WriteSpace();
			Write(@namespace);
			this.formatter.WriteNamespaceStartBlock();
			WriteBeginBlock();
			WriteLine();
		}

		public void WriteSecurityDeclarationNamespaceIfNeeded()
		{
			if (writeFullyQualifiedNames)
			{
				Write("System.Security.Permissions");
				WriteToken(".");
			}
		}

		public void WriteUsings(ICollection<string> usedNamespaces)
		{
			string[] namespaces = usedNamespaces.ToArray();
			Array.Sort(namespaces);

			bool isFirstUsing = true;
			foreach (string @namespace in namespaces)
			{
				if (!isFirstUsing)
				{
					WriteLine();
				}

				WriteKeyword(KeyWordWriter.NamespaceUsing);
				WriteSpace();

				if (isFirstUsing)
				{
					this.formatter.WriteStartUsagesBlock();
					isFirstUsing = false;
				}

				string namespaceName = @namespace;
				if (ModuleContext.RenamedNamespacesMap.ContainsKey(@namespace))
				{
					namespaceName = ModuleContext.RenamedNamespacesMap[@namespace];
				}
				Write(namespaceName);
				WriteEndOfStatement();
			}

			this.formatter.WriteEndUsagesBlock();
		}

		public void WriteAssemblyUsings()
		{
			WriteUsings(AssemblyContext.AssemblyNamespaceUsings);
		}

		public void WriteModuleUsings()
		{
			WriteUsings(ModuleContext.ModuleNamespaceUsings);
		}

		public void WriteAssemblyAndModuleUsings()
		{
			WriteUsings(Utilities.GetAssemblyAndModuleNamespaceUsings(this.AssemblyContext, this.ModuleContext));
		}

		protected sealed override void WriteTypeNamespaceStart(TypeDefinition type)
		{
			currentNamespace = type.GetNamespace();
			if (writeNamespacesandUsings && type.Namespace != String.Empty)
			{
				WriteNamespaceDeclaration(currentNamespace);
				Indent();
			}			
		}

		protected sealed override void WriteTypeNamespaceEnd(TypeDefinition type)
		{
			if (writeNamespacesandUsings && type.Namespace != String.Empty)
			{
				WriteLine();
				Outdent();
				WriteEndBlock(KeyWordWriter.Namespace);
				this.formatter.WriteNamespaceEndBlock();
			}
		}

		protected override void WriteTypeInANewWriterIfNeeded(TypeDefinition type, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
		{
			if (this.CurrentType != type)
			{
				ILanguageWriter writer = Language.GetWriter(this.formatter, this.exceptionFormatter, this.WriteExceptionsAsComments);
				List<WritingInfo> nestedWritingInfos = (writer as NamespaceImperativeLanguageWriter).WriteType(type, writerContextService, writeDocumentation, showCompilerGeneratedMembers, writeFullyQualifiedNames);
				this.writingInfos.AddRange(nestedWritingInfos);
			}
			else
			{
				WriteType(type, writeDocumentation, showCompilerGeneratedMembers);
			}
		}

		protected override sealed void WriteTypeAndName(TypeReference typeReference, string name, object reference)
		{
			DoWriteTypeAndName(typeReference, name, reference);
		}

		protected override sealed void WriteTypeAndName(TypeReference typeReference, string name)
		{
			DoWriteTypeAndName(typeReference, name);			
		}
 
		protected override sealed void WriteParameterTypeAndName(TypeReference type, string name, ParameterDefinition reference)
		{
			DoWriteParameterTypeAndName(type, name, reference);
		}

		protected override void WriteNamespace(object reference, bool forceWriteNamespace = false)
		{
			if (reference is TypeReference)
			{
				WriteNamespaceIfNeeded(reference as TypeReference, forceWriteNamespace);
				return;
			}
			base.WriteNamespace(reference, forceWriteNamespace);
		}

		internal void WriteEnumValueField(FieldDefinition field)
		{
			string fieldName = GetFieldName(field);
			this.WriteReference(fieldName, field);
		}

		internal override void WriteReference(string name, object reference)
		{
			if (reference is TypeReference)
			{
				WriteNamespaceIfNeeded(reference as TypeReference);
				WriteTypeReference(name, reference as TypeReference);
				return;
			}
			if (reference is MethodReference)
			{
				WriteMethodReference(name, reference as MethodReference);
				return;
			}
			base.WriteReference(name, reference);
		}

		protected virtual void WriteMethodReference(string name, MethodReference reference)
		{
			base.WriteReference(name, reference);
		}

		protected virtual void WriteTypeReference(string name, TypeReference reference)
		{
			base.WriteReference(name, reference);
		}

		public void WriteBody(IMemberDefinition member, IWriterContextService writerContextService, bool writeFullQualifiedNames)
		{
			this.writerContextService = writerContextService;
			this.writerContext = writerContextService.GetWriterContext(member, Language);
			this.currentWritingInfo = new WritingInfo(member);
			UpdateWritingInfo(this.writerContext, this.currentWritingInfo);
			this.writingInfos = new List<WritingInfo>();
			this.writingInfos.Add(this.currentWritingInfo);
			WriteBodyInternal(member, writeFullQualifiedNames);
		}

		protected void WriteBodyInternal(IMemberDefinition member, bool writeFullyQualifiedNames)
		{
			membersStack.Push(member);
			this.writeFullyQualifiedNames = writeFullyQualifiedNames;
			this.currentNamespace = member.DeclaringType.Namespace;
			WriteBodyInternal(member);
			membersStack.Pop();
		}

		private void WriteNamespaceIfNeeded(TypeReference reference, bool forceWriteNamespace = false)
		{
			if ((!forceWriteNamespace) && (!writeFullyQualifiedNames || CheckForSpecialName(reference)))
			{
				return;
			}

			string @namespace = string.Empty;
			if (forceWriteNamespace)
			{
				@namespace = reference.Namespace;
			}
			else
			{
				if (reference.Namespace != currentNamespace)
				{
					@namespace = reference.Namespace;
				}
			}

			if (ModuleContext.RenamedNamespacesMap.ContainsKey(@namespace))
			{
				@namespace = ModuleContext.RenamedNamespacesMap[@namespace];
			}

			if (@namespace != string.Empty)
			{
				@namespace += '.';
			}

			Write(@namespace);
		}

		private bool CheckForSpecialName(TypeReference reference)
		{
			if(reference.HasGenericParameters)
			{
				return false;
			}
			return reference.Name != ToTypeString(reference);
		}
		
		protected abstract void DoWriteTypeAndName(TypeReference typeReference, string name, object reference);
		protected abstract void DoWriteTypeAndName(TypeReference typeReference, string name);
		protected abstract void DoWriteParameterTypeAndName(TypeReference type, string name, ParameterDefinition reference);
	}
}
