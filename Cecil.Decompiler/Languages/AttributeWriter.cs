using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using System.Collections;
using Mono.Collections.Generic;
using Telerik.JustDecompiler.Common;
using Mono.Cecil.Extensions;
using System;
using Telerik.JustDecompiler.Decompiler;
using System.Runtime.InteropServices;

namespace Telerik.JustDecompiler.Languages
{
	public abstract class AttributeWriter
	{
        protected readonly HashSet<string> attributesNotToShow = new HashSet<string>(new string[] { "System.ParamArrayAttribute",
                                                                                                    "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
                                                                                                    "Microsoft.VisualBasic.CompilerServices.StandardModuleAttribute" });
        protected NamespaceImperativeLanguageWriter genericWriter;
        private Dictionary<SecurityAttribute, SecurityDeclaration> securityAttributeToDeclaration = new Dictionary<SecurityAttribute, SecurityDeclaration>();
        private const string ASSEMBLYNOTRESOLVEDERROR = "JustDecompile was unable to locate the assembly where attribute parameters types are defined. Generating parameters values is impossible.";

        public AttributeWriter(NamespaceImperativeLanguageWriter writer)
        {
            genericWriter = writer;
        }

        public virtual void WriteMemberAttributesAndNewLine(IMemberDefinition member, IEnumerable<string> ignored = null, bool isWinRTImplementation = false)
        {
            //TODO Handle attributes that take arrays as arguments
            //TODO write tests for all possible types of attribute values and targets
            if (ignored != null)
            {
                foreach (string ignoredAttributeType in ignored)
                {
                    attributesNotToShow.Add(ignoredAttributeType);
                }
            }

            WriteMemberAttributesInternal(member, isWinRTImplementation);

            if (ignored != null)
            {
                foreach (string ignoredAttributeType in ignored)
                {
                    attributesNotToShow.Remove(ignoredAttributeType);
                }
            }
        }

        public abstract void WriteMemberReturnValueAttributes(IMemberDefinition member);
        
        protected virtual void WriteMemberAttributesInternal(IMemberDefinition member, bool isWinRTImplementation)
        {
            securityAttributeToDeclaration = new Dictionary<SecurityAttribute, SecurityDeclaration>();

            List<ICustomAttribute> attributes = CollectSecurityAttributes(member);

            foreach (CustomAttribute attribute in member.CustomAttributes)
            {
                attribute.Resolve();
                if (!isWinRTImplementation || !IsWinRTActivatableAttribute(attribute))
                {
                    attributes.Add(attribute);
                }
            }
            attributes.AddRange(WritePropertiesAsAttributes(member));

            SortAttributes(attributes);
            WriteAttributesInternal(member, attributes, false, false);
        }

        private void SortAttributes(List<ICustomAttribute> attributes)
        {
            attributes.Sort((x, y) => { return CompareAttributes(x, y); });
        }

        protected void WriteAttributesInternal(IMemberDefinition member, List<ICustomAttribute> attributes, bool skipTheNewLine, bool areReturnValueAttributes)
        {
            foreach (ICustomAttribute attribute in attributes)
            {
                if (attribute is CustomAttribute)
                {
                    WriteAttribute(attribute as CustomAttribute, skipTheNewLine, areReturnValueAttributes);
                }
                else if (attribute is SecurityAttribute)
                {
                    bool b;
                    WriteSecurityAttribute(GetModuleDefinition(member), false, attribute as SecurityAttribute, securityAttributeToDeclaration[attribute as SecurityAttribute], out b, skipTheNewLine, areReturnValueAttributes);
                }

                if (skipTheNewLine && areReturnValueAttributes)
                {
                    this.genericWriter.WriteSpace();
                }
            }
        }

        protected virtual List<ICustomAttribute> GetSortedReturnValueAttributes(IMethodSignature member)
        {
            List<ICustomAttribute> result = new List<ICustomAttribute>();
            
            if (member != null && member.MethodReturnType.HasCustomAttributes)
            {
                result.AddRange(member.MethodReturnType.CustomAttributes);
            }

            SortAttributes(result);

            return result;
        }

        private bool IsWinRTActivatableAttribute(CustomAttribute attribute)
        {
            if(attribute.AttributeType.FullName != "Windows.Foundation.Metadata.ActivatableAttribute" || attribute.ConstructorArguments.Count != 2)
            {
                return false;
            }

            TypeDefinition typeArgument = attribute.ConstructorArguments[0].Value as TypeDefinition;
            return typeArgument != null && typeArgument.IsWindowsRuntime;
        }

