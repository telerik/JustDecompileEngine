using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Telerik.JustDecompiler.Languages;
using Mono.Cecil;
using Mono.Cecil.Extensions;
using Telerik.JustDecompiler.Ast.Statements;
using Telerik.JustDecompiler.Decompiler.MemberRenamingServices;

namespace Telerik.JustDecompiler.Decompiler.WriterContextServices
{
	public class TypeDeclarationsWriterContextService : BaseWriterContextService
	{
		public override WriterContext GetWriterContext(IMemberDefinition member, ILanguage language)
		{
			TypeDefinition type = Utilities.GetDeclaringTypeOrSelf(member);

            Dictionary<FieldDefinition, PropertyDefinition> fieldToPropertyMap = type.GetFieldToPropertyMap(language);
            IEnumerable<FieldDefinition> propertyFields = fieldToPropertyMap.Keys;
            HashSet<PropertyDefinition> autoImplementedProperties = new HashSet<PropertyDefinition>(fieldToPropertyMap.Values);
			HashSet<EventDefinition> autoImplementedEvents = GetAutoImplementedEvents(type);

			TypeSpecificContext typeContext = new TypeSpecificContext(type) { AutoImplementedProperties = autoImplementedProperties, AutoImplementedEvents = autoImplementedEvents };

			TypeDefinition declaringType = Utilities.GetDeclaringTypeOrSelf(member);

			Dictionary<string, string> renamedNamespacesMap = new Dictionary<string, string>();
			MemberRenamingData memberReanmingData = GetMemberRenamingData(declaringType.Module, language);

			ModuleSpecificContext moduleContext =
				new ModuleSpecificContext(declaringType.Module, new List<string>(), new Dictionary<string, List<string>>(), new Dictionary<string, HashSet<string>>(), 
					renamedNamespacesMap, memberReanmingData.RenamedMembers, memberReanmingData.RenamedMembersMap);			

			return new WriterContext(
				new AssemblySpecificContext(),
				moduleContext,
				typeContext,
				new Dictionary<string, MethodSpecificContext>(), 
				GetDecompiledStatements(member, language, propertyFields));
		}

		public TypeDeclarationsWriterContextService(bool renameInvalidMembers) : base(null, renameInvalidMembers) { }

		new public bool ExceptionsWhileDecompiling
		{
			get
			{
				// no decompilation is done in TypeDeclarationsWriterContextService
				return false;
			}
		}

		private HashSet<EventDefinition> GetAutoImplementedEvents(TypeDefinition type)
		{
			HashSet<EventDefinition> result = new HashSet<EventDefinition>();

			if (type.HasEvents)
			{
				foreach (EventDefinition @event in type.Events)
				{
					AutoImplementedEventMatcher matcher = new AutoImplementedEventMatcher(@event);
					bool isAutoImplemented = matcher.IsAutoImplemented();

					if (isAutoImplemented)
					{
						result.Add(@event);
					}
				}
			}

			return result;
		}

		private Dictionary<string, Statement> GetDecompiledStatements(IMemberDefinition member, ILanguage language, IEnumerable<FieldDefinition> propertyFields)
		{
			Dictionary<string, Statement> decompiledStatements = new Dictionary<string, Statement>();

			Queue<IMemberDefinition> decompilationQueue = new Queue<IMemberDefinition>();

			decompilationQueue.Enqueue(member);
			while (decompilationQueue.Count > 0)
			{
				IMemberDefinition currentMember = decompilationQueue.Dequeue();

				if (currentMember is TypeDefinition && currentMember == member)
				{
					TypeDefinition currentType = (currentMember as TypeDefinition);

					List<IMemberDefinition> members = Utilities.GetTypeMembers(currentType, language, propertyFields: propertyFields);
					foreach (IMemberDefinition typeMember in members)
					{
						decompilationQueue.Enqueue(typeMember);
					}
				}

				if (currentMember is MethodDefinition)
				{
					decompiledStatements.Add(Utilities.GetMemberUniqueName(currentMember), new BlockStatement());
				}
				if (currentMember is EventDefinition)
				{
					EventDefinition eventDefinition = (currentMember as EventDefinition);

					if (eventDefinition.AddMethod != null)
					{
						decompiledStatements.Add(Utilities.GetMemberUniqueName(eventDefinition.AddMethod), new BlockStatement());
					}

					if (eventDefinition.RemoveMethod != null)
					{
						decompiledStatements.Add(Utilities.GetMemberUniqueName(eventDefinition.RemoveMethod), new BlockStatement());
					}

					if (eventDefinition.InvokeMethod != null)
					{
						decompiledStatements.Add(Utilities.GetMemberUniqueName(eventDefinition.InvokeMethod), new BlockStatement());
					}
				}
				if (currentMember is PropertyDefinition)
				{
					PropertyDefinition propertyDefinition = (currentMember as PropertyDefinition);

					if (propertyDefinition.GetMethod != null)
					{
						decompiledStatements.Add(Utilities.GetMemberUniqueName(propertyDefinition.GetMethod), new BlockStatement());
					}

					if (propertyDefinition.SetMethod != null)
					{
						decompiledStatements.Add(Utilities.GetMemberUniqueName(propertyDefinition.SetMethod), new BlockStatement());
					}
				}
			}

			return decompiledStatements;
		}

		public override AssemblySpecificContext GetAssemblyContext(AssemblyDefinition assembly, ILanguage language)
		{
			return new AssemblySpecificContext();
		}

		public ModuleSpecificContext GetModuleContext(AssemblyDefinition assembly, ILanguage language)
		{
			return new ModuleSpecificContext();
		}

		public override ModuleSpecificContext GetModuleContext(ModuleDefinition module, ILanguage language)
		{
			return new ModuleSpecificContext();
		}
	}
}