		public int WriteParameterAttributes(ParameterDefinition parameter, bool isWinRTMethodImplementation)
		{
			CustomAttribute inAttribute = isWinRTMethodImplementation ? null : GetInAttribute(parameter);
			CustomAttribute outAttribute = isWinRTMethodImplementation ? null : GetOutAttribute(parameter);

			List<CustomAttribute> customAttributes = new List<CustomAttribute>();
			if (inAttribute != null)
			{
				customAttributes.Add(inAttribute);
			}
			if (outAttribute != null)
			{
				customAttributes.Add(outAttribute);
			}
			customAttributes.AddRange(parameter.CustomAttributes);

            int attributesWriten = 0;
            foreach (CustomAttribute attribute in customAttributes)
            {
                if (attributesNotToShow.Contains(attribute.AttributeType.FullName))
                {
                    continue;
                }

                if(attributesWriten != 0)
                {
                    genericWriter.Write(ParameterAttributeSeparator);
                }

                WriteAttribute(attribute, true);
                attributesWriten++;
            }

            return attributesWriten;
		}
  
		protected virtual CustomAttribute GetOutAttribute(ParameterDefinition parameter)
		{
			if (parameter.IsOutParameter())
			{
				return null;
			}
			if (parameter == null || !parameter.IsOut)
			{
				return null;
			}
			//ModuleDefinition paramDeclaringModule = parameter.Method.ReturnType.Module;
			//IMetadataScope referencedCorlib = paramDeclaringModule.TypeSystem.Corlib;

			///// The type reference is created this way in order to avoid resolving it.
			///// This will help the program print valid code, even in cases when the targeted corlib cannot be found (i.e. decompiling .NET 1.1)
			///// The reference to the parameterless constructor is created for the same reason.

			//TypeReference outAttributeTypeReference = new TypeReference("System.Runtime.InteropServices", "OutAttribute", paramDeclaringModule, referencedCorlib);
			
			//if (outAttributeTypeReference == null)
			//{
			//	return null;
			//}

			//MethodReference emptyCtor = new MethodReference(".ctor", paramDeclaringModule.TypeSystem.Void, outAttributeTypeReference);

			//return new CustomAttribute(emptyCtor);
			return GetInOrOutAttribute(parameter, false);
		}
  
		protected virtual CustomAttribute GetInAttribute(ParameterDefinition parameter)
		{
			if (parameter == null || !parameter.IsIn)
			{
				return null;
			}
			return GetInOrOutAttribute(parameter, true);
		}
  
		protected CustomAttribute GetInOrOutAttribute(ParameterDefinition parameter, bool isInAttribute)
		{
			ModuleDefinition paramDeclaringModule = parameter.Method.ReturnType.Module;
			IMetadataScope referencedCorlib = paramDeclaringModule.TypeSystem.Corlib;
			string shortAttributeTypeName = isInAttribute ? "InAttribute" : "OutAttribute";
			/// The type reference is created this way in order to avoid resolving it.
			/// This will help the program print valid code, even in cases when the targeted corlib cannot be found (i.e. decompiling .NET 1.1)
			/// The reference to the parameterless constructor is created for the same reason.

			TypeReference inAttributeTypeReference = new TypeReference("System.Runtime.InteropServices", shortAttributeTypeName , paramDeclaringModule, referencedCorlib);
			if (inAttributeTypeReference == null)
			{
				return null;
			}

			MethodReference emptyCtor = new MethodReference(".ctor", paramDeclaringModule.TypeSystem.Void, inAttributeTypeReference);

			return new CustomAttribute(emptyCtor);
		}

        private int CompareAttributes(ICustomAttribute x, ICustomAttribute y)
        {
            string xName = x.AttributeType.Name;
            string yName = y.AttributeType.Name;

            if (xName != yName)
            {
                return xName.CompareTo(yName);
            }

            //case: same attribute applied with different parameters.
            if (x is CustomAttribute && y is CustomAttribute)
            {
                return (x as CustomAttribute).CompareToCustomAttribute(y as CustomAttribute);
            }

            if (x is SecurityAttribute && y is SecurityAttribute)
            {
                return CompareSecurityAttributes(x as SecurityAttribute, y as SecurityAttribute);
            }

            return 0;
        }
  
        private int CompareSecurityAttributes(SecurityAttribute first, SecurityAttribute second)
        {
            if (first == second)
            {
                return 0;
            }
            SecurityDeclaration firstDeclaration = securityAttributeToDeclaration[first];
            SecurityDeclaration secondDeclaration = securityAttributeToDeclaration[second];

            return first.CompareToSecurityAttribute(second, firstDeclaration, secondDeclaration);
        }

        public virtual void WriteAssemblyAttributes(AssemblyDefinition assembly, ICollection<string> attributesToIgnore = null)
        {
            List<ICustomAttribute> assemblyAttributes = new List<ICustomAttribute>();

            securityAttributeToDeclaration = new Dictionary<SecurityAttribute, SecurityDeclaration>();

			CustomAttribute assemblyVersionAttribute = AttributesUtilities.GetAssemblyVersionAttribute(assembly); 
			assemblyAttributes.Add(assemblyVersionAttribute);

            foreach (CustomAttribute attribute in assembly.CustomAttributes)
            {
                attribute.Resolve();
                assemblyAttributes.Add(attribute);
            }

            if (assembly.HasSecurityDeclarations)
            {
                foreach (SecurityDeclaration declaration in assembly.SecurityDeclarations)
                {
                    assemblyAttributes.AddRange(GetSecurityDeclaration(declaration));
                }
            }

            if (assembly.MainModule.HasExportedTypes)
            {
                foreach (ExportedType exportedType in assembly.MainModule.ExportedTypes)
                {
					if (!(exportedType.Scope is ModuleReference))
					{
						assemblyAttributes.Add(AttributesUtilities.GetExportedTypeAttribute(exportedType, assembly.MainModule));
					}
                }
            }

            assemblyAttributes.Sort((x,y) => CompareAttributes(x,y));

            foreach (ICustomAttribute attr in assemblyAttributes)
            {
				if (attributesToIgnore != null)
				{
					if (attributesToIgnore.Contains(attr.AttributeType.FullName))
					{
						continue;
					}
				}
                if (attr is CustomAttribute)
                {
                    WriteAssemblyAttribute(attr as CustomAttribute);
                }
                else if (attr is SecurityAttribute)
                {
                    bool b;
					WriteSecurityAttribute(assembly.MainModule, true, attr as SecurityAttribute, securityAttributeToDeclaration[attr as SecurityAttribute], out b);
                }
            }

			//if (writeUsings)
			//{
			//    genericWriter.WriteUsings();
			//}

			//WriteModuleAttributes(assembly.MainModule, analysisData, attributesToIgnore);
        }

        protected virtual IEnumerable<ICustomAttribute> WritePropertiesAsAttributes(IMemberDefinition member)
        {
            List<ICustomAttribute> attributes = new List<ICustomAttribute>();
            if (member is FieldDefinition)
            {
                attributes.AddRange(WriteFieldDefinitionFieldsAsAttributes(member as FieldDefinition));
            } 
            else if (member is TypeDefinition)
            {
                attributes.AddRange(WriteTypeDefinitionFieldsAsAttributes(member as TypeDefinition));
            }
            else if (member is MethodDefinition)
            {
                attributes.AddRange(WriteMethodFieldsAsAttributes(member as MethodDefinition));
            }
            return attributes;
        }

        private IEnumerable<ICustomAttribute> WriteFieldDefinitionFieldsAsAttributes(FieldDefinition member)
        {
            List<ICustomAttribute> attributes = new List<ICustomAttribute>();

            //write [NotSerialized] attribute
            if (member.IsNotSerialized)
            {
                attributes.Add(AttributesUtilities.GetFieldNotSerializedAttribute(member));
            }

            //write [FieldOffset(x)] attribute
            if (member.DeclaringType.IsExplicitLayout)
            {
                attributes.Add(AttributesUtilities.GetFieldFieldOffsetAttribute(member));
            }

            return attributes;
        }

        private IEnumerable<ICustomAttribute> WriteTypeDefinitionFieldsAsAttributes(TypeDefinition member)
        {
            List<ICustomAttribute> attributes = new List<ICustomAttribute>();

            //write [Serializable] attribute
            if (member.IsSerializable)
            {
                attributes.Add(AttributesUtilities.GetTypeSerializableAttribute(member));
            }

            //write [StructLayout(LayoutKind.Explicit)] attribute
            if (member.IsExplicitLayout)
            {
                attributes.Add(AttributesUtilities.GetTypeExplicitLayoutAttribute(member));
            }
            return attributes;
        }

        private IEnumerable<ICustomAttribute> WriteMethodFieldsAsAttributes(MethodDefinition method)
        {
            List<ICustomAttribute> attributes = new List<ICustomAttribute>();

            // write [DllImport(dllName,CharSet=CharSet.*)] attribute
            if (method.HasPInvokeInfo)
            {
                attributes.Add(AttributesUtilities.GetMethodDllImportAttribute(method));
            }

            if (method.HasImplAttributes &&
                AttributesUtilities.ShouldWriteMethodImplAttribute(method))
            {
                attributes.Add(AttributesUtilities.GetMethodImplAttribute(method));
            }

            return attributes;
        }

        public virtual List<ICustomAttribute> CollectSecurityAttributes(IMemberDefinition member)
        {
            List<ICustomAttribute> attributes = new List<ICustomAttribute>();
            if (member is ISecurityDeclarationMemberDefinition)
            {
                foreach (SecurityDeclaration securityDeclaration in (member as ISecurityDeclarationMemberDefinition).SecurityDeclarations)
                {
                    attributes.AddRange(GetSecurityDeclaration(securityDeclaration));
                }
            }
            return attributes;
        }

        private IEnumerable<ICustomAttribute> GetSecurityDeclaration(SecurityDeclaration securityDeclaration)
        {
            List<ICustomAttribute> attributes = new List<ICustomAttribute>();
            if (securityDeclaration.HasSecurityAttributes)
            {
                foreach (SecurityAttribute attribute in securityDeclaration.SecurityAttributes)
                {
                    attributes.Add(attribute);
                    securityAttributeToDeclaration.Add(attribute, securityDeclaration);
                }
            }
            return attributes;
        }

		private ModuleDefinition GetModuleDefinition(IMemberDefinition member)
		{
			if (member is TypeDefinition)
			{
				return (member as TypeDefinition).Module;
			}
			else
			{
				return member.DeclaringType.Module;
			}
		}

        private bool WriteSecurityAttribute(ModuleDefinition module, bool isAssemblyDeclaration, SecurityAttribute attribute, SecurityDeclaration securityDeclaration, out bool wroteArgument, bool skipTheNewLine = false, bool isReturnValueAttribute = false)
        {
            genericWriter.WriteToken(OpeningBracket);
            if (isAssemblyDeclaration)
            {
                genericWriter.WriteKeyword(genericWriter.KeyWordWriter.Assembly);
                genericWriter.Write(":");
                genericWriter.WriteSpace();
            }
            else if (isReturnValueAttribute)
            {
                this.WriteReturnValueAttributeKeyword();
            }
            string attributeName = attribute.AttributeType.Name.EndsWith("Attribute") ? attribute.AttributeType.Name.Remove(attribute.AttributeType.Name.LastIndexOf("Attribute")) : attribute.AttributeType.Name;
			genericWriter.WriteNamespaceIfTypeInCollision(attribute.AttributeType);
            genericWriter.WriteReference(attributeName, attribute.AttributeType);

            genericWriter.WriteToken("(");

			TypeReference securityActionTypeReference = securityDeclaration.GetSecurityActionTypeReference(module);
			TypeDefinition argumentTypeDefinition = securityActionTypeReference.IsDefinition ? securityActionTypeReference as TypeDefinition : securityActionTypeReference.Resolve();
			if (argumentTypeDefinition != null && argumentTypeDefinition.IsEnum)
			{
				List<FieldDefinition> fields = EnumValueToFieldCombinationMatcher.GetEnumFieldDefinitionByValue(argumentTypeDefinition.Fields, (int)securityDeclaration.Action, argumentTypeDefinition.CustomAttributes);
				if (fields.Count != 0)
				{
					for (int i = 0; i < fields.Count; i++)
					{
						genericWriter.WriteReferenceAndNamespaceIfInCollision(fields[i].DeclaringType);
						genericWriter.WriteToken(".");
						genericWriter.WriteEnumValueField(fields[i]);

						if (i + 1 < fields.Count)
						{
							genericWriter.WriteSpace();
							genericWriter.WriteBitwiseOr();
							genericWriter.WriteSpace();
						}
					}
				}
				else
				{
					WriteSecurityAttributeAction(securityDeclaration.Action);
				}
			}

            wroteArgument = true;

            if (attribute.HasFields || attribute.HasProperties)
            {
                var attributeType = attribute.AttributeType.Resolve();
                if (attribute.HasProperties)
                {
                    wroteArgument = WriteAttributeNamedArgs(attributeType, attribute.Properties, false, wroteArgument);
                }
                if (attribute.HasFields)
                {
                    WriteAttributeNamedArgs(attributeType, attribute.Fields, true, wroteArgument);
                }
            }

            genericWriter.WriteToken(")");
            genericWriter.WriteToken(ClosingBracket);
            if (!skipTheNewLine)
            {
                genericWriter.WriteLine();
            }
            return wroteArgument;
        }

        private void WriteSecurityAttributeAction(SecurityAction action)
        {
			genericWriter.WriteSecurityDeclarationNamespaceIfNeeded();
            genericWriter.Write("SecurityAction");
            genericWriter.WriteToken(".");

            switch (action)
            {
                case SecurityAction.PreJitGrant:
                case SecurityAction.Assert:
                    genericWriter.Write("Assert");
                    break;
                case SecurityAction.NonCasDemand:
                case SecurityAction.Demand:
                    genericWriter.Write("Demand");
                    break;
                case SecurityAction.PreJitDeny:
                case SecurityAction.Deny:
                    genericWriter.Write("Deny");
                    break;
                case SecurityAction.NonCasInheritance:
                case SecurityAction.InheritDemand:
                    genericWriter.Write("InheritanceDemand");
                    break;
                case SecurityAction.NonCasLinkDemand:
                case SecurityAction.LinkDemand:
                    genericWriter.Write("LinkDemand");
                    break;
                case SecurityAction.PermitOnly:
                    genericWriter.Write("PermitOnly");
                    break;
                case SecurityAction.Request:
                    //TODO: Not clear what value of System.Security.Permissions.SecurityAction or other code construct makes C# produce this.
                    //Left empty on purpose so that we eventually get an error report with sample code
                    genericWriter.Write("");
                    break;
                case SecurityAction.RequestMinimum:
                    genericWriter.Write("RequestMinimum");
                    break;
                case SecurityAction.RequestOptional:
                    genericWriter.Write("RequestOptional");
                    break;
                case SecurityAction.RequestRefuse:
                    genericWriter.Write("RequestRefuse");
                    break;

            }
        }

		private void WriteAssemblyAttribute(CustomAttribute attribute)
		{ 
			WriteGlobalAttribute(attribute, genericWriter.KeyWordWriter.Assembly);
		}

        private void WriteGlobalAttribute(CustomAttribute attribute, string keyword)
        {
            if (attributesNotToShow.Contains(attribute.AttributeType.FullName))
            {
                return;
            }

            bool resolvingProblem = false;
            attribute.Resolve();
            genericWriter.WriteToken(OpeningBracket);

            genericWriter.WriteKeyword(keyword);
            genericWriter.Write(":");
			genericWriter.WriteSpace();

            resolvingProblem = WriteAttributeSignature(attribute, resolvingProblem);

            genericWriter.WriteToken(ClosingBracket);

            if (resolvingProblem)
            {
                genericWriter.Write("    ");
                string comment = genericWriter.Language.CommentLines(ASSEMBLYNOTRESOLVEDERROR);
                genericWriter.Write(comment.Remove(comment.Length - 2));
            }

            genericWriter.WriteLine();
        }

        protected void WriteAttribute(CustomAttribute attribute, bool skipNewLine = false, bool isReturnValueAtrribute = false)
        {
            if (attributesNotToShow.Contains(attribute.AttributeType.FullName))
            {
                return;
            }

            bool resolvingProblem = false;
            attribute.Resolve();
            genericWriter.WriteToken(OpeningBracket);

            if (isReturnValueAtrribute)
            {
                WriteReturnValueAttributeKeyword();
            }

            resolvingProblem = WriteAttributeSignature(attribute, resolvingProblem);

            genericWriter.WriteToken(ClosingBracket);

            if (resolvingProblem)
            {
                genericWriter.Write("    ");
                string comment = genericWriter.Language.CommentLines(ASSEMBLYNOTRESOLVEDERROR);
                genericWriter.Write(comment.Remove(comment.Length - 2));
            }

            if (!skipNewLine)
            {
                genericWriter.WriteLine();
            }
        }

        protected abstract void WriteReturnValueAttributeKeyword();

        private bool WriteAttributeSignature(CustomAttribute attribute, bool resolvingProblem)
        {
            //Removing the "Attribute" suffix if present
            string attributeName = attribute.AttributeType.Name.EndsWith("Attribute") ? attribute.AttributeType.Name.Remove(attribute.AttributeType.Name.LastIndexOf("Attribute")) : attribute.AttributeType.Name;

			if (genericWriter.Language.IsGlobalKeyword(attributeName))
			{
				// Return the "Attribute" suffix, if removing it makes the name match a global keyword.
				attributeName = attribute.AttributeType.Name;
			}

			genericWriter.WriteNamespaceIfTypeInCollision(attribute.AttributeType);
            genericWriter.WriteReference(attributeName, attribute.AttributeType);

            if (attribute.HasConstructorArguments || attribute.HasFields || attribute.HasProperties)
            {
                genericWriter.WriteToken("(");

                bool wroteArgument = false;

                for (int argIndex = 0; argIndex < attribute.ConstructorArguments.Count; argIndex++)
                {
                    wroteArgument = true;

                    WriteAttributeArgumentValue(attribute.ConstructorArguments[argIndex]);

                    if (argIndex + 1 < attribute.ConstructorArguments.Count)
                    {
                        genericWriter.Write(",");
                        genericWriter.WriteSpace();
                    }
                }

                if (attribute.HasProperties)
                {
                    TypeDefinition attributeType = attribute.AttributeType.Resolve();
                    wroteArgument = WriteAttributeNamedArgs(attributeType, attribute.Properties, false, wroteArgument);
                }
                if (attribute.HasFields)
                {
                    TypeDefinition attributeType = attribute.AttributeType.Resolve();
                    WriteAttributeNamedArgs(attributeType, attribute.Fields, true, wroteArgument);
                }

                genericWriter.WriteToken(")");
            }
            else if (attribute.IsResolved == false && attribute.GetBlob().Length > 4)
            {
                genericWriter.WriteToken("(");
                genericWriter.Write(",");
                genericWriter.WriteToken(")");
                resolvingProblem = true;
            }
            return resolvingProblem;
        }

        private bool WriteAttributeNamedArgs(TypeDefinition attributeType, Collection<CustomAttributeNamedArgument> namedArguments, bool fields, bool wroteArgument)
        {
            for (int propertyIndex = 0; propertyIndex < namedArguments.Count; propertyIndex++)
            {
                if (propertyIndex == 0 && wroteArgument)
                {
                    genericWriter.WriteToken(",");
                    genericWriter.WriteSpace();
                }

                if (attributeType == null)
                {
                    genericWriter.Write(namedArguments[propertyIndex].Name);
                }
                else
                {
                    IList fieldsOrProperties = fields ? (IList)attributeType.Fields : (IList)attributeType.Properties;

                    MemberReference memberArgmentRefersTo = null;
                    IList currentFieldsOrProperties = fieldsOrProperties;
                    TypeDefinition currentTypeInInheritanceChain = attributeType;
                    do
                    {
                        memberArgmentRefersTo = Utilities.FindMemberArgumentRefersTo(currentFieldsOrProperties, namedArguments[propertyIndex]);


                        if (currentTypeInInheritanceChain.BaseType != null)
                        {
                            currentTypeInInheritanceChain = currentTypeInInheritanceChain.BaseType.Resolve();
                            if (currentTypeInInheritanceChain == null)
                            {
                                break;
                            }
                            currentFieldsOrProperties = fields ? (IList)currentTypeInInheritanceChain.Fields : (IList)currentTypeInInheritanceChain.Properties;
                        }
                        else
                        {
                            break;
                        }
                    }
                    while (memberArgmentRefersTo == null);

                    if (memberArgmentRefersTo != null)
                    {
                        genericWriter.WriteReference(memberArgmentRefersTo.Name, memberArgmentRefersTo);
                    }
                    else //Happens in Silverlight mscorlib 
                    {
                        genericWriter.Write(namedArguments[propertyIndex].Name);
                    }
                }

                genericWriter.WriteToken(EqualsSign);
                WriteAttributeArgumentValue(namedArguments[propertyIndex].Argument);

                if (propertyIndex + 1 < namedArguments.Count)
                {
                    genericWriter.WriteToken(",");
                    genericWriter.WriteSpace();
                }

                wroteArgument = true;
            }
            return wroteArgument;
        }

        private void WriteAttributeArgumentValue(CustomAttributeArgument argument)
        {
            if (argument.Value is CustomAttributeArgument)
            {
                WriteAttributeArgumentValue((CustomAttributeArgument)argument.Value);
                return;
            }
            else if (argument.Value is CustomAttributeArgument[])
            {
                WriteAttributeArgumentArray(argument);
                return;
            }

            TypeDefinition argumentTypeDefinition = argument.Type.IsDefinition ? argument.Type as TypeDefinition : argument.Type.Resolve();
            if (argumentTypeDefinition != null && argumentTypeDefinition.IsEnum)
            {
                List<FieldDefinition> fields = EnumValueToFieldCombinationMatcher.GetEnumFieldDefinitionByValue(argumentTypeDefinition.Fields, argument.Value, argumentTypeDefinition.CustomAttributes);
                if (fields.Count != 0)
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        genericWriter.WriteReferenceAndNamespaceIfInCollision(fields[i].DeclaringType);
                        genericWriter.WriteToken(".");
                        genericWriter.WriteEnumValueField(fields[i]);

                        if (i + 1 < fields.Count)
                        {
                            genericWriter.WriteSpace();
							genericWriter.WriteBitwiseOr();
                            genericWriter.WriteSpace();
                        }
                    }
                }
                else
                {
                    genericWriter.WriteLiteralInLanguageSyntax(argument.Value);
                }
            }
            else
            {
                if (argument.Value == null || argument.Type.Name != "Type" || argument.Type.Namespace != "System")
                {
                    genericWriter.WriteLiteralInLanguageSyntax(argument.Value);
                }
                else
                {
                    genericWriter.WriteKeyword(genericWriter.KeyWordWriter.TypeOf);
                    genericWriter.WriteToken("(");
                    genericWriter.WriteGenericReference(argument.Value as TypeReference);
                    genericWriter.WriteToken(")");
                }
            }
        }

        private void WriteAttributeArgumentArray(CustomAttributeArgument argument)
        {
            CustomAttributeArgument[] elements = argument.Value as CustomAttributeArgument[];

            genericWriter.WriteKeyword(genericWriter.KeyWordWriter.New);
            genericWriter.WriteSpace();

            string argumentTypeName = string.Format("{0}{1}{2}", GetElementTypeName(argument), genericWriter.IndexLeftBracket, genericWriter.IndexRightBracket);
			genericWriter.WriteNamespaceIfTypeInCollision(argument.Type);
            genericWriter.WriteReference(argumentTypeName, argument.Type);

            genericWriter.WriteSpace();

            genericWriter.WriteToken("{");
            genericWriter.WriteSpace();
            for (int i = 0; i < elements.Length; i++)
            {
                WriteAttributeArgumentValue(elements[i]);
                if (i + 1 < elements.Length)
                {
                    genericWriter.WriteToken(",");
                    genericWriter.WriteSpace();
                }
            }
            genericWriter.WriteSpace();
            genericWriter.WriteToken("}");
        }

        private string GetElementTypeName(CustomAttributeArgument argument)
        {
            return genericWriter.ToEscapedTypeString((argument.Type as ArrayType).ElementType);
        }

        protected abstract string OpeningBracket { get; }
        protected abstract string ClosingBracket { get; }
        protected abstract string EqualsSign { get; }
        protected abstract string ParameterAttributeSeparator { get; }

		public void WriteModuleAttributes(ModuleDefinition module, ICollection<string> attributesToIgnore = null)
		{
			List<CustomAttribute> moduleAttributes = new List<CustomAttribute>();

			foreach (CustomAttribute attribute in module.CustomAttributes)
			{
				attribute.Resolve();
				moduleAttributes.Add(attribute);
			}

			moduleAttributes.Sort((x, y) => CompareAttributes(x, y));

			foreach (CustomAttribute attr in moduleAttributes)
			{
				if (attributesToIgnore != null)
				{
					if (attributesToIgnore.Contains(attr.AttributeType.FullName))
					{
						continue;
					}
				}
				WriteModuleAttribute(attr);
			}

			//if (writeUsings)
			//{
			//    genericWriter.WriteUsings();
			//}
		}
  
		private void WriteModuleAttribute(CustomAttribute attr)
		{
			WriteGlobalAttribute(attr, genericWriter.KeyWordWriter.Module);
		}
	}
}