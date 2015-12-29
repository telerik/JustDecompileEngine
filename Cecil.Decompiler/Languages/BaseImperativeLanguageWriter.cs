#region license
//
//	(C) 2007 - 2008 Novell, Inc. http://www.novell.com
//	(C) 2007 - 2008 Jb Evain http://evain.net
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
#endregion
using System.Collections;
using System.Text;
using Mono.Cecil;
using Mono.Collections.Generic;
using Telerik.JustDecompiler.Ast;
using System;
using Mono.Cecil.Extensions;
using Telerik.JustDecompiler.Ast.Expressions;
using Telerik.JustDecompiler.Decompiler;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using Telerik.JustDecompiler.Ast.Statements;
using System.Linq;
using Telerik.JustDecompiler.Steps;

namespace Telerik.JustDecompiler.Languages
{
	public abstract class BaseImperativeLanguageWriter : BaseLanguageWriter
	{
		private AttributeWriter attributeWriter;

		private readonly Stack<bool> shouldOmitSemicolon = new Stack<bool>();

		private readonly Stack<MethodReference> methodReferences = new Stack<MethodReference>();

		protected Stack<MethodReference> MethodReferences
		{
			get
			{
				return methodReferences;
			}
		}

		protected Stack<bool> ShouldOmitSemicolon
		{
			get
			{
				return shouldOmitSemicolon;
			}
		}

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

		public IKeyWordWriter KeyWordWriter { get; private set; }

		public BaseImperativeLanguageWriter(ILanguage language, IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
			: base(language, formatter, exceptionFormatter, writeExceptionsAsComments)
		{
			this.KeyWordWriter = CreateKeyWordWriter();
		}

		protected override void WriteAttributes(IMemberDefinition member, IEnumerable<string> ignoredAttributes = null)
		{
			MethodDefinition methodDef = member as MethodDefinition;
			if (methodDef != null && methodDef.IsAsync())
			{
				string[] asyncAttributes = new string[] {
					"System.Diagnostics.DebuggerStepThroughAttribute",
					"System.Runtime.CompilerServices.AsyncStateMachineAttribute" };
				if (ignoredAttributes == null)
				{
					ignoredAttributes = new List<string>(asyncAttributes);
				}
				else
				{
					ignoredAttributes = new List<string>(ignoredAttributes);
					((List<string>)ignoredAttributes).AddRange(asyncAttributes);
				}
			}

			AttributeWriter.WriteMemberAttributesAndNewLine(member, ignoredAttributes, TypeContext.CurrentType == member && TypeContext.IsWinRTImplementation);
		}

		protected virtual void WriteTypeStaticKeywordAndSpace()
		{
			WriteKeyword(KeyWordWriter.Static);
			WriteSpace();
		}

		protected override string WriteTypeDeclaration(TypeDefinition type, bool isPartial = false)
		{
			string nonGenericName = GenericHelper.GetNonGenericName(type.Name);
			if (this.ModuleContext.RenamedMembers.Contains(type.MetadataToken.ToUInt32()))
			{
				WriteComment(nonGenericName);
				WriteLine();
			}

			string typeKeyword = string.Empty;
			WriteTypeVisiblity(type);
			WriteSpace();

			if (isPartial)
			{
				WriteKeyword(KeyWordWriter.Partial);
				WriteSpace();
			}

			if (type.IsEnum)
			{
				typeKeyword = KeyWordWriter.Enum;
			}
			else if (type.IsValueType)
			{
				typeKeyword = KeyWordWriter.Struct;
			}
			else
			{
				if (type.IsClass)
				{
					if (type.IsStaticClass)
					{
						WriteTypeStaticKeywordAndSpace();
					}
					if (type.IsSealed)
					{
						WriteKeyword(KeyWordWriter.SealedType);
						WriteSpace();
					}
					if (type.IsAbstract)
					{
						WriteKeyword(KeyWordWriter.AbstractType);
						WriteSpace();
					}
					typeKeyword = KeyWordWriter.Class;
				}
				if (type.IsInterface)
				{
					typeKeyword = KeyWordWriter.Interface;
				}
			}
			WriteKeyword(typeKeyword);
			WriteSpace();

			int startPosition = this.formatter.CurrentPosition;
			WriteGenericName(type);
			int endPosition = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[type] = new OffsetSpan(startPosition, endPosition);

			if (!type.IsDefaultEnum && type.IsEnum)
			{
				TypeReference defaultEnumType = type.Fields[0].FieldType;
				if (defaultEnumType.Name != "Int32")
				{
					WriteEnumBaseTypeInheritColon();
					WriteReferenceAndNamespaceIfInCollision(defaultEnumType);
				}
			}

			WriteTypeBaseTypesAndInterfaces(type, isPartial);
			if (type.HasGenericParameters)
			{
				PostWriteGenericParametersConstraints(type);
			}

			return typeKeyword;
		}

		protected virtual void WriteEnumBaseTypeInheritColon()
		{
			WriteBaseTypeInheritColon();
		}

		protected virtual void WriteTypeBaseTypesAndInterfaces(TypeDefinition type, bool isPartial)
		{
			bool baseTypeWritten = false;
			if (!type.IsEnum && !type.IsValueType)
			{
				baseTypeWritten = WriteTypeBaseTypes(type, isPartial);
			}
			if (!type.IsEnum)
			{
				WriteTypeInterfaces(type, isPartial, baseTypeWritten);
			}
		}

		protected virtual bool WriteTypeBaseTypes(TypeDefinition type, bool isPartial = false)
		{
			bool hasBaseType = false;
			if ((type.BaseType != null) && (type.BaseType.FullName != Constants.Object))
			{
				if (!isPartial || (IsImplemented(type, type.BaseType.Resolve())))
				{
					hasBaseType = true;
					WriteBaseTypeInheritColon();
					WriteReferenceAndNamespaceIfInCollision(type.BaseType);
				}
			}
			return hasBaseType;
		}

		protected abstract void WriteTypeInterfaces(TypeDefinition type, bool isPartial, bool baseTypeWritten);

		/// <summary>
		/// Checks if all anstract methods of <paramref name="baseType"/> are implemented in <paramref name="type"/> by user methods.
		/// </summary>
		/// <param name="type">The inheriting type.</param>
		/// <param name="baseType"> The base type.</param>
		/// <returns>Returns true, if all abstract methods of <paramref name="baseType"/> are implemented in <paramref name="type"/>.</returns>
		protected bool IsImplemented(TypeDefinition type, TypeDefinition baseType)
		{
			if (baseType == null || type == null)
			{
				return true;
			}
			if (!baseType.IsAbstract)
			{
				return true;
			}
			foreach (MethodDefinition method in baseType.Methods)
			{
				if (method.IsAbstract)
				{
					if (type.Methods.FirstOrDefault(x => method.HasSameSignatureWith(x)) == null)
					{
						return false;
					}
				}
			}
			return true;
		}

		protected void WriteInheritComma()
		{
			WriteToken(",");
			WriteSpace();
		}

		protected void WriteMemberVisibility(IVisibilityDefinition member)
		{
			if (member.IsPrivate)
				WriteKeyword(KeyWordWriter.Private);
			else if (member.IsPublic)
				WriteKeyword(KeyWordWriter.Public);
			else if (member.IsFamily)
				WriteKeyword(KeyWordWriter.Protected);
			else if (member.IsAssembly)
				WriteKeyword(KeyWordWriter.Internal);
			else if (member.IsFamilyOrAssembly || member.IsFamilyAndAssembly)
			{
				WriteKeyword(KeyWordWriter.Protected);
				WriteSpace();
				WriteKeyword(KeyWordWriter.Internal);
			}
			else if ((member is MethodDefinition && (member as MethodDefinition).IsCompilerControlled) || (member is FieldDefinition && (member as FieldDefinition).IsCompilerControlled))
			{
				WriteComment("privatescope");
				WriteLine();
				WriteKeyword(KeyWordWriter.Internal);
			}
			else
				throw new NotSupportedException();
		}

		protected void WriteTypeVisiblity(TypeDefinition type)
		{
            if (TypeContext.IsWinRTImplementation)
            {
                WriteKeyword(KeyWordWriter.Public);
                return;
            }

			if (type.IsPublic)
				WriteKeyword(KeyWordWriter.Public);
			else if (type.IsNestedPublic)
				WriteKeyword(KeyWordWriter.Public);
			else if (type.IsNestedFamily)
				WriteKeyword(KeyWordWriter.Protected);
			else if (type.IsNestedPrivate)
				WriteKeyword(KeyWordWriter.Private);
			else if (type.IsNestedAssembly)
				WriteKeyword(KeyWordWriter.Internal);
			else if (type.IsNestedFamilyOrAssembly || type.IsNestedFamilyAndAssembly)
			{
				WriteKeyword(KeyWordWriter.Protected);
				WriteSpace();
				WriteKeyword(KeyWordWriter.Internal);
			}
			else if (type.IsNotPublic)
				WriteKeyword(KeyWordWriter.Internal);
			else
				throw new NotSupportedException();
		}

		protected void WriteMoreRestrictiveMethodVisibility(MethodDefinition currentMethod, MethodDefinition otherMethod)
		{
			if (otherMethod == null)
				return;

			if (currentMethod.IsPrivate)
			{
				if (!otherMethod.IsPrivate)
				{
					WriteMethodVisibilityAndSpace(currentMethod);
				}
			}
			if (currentMethod.IsFamily || currentMethod.IsAssembly)
			{
				if (otherMethod.IsPublic || otherMethod.IsFamilyOrAssembly)
				{
					WriteMethodVisibilityAndSpace(currentMethod);
				}
			}
			if (currentMethod.IsFamilyOrAssembly)
			{
				if (otherMethod.IsPublic)
				{
					WriteMethodVisibilityAndSpace(currentMethod);
				}
			}
		}

		private string GetCollidingTypeName(TypeReference typeReference)
		{
			return typeReference.Namespace == "System" ? ToTypeString(typeReference) : typeReference.Name;
		}

		private TypeReference GetCollidingType(TypeReference typeReference)
		{
			if (typeReference is TypeSpecification)
			{
				TypeSpecification typeSpecification = typeReference as TypeSpecification;

				PointerType pointer = typeSpecification as PointerType;
				if (pointer != null)
				{
					return typeSpecification.ElementType;
				}

				PinnedType pinnedType = typeSpecification as PinnedType;
				if (pinnedType != null)
				{
					if (typeSpecification.ElementType is ByReferenceType)
					{
						return ((typeSpecification.ElementType) as ByReferenceType).ElementType;
					}
					else
					{
						return typeSpecification.ElementType;
					}
				}

				ByReferenceType reference = typeSpecification as ByReferenceType;
				if (reference != null)
				{
					return typeSpecification.ElementType;
				}

				ArrayType array = typeSpecification as ArrayType;
				if (array != null)
				{
					TypeReference elementType = typeSpecification.ElementType;
					while (elementType is ArrayType)
					{
						ArrayType ar = (elementType as ArrayType);
						elementType = ar.ElementType;
					}

					return elementType;
				}

				GenericInstanceType generic = typeSpecification as GenericInstanceType;
				if (generic != null)
				{
					if (SupportsSpecialNullable && (generic.GetFriendlyFullName(Language).IndexOf("System.Nullable<") == 0) &&
						generic.GenericArguments[0].IsValueType)
					{
						TypeReference toWrite = generic.GenericArguments[0];
						if (generic.PostionToArgument.ContainsKey(0))
						{
							toWrite = generic.PostionToArgument[0];
						}
						return toWrite;
					}
					else
					{
						return generic;
					}
				}

				return typeSpecification;
			}

			return typeReference;
		}

		public virtual void WriteNamespaceIfTypeInCollision(TypeReference reference)
		{
			if ((reference.IsPrimitive && (reference.MetadataType != MetadataType.IntPtr && reference.MetadataType != MetadataType.UIntPtr)) ||
				reference.MetadataType == MetadataType.String ||
				reference.MetadataType == MetadataType.Object)
			{
				return;
			}

			TypeReference outerMostDeclaringType = GetCollidingType(reference);
			while (outerMostDeclaringType.DeclaringType != null)
			{
				outerMostDeclaringType = GetCollidingType(outerMostDeclaringType.DeclaringType);
			}

			string typeName = GetCollidingTypeName(outerMostDeclaringType);

			if (IsTypeNameInCollision(typeName))
			{
				WriteNamespace(outerMostDeclaringType, true);
			}
		}

		public virtual void WriteReferenceAndNamespaceIfInCollision(TypeReference reference)
		{
			WriteNamespaceIfTypeInCollision(reference);
			WriteReference(reference);
		}

		private void WriteReference(TypeReference reference)
		{
			if (reference.DeclaringType != null)
			{
				TypeReference declaringType = reference.DeclaringType;
				if (reference.IsGenericInstance)
				{
					GenericInstanceType referenceGeneric = reference as GenericInstanceType;
					if (declaringType.HasGenericParameters)
					{
						/// Transfer the parameters from reference up to the declaring type.
						/// Bare in mind, that the declaring type might have less generic parameters.
						/// Transfer just the first X that match.
						/// This is needed, because VB and C# don't allow other language syntax

						GenericInstanceType declaringTypeInstance = new GenericInstanceType(declaringType);

						Collection<TypeReference> nestedTypeBackup = new Collection<TypeReference>(referenceGeneric.GenericArguments);
						Collection<TypeReference> declaringBackup = new Collection<TypeReference>(declaringTypeInstance.GenericArguments);
						int parametersToMoveUp = declaringType.GenericParameters.Count;
						for (int i = 0; i < parametersToMoveUp; i++)
						{
							/// check if it moves the parameters forward or not
							declaringTypeInstance.AddGenericArgument(referenceGeneric.GenericArguments[i]);
							declaringTypeInstance.GenericArguments.Add(referenceGeneric.GenericArguments[i]);
						}
						WriteReference(declaringTypeInstance);
						Write(".");
						if (referenceGeneric.GenericArguments.Count - parametersToMoveUp > 0)
						{
							WriteTypeSpecification(referenceGeneric, parametersToMoveUp);
						}
						else
						{
							string typeName = GetTypeName(reference);
							WriteReference(typeName, reference);
						}
						referenceGeneric.GenericArguments.Clear();
						referenceGeneric.GenericArguments.AddRange(nestedTypeBackup);

						declaringTypeInstance.GenericArguments.Clear();
						declaringTypeInstance.GenericArguments.AddRange(declaringBackup);
						return;
					}
					else
					{
						WriteReference(declaringType);
						Write(".");
					}
				}
				else
				{
					WriteReference(declaringType);
					Write(".");
				}
			}

			if (reference is TypeSpecification)
			{
				WriteTypeSpecification(reference as TypeSpecification);
				return;
			}

			string typeString;
			if (reference.Namespace != "System")
			{
				typeString = GetTypeName(reference);
			}
			else
			{
				typeString = ToTypeString(reference);

				if (reference.Scope.Name != "mscorlib" && reference.Scope.Name != "CommonLanguageRuntimeLibrary")
				{
					typeString = Utilities.EscapeTypeName(typeString, this.Language);
				}
			}

			if (reference.HasGenericParameters || (!Language.IsEscapedWord(typeString) && !Language.IsValidIdentifier(typeString)))
			{
				if (reference is TypeDefinition && reference.HasGenericParameters)
				{
					typeString = GetTypeName(reference);

					int genericParametersStartIndex = 0;
					if (reference.DeclaringType != null && reference.DeclaringType.HasGenericParameters)
					{
						genericParametersStartIndex = reference.DeclaringType.GenericParameters.Count;
					}

					if (genericParametersStartIndex < reference.GenericParameters.Count)
					{
						typeString += GenericLeftBracket;
						for (int i = genericParametersStartIndex; i < reference.GenericParameters.Count; i++)
						{
							if (i > genericParametersStartIndex)
							{
								typeString += ", ";
							}
							if (!Language.IsValidIdentifier(reference.GenericParameters[i].Name))
							{
								typeString += Language.ReplaceInvalidCharactersInIdentifier(reference.GenericParameters[i].Name);
							}
							else
							{
								typeString += reference.GenericParameters[i].Name;
							}
						}

						typeString += GenericRightBracket;
						// check if the type name is actually correct
					}
				}
				else
				{
					typeString = GetTypeName(reference);
					//typeString = Language.ReplaceInvalidCharactersInIdentifier(typeString);
				}
			}

			if (reference.IsGenericParameter)
			{
				WriteReference(typeString, null);
			}
			else
			{
				WriteReference(typeString, reference);
			}
		}

		protected void LeaveMethodInvocation()
		{
			methodReferences.Pop();
		}

		protected void EnterMethodInvocation(MethodReference methodReference)
		{
			methodReferences.Push(methodReference);
		}

		protected virtual void WriteMethodReturnType(MethodDefinition method)
		{
			WriteReferenceAndNamespaceIfInCollision(method.ReturnType);
		}

		protected void WriteMethodVisibilityAndSpace(MethodDefinition currentMethod)
		{
			if (WriteMethodVisibility(currentMethod))
			{
				WriteSpace();
			}
		}

		protected virtual bool WriteMethodVisibility(MethodDefinition method)
		{
			if (method.DeclaringType.IsInterface)
			{
				return false;
			}

			WriteMemberVisibility(method);
			return true;
		}

		protected override void WriteTypeOpeningBlock(TypeDefinition type)
		{
			WriteLine();
			Indent();
		}

		protected override void WriteBodyInternal(IMemberDefinition member)
		{
			membersStack.Push(member);
			if (member is MethodDefinition)
			{
				MethodDefinition method = (MethodDefinition)member;
				if (method.Body == null)
				{
					WriteEndOfStatement();
				}
				else
				{
					Statement methodBody = GetStatement(method);

					if (MethodContext.Method.IsConstructor && !MethodContext.Method.IsStatic && MethodContext.CtorInvokeExpression != null && !RemoveBaseConstructorInvocation)
					{
						WriteBaseConstructorInvokation(MethodContext.CtorInvokeExpression);
					}

					Write(methodBody);
				}
			}
			else if (member is PropertyDefinition)
			{
				PropertyDefinition property = (PropertyDefinition)member;
				WritePropertyMethods(property);
			}
			else if (member is EventDefinition)
			{
				EventDefinition @event = (EventDefinition)member;
				if (@event.AddMethod != null && @event.AddMethod.Body == null || @event.RemoveMethod != null && @event.RemoveMethod.Body == null)
				{
					return;
				}
				WriteEventMethods(@event);
			}
			membersStack.Pop();
		}

		protected override void Write(FieldDefinition field)
		{
			if (TryWriteEnumField(field))
			{
				return;
			}
			WriteFieldDeclaration(field);
			if (TypeContext.AssignmentData.ContainsKey(field.FullName) &&
				TypeContext.AssignmentData[field.FullName] != null)
			{
				WriteSpace();
				WriteToken("=");
				WriteSpace();
				Visit(TypeContext.AssignmentData[field.FullName].AssignmentExpression);
			}
			WriteEndOfStatement();
		}

		protected virtual string GetOriginalFieldName(FieldDefinition field)
		{
			return this.TypeContext.BackingFieldToNameMap.ContainsKey(field) ? TypeContext.BackingFieldToNameMap[field] : field.Name;
		}

		protected virtual void WriteFieldDeclaration(FieldDefinition field)
		{
			if (!this.TypeContext.BackingFieldToNameMap.ContainsKey(field) && this.ModuleContext.RenamedMembers.Contains(field.MetadataToken.ToUInt32()))
			{
				string originalFieldName = GetOriginalFieldName(field);
				WriteComment(originalFieldName);
				WriteLine();
			}

			WriteMemberVisibility(field);
			WriteSpace();
			if (field.IsInitOnly)
			{
				WriteKeyword(KeyWordWriter.ReadOnly);
				WriteSpace();
			}
			if (field.IsLiteral)
			{
				WriteKeyword(KeyWordWriter.Const);
				WriteSpace();
			}
			else if (field.IsStatic)
			{
				WriteKeyword(KeyWordWriter.Static);
				WriteSpace();
			}

			WriteFieldTypeAndName(field);

			if (field.HasConstant)
			{
				WriteSpace();
				WriteToken("=");
				WriteSpace();
				TypeDefinition fieldType = field.FieldType.Resolve();
				if (fieldType != null && fieldType.IsEnum)
				{
					LiteralExpression fieldConstant = new LiteralExpression(field.Constant.Value, field.DeclaringType.Module.TypeSystem, null);
					Expression constant = EnumHelper.GetEnumExpression(fieldType, fieldConstant, field.DeclaringType.Module.TypeSystem);
					Write(constant);
				}
				else
				{
					WriteLiteralInLanguageSyntax(field.Constant.Value);
				}
			}
		}

		protected virtual void WriteFieldTypeAndName(FieldDefinition field)
		{
			string fieldName = GetFieldName(field);
			//if (!Language.IsValidIdentifier(fieldName))
			//{
			//	fieldName = Language.ReplaceInvalidCharactersInIdentifier(fieldName);
			//}
			WriteTypeAndName(field.FieldType, fieldName, field);
		}

		protected void WritePropertyMethods(PropertyDefinition property, bool inline = false)
		{
			bool isAutoImplemented = TypeContext.AutoImplementedProperties.Contains(property);

			if (property.GetMethod != null)
			{
				WriteGetMethod(property, isAutoImplemented);
			}

			if (property.SetMethod != null)
			{
				if (property.GetMethod != null)
				{
                    if (inline)
                    {
                        WriteSpace();
                    }
                    else
                    {
					WriteLine();
				}
				}

				WriteSetMethod(property, isAutoImplemented);
			}
		}

		private bool IsNull(Expression node)
		{
			return (node.CodeNodeType == CodeNodeType.LiteralExpression) && ((node as LiteralExpression).Value == null);
		}

		public override void VisitBinaryExpression(BinaryExpression node)
		{
			Visit(node.Left);

			WriteSpace();

			bool isOneSideNull = IsNull(node.Left) || IsNull(node.Right);
			string @operator = ToString(node.Operator, isOneSideNull);

			if (this.Language.IsOperatorKeyword(@operator))
			{
				WriteKeyword(@operator);
			}
			else
			{
				Write(@operator);
			}

			if (node.Right.CodeNodeType == CodeNodeType.InitializerExpression)
			{
				StartInitializer(node.Right as InitializerExpression);
			}
			else
			{
				WriteSpace();
			}

			Visit(node.Right);
		}

		public override void VisitUnaryExpression(UnaryExpression node)
		{
			if (node.Operator == UnaryOperator.AddressDereference)
			{
				VisitAddressDereferenceExpression(node);
			}
			else
			{
				Visit(node.Operand);
			}
		}

		protected override void WritePropertyDeclaration(PropertyDefinition property)
		{
			MethodDefinition moreVisibleMethod = property.GetMethod.GetMoreVisibleMethod(property.SetMethod);
			if (property.IsIndexer())
			{
				WriteIndexerKeywords();
			}

			if (this.ModuleContext.RenamedMembers.Contains(property.MetadataToken.ToUInt32()))
			{
				WriteComment(property.Name);
				WriteLine();
			}

			if (!property.IsExplicitImplementation())
			{
				WriteMethodVisibilityAndSpace(moreVisibleMethod);
			}

			if (!(property.IsVirtual() && !property.IsNewSlot()))
			{
				bool isIndexerProperty = property.IsIndexer();

				if ((isIndexerProperty && IsIndexerPropertyHiding(property)) || (!isIndexerProperty && IsPropertyHiding(property)))
				{
					WriteKeyword(KeyWordWriter.Hiding);
					WriteSpace();
				}
			}

			if (property.IsVirtual() && !property.DeclaringType.IsInterface)
			{
				if (WritePropertyKeywords(property))
				{
					WriteSpace();
				}
			}

			//covers the case of properties with only one of the get/set methods in VB
			WriteReadOnlyWriteOnlyProperty(property);

			if (property.IsStatic())
			{
				WriteKeyword(KeyWordWriter.Static);
				WriteSpace();
			}

			if (KeyWordWriter.Property != null)
			{
				WriteKeyword(KeyWordWriter.Property);
				WriteSpace();
			}

			if (property.IsIndexer())
			{
				if (WritePropertyAsIndexer(property))
				{
					return;
				}
			}

			WritePropertyTypeAndNameWithArguments(property);

			WritePropertyInterfaceImplementations(property);
		}

		protected virtual void WritePropertyInterfaceImplementations(PropertyDefinition property)
		{
		}

		private bool IsIndexerPropertyHiding(PropertyDefinition property)
		{
			MethodDefinition propertyMethod = property.GetMethod != null ? property.GetMethod : property.SetMethod;

			TypeDefinition baseType = property.DeclaringType.BaseType != null ? property.DeclaringType.BaseType.Resolve() : null;

			while (baseType != null)
			{
				foreach (PropertyDefinition baseTypeProperty in baseType.Properties)
				{
					if (!baseTypeProperty.IsPrivate() && baseTypeProperty.IsIndexer())
					{
						MethodDefinition baseTypePropertyMethod = baseTypeProperty.GetMethod != null ? baseTypeProperty.GetMethod : baseTypeProperty.SetMethod;

						if (baseTypePropertyMethod.Parameters.Count == propertyMethod.Parameters.Count)
						{
							bool methodsParamsEqual = true;

							for (int i = 0; i < baseTypePropertyMethod.Parameters.Count; i++)
							{
								if (baseTypePropertyMethod.Parameters[i].ParameterType.FullName != propertyMethod.Parameters[i].ParameterType.FullName)
								{
									methodsParamsEqual = false;
									break;
								}
							}

							if (methodsParamsEqual)
							{
								return true;
							}
						}
					}
				}

				baseType = baseType.BaseType != null ? baseType.BaseType.Resolve() : null;
			}

			return false;
		}

		private bool IsPropertyHiding(PropertyDefinition property)
		{
			TypeDefinition baseType = property.DeclaringType.BaseType != null ? property.DeclaringType.BaseType.Resolve() : null;

			while (baseType != null)
			{
				foreach (PropertyDefinition baseTypeProperty in baseType.Properties)
				{
					if (!baseTypeProperty.IsPrivate() && baseTypeProperty.Name == property.Name)
					{
						return true;
					}
				}

				baseType = baseType.BaseType != null ? baseType.BaseType.Resolve() : null;
			}

			return false;
		}

		protected bool HasArguments(PropertyDefinition property)
		{
			MethodDefinition method;
			int autoAddedArguments = 0;
			if (property.GetMethod != null)
			{
				method = property.GetMethod;
			}
			else
			{
				method = property.SetMethod;
				autoAddedArguments = 1;
			}
			if (method == null)
			{
				return false;
			}
			return method.Parameters.Count > autoAddedArguments;
		}

		protected void WritePropertyParameters(PropertyDefinition property)
		{
			MethodDefinition method;
			int autoAddedArguments = 0;
			if (property.GetMethod != null)
			{
				method = property.GetMethod;
			}
			else
			{
				method = property.SetMethod;
				autoAddedArguments = 1;
			}
			if (method == null)
			{
				return;
			}
			for (int i = 0; i < method.Parameters.Count - autoAddedArguments; i++)
			{
				ParameterDefinition parameter = method.Parameters[i];

				if (i > 0)
				{
					WriteToken(",");
					WriteSpace();
				}

				TypeReference parameterType = parameter.ParameterType;

				if (parameter is ByReferenceType)
				{
					WriteOutOrRefKeyWord(parameter);
					WriteSpace();
					parameterType = (parameterType as TypeSpecification).ElementType;
				}
				else if (KeyWordWriter.ByVal != null)
				{
					WriteKeyword(KeyWordWriter.ByVal);
					WriteSpace();
				}

				string paramName = parameter.Name;
				if (MethodContext != null && MethodContext.Method == parameter.Method && MethodContext.ParameterDefinitionToNameMap.ContainsKey(parameter)) // this check should be removed once the context is maintained in correct state
				{
					paramName = MethodContext.ParameterDefinitionToNameMap[parameter];
				}

				WriteParameterTypeAndName(parameterType, paramName, parameter);
			}
		}

		protected abstract void WriteIndexerKeywords();

		protected abstract bool WritePropertyAsIndexer(PropertyDefinition property);

		protected virtual void WritePropertyTypeAndNameWithArguments(PropertyDefinition property)
		{
		}

		protected virtual void WriteReadOnlyWriteOnlyProperty(PropertyDefinition property)
		{
			return;
		}

		protected override void Write(Statement statement)
		{
			Visit(statement);
		}


		protected bool IsPostUnaryOperator(UnaryOperator op)
		{
			switch (op)
			{
				case UnaryOperator.PostIncrement:
				case UnaryOperator.PostDecrement:
					return true;
				default:
					return false;
			}
		}

		public override void Visit(ICodeNode node)
		{
			if (!isStopped)
			{
				WriteCodeNodeLabel(node);

				if (node != null &&
					node.CodeNodeType != CodeNodeType.EmptyStatement)
				{
					int beginStatement = formatter.CurrentPosition;
					base.Visit(node);
					int endStatement = formatter.CurrentPosition - 1;

					if (node != null)
					{
						this.currentWritingInfo.StatementPositions.Add(node, new OffsetSpan(beginStatement, endStatement));
					}
				}
				else
				{
					DoVisit(node);
				}
			}
		}

		protected void DoVisit(ICodeNode node)
		{
			base.Visit(node);
		}

		protected void WriteCodeNodeLabel(ICodeNode node)
		{
			if (node is Statement &&
				!(node.CodeNodeType == CodeNodeType.BlockStatement) /*the label goes before the first statement in a block*/)
			{
				Statement statement = node as Statement;
				WriteLabel((statement).Label);

				if (node.CodeNodeType != CodeNodeType.EmptyStatement && statement.Label != "") //Labeled statement. The statement should start on the next line after label
				{
					WriteLine();
				}
			}
		}

		protected abstract void WriteLabel(string label);

		protected override void Write(EventDefinition @event)
		{
			bool isAbstractEvent = @event.AddMethod != null && @event.AddMethod.Body == null || @event.RemoveMethod != null && @event.RemoveMethod.Body == null;
			bool isAutoGenerated = true;
			if (!isAbstractEvent)
			{
				//if (typeContext != null)
				//{
				//    isAutoGenerated = typeContext.FieldToEventMap.Values.Contains(@event);
				//}
				//else
				//{
				isAutoGenerated = TypeContext.AutoImplementedEvents.Contains(@event);
				//}
			}
			WriteEventDeclaration(@event, isAutoGenerated);
			if (!isAutoGenerated)
			{
				int startFoldingOffset = this.formatter.CurrentPosition;

				this.formatter.WriteStartBlock();
				WriteLine();
				WriteBlock(() =>
				{
					WriteEventMethods(@event);
					WriteLine();
				}
				, "");

				WriteSpecialEndBlock(KeyWordWriter.Event);

				this.currentWritingInfo.MemberDefinitionToFoldingPositionMap[@event] = new OffsetSpan(startFoldingOffset, formatter.CurrentPosition - 1);

				this.formatter.WriteEndBlock();
			}
			else  //if the event is abstract then isAutoGenerated will be true
			{
				WriteEndOfStatement();
			}
		}

		protected override void WriteEventDeclaration(EventDefinition @event)
		{
			WriteEventDeclaration(@event, true);
		}

		private void WriteEventDeclaration(EventDefinition @event, bool isAutoGenerated)
		{
			MethodDefinition moreVisibleMethod = @event.AddMethod.GetMoreVisibleMethod(@event.RemoveMethod);
			WriteMethodVisibilityAndSpace(moreVisibleMethod);

			if (moreVisibleMethod.IsStatic)
			{
				WriteKeyword(KeyWordWriter.Static);
				WriteSpace();
			}

			if (!isAutoGenerated && KeyWordWriter.Custom != null)
			{
				WriteKeyword(KeyWordWriter.Custom);
				WriteSpace();
			}

			if (@event.IsVirtual() && !@event.DeclaringType.IsInterface)
			{
				if (WriteEventKeywords(@event))
				{
					WriteSpace();
				}
			}

			WriteKeyword(KeyWordWriter.Event);
			WriteSpace();
			WriteEventTypeAndName(@event);
			WriteEventInterfaceImplementations(@event);
		}

		protected virtual void WriteEventTypeAndName(EventDefinition @event)
		{
			WriteTypeAndName(@event.EventType, @event.Name, @event);
		}

		protected virtual void WriteEventInterfaceImplementations(EventDefinition @event)
		{
		}

		protected override void Write(PropertyDefinition property)
		{
			if (property.ShouldStaySplit() && !MethodContextsMissing)
			{
				WriteSplitProperty(property);
				return;
			}

            if (this.TypeContext.AutoImplementedProperties.Contains(property) &&
                TypeContext.AssignmentData.ContainsKey(property.FullName) &&
                TypeContext.AssignmentData[property.FullName] != null)
            {
                WriteInitializedAutoProperty(property, TypeContext.AssignmentData[property.FullName].AssignmentExpression);
                return;
            }

			WritePropertyDeclaration(property);

			int startFoldingOffset = this.formatter.CurrentPosition;

			this.formatter.WriteStartBlock();

			WriteLine();

			WriteBlock(() =>
			{
				WritePropertyMethods(property);
				WriteLine();
			}
					   , "");

			if (KeyWordWriter.Property != null)
			{
				WriteSpecialEndBlock(KeyWordWriter.Property);
			}
			this.currentWritingInfo.MemberDefinitionToFoldingPositionMap[property] = new OffsetSpan(startFoldingOffset, formatter.CurrentPosition - 1);

			this.formatter.WriteEndBlock();
		}

		#region Split properties

		private void WriteSplitProperty(PropertyDefinition property)
		{
			#region PropertyDeclaration

			var method = property.GetMethod.GetMoreVisibleMethod(property.SetMethod);
			WriteMethodVisibilityAndSpace(method);

			if (KeyWordWriter.Property != null)
			{
				WriteKeyword(KeyWordWriter.Property);
				WriteSpace();
			}

			WritePropertyTypeAndNameWithArguments(property);

			WritePropertyInterfaceImplementations(property);

			#endregion

			WriteBeginBlock();
			WriteLine();
			Indent();
			if (property.GetMethod != null)
			{
				uint getMethodMetadataToken = property.GetMethod.MetadataToken.ToUInt32();

				int attributesStartIndex = this.formatter.CurrentPosition;
				AttributeWriter.WriteMemberAttributesAndNewLine(property.GetMethod, new string[1] { "System.Runtime.CompilerServices.CompilerGeneratedAttribute" });
				AddMemberAttributesOffsetSpan(getMethodMetadataToken, attributesStartIndex, this.formatter.CurrentPosition);

				membersStack.Push(property.GetMethod);

				int decompiledCodeStartIndex = this.formatter.CurrentPosition;
				WriteSplitPropertyGetter(property);
				this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(getMethodMetadataToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));

				membersStack.Pop();
				WriteLine();
			}
			if (property.SetMethod != null)
			{
				uint setMethodMetadataToken = property.SetMethod.MetadataToken.ToUInt32();

				int attributesStartIndex = this.formatter.CurrentPosition;
				AttributeWriter.WriteMemberAttributesAndNewLine(property.SetMethod, new string[1] { "System.Runtime.CompilerServices.CompilerGeneratedAttribute" });
				AddMemberAttributesOffsetSpan(setMethodMetadataToken, attributesStartIndex, this.formatter.CurrentPosition);

				membersStack.Push(property.SetMethod);

				int decompiledCodeStartIndex = this.formatter.CurrentPosition;
				WriteSplitPropertySetter(property);
				this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(setMethodMetadataToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));

				membersStack.Pop();
				WriteLine();
			}
			Outdent();
			WriteEndBlock("Property");
			//WriteLine();

			FieldDefinition backingField = Utilities.GetCompileGeneratedBackingField(property);
			if (backingField != null)
			{
				WriteLine();
				WriteLine();
				Write(backingField);
			}

			if (property.GetMethod != null)
			{
				WriteLine();
				WriteLine();
				Write(property.GetMethod);
			}

			if (property.SetMethod != null)
			{
				WriteLine();
				WriteLine();
				Write(property.SetMethod);
			}

			return;
		}

		private void WriteSplitPropertyGetter(PropertyDefinition property)
		{
			WriteKeyword(KeyWordWriter.Get);
			WriteBeginBlock();
			WriteLine();
			Indent();
			MethodReferenceExpression jdGetterReference = new MethodReferenceExpression(null, property.GetMethod, null);
			MethodInvocationExpression jdMethodInvocation = new MethodInvocationExpression(jdGetterReference, null);
			jdMethodInvocation.Arguments = CopyMethodParametersAsArguments(property.GetMethod);
			ReturnExpression returnEx = new ReturnExpression(jdMethodInvocation, null);
			ExpressionStatement toWrite = new ExpressionStatement(returnEx);
			Write(toWrite);
			WriteLine();
			Outdent();
			WriteEndBlock("Get");
		}

		private void WriteSplitPropertySetter(PropertyDefinition property)
		{
			WriteKeyword(KeyWordWriter.Set);
			WriteBeginBlock();
			WriteLine();
			Indent();
			MethodReferenceExpression jdSetterReference = new MethodReferenceExpression(null, property.SetMethod, null);
			MethodInvocationExpression jdMethodInvocation = new MethodInvocationExpression(jdSetterReference, null);
			jdMethodInvocation.Arguments = CopyMethodParametersAsArguments(property.SetMethod);
			ExpressionStatement toWrite = new ExpressionStatement(jdMethodInvocation);
			Write(toWrite);
			WriteLine();
			Outdent();
			WriteEndBlock("Set");
		}

        private void WriteInitializedAutoProperty(PropertyDefinition property, Expression assignment)
        {
            WritePropertyDeclaration(property);

            WriteBeginBlock(inline: true);
            WriteSpace();
            WritePropertyMethods(property, inline: true);
            WriteSpace();
            WriteEndBlock(property.Name);

            WriteSpace();
            WriteToken("=");
            WriteSpace();
            Visit(assignment);
            WriteEndOfStatement();
        }

		private ExpressionCollection CopyMethodParametersAsArguments(MethodDefinition method)
		{
			ExpressionCollection result = new ExpressionCollection();
			if (method.HasParameters)
			{
				foreach (ParameterDefinition param in method.Parameters)
				{
					result.Add(new ArgumentReferenceExpression(param, null));
				}
			}
			return result;
		}

		#endregion

		protected void WriteSetMethod(PropertyDefinition property, bool isAutoImplemented)
		{
			uint setMethodMetadataToken = property.SetMethod.MetadataToken.ToUInt32();

			membersStack.Push(property.SetMethod);

			int attributesStartIndex = this.formatter.CurrentPosition;
			AttributeWriter.WriteMemberAttributesAndNewLine(property.SetMethod, new string[1] { "System.Runtime.CompilerServices.CompilerGeneratedAttribute" });
			AddMemberAttributesOffsetSpan(setMethodMetadataToken, attributesStartIndex, this.formatter.CurrentPosition);

			int startPosition = this.formatter.CurrentPosition;
			int decompiledCodeStartIndex = this.formatter.CurrentPosition;

			WriteMoreRestrictiveMethodVisibility(property.SetMethod, property.GetMethod);

			int startIndex = this.formatter.CurrentPosition;
			WriteKeyword(KeyWordWriter.Set);
			int endIndex = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[property.SetMethod] = new OffsetSpan(startIndex, endIndex);

			if (KeyWordWriter.ByVal != null)
			{
				WriteToken("(");
				WriteKeyword(KeyWordWriter.ByVal);
				WriteSpace();
				WriteTypeAndName(property.PropertyType, "value", null);
				WriteToken(")");
			}
			if (property.SetMethod.Body == null || SupportsAutoProperties && isAutoImplemented)
			{
				WriteEndOfStatement();
			}
			else
			{
				WriteLine();

				Statement setterBody = GetStatement(property.SetMethod);

				Write(setterBody);
			}
			WriteSpecialEndBlock(KeyWordWriter.Set);

			this.currentWritingInfo.MemberDefinitionToFoldingPositionMap.Add(property.SetMethod, new OffsetSpan(startPosition, formatter.CurrentPosition - 1));
			this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(setMethodMetadataToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));

			membersStack.Pop();
		}

		protected void WriteGetMethod(PropertyDefinition property, bool isAutoImplemented)
		{
			uint getMethodMetadataToken = property.GetMethod.MetadataToken.ToUInt32();

			membersStack.Push(property.GetMethod);

			int attributesStartIndex = this.formatter.CurrentPosition;
			AttributeWriter.WriteMemberAttributesAndNewLine(property.GetMethod, new string[1] { "System.Runtime.CompilerServices.CompilerGeneratedAttribute" });
			AddMemberAttributesOffsetSpan(getMethodMetadataToken, attributesStartIndex, this.formatter.CurrentPosition);

			int startPosition = this.formatter.CurrentPosition;
			int decompiledCodeStartIndex = this.formatter.CurrentPosition;

			WriteMoreRestrictiveMethodVisibility(property.GetMethod, property.SetMethod);
			int startIndex = this.formatter.CurrentPosition;
			WriteKeyword(KeyWordWriter.Get);
			int endIndex = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[property.GetMethod] = new OffsetSpan(startIndex, endIndex);
			if (property.GetMethod.Body == null || SupportsAutoProperties && isAutoImplemented)
			{
				WriteEndOfStatement();
			}
			else
			{
				WriteLine();

				Statement getterBody = GetStatement(property.GetMethod);

				Write(getterBody);
			}
			WriteSpecialEndBlock(KeyWordWriter.Get);

			this.currentWritingInfo.MemberDefinitionToFoldingPositionMap.Add(property.GetMethod, new OffsetSpan(startPosition, formatter.CurrentPosition - 1));
			this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(getMethodMetadataToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));

			membersStack.Pop();
		}

		private string GetMethodOriginalName(MethodDefinition method)
		{
			return TypeContext.MethodDefinitionToNameMap.ContainsKey(method) ? TypeContext.MethodDefinitionToNameMap[method] : method.Name;
		}

		protected override void WriteMethodDeclaration(MethodDefinition method, bool writeDocumentation = false)
		{
			// handle explicit method overrides

			if (!method.IsConstructor && !method.HasOverrides && this.ModuleContext.RenamedMembers.Contains(method.MetadataToken.ToUInt32()))
			{
				// comment is not needed for the constructor - the comment is already put above the type declaration.
				// the constructor simply copies the type's name

				string methodName = GenericHelper.GetNonGenericName(GetMethodOriginalName(method));
				WriteComment(methodName);
				WriteLine();
			}

			if (method.DeclaringType.IsInterface)
			{
				WriteMethodName(method);
			}

			else
			{
				if (!method.IsStatic || !method.IsConstructor)
				{
					WriteMethodVisibilityAndSpace(method);
				}

				if (method.IsVirtual)
				{
					if (WriteMethodKeywords(method))
					{
						WriteSpace();
					}
				}

				if (method.IsStatic)
				{
					WriteKeyword(KeyWordWriter.Static);
					WriteSpace();
				}

				if (!method.IsConstructor && !(method.IsVirtual && !method.IsNewSlot) && IsMethodHiding(method))
				{
					WriteKeyword(KeyWordWriter.Hiding);
					WriteSpace();
				}

				WriteExternAndSpaceIfNecessary(method);

				if (method.IsUnsafe && KeyWordWriter.Unsafe != string.Empty)
				{
					WriteKeyword(KeyWordWriter.Unsafe);
					WriteSpace();
				}

				if (KeyWordWriter.Async != null && method.IsAsync())
				{
					WriteKeyword(KeyWordWriter.Async);
					WriteSpace();
				}

				if (!method.IsConstructor)
				{
					if (!TryWriteMethodAsOperator(method))
					{
						WriteMethodName(method);
					}
				}
				else
				{
					WriteConstructorName(method);
				}
			}

			WriteToken("(");
			WriteParameters(method);
			WriteToken(")");

			PostWriteMethodReturnType(method);

			if (method.HasGenericParameters)
			{
				PostWriteGenericParametersConstraints(method);
			}

			if (!method.IsGetter && !method.IsSetter)
			{
				WriteMethodInterfaceImplementations(method);
			}
		}

		private bool IsMethodHiding(MethodDefinition method)
		{
			TypeDefinition baseType = method.DeclaringType.BaseType != null ? method.DeclaringType.BaseType.Resolve() : null;

			while (baseType != null)
			{
				foreach (MethodDefinition baseTypeMethod in baseType.Methods)
				{
					if (!baseTypeMethod.IsPrivate && baseTypeMethod.Name == method.Name && baseTypeMethod.Parameters.Count == method.Parameters.Count)
					{
						bool methodsParamsEqual = true;

						for (int i = 0; i < baseTypeMethod.Parameters.Count; i++)
						{
							if (baseTypeMethod.Parameters[i].ParameterType.FullName != method.Parameters[i].ParameterType.FullName)
							{
								methodsParamsEqual = false;
								break;
							}
						}

						if (methodsParamsEqual)
						{
							return true;
						}
					}
				}

				baseType = baseType.BaseType != null ? baseType.BaseType.Resolve() : null;
			}

			return false;
		}

		protected virtual void PostWriteGenericParametersConstraints(IGenericDefinition generic)
		{
		}

		protected override void WriteDelegate(TypeDefinition delegateDefinition)
		{
			WriteTypeVisiblity(delegateDefinition);
			WriteSpace();
			WriteKeyword(KeyWordWriter.Delegate);
			WriteSpace();
			MethodDefinition method = delegateDefinition.Methods[0];
			for (int i = 1; i < delegateDefinition.Methods.Count && method.Name != "Invoke"; i++)
			{
				method = delegateDefinition.Methods[i];
			}
			if ((KeyWordWriter.Sub != null) &&
				(KeyWordWriter.Function != null))
			{
				WriteKeyword(GetMethodKeyWord(method));
				WriteSpace();
			}
			else
			{
				WriteMethodReturnType(method);
				WriteSpace();
			}

			WriteGenericName(delegateDefinition);
			WriteToken("(");
			WriteParameters(method);
			WriteToken(")");
			PostWriteMethodReturnType(method);
			if (delegateDefinition.HasGenericParameters)
			{
				PostWriteGenericParametersConstraints(delegateDefinition);
			}
			WriteEndOfStatement();
		}

		private bool TryWriteMethodAsOperator(MethodDefinition method)
		{
			if (!method.IsOperator)
			{
				return false;
			}

			string operatorName;
			if (!Language.TryGetOperatorName(method.OperatorName, out operatorName))
			{
				return false;
			}

			bool isCastOperator = false;
			if (method.OperatorName == "Implicit" || method.OperatorName == "Explicit")
			{
				if (method.OperatorName == "Implicit")
				{
					WriteKeyword(KeyWordWriter.Implicit);
				}
				else
				{
					WriteKeyword(KeyWordWriter.Explicit);
				}
				if (operatorName == "") //covers C# case
				{
					operatorName = method.ReturnType.GetGenericName(Language, GenericLeftBracket, GenericRightBracket);
				}
				WriteSpace();

				isCastOperator = true;
			}
			if (!isCastOperator)
			{
				if (KeyWordWriter.Sub == null) //if language is C#, not VB
				{
					WriteMethodReturnType(method);
					WriteSpace();
				}
			}
			WriteKeyword(KeyWordWriter.Operator);
			WriteSpace();
			int startIndex = this.formatter.CurrentPosition;
			WriteReference(operatorName, method);
			int endIndex = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[method] = new OffsetSpan(startIndex, endIndex);
			return true;
		}

		private void WriteMethodName(MethodDefinition method)
		{
			if ((KeyWordWriter.Sub != null) &&
				(KeyWordWriter.Function != null))
			{
				WriteKeyword(GetMethodKeyWord(method));
				WriteSpace();
			}
			else
			{
				WriteMethodReturnType(method);
				WriteSpace();
			}
			int startIndex = this.formatter.CurrentPosition;
			WriteGenericName(method);
			int endIndex = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[method] = new OffsetSpan(startIndex, endIndex);
		}

		protected string GetMethodKeyWord(MethodDefinition method)
		{
			if (IsOperator(method))
			{
				return KeyWordWriter.Operator;
			}

			if (method.IsFunction())
			{
				return KeyWordWriter.Function;
			}

			return KeyWordWriter.Sub;
		}

		private bool IsOperator(MethodDefinition method)
		{
			if (!method.IsOperator)
			{
				return false;
			}

			string operatorName;
			if (!this.Language.TryGetOperatorName(method.OperatorName, out operatorName))
			{
				return false;
			}

			return true;
		}

		private void WriteConstructorName(MethodDefinition method)
		{
			string constructorName = GetTypeName(method.DeclaringType);

			if (KeyWordWriter.Constructor != null)
			{
				if (KeyWordWriter.Sub != null)
				{
					WriteKeyword(KeyWordWriter.Sub);
					WriteSpace();
				}
				constructorName = KeyWordWriter.Constructor;
			}

			int startPosition = this.formatter.CurrentPosition;
			WriteReference(constructorName, method);
			int endPosition = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[method] = new OffsetSpan(startPosition, endPosition);
		}

		protected virtual void WriteBaseConstructorInvokation(MethodInvocationExpression baseConstructorInvokation)
		{
		}

		protected abstract void WriteDestructor(MethodDefinition method);

		protected override void Write(MethodDefinition method)
		{
			if (MethodContext != null && MethodContext.IsDestructor)
			{
				WriteDestructor(method);
			}
			else
			{
				WriteMethod(method);
			}
		}

		protected virtual void WriteEmptyMethodEndOfStatement(MethodDefinition method)
		{
			WriteEndOfStatement();
		}

		protected void WriteMethod(MethodDefinition method)
		{
			membersStack.Push(method);
			bool writedOpenBrecket = false;
			try
			{
                Statement statement = method.Body != null && (method.Body.Instructions.Count > 0 || method.IsJustDecompileGenerated) ? GetStatement(method) : null;

				WriteMethodDeclaration(method);

				if (method.Body == null)
				{
					WriteEmptyMethodEndOfStatement(method);
					membersStack.Pop();
					return;
				}

				// Empty block
				if (method.Body.Instructions.Count == 0 && !method.IsJustDecompileGenerated)
				{
					WriteBeginBlock();
					WriteLine();
					WriteEndBlock(GetMethodName(method));
					membersStack.Pop();
					return;
				}

				if (MethodContext != null && MethodContext.Method.IsConstructor && MethodContext.CtorInvokeExpression != null)
				{
					WriteBaseConstructorInvokation(MethodContext.CtorInvokeExpression);
				}
				bool isExtern = method.IsExtern();

				int startIndex = 0;

				if (!method.IsAbstract && !isExtern)
				{
					startIndex = this.formatter.CurrentPosition;

					this.formatter.WriteStartBlock();

					writedOpenBrecket = true;
				}
				WriteLine();

				Write(statement);

				if ((KeyWordWriter.Sub != null) && (KeyWordWriter.Function != null) && (KeyWordWriter.Operator != null))
				{
					var methodKeyWord = GetMethodKeyWord(method);
					WriteSpecialEndBlock(methodKeyWord);
				}
				if (!method.IsAbstract && !isExtern)
				{
					this.currentWritingInfo.MemberDefinitionToFoldingPositionMap[method] = new OffsetSpan(startIndex, formatter.CurrentPosition - 1);

					this.formatter.WriteEndBlock();
				}
			}
			catch (Exception)
			{
				if (writedOpenBrecket)
				{
					this.formatter.WriteEndBlock();
				}
				membersStack.Pop();
				throw;
			}
			membersStack.Pop();
		}

		private void WriteExternAndSpaceIfNecessary(MethodDefinition method)
		{
			bool isExtern = method.IsExtern();

			if (isExtern)
			{
				if (KeyWordWriter.Extern != null)
				{
					WriteKeyword(KeyWordWriter.Extern);
					WriteSpace();
				}
			}
		}

		private bool WriteMethodKeywords(MethodDefinition method)
		{
			if (!method.IsNewSlot)
			{
				if (method.IsFinal)
				{
					WriteKeyword(KeyWordWriter.SealedMethod);
					WriteSpace();
				}
				WriteKeyword(KeyWordWriter.Override);
				if (method.IsAbstract)
				{
					WriteSpace();
					WriteKeyword(KeyWordWriter.AbstractMember);
				}
				return true;
			}
			else if (method.IsAbstract)
			{
				WriteKeyword(KeyWordWriter.AbstractMember);
				return true;
			}
			else
			{
				if (!method.IsFinal && !method.DeclaringType.IsSealed)
				{
					WriteKeyword(KeyWordWriter.Virtual);
					return true;
				}
			}
			return false;
		}

		private bool WritePropertyKeywords(PropertyDefinition property)
		{
			if (!property.IsNewSlot())
			{
				if (property.IsFinal())
				{
					WriteKeyword(KeyWordWriter.SealedMethod);
					WriteSpace();
				}
				WriteKeyword(KeyWordWriter.Override);
				return true;
			}
			else if (property.IsAbstract())
			{
				WriteKeyword(KeyWordWriter.AbstractMember);
				return true;
			}
			else
			{
				if (!property.IsFinal() && !property.DeclaringType.IsSealed)
				{
					WriteKeyword(KeyWordWriter.Virtual);
					return true;
				}
			}
			return false;
		}

		private bool WriteEventKeywords(EventDefinition @event)
		{
			if (!@event.IsNewSlot())
			{
				if (@event.IsFinal())
				{
					WriteKeyword(KeyWordWriter.SealedMethod);
					WriteSpace();
				}
				WriteKeyword(KeyWordWriter.Override);
				return true;
			}
			else if (@event.IsAbstract())
			{
				WriteKeyword(KeyWordWriter.AbstractMember);
				return true;
			}
			else
			{
				if (!@event.IsFinal())
				{
					WriteKeyword(KeyWordWriter.Virtual);
					return true;
				}
			}
			return false;
		}

		protected string GetParameterName(ParameterDefinition parameter)
		{
			string paramName = parameter.Name;
			if (MethodContext != null && (MethodContext.Method.Body.Instructions.Count > 0 || MethodContext.Method.IsJustDecompileGenerated) && MethodContext.Method == parameter.Method) // this check should be removed once the context is maintained in correct state
			{
				if (!MethodContext.ParameterDefinitionToNameMap.TryGetValue(parameter, out paramName))
				{
					paramName = parameter.Name;
				}
			}

			return paramName;
		}

		protected virtual void WriteParameters(MethodDefinition method)
		{
			for (int i = 0; i < method.Parameters.Count; i++)
			{
				ParameterDefinition parameter = method.Parameters[i];

				if (i > 0)
				{
					WriteToken(",");
					WriteSpace();
				}

				if ((i == 0) && (KeyWordWriter.ExtensionThis != null) && method.IsExtensionMethod)
				{
					WriteKeyword(KeyWordWriter.ExtensionThis);
					WriteSpace();
				}

				int attributesWriten = AttributeWriter.WriteParameterAttributes(parameter, TypeContext.IsWinRTImplementation && TypeContext.CurrentType == method.DeclaringType);
				if (attributesWriten > 0)
				{
					WriteSpace();
				}

				if (parameter.IsOptional)
				{
					WriteOptional(parameter);
				}

				TypeReference paramenterType = parameter.ParameterType;

				if (paramenterType is ByReferenceType)
				{
					WriteOutOrRefKeyWord(parameter);
					WriteSpace();
					paramenterType = (paramenterType as TypeSpecification).ElementType;
				}
				else if (KeyWordWriter.ByVal != null)
				{
					WriteKeyword(KeyWordWriter.ByVal);
					WriteSpace();
				}

				string paramName = GetParameterName(parameter);
				WriteParameterTypeAndName(paramenterType, paramName, parameter);

				if (parameter.IsOptional)
				{
					WriteSpace();
					Write("=");
					WriteSpace();
					if (parameter.HasDefault)
					{
						WriteLiteralInLanguageSyntax(parameter.Constant.Value);
					}
					else
					{
						DefaultObjectExpression d = GetDefaultValueExpression(parameter);
						Write(d);
					}

				}
			}
		}

		private DefaultObjectExpression GetDefaultValueExpression(ParameterDefinition parameter)
		{
			TypeReference defaultedType = parameter.ParameterType;
			if (defaultedType.IsByReference)
			{
				defaultedType = (defaultedType as ByReferenceType).ElementType;
			}
			DefaultObjectExpression d = new DefaultObjectExpression(defaultedType, null);
			return d;
		}

		protected virtual void WriteOptional(ParameterDefinition parameter)
		{
		}

		protected virtual void WriteOutOrRefKeyWord(ParameterDefinition parameter)
		{
			if (parameter.IsOutParameter())
			{
				WriteKeyword(KeyWordWriter.Out);
			}
			else
			{
				WriteKeyword(KeyWordWriter.ByRef);
			}
		}

		protected override void Write(Expression expression)
		{
			Visit(expression);
		}

		public override void VisitBlockStatement(BlockStatement node)
		{
			WriteBlock(() =>
			{
				Visit(node.Statements);
				if (node.Statements.Count != 0 && HasNoEmptyStatements(node.Statements))
				{
					WriteLine();
				}
			}, node.Label);
		}

		private bool HasNoEmptyStatements(StatementCollection statements)
		{
			foreach (Statement statement in statements)
			{
				if (!(statement is EmptyStatement))
				{
					return true;
				}
			}
			return false;
		}

		public override void Visit(IEnumerable collection)
		{
			bool memberWritten = false;
			foreach (ICodeNode node in collection)
			{
				if (node.CodeNodeType != CodeNodeType.EmptyStatement ||
					(node is Statement && (node as Statement).Label != "")) //Labeled EmotyStatement should be accounted for. Newline needed after stastement written.
				{
					if (memberWritten)
					{
						WriteLine();
					}
					else
					{
						memberWritten = true;
					}
				}

				Visit(node);
			}
		}

		public override void VisitExpressionStatement(ExpressionStatement node)
		{
			Visit(node.Expression);
			if ((ShouldOmitSemicolon.Count == 0) || (!ShouldOmitSemicolon.Peek()))
			{
				WriteEndOfStatement();
			}
		}

		public override void VisitArgumentReferenceExpression(ArgumentReferenceExpression node)
		{
			Write(GetArgumentName(node.Parameter));
		}

		private MethodSpecificContext GetCurrentMethodContext()
		{
			if (this.membersStack.Peek() is FieldDefinition)
			{
				FieldDefinition field = this.membersStack.Peek() as FieldDefinition;
				return GetMethodContext(TypeContext.AssignmentData[field.FullName].AssignmentMethod);
			}
			else
			{
				return MethodContext;
			}
		}

		protected string GetVariableName(VariableReference variable)
		{
			VariableDefinition variableDefinition = variable.Resolve();

			string result;
			if (!GetCurrentMethodContext().VariableDefinitionToNameMap.TryGetValue(variableDefinition, out result))
			{
				result = variableDefinition.Name;
			}

			return result;
		}

		protected virtual string GetArgumentName(ParameterReference parameter)
		{
			ParameterDefinition parameterDefinition = parameter.Resolve();

			MethodSpecificContext referingMethodContext = GetCurrentMethodContext();

			string result;
			if (!referingMethodContext.ParameterDefinitionToNameMap.TryGetValue(parameterDefinition, out result))
			{
				result = parameterDefinition.Name;
			}

			if (!Language.IsValidIdentifier(result))
			{
				result = Language.ReplaceInvalidCharactersInIdentifier(result);
			}

			if (Language.IsGlobalKeyword(result))
			{
				result = Utilities.EscapeName(result, this.Language);
			}

			return result;
		}

		public override void VisitVariableReferenceExpression(VariableReferenceExpression node)
		{
			Write(GetVariableName(node.Variable));
		}

		public override void VisitLiteralExpression(LiteralExpression node)
		{
			WriteLiteralInLanguageSyntax(node.Value);
		}

		public void WriteLiteralInLanguageSyntax(object value)
		{
			if (value == null)
			{
				WriteKeyword(KeyWordWriter.Null);
				return;
			}

			switch (Type.GetTypeCode(value.GetType()))
			{
				case TypeCode.Boolean:
					WriteKeyword((bool)value ? KeyWordWriter.True : KeyWordWriter.False);
					return;
				case TypeCode.Char:
					WriteEscapeCharLiteral((char)value);
					return;
				case TypeCode.String:
					WriteLiteral("\"");
					WriteEscapeLiteral(value.ToString());
					WriteLiteral("\"");
					return;
				case TypeCode.Int16:
					short shortValue = (short)value;
					if ((shortValue >= 255) && Language.WriteLargeNumbersInHex)
					{
						WriteLiteral(HexValuePrefix + shortValue.ToString("X").ToLowerInvariant());
					}
					else
					{
						WriteLiteral(shortValue.ToString());
					}
					return;
				case TypeCode.Int32:
					int intValue = (int)value;
					if ((intValue >= 255) && Language.WriteLargeNumbersInHex)
					{
						WriteLiteral(HexValuePrefix + intValue.ToString("X").ToLowerInvariant());
					}
					else
					{
						WriteLiteral(intValue.ToString());
					}
					return;
				case TypeCode.UInt16:
					ushort ushortValue = (ushort)value;
					if ((ushortValue >= 255) && Language.WriteLargeNumbersInHex)
					{
						WriteLiteral(HexValuePrefix + ushortValue.ToString("X").ToLowerInvariant());
					}
					else
					{
						WriteLiteral(ushortValue.ToString());
					}
					return;
				case TypeCode.UInt32:
					uint uintValue = (uint)value;
					if ((uintValue >= 255) && Language.WriteLargeNumbersInHex)
					{
						WriteLiteral(HexValuePrefix + uintValue.ToString("X").ToLowerInvariant());
					}
					else
					{
						WriteLiteral(uintValue.ToString());
					}
					return;
				case TypeCode.Int64:
					long longValue = (long)value;
					if ((longValue >= 255) && Language.WriteLargeNumbersInHex)
					{
						WriteLiteral(HexValuePrefix + longValue.ToString("X").ToLowerInvariant() + "L");
					}
					else
					{
						WriteLiteral(longValue.ToString() + "L");
					}
					return;
				case TypeCode.UInt64:
					ulong ulongValue = (ulong)value;
					if ((ulongValue >= 255) && Language.WriteLargeNumbersInHex)
					{
						WriteLiteral(HexValuePrefix + ulongValue.ToString("X").ToLowerInvariant() + "L");
					}
					else
					{
						WriteLiteral(ulongValue.ToString() + "L");
					}
					return;
				case TypeCode.Double:
					WriteDoubleLiteral(value);
					return;
				case TypeCode.Single:
					WriteFloatLiteral(value);
					return;
				default:
					WriteLiteral(value.ToString());
					return;
			}
		}

		#region Double literals

		private const string DoublePositiveInfinityFieldSignature = "System.Double System.Double::PositiveInfinity";
		private const string DoubleNegativeInfinityFieldSignature = "System.Double System.Double::NegativeInfinity";
		private const string DoubleNanFieldSignature = "System.Double System.Double::NaN";
		private const string DoubleMaxValueFieldSignature = "System.Double System.Double::MaxValue";
		private const string DoubleMinValueFieldSignature = "System.Double System.Double::MinValue";
		private const string DoubleEpsilonFieldSignature = "System.Double System.Double::Epsilon";

		private void WriteDoubleLiteral(object value)
		{
			double doubleValue = (double)value;
			IMemberDefinition currentMember = null;
			if (membersStack.Count > 0)
			{
				currentMember = membersStack.Peek();
			}

			if (double.IsPositiveInfinity(doubleValue))
			{
				WriteDoubleConstantValue(currentMember, doubleValue, DoublePositiveInfinityFieldSignature);
			}
			else if (double.IsNegativeInfinity(doubleValue))
			{
				WriteDoubleConstantValue(currentMember, doubleValue, DoubleNegativeInfinityFieldSignature);
			}
			else if (double.IsNaN(doubleValue))
			{
				WriteDoubleConstantValue(currentMember, doubleValue, DoubleNanFieldSignature);
			}
			else if (double.MaxValue == doubleValue)
			{
				WriteDoubleConstantValue(currentMember, doubleValue, DoubleMaxValueFieldSignature);
			}
			else if (double.MinValue == doubleValue)
			{
				WriteDoubleConstantValue(currentMember, doubleValue, DoubleMinValueFieldSignature);
			}
			else
			{
				WriteLiteral(doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
		}

		private void WriteDoubleConstantValue(IMemberDefinition currentMember, double value, string fieldName)
		{
			TypeDefinition doubleType = null;
			if (currentMember != null)
			{
				if (currentMember is TypeDefinition)
				{
					doubleType = (currentMember as TypeDefinition).Module.TypeSystem.Double.Resolve();
				}
				else
				{
					doubleType = currentMember.DeclaringType.Module.TypeSystem.Double.Resolve();
				}
			}
			if (currentMember.FullName == fieldName)
			{
				if (double.IsInfinity(value) || double.IsNaN(value))
				{
					WriteSpecialDoubleConstants(value, currentMember);
				}
				else
				{
					WriteLiteral(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				}
				return;
			}
			FieldDefinition infinityField = null;
			if (doubleType != null)
			{
				infinityField = doubleType.Fields.First(x => x.FullName == fieldName);
				WriteReferenceAndNamespaceIfInCollision(doubleType);
				WriteToken(".");
			}
			if (infinityField != null)
			{
				WriteReference(infinityField.Name, infinityField);
			}
			else
			{
				WriteToken(fieldName.Substring(fieldName.IndexOf("::") + 2));
			}
		}

		private void WriteSpecialDoubleConstants(double value, IMemberDefinition currentMember)
		{
			if (double.IsNaN(value))
			{
				WriteDoubleNan(currentMember);
			}
			if (double.IsInfinity(value))
			{
				WriteDoubleInfinity(currentMember, value);
			}
		}

		private void WriteDoubleNan(IMemberDefinition nanMember)
		{
			FieldDefinition positiveInfinityField = nanMember.DeclaringType.Fields.First(x => x.FullName == DoublePositiveInfinityFieldSignature);
			WriteReference("PositiveInfinity", positiveInfinityField);
			WriteSpace();
			Write("/");
			WriteSpace();
			WriteReference("PositiveInfinity", positiveInfinityField);
		}

		private void WriteDoubleInfinity(IMemberDefinition infinityMember, double value)
		{
			FieldDefinition epsilonField = infinityMember.DeclaringType.Fields.First(x => x.FullName == DoubleEpsilonFieldSignature);
			if (double.IsPositiveInfinity(value))
			{
				WriteLiteral("1");
			}
			else
			{
				WriteLiteral("-1");
			}
			WriteSpace();
			Write("/");
			WriteSpace();
			WriteReference("Epsilon", epsilonField);
		}

		#endregion

		#region Float literals

		private const string FloatPositiveInfinityFieldSignature = "System.Single System.Single::PositiveInfinity";
		private const string FloatNegativeInfinityFieldSignature = "System.Single System.Single::NegativeInfinity";
		private const string FloatNanFieldSignature = "System.Single System.Single::NaN";
		private const string FloatMaxValueFieldSignature = "System.Single System.Single::MaxValue";
		private const string FloatMinValueFieldSignature = "System.Single System.Single::MinValue";
		private const string FloatEpsilonFieldSignature = "System.Single System.Single::Epsilon";
		private void WriteFloatLiteral(object value)
		{
			float floatValue = (float)value;
			IMemberDefinition currentMember = null;
			if (membersStack.Count > 0)
			{
				currentMember = membersStack.Peek();
			}
			if (float.IsPositiveInfinity(floatValue))
			{
				WriteFloatInfinityValue(currentMember, floatValue, FloatPositiveInfinityFieldSignature);
			}
			else if (float.IsNegativeInfinity(floatValue))
			{
				WriteFloatInfinityValue(currentMember, floatValue, FloatNegativeInfinityFieldSignature);
			}
			else if (float.MinValue == floatValue)
			{
				WriteFloatInfinityValue(currentMember, floatValue, FloatMinValueFieldSignature);
			}
			else if (float.MaxValue == floatValue)
			{
				WriteFloatInfinityValue(currentMember, floatValue, FloatMaxValueFieldSignature);
			}
			else if (float.IsNaN(floatValue))
			{
				WriteFloatInfinityValue(currentMember, floatValue, FloatNanFieldSignature);
			}
			else
			{
				WriteLiteral(floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + Language.FloatingLiteralsConstant);
			}
			return;
		}

		private void WriteFloatInfinityValue(IMemberDefinition currentMember, float value, string fieldName)
		{
			TypeDefinition floatType = null;
			if (currentMember != null)
			{
				if (currentMember is TypeDefinition)
				{
					floatType = (currentMember as TypeDefinition).Module.TypeSystem.Single.Resolve();
				}
				else
				{
					floatType = currentMember.DeclaringType.Module.TypeSystem.Single.Resolve();
				}
			}
			if (currentMember.FullName == fieldName)
			{
				if (float.IsInfinity(value) || float.IsNaN(value))
				{
					WriteSpecialFloatValue(value, currentMember);
				}
				else
				{
					WriteLiteral(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + Language.FloatingLiteralsConstant);
				}
				return;
			}
			FieldDefinition infinityField = null;
			if (floatType != null)
			{
				infinityField = floatType.Fields.First(x => x.FullName == fieldName);
				WriteReferenceAndNamespaceIfInCollision(floatType);
				WriteToken(".");
			}
			if (infinityField != null)
			{
				WriteReference(infinityField.Name, infinityField);
			}
			else
			{
				WriteToken(fieldName.Substring(fieldName.IndexOf("::") + 2));
			}
		}

		private void WriteSpecialFloatValue(float value, IMemberDefinition currentMember)
		{
			if (float.IsNaN(value))
			{
				WriteFloatNan(currentMember);
			}
			if (float.IsInfinity(value))
			{
				WriteFloatInfinity(currentMember, value);
			}
		}

		private void WriteFloatNan(IMemberDefinition nanMember)
		{
			FieldDefinition positiveInfinityField = nanMember.DeclaringType.Fields.First(x => x.FullName == FloatPositiveInfinityFieldSignature);
			WriteReference("PositiveInfinity", positiveInfinityField);
			WriteSpace();
			Write("/");
			WriteSpace();
			WriteReference("PositiveInfinity", positiveInfinityField);
		}

		private void WriteFloatInfinity(IMemberDefinition infinityMember, double value)
		{
			FieldDefinition epsilonField = infinityMember.DeclaringType.Fields.First(x => x.FullName == FloatEpsilonFieldSignature);
			if (double.IsPositiveInfinity(value))
			{
				WriteLiteral("1");
			}
			else
			{
				WriteLiteral("-1");
			}
			WriteSpace();
			Write("/");
			WriteSpace();
			WriteReference("Epsilon", epsilonField);
		}

		#endregion

		protected virtual void WriteEscapeCharLiteral(char p)
		{
		}

		private void WriteMethodTarget(Expression expression)
		{
			bool isComplexTarget = IsComplexTarget(expression);

			if (isComplexTarget)
			{
				WriteToken("(");
			}

			Visit(expression);

			if (isComplexTarget)
			{
				WriteToken(")");
			}

			WriteToken(".");
		}

		public override void VisitMethodInvocationExpression(MethodInvocationExpression node)
		{
			bool isExtentionMethod = false;

			if (node.MethodExpression.CodeNodeType == CodeNodeType.MethodReferenceExpression)
			{
				MethodReferenceExpression methodReference = node.MethodExpression;

				MethodReference method = methodReference.Method;

				if (method != null)
				{
					// performance : must be static and do not use method.Resolve that resolves the whole graph.
					if (!method.HasThis && methodReference.MethodDefinition != null)
					{
						isExtentionMethod = methodReference.MethodDefinition.IsExtensionMethod;
					}
				}
			}

			if (isExtentionMethod)
			{
				// TODO: This shouldn't happen but fact is it happens. Seems like some other issue that needs fixing.
				if (node.Arguments.Count > 0)
				{
					ProcessExtensionMethod(node.MethodExpression.Method);
					WriteMethodTarget(node.Arguments[0]);
				}
			}
			else
			{
				if (node.MethodExpression.Target != null)
				{
					WriteMethodTarget(node.MethodExpression.Target);
				}

				if (!node.MethodExpression.Method.HasThis)
				{
					if (node.MethodExpression.MethodDefinition != null && !node.MethodExpression.MethodDefinition.IsExtensionMethod || node.MethodExpression.MethodDefinition == null)
					{
						WriteReferenceAndNamespaceIfInCollision(node.MethodExpression.Method.DeclaringType);
						WriteToken(".");
					}
				}
			}

			WriteMethodReference(node.MethodExpression);
			//VisitMethodReferenceExpression(node.MethodExpression);

			bool indexProp = false;

			WriteToken(indexProp ? IndexLeftBracket : "(");
			if (node.MethodExpression is MethodReferenceExpression)
			{
				EnterMethodInvocation(node.MethodExpression.Method);
				if (!isExtentionMethod)
				{
					VisitMethodParameters(node.Arguments);
				}
				else
				{
					VisitExtensionMethodParameters(node.Arguments);
				}
				LeaveMethodInvocation();
			}
			else
			{
				VisitMethodParameters(node.Arguments);
			}
			WriteToken(indexProp ? IndexRightBracket : ")");
		}

		protected virtual void ProcessExtensionMethod(MethodReference extensionMethod)
		{
		}

		public override void VisitBlockExpression(BlockExpression node)
		{
			WriteToken("{ ");
			VisitList(node.Expressions);
			WriteToken(" }");
		}

		private void VisitExtensionMethodParameters(IList<Expression> list)
		{
			VisitMethodParametersInternal(list, true);
		}

		private void VisitMethodParametersInternal(IList<Expression> list, bool isExtensionMethod)
		{
			for (int i = isExtensionMethod ? 1 : 0; i < list.Count; i++)
			{
				if ((i > 0 && !isExtensionMethod) || (i > 1 && isExtensionMethod))
				{
					WriteToken(",");
					WriteSpace();
				}

				bool byRef = false;

				if (list[i].CodeNodeType == CodeNodeType.ArgumentReferenceExpression)
				{
					ArgumentReferenceExpression argument = list[i] as ArgumentReferenceExpression;
					if (argument.Parameter.ParameterType.IsByReference)
					{
						byRef = true;
					}
				}

				if ((list[i] is UnaryExpression && (list[i] as UnaryExpression).Operator == UnaryOperator.AddressReference) && (MethodReferences.Count > 0))
				{
					byRef = true;
				}

				if (byRef)
				{
					MethodDefinition methodDefinition = MethodReferences.Peek().Resolve();
					if (methodDefinition != null)
					{
						//TODO: Ask for the assembly location.
						if (ShouldWriteOutAndRefOnInvocation)
						{
							WriteOutOrRefKeyWord(methodDefinition.Parameters[i]);
							WriteSpace();
						}
					}
					else
					{
						if (ShouldWriteOutAndRefOnInvocation)
						{
							WriteKeyword(KeyWordWriter.ByRef);
							WriteSpace();
						}
					}
					if (list[i] is UnaryExpression)
					{
						Visit((list[i] as UnaryExpression).Operand);
						continue;
					}
				}

				Visit(list[i]);
			}
		}

		protected void VisitMethodParameters(IList<Expression> list)
		{
			VisitMethodParametersInternal(list, false);
		}

		protected void VisitList(IList<Expression> list)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (i > 0)
				{
					WriteToken(",");
					WriteSpace();
				}

				Visit(list[i]);
			}
		}

		private void VisitMultilineList(ExpressionCollection expressions)
		{
			for (int i = 0; i < expressions.Count; i++)
			{
				Visit(expressions[i]);

				if (i != expressions.Count - 1)
				{
					WriteToken(",");
					WriteLine();
				}
			}
		}

		public override void VisitMethodReferenceExpression(MethodReferenceExpression node)
		{
			if (node.Target != null)
			{
				WriteMethodTarget(node.Target);
			}
			else
			{
				WriteReferenceAndNamespaceIfInCollision(node.Method.DeclaringType);
				WriteToken(".");
			}

			WriteMethodReference(node);
		}

		protected virtual void WriteMethodReference(MethodReferenceExpression methodReferenceExpression)
		{
			MethodReference methodReference = methodReferenceExpression.Method;

			if (methodReference is GenericInstanceMethod)
			{
				WriteGenericInstanceMethod(methodReference as GenericInstanceMethod);
				return;
			}

			string methodName = GetMethodName(methodReference);
			WriteReference(methodName, methodReference);
		}

		protected virtual void WriteGenericInstanceMethod(GenericInstanceMethod genericMethod)
		{
			MethodReference method = genericMethod.ElementMethod;
			string methodName = GetMethodName(method);
			WriteReference(methodName, method);

			if (genericMethod.HasAnonymousArgument())
			{
				return;
			}

			WriteToken(GenericLeftBracket);
			Mono.Collections.Generic.Collection<TypeReference> arguments = genericMethod.GenericArguments;
			for (int i = 0; i < arguments.Count; i++)
			{
				if (i > 0)
				{
					WriteToken(",");
					WriteSpace();
				}
				WriteReferenceAndNamespaceIfInCollision(arguments[i]);
			}
			WriteToken(GenericRightBracket);
			return;
		}

		public override void VisitThisReferenceExpression(ThisReferenceExpression node)
		{
			WriteKeyword(KeyWordWriter.This);
		}

		public override void VisitBaseReferenceExpression(BaseReferenceExpression node)
		{
			WriteKeyword(KeyWordWriter.Base);
		}

		protected string WriteLogicalToken(BinaryOperator logical)
		{
			switch (logical)
			{
				case BinaryOperator.LogicalAnd:
					return ToString(BinaryOperator.LogicalAnd);
				case BinaryOperator.LogicalOr:
					return ToString(BinaryOperator.LogicalOr);
			}
			return string.Empty;
		}

		private bool IsTypeNameInCollisionWithParameters(string typeName)
		{
			if (CurrentMethod != null)
			{
				foreach (ParameterDefinition parameter in CurrentMethod.Parameters)
				{
					if (this.Language.IdentifierComparer.Compare(parameter.Name, typeName) == 0)
					{
						return true;
					}
				}
			}

			return false;
		}

		private bool IsTypeNameInCollisionWithNamespace(string typeName)
		{
			string currentNamespace = TypeContext.CurrentType.Namespace;
			string[] currentNamespaceParts = currentNamespace.Split('.');

			if (currentNamespaceParts.Length > 0)
			{
				StringBuilder parentNamespaceBuilder = new StringBuilder();

				for (int i = 0; i < currentNamespaceParts.Length; i++)
				{
					if (i > 0)
					{
						parentNamespaceBuilder.Append(".");
					}
					parentNamespaceBuilder.Append(currentNamespaceParts[i]);

					string currentParentNamespace = parentNamespaceBuilder.ToString();

					HashSet<string> currentParentNamespaceChildren;
					if (ModuleContext.NamespaceHieararchy.TryGetValue(currentParentNamespace, out currentParentNamespaceChildren))
					{
						if (currentParentNamespaceChildren.Contains(typeName))
						{
							return true;
						}
					}
				}
			}

			return false;
		}

		private ICollection<string> GetUsedNamespaces()
		{
			HashSet<string> usedNamespaces = new HashSet<string>();

			foreach (string usedNamespace in TypeContext.UsedNamespaces)
			{
				usedNamespaces.Add(usedNamespace);
			}

			string currentNamespace = TypeContext.CurrentType.Namespace;
			string[] currentNamespaceParts = currentNamespace.Split('.');

			if (currentNamespaceParts.Length > 0)
			{
				StringBuilder parentNamespaceBuilder = new StringBuilder();

				for (int i = 0; i < currentNamespaceParts.Length; i++)
				{
					if (i > 0)
					{
						parentNamespaceBuilder.Append(".");
					}
					parentNamespaceBuilder.Append(currentNamespaceParts[i]);

					string currentParentNamespace = parentNamespaceBuilder.ToString();

					if (!usedNamespaces.Contains(currentParentNamespace))
					{
						usedNamespaces.Add(currentParentNamespace);
					}
				}
			}

			return usedNamespaces;
		}

		private bool IsTypeNameInCollisionWithOtherType(string typeName)
		{
			ICollection<string> usedNamespaces = GetUsedNamespaces();
			List<string> typeCollisionNamespaces;
			if (ModuleContext.CollisionTypesData.TryGetValue(typeName, out typeCollisionNamespaces))
			{
				IEnumerable<string> namespacesIntersection = typeCollisionNamespaces.Intersect(usedNamespaces);

				if (namespacesIntersection.Count() > 1)
				{
					return true;
				}
			}

			return false;
		}

		private bool IsTypeNameInCollisionWithMembers(string typeName)
		{
			return this.TypeContext.VisibleMembersNames.Contains(typeName);
		}

		private bool IsTypeNameinCollisionWithVariables(string typeName)
		{
			if (this.MethodContext == null)
			{
				return false;
			}

			return this.MethodContext.VariableNamesCollection.Contains(typeName);
		}

		protected virtual bool IsTypeNameInCollision(string typeName)
		{
			if (IsTypeNameInCollisionWithParameters(typeName))
			{
				return true;
			}

			if (IsTypeNameInCollisionWithNamespace(typeName))
			{
				return true;
			}

			if (IsTypeNameInCollisionWithOtherType(typeName))
			{
				return true;
			}

			if (IsTypeNameInCollisionWithMembers(typeName))
			{
				return true;
			}

			if (IsTypeNameinCollisionWithVariables(typeName))
			{
				return true;
			}

			return false;
		}

		protected virtual void WriteTypeSpecification(TypeSpecification typeSpecification, int startingArgument = 0)
		{
			PointerType pointer = typeSpecification as PointerType;
			if (pointer != null)
			{
				WriteReference(typeSpecification.ElementType);
				WriteToken("*");
				return;
			}

			PinnedType pinnedType = typeSpecification as PinnedType;
			if (pinnedType != null)
			{
				if (typeSpecification.ElementType is ByReferenceType)
				{
					WriteReference(((typeSpecification.ElementType) as ByReferenceType).ElementType);
					WriteToken("*");
				}
				else
				{
					WriteReference(typeSpecification.ElementType);
				}
				return;
			}

			ByReferenceType reference = typeSpecification as ByReferenceType;
			if (reference != null)
			{
				WriteReference(typeSpecification.ElementType);
				WriteToken("&");
				return;
			}

			ArrayType array = typeSpecification as ArrayType;
			if (array != null)
			{
				List<int> dimentions = new List<int>();
				TypeReference elementType = typeSpecification.ElementType;
				while (elementType is ArrayType)
				{
					ArrayType ar = (elementType as ArrayType);
					dimentions.Add(ar.Dimensions.Count);
					elementType = ar.ElementType;
				}

				WriteReference(elementType);

				WriteToken(IndexLeftBracket);
				if (array.Dimensions != null)
				{
					for (int i = 1; i < array.Dimensions.Count; i++)
					{
						WriteToken(",");
					}
				}
				WriteToken(IndexRightBracket);
				foreach (int dimentionsSize in dimentions)
				{
					WriteToken(IndexLeftBracket);
					for (int i = 1; i < dimentionsSize; i++)
					{
						WriteToken(",");
					}
					WriteToken(IndexRightBracket);
				}
				return;
			}

			GenericInstanceType generic = typeSpecification as GenericInstanceType;
			if (generic != null)
			{
				if (SupportsSpecialNullable && (generic.GetFriendlyFullName(Language).IndexOf("System.Nullable<") == 0) &&
					generic.GenericArguments[0].IsValueType)
				{
					TypeReference toWrite = generic.GenericArguments[0];
					if (generic.PostionToArgument.ContainsKey(0))
					{
						toWrite = generic.PostionToArgument[0];
					}
					WriteReference(toWrite);
					WriteToken("?");
					return;
				}
				else
				{
					WriteGenericInstance(generic, startingArgument);
					return;
				}
			}

			if (typeSpecification.MetadataType == MetadataType.OptionalModifier)
			{
				Write(typeSpecification.Name);
				return;
			}

			if (typeSpecification.IsRequiredModifier)
			{
				if (isWritingComment && (typeSpecification as RequiredModifierType).ModifierType.FullName == "System.Runtime.CompilerServices.IsVolatile")
				{
					//Corner case when writing field declarations in visual basic
					WriteVolatileType(typeSpecification.ElementType);
				}
				else
				{
					WriteReference(typeSpecification.ElementType);
				}
				return;
			}

			throw new NotSupportedException();
		}

		public virtual void WriteGenericReference(TypeReference type)
		{
			if (type.IsNested)
			{
				WriteGenericReference(type.DeclaringType);
				WriteToken(".");
			}

			if (type is TypeSpecification)
			{
				WriteTypeSpecification(type as TypeSpecification);
				return;
			}

			if (!type.IsNested)
			{
				WriteNamespaceIfTypeInCollision(type);
			}

			if (type.IsGenericParameter)
			{
				string typeString = ToTypeString(type);
				WriteReference(typeString, null);
				return;
			}

			TypeDefinition typeDef = type.Resolve();
			if (typeDef != null && typeDef.HasGenericParameters)
			{
				WriteReference(GetTypeName(typeDef), typeDef);
				WriteToken(GenericLeftBracket);
				for (int i = 1; i < typeDef.GenericParameters.Count; i++)
				{
					WriteToken(",");
				}
				WriteToken(GenericRightBracket);
			}
			else if (type.Namespace != "System")
			{
				WriteReference(GetTypeName(type), type);
			}
			else
			{
				WriteReference(ToTypeString(type), type);
			}
		}

		protected void WriteGenericInstance(GenericInstanceType genericInstance, int startingArgument = 0)
		{
			string name = GetTypeName(genericInstance);
			WriteReference(name, genericInstance.ElementType);
			WriteToken(GenericLeftBracket);

			for (int i = startingArgument; i < genericInstance.GenericArguments.Count; i++)
			{
				if (i > startingArgument)
				{
					WriteToken(",");
					WriteSpace();
				}

				TypeReference genericArg = genericInstance.GenericArguments[i];
				if (genericInstance.PostionToArgument.ContainsKey(i))
				{
					genericArg = genericInstance.PostionToArgument[i];
				}

				WriteReferenceAndNamespaceIfInCollision(genericArg);
			}

			WriteToken(GenericRightBracket);
		}

		protected void WriteTokenBetweenSpace(string token)
		{
			WriteSpace();
			WriteToken(token);
			WriteSpace();
		}

		public override void VisitFieldReferenceExpression(FieldReferenceExpression node)
		{
			if (node.Target != null)
			{
				bool isComplexTarget = IsComplexTarget(node.Target);
				if (isComplexTarget)
				{
					WriteToken("(");
				}

				Visit(node.Target);

				if (isComplexTarget)
				{
					WriteToken(")");
				}
			}
			else
			{
				WriteReferenceAndNamespaceIfInCollision(node.Field.DeclaringType);
			}

			WriteToken(".");
			WriteFieldName(node.Field);
		}

		protected virtual void WriteFieldName(FieldReference field)
		{
			string fieldName = GetFieldName(field);
			WriteReference(fieldName, field);
		}

		public override void VisitPropertyReferenceExpression(PropertyReferenceExpression node)
		{
			if (node.Target != null)
			{
				bool isComplexTarget = IsComplexTarget(node.Target);

				if (isComplexTarget)
				{
					WriteToken("(");
				}

				Visit(node.Target);

				if (isComplexTarget)
				{
					WriteToken(")");
				}
			}
			else
			{
				WriteReferenceAndNamespaceIfInCollision(node.DeclaringType);
			}

			if (!node.IsIndexer)
			{
				WriteToken(".");
				WritePropertyName(node.Property);
			}
			else
			{
				WriteToken(IndexLeftBracket);
				for (int i = 0; i < node.Arguments.Count; i++)
				{
					Expression parameter = node.Arguments[i];

					if (i > 0)
					{
						WriteToken(",");
						WriteSpace();
					}

					Write(parameter);
				}
				WriteToken(IndexRightBracket);
			}
		}

		protected virtual void WritePropertyName(PropertyDefinition property)
		{
			string propertyName = GetPropertyName(property);
			WriteReference(propertyName, property);
		}

		public override void VisitAnonymousPropertyInitializerExpression(AnonymousPropertyInitializerExpression node)
		{
			WritePropertyName(node.Property);
		}

		public override void VisitPropertyInitializerExpression(PropertyInitializerExpression node)
		{
			WritePropertyName(node.Property);
		}

		public override void VisitFieldInitializerExpression(FieldInitializerExpression node)
		{
			WriteFieldName(node.Field);
		}

		public override void VisitEventReferenceExpression(EventReferenceExpression node)
		{
			if (node.Target != null)
			{
				bool isComplexTarget = IsComplexTarget(node.Target);
				if (isComplexTarget)
				{
					WriteToken("(");
				}
				Visit(node.Target);
				if (isComplexTarget)
				{
					WriteToken(")");
				}
			}
			else
			{
				WriteReferenceAndNamespaceIfInCollision(node.Event.DeclaringType);
			}

			WriteToken(".");
			WriteReference(node.Event.Name, node.Event);
		}

		public override void VisitEnumExpression(EnumExpression node)
		{
			string value = node.FieldName;

			if (value == null)
			{
				WriteKeyword(KeyWordWriter.Null);
				return;
			}

			WriteReferenceAndNamespaceIfInCollision(node.ExpressionType);
			Write(".");

			string fieldName = GetFieldName(node.Field);
			WriteReference(fieldName, node.Field);
		}

		public override void VisitVariableDeclarationExpression(VariableDeclarationExpression node)
		{
			VariableDefinition variable = node.Variable;
			WriteTypeAndName(variable.VariableType, GetVariableName(variable));
		}

		public override void VisitGotoStatement(GotoStatement node)
		{
			WriteKeyword(KeyWordWriter.GoTo);
			WriteSpace();
			Write(node.TargetLabel);
			WriteEndOfStatement();
		}

		public override void VisitIfStatement(IfStatement node)
		{
			WriteKeyword(KeyWordWriter.If);
			WriteSpace();
			WriteBetweenParenthesis(node.Condition);
			if (KeyWordWriter.Then != null)
			{
				WriteSpace();
				WriteKeyword(KeyWordWriter.Then);
			}
			WriteLine();

			Visit(node.Then);

			if (node.Else == null)
			{
				WriteSpecialEndBlock(KeyWordWriter.If);
				return;
			}

			WriteLine();
			WriteKeyword(KeyWordWriter.Else);
			WriteLine();

			Visit(node.Else);
			WriteSpecialEndBlock(KeyWordWriter.If);
		}

		public override void VisitIfElseIfStatement(IfElseIfStatement node)
		{
			for (int i = 0; i < node.ConditionBlocks.Count; i++)
			{
				if (i == 0)
				{
					WriteKeyword(KeyWordWriter.If);
				}
				else
				{
					WriteLine();
					WriteKeyword(KeyWordWriter.ElseIf);
				}

				WriteSpace();
				WriteBetweenParenthesis(node.ConditionBlocks[i].Key);
				if (KeyWordWriter.Then != null)
				{
					WriteSpace();
					WriteKeyword(KeyWordWriter.Then);
				}
				WriteLine();

				Visit(node.ConditionBlocks[i].Value);
			}

			if (node.Else != null)
			{
				WriteLine();
				WriteKeyword(KeyWordWriter.Else);
				WriteLine();

				Visit(node.Else);
			}

			WriteSpecialEndBlock(KeyWordWriter.If);
		}

		protected virtual void WriteSpecialBetweenParenthesis(Expression expression)
		{
		}

		protected virtual void WriteSpecialBetweenParenthesis(Action action)
		{
		}

		protected void WriteBetweenParenthesis(Expression expression)
		{
			WriteToken("(");
			Visit(expression);
			WriteToken(")");
		}

		public override void VisitReturnExpression(ReturnExpression node)
		{
			WriteKeyword(KeyWordWriter.Return);

			if (node.Value != null)
			{
				WriteSpace();
				Visit(node.Value);
			}
		}

		public override void VisitShortFormReturnExpression(ShortFormReturnExpression node)
		{
			if (node.Value != null)
			{
				Visit(node.Value);
			}
		}

		public override void VisitThrowExpression(ThrowExpression node)
		{
			WriteKeyword(KeyWordWriter.Throw);
			if (node.Expression != null)
			{
				WriteSpace();
				Visit(node.Expression);
			}
		}

		public override void VisitWhileStatement(WhileStatement node)
		{
			WriteKeyword(KeyWordWriter.While);
			WriteSpace();
			WriteSpecialBetweenParenthesis(node.Condition);
			WriteLine();
			Visit(node.Body);
			WriteSpecialEndBlock(KeyWordWriter.While);
		}

		public override void VisitDoWhileStatement(DoWhileStatement node)
		{
			WriteKeyword(KeyWordWriter.Do);
			WriteLine();
			Visit(node.Body);
			WriteLine();
			WriteKeyword(KeyWordWriter.LoopWhile);
			WriteSpace();
			WriteSpecialBetweenParenthesis(node.Condition);
			WriteEndOfStatement();
		}

		public override void VisitFixedStatement(FixedStatement expression)
		{
			WriteKeyword(KeyWordWriter.Fixed);
			WriteSpace();
			WriteBetweenParenthesis(expression.Expression);
			WriteLine();
			Visit(expression.Body);
			WriteSpecialEndBlock(KeyWordWriter.Fixed);
		}

		public override void VisitMakeRefExpression(MakeRefExpression node)
		{
			WriteKeyword("__makeref");
			WriteToken("(");
			Visit(node.Expression);
			WriteToken(")");
		}

		public override void VisitTryStatement(TryStatement node)
		{
			WriteKeyword(KeyWordWriter.Try);
			WriteLine();
			Visit(node.Try);

			if (node.CatchClauses.Count != 0)
			{
				WriteLine();
				Visit(node.CatchClauses);
			}

			if (node.Finally != null)
			{
				WriteLine();
				WriteKeyword(KeyWordWriter.Finally);
				WriteLine();
				Visit(node.Finally);
			}
			WriteSpecialEndBlock(KeyWordWriter.Try);
		}

		public override void VisitArrayCreationExpression(ArrayCreationExpression node)
		{
			WriteKeyword(KeyWordWriter.New);
			WriteSpace();
			WriteReferenceAndNamespaceIfInCollision(GetBaseElementType(node.ElementType));

			bool isInitializerPresent = Utilities.IsInitializerPresent(node.Initializer);

			WriteArrayDimensions(node.Dimensions, node.ElementType, isInitializerPresent);

			if (isInitializerPresent)
			{
				StartInitializer(node.Initializer);
				Visit(node.Initializer);
			}
		}

		protected TypeReference GetBaseElementType(TypeReference type)
		{
			TypeReference elementType = type;
			while (elementType is ArrayType)
			{
				elementType = (elementType as ArrayType).ElementType;
			}

			return elementType;
		}

		public override void VisitArrayVariableDeclarationExpression(ArrayVariableDeclarationExpression node)
		{
			Visit(node.Variable);
		}

		public override void VisitArrayAssignmentVariableReferenceExpression(ArrayVariableReferenceExpression node)
		{
			Visit(node.Variable);
		}

		public override void VisitArrayAssignmentFieldReferenceExpression(ArrayAssignmentFieldReferenceExpression node)
		{
			Visit(node.Field);
		}

		protected virtual void WriteArrayDimensions(ExpressionCollection dimensions, TypeReference arrayType, bool isInitializerPresent)
		{
			List<int> emptyDimensions = new List<int>();
			TypeReference elementType = arrayType;
			while (elementType is ArrayType)
			{
				ArrayType arr = elementType as ArrayType;
				emptyDimensions.Add(arr.Dimensions.Count);
				elementType = arr.ElementType;
			}

			#region Indexes
			WriteToken(IndexLeftBracket);

			for (int i = 0; i < dimensions.Count; i++)
			{
				if (i > 0)
				{
					WriteToken(",");
					if (!isInitializerPresent)
					{
						WriteToken(" ");
					}
				}
				if (!isInitializerPresent)
				{
					Visit(dimensions[i]);
				}
			}
			WriteToken(IndexRightBracket);
			#endregion

			//remaining brackets
			foreach (int dimentionSize in emptyDimensions)
			{
				WriteToken(IndexLeftBracket);
				for (int i = 1; i < dimentionSize; i++)
				{
					WriteToken(",");
				}
				WriteToken(IndexRightBracket);
			}
		}

		protected override void VisitIIndexerExpression(IIndexerExpression node)
		{
			bool isComplexTarget = IsComplexTarget(node.Target);

			if (isComplexTarget)
			{
				WriteToken("(");
			}
			Visit(node.Target);
			if (isComplexTarget)
			{
				WriteToken(")");
			}

			WriteToken(IndexLeftBracket);
			VisitList(node.Indices);
			WriteToken(IndexRightBracket);
		}

		protected virtual bool IsComplexTarget(Expression target)
		{
			if (target.CodeNodeType == CodeNodeType.UnaryExpression)
			{
				UnaryExpression unarTarget = target as UnaryExpression;
				if (unarTarget.Operator == UnaryOperator.AddressDereference && unarTarget.Operand.CodeNodeType == CodeNodeType.ArgumentReferenceExpression)
				{
					ArgumentReferenceExpression argument = (unarTarget.Operand as ArgumentReferenceExpression);
					if (argument.ExpressionType.IsByReference)
					{
						// the argument is dereferenced out/ref parameter
						return false;
					}
				}
			}

			return target.CodeNodeType == CodeNodeType.BinaryExpression ||
				   target.CodeNodeType == CodeNodeType.UnaryExpression ||
				   target.CodeNodeType == CodeNodeType.ConditionExpression ||
				   target.CodeNodeType == CodeNodeType.CastExpression ||
				   target.CodeNodeType == CodeNodeType.SafeCastExpression ||
				   target.CodeNodeType == CodeNodeType.ArrayCreationExpression ||
				   target.CodeNodeType == CodeNodeType.ObjectCreationExpression ||
				   target.CodeNodeType == CodeNodeType.LambdaExpression;
		}

		private void WriteConstructorNameAndGenericArguments(ObjectCreationExpression node, bool writeGenericArguments = true, int startArgumentIndex = 0)
		{
			string constructorName = GetTypeName(node.Constructor.DeclaringType);

			WriteReference(constructorName, node.Constructor);

			if (writeGenericArguments)
			{
				if (node.ExpressionType.IsGenericInstance)
				{
					WriteToken(GenericLeftBracket);
					Mono.Collections.Generic.Collection<TypeReference> arguments = (node.ExpressionType as GenericInstanceType).GenericArguments;
					for (int i = startArgumentIndex; i < arguments.Count; i++)
					{
						if (i > startArgumentIndex)
						{
							WriteToken(",");
							WriteSpace();
						}
						WriteReferenceAndNamespaceIfInCollision(arguments[i]);
					}
					WriteToken(GenericRightBracket);
				}
			}
		}

		private void WriteConstructorInvocation(ObjectCreationExpression node)
		{
			if (node.Constructor.DeclaringType is TypeSpecification)
			{
				GenericInstanceType generic = node.Constructor.DeclaringType as GenericInstanceType;
				if (generic != null)
				{
					if (SupportsSpecialNullable && (generic.GetFriendlyFullName(Language).IndexOf("System.Nullable<") == 0) &&
						generic.GenericArguments[0].IsValueType)
					{
						TypeReference toWrite = generic.GenericArguments[0];
						if (generic.PostionToArgument.ContainsKey(0))
						{
							toWrite = generic.PostionToArgument[0];
						}
						WriteReferenceAndNamespaceIfInCollision(toWrite);
						WriteToken("?");
						return;
					}
				}
			}

			if (node.Constructor.DeclaringType.Namespace == "System")
			{
				WriteReferenceAndNamespaceIfInCollision(node.Constructor.DeclaringType);
				return;
			}

			if (node.Constructor.DeclaringType.DeclaringType != null)
			{
				TypeReference declaringType = node.Constructor.DeclaringType.DeclaringType;
				if (node.Constructor.DeclaringType.IsGenericInstance)
				{
					GenericInstanceType referenceGeneric = node.Constructor.DeclaringType as GenericInstanceType;
					if (declaringType.HasGenericParameters)
					{
						/// Transfer the parameters from reference up to the declaring type.
						/// Bare in mind, that the declaring type might have less generic parameters.
						/// Transfer just the first X that match.
						/// This is needed, because VB and C# don't allow other language syntax

						GenericInstanceType declaringTypeInstance = new GenericInstanceType(declaringType);

						Collection<TypeReference> nestedTypeBackup = new Collection<TypeReference>(referenceGeneric.GenericArguments);
						Collection<TypeReference> declaringBackup = new Collection<TypeReference>(declaringTypeInstance.GenericArguments);
						int parametersToMoveUp = declaringType.GenericParameters.Count;
						for (int i = 0; i < parametersToMoveUp; i++)
						{
							/// check if it moves the parameters forward or not
							declaringTypeInstance.AddGenericArgument(referenceGeneric.GenericArguments[i]);
							declaringTypeInstance.GenericArguments.Add(referenceGeneric.GenericArguments[i]);
						}
						WriteReferenceAndNamespaceIfInCollision(declaringTypeInstance);
						Write(".");
						if (referenceGeneric.GenericArguments.Count - parametersToMoveUp > 0)
						{
							WriteConstructorNameAndGenericArguments(node, true, parametersToMoveUp);
						}
						else
						{
							WriteConstructorNameAndGenericArguments(node, false);
						}
						referenceGeneric.GenericArguments.Clear();
						referenceGeneric.GenericArguments.AddRange(nestedTypeBackup);

						declaringTypeInstance.GenericArguments.Clear();
						declaringTypeInstance.GenericArguments.AddRange(declaringBackup);
						return;
					}
				}

				WriteReferenceAndNamespaceIfInCollision(declaringType);
				Write(".");
				WriteConstructorNameAndGenericArguments(node);
			}
			else
			{
				bool isTypeNameInCollision = IsTypeNameInCollision(node.Constructor.DeclaringType.Name);
				WriteNamespace(node.Constructor.DeclaringType.GetElementType(), isTypeNameInCollision);
				WriteConstructorNameAndGenericArguments(node);
			}
		}

		public override void VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpression node)
		{
			WriteKeyword(KeyWordWriter.New);

			if (KeyWordWriter.ObjectInitializer != null)
			{
				WriteSpace();
				WriteKeyword(KeyWordWriter.ObjectInitializer);
			}
			StartInitializer(node.Initializer);
			Visit(node.Initializer);
		}

		public override void VisitObjectCreationExpression(ObjectCreationExpression node)
		{
			if (IsNewDelegate(node))
			{
				WriteDelegateCreation(node);
				return;
			}

			WriteKeyword(KeyWordWriter.New);

			WriteSpace();

			if (node.Constructor != null)
			{
				WriteConstructorInvocation(node);
			}
			else
			{
				WriteReferenceAndNamespaceIfInCollision(node.Type);
			}

			WriteToken("(");
			EnterMethodInvocation(node.Constructor);
			VisitMethodParameters(node.Arguments);
			LeaveMethodInvocation();
			WriteToken(")");

			if (node.Initializer != null)
			{
				if (node.Initializer.InitializerType == InitializerType.ObjectInitializer)
				{
					if (KeyWordWriter.ObjectInitializer != null)
					{
						WriteSpace();
						WriteKeyword(KeyWordWriter.ObjectInitializer);
					}
				}
				else if (node.Initializer.InitializerType == InitializerType.CollectionInitializer)
				{
					if (KeyWordWriter.CollectionInitializer != null)
					{
						WriteSpace();
						WriteKeyword(KeyWordWriter.CollectionInitializer);
					}
				}

				StartInitializer(node.Initializer);
				Visit(node.Initializer);
			}
		}

		protected void StartInitializer(InitializerExpression node)
		{
			if (node.IsMultiLine)
			{
				WriteLine();
			}
			else
			{
				WriteSpace();
			}
		}

		public override void VisitInitializerExpression(InitializerExpression node)
		{
			WriteToken("{");

			if (node.IsMultiLine)
			{
				Indent();
				WriteLine();

				VisitMultilineList(node.Expressions);

				WriteLine();
				Outdent();
			}
			else
			{
				WriteSpace();
				VisitList(node.Expressions);
				WriteSpace(); 
			}

			WriteToken("}");
		}

		protected virtual void WriteDelegateCreation(ObjectCreationExpression node)
		{
			WriteKeyword(KeyWordWriter.New);
			WriteSpace();
			WriteReferenceAndNamespaceIfInCollision(node.ExpressionType);
			WriteToken("(");
			WriteDelegateArgument(node);
			WriteToken(")");
		}

		protected void WriteDelegateArgument(ObjectCreationExpression node)
		{
			//if (node.Arguments[1].CodeNodeType == CodeNodeType.LambdaExpression)
			//{
			//	VisitLambdaExpression((LambdaExpression)node.Arguments[1]);
			//	return;
			//}
			if ((node.Arguments[0].CodeNodeType != CodeNodeType.LiteralExpression ||
				(node.Arguments[0] as LiteralExpression).Value != null) &&
				(node.Arguments[1] as MethodReferenceExpression) != null &&
				(node.Arguments[1] as MethodReferenceExpression).Target == null)
			{
				Write(node.Arguments[0]);
				WriteToken(".");
				WriteMethodReference(node.Arguments[1] as MethodReferenceExpression);
			}
			else
			{
				Write(node.Arguments[1]);
			}
		}

		private bool IsNewDelegate(ObjectCreationExpression node)
		{
			if (node.Constructor == null || node.Constructor.DeclaringType == null)
			{
				return false;
			}

			TypeDefinition typeDef = node.Constructor.DeclaringType.Resolve();

			if (typeDef != null && typeDef.BaseType != null && typeDef.BaseType.FullName == typeof(MulticastDelegate).FullName)
			{
				return true;
			}
			return false;
		}

		public override void VisitLockStatement(LockStatement expression)
		{
			WriteKeyword(KeyWordWriter.Lock);
			WriteSpace();
			WriteSpecialBetweenParenthesis(expression.Expression);
			WriteLine();
			Visit(expression.Body);
			WriteSpecialEndBlock(KeyWordWriter.Lock);
		}

		public override void VisitTypeOfExpression(TypeOfExpression node)
		{
			WriteKeyword(KeyWordWriter.TypeOf);
			WriteToken("(");
			WriteGenericReference(node.Type);
			WriteToken(")");
		}

		public override void VisitCanCastExpression(CanCastExpression node)
		{
			if (KeyWordWriter.IsType != null)
			{
				WriteKeyword(KeyWordWriter.IsType);
				WriteSpace();
			}
			Visit(node.Expression);
			WriteSpace();
			WriteKeyword(KeyWordWriter.Is);
			WriteSpace();
			WriteReferenceAndNamespaceIfInCollision(node.TargetType);
		}

		public override void VisitSizeOfExpression(SizeOfExpression node)
		{
			WriteKeyword(KeyWordWriter.SizeOf);
			WriteToken("(");
			WriteReferenceAndNamespaceIfInCollision(node.Type);
			WriteToken(")");
		}

		public override void VisitTypeReferenceExpression(TypeReferenceExpression node)
		{
			WriteReferenceAndNamespaceIfInCollision(node.Type);
		}

		public override void VisitStackAllocExpression(StackAllocExpression node)
		{
			WriteKeyword(KeyWordWriter.Stackalloc);
			WriteSpace();
			TypeReference exprType = (node.ExpressionType as PointerType).ElementType;
			WriteReference(exprType);
			WriteToken(IndexLeftBracket);
			Visit(node.Expression);
			WriteToken(IndexRightBracket);
		}

		public override void VisitDelegateInvokeExpression(DelegateInvokeExpression node)
		{
			bool isComplexTarget = IsComplexTarget(node.Target);
			bool needsCast = false;

			if (isComplexTarget)
			{
				WriteToken("(");

				if (node.Target.CodeNodeType == CodeNodeType.LambdaExpression && (node.Target as LambdaExpression).HasType)
				{
					needsCast = true;
					WriteToken("(");
					WriteReferenceAndNamespaceIfInCollision((node.Target as LambdaExpression).ExpressionType);
					WriteToken(")");
					WriteToken("(");
				}
			}

			Visit(node.Target);

			if (isComplexTarget)
			{
				if (needsCast)
				{
					WriteToken(")");
				}

				WriteToken(")");
			}

			WriteToken("(");
			EnterMethodInvocation(node.InvokeMethodReference);
			VisitMethodParameters(node.Arguments);
			LeaveMethodInvocation();
			WriteToken(")");
		}

		public override void VisitLambdaExpression(LambdaExpression node)
		{
			if (node.IsAsync && KeyWordWriter.Async != null)
			{
				WriteKeyword(KeyWordWriter.Async);
				WriteSpace();
			}
		}

		private void VisitAddressDereferenceExpression(UnaryExpression node)
		{
			if (node.Operand.CodeNodeType == CodeNodeType.ArgumentReferenceExpression && node.Operand.ExpressionType.IsByReference)
			{
				// out/ref parameters are automatically dereferenced by the compiler.
				// adding the dereference symbol will result in illegal code in C#/VB
				Visit(node.Operand);
				return;
			}
			WriteKeyword(KeyWordWriter.Dereference);
			Visit(node.Operand);
		}

		protected virtual bool SupportsAutoProperties
		{
			get
			{
				return false;
			}
		}

		protected virtual bool SupportsSpecialNullable
		{
			get
			{
				return false;
			}
		}

		protected virtual bool ShouldWriteOutAndRefOnInvocation
		{
			get
			{
				return false;
			}
		}

		protected virtual bool RemoveBaseConstructorInvocation
		{
			get
			{
				return false;
			}
		}

		protected virtual void WriteGenericName(IGenericDefinition genericDefinition)
		{
			IGenericParameterProvider provider;

			string referenceName;
			if (genericDefinition is MethodDefinition)
			{
				referenceName = GetMethodName(genericDefinition as MethodDefinition);
				provider = genericDefinition as MethodDefinition;
			}
			else
			{
				referenceName = GetTypeName(genericDefinition as TypeDefinition);
				provider = genericDefinition as TypeDefinition;
			}

			WriteReference(referenceName, genericDefinition);
			WriteGenericParametersToDefinition(provider, WriteGenericParameterConstraints, true);
		}

		private void WriteGenericParametersToDefinition(IGenericParameterProvider genericDefinition, Action<GenericParameter> writeParamConstraints, bool renameInvalidCharacters)
		{
			int i = 0;

			if (genericDefinition is TypeDefinition)
			{
				TypeDefinition nestedType = genericDefinition as TypeDefinition;
				TypeDefinition parentType = nestedType.DeclaringType;
				if (parentType != null && parentType.HasGenericParameters)
				{
					i = parentType.GenericParameters.Count;
				}
			}
			if (i < genericDefinition.GenericParameters.Count)
			{
				WriteToken(GenericLeftBracket);
				for (; i < genericDefinition.GenericParameters.Count; i++)
				{
					GenericParameter currentParameter = genericDefinition.GenericParameters[i];
					if (currentParameter.IsCovariant)
					{
						WriteKeyword(KeyWordWriter.Covariant);
						WriteSpace();
					}
					if (currentParameter.IsContravariant)
					{
						WriteKeyword(KeyWordWriter.Contravariant);
						WriteSpace();
					}
					WriteReference(renameInvalidCharacters ? GenericHelper.ReplaceInvalidCharactersName(Language, currentParameter.Name) : currentParameter.Name, null);
					if (currentParameter.HasConstraints || currentParameter.HasDefaultConstructorConstraint ||
						currentParameter.HasReferenceTypeConstraint || currentParameter.HasNotNullableValueTypeConstraint)
					{
						if (writeParamConstraints != null)
						{
							writeParamConstraints(currentParameter);
						}
						//WriteGenericParameterConstraints(currentParameter);
					}
					if (i != genericDefinition.GenericParameters.Count - 1)
					{
						WriteToken(",");
						WriteSpace();
					}
				}
				WriteToken(GenericRightBracket);
			}
		}

		protected virtual void WriteGenericParameterConstraints(GenericParameter parameter)
		{
		}

		protected void WriteSingleGenericParameterConstraintsList(GenericParameter genericParameter)
		{
			bool wroteConstraint = false;
			if (genericParameter.HasNotNullableValueTypeConstraint)
			{
				if (wroteConstraint)
				{
					WriteToken(",");
					WriteSpace();
				}
				wroteConstraint = true;
				WriteKeyword(KeyWordWriter.Struct);
			}
			if (genericParameter.HasReferenceTypeConstraint)
			{
				if (wroteConstraint)
				{
					WriteToken(",");
					WriteSpace();
				}
				wroteConstraint = true;
				WriteKeyword(KeyWordWriter.Class);
			}
			foreach (TypeReference constraint in genericParameter.Constraints)
			{
				if (genericParameter.HasNotNullableValueTypeConstraint && constraint.FullName == "System.ValueType")
				{
					continue;
				}
				if (wroteConstraint)
				{
					WriteToken(",");
					WriteSpace();
				}
				WriteReferenceAndNamespaceIfInCollision(constraint);
				wroteConstraint = true;
			}

			if (genericParameter.HasDefaultConstructorConstraint && !genericParameter.HasNotNullableValueTypeConstraint)
			{
				if (wroteConstraint)
				{
					WriteToken(",");
					WriteSpace();
				}
				wroteConstraint = true;
				WriteConstructorGenericConstraint();
			}
		}

		protected virtual void WriteConstructorGenericConstraint()
		{
			WriteKeyword(KeyWordWriter.New);
		}

		protected virtual void PostWriteMethodReturnType(MethodDefinition method)
		{
		}

		protected virtual void WriteMethodInterfaceImplementations(MethodDefinition method)
		{
		}

		protected virtual void WriteSpecialEndBlock(string statementName)
		{
		}

		protected virtual void WriteTypeAndName(TypeReference typeReference, string name, object reference)
		{
		}

		protected virtual void WriteTypeAndName(TypeReference typeReference, string name)
		{
		}

		protected virtual void WriteParameterTypeAndName(TypeReference typeReference, string name, ParameterDefinition reference)
		{
		}

		protected virtual void WriteBaseTypeInheritColon()
		{
		}

		protected virtual void WriteInterfacesInheritColon(TypeDefinition type)
		{
		}

		protected virtual void WriteBlock(Action action, string label)
		{
		}

		protected void WriteEventMethods(EventDefinition eventDef)
		{
			if (eventDef.AddMethod != null)
			{
				WriteAddOn(eventDef);
			}
			if (eventDef.RemoveMethod != null)
			{
				if (eventDef.AddMethod != null)
					WriteLine();
				WriteRemoveOn(eventDef);
			}
			if (eventDef.InvokeMethod != null)
			{
				//WriteFire(eventDef, analysisData, typeContext);
			}
		}

		protected virtual void WriteEventAddOnParameters(EventDefinition @event)
		{
		}

		protected virtual void WriteEventRemoveOnParameters(EventDefinition @event)
		{
		}

		protected virtual void WriteAddOn(EventDefinition @event)
		{
			uint eventAddMethodMetadataToken = @event.AddMethod.MetadataToken.ToUInt32();

			membersStack.Push(@event.AddMethod);

			int attributesStartIndex = this.formatter.CurrentPosition;
			AttributeWriter.WriteMemberAttributesAndNewLine(@event.AddMethod, new string[1] { "System.Runtime.CompilerServices.CompilerGeneratedAttribute" });
			AddMemberAttributesOffsetSpan(eventAddMethodMetadataToken, attributesStartIndex, this.formatter.CurrentPosition);

			int startPosition = this.formatter.CurrentPosition;
			int decompiledCodeStartIndex = this.formatter.CurrentPosition;

			WriteMoreRestrictiveMethodVisibility(@event.AddMethod, @event.RemoveMethod);
			int startIndex = this.formatter.CurrentPosition;
			WriteKeyword(KeyWordWriter.AddOn);
			WriteEventAddOnParameters(@event);
			int endIndex = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[@event.AddMethod] = new OffsetSpan(startIndex, endIndex);

			WriteLine();
			Statement addMethodBody = GetStatement(@event.AddMethod);

			Write(addMethodBody);
			WriteSpecialEndBlock(KeyWordWriter.AddOn);

			this.currentWritingInfo.MemberDefinitionToFoldingPositionMap.Add(@event.AddMethod, new OffsetSpan(startPosition, formatter.CurrentPosition - 1));
			this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(eventAddMethodMetadataToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));

			membersStack.Pop();
		}

		protected virtual void WriteRemoveOn(EventDefinition @event)
		{
			uint eventRemoveMethodMetadataToken = @event.RemoveMethod.MetadataToken.ToUInt32();

			membersStack.Push(@event.RemoveMethod);

			int attributesStartIndex = this.formatter.CurrentPosition;
			AttributeWriter.WriteMemberAttributesAndNewLine(@event.RemoveMethod, new string[1] { "System.Runtime.CompilerServices.CompilerGeneratedAttribute" });
			AddMemberAttributesOffsetSpan(eventRemoveMethodMetadataToken, attributesStartIndex, this.formatter.CurrentPosition);

			int startPosition = this.formatter.CurrentPosition;
			int decompiledCodeStartIndex = this.formatter.CurrentPosition;

			WriteMoreRestrictiveMethodVisibility(@event.RemoveMethod, @event.AddMethod);
			int startIndex = this.formatter.CurrentPosition;
			WriteKeyword(KeyWordWriter.RemoveOn);
			WriteEventRemoveOnParameters(@event);
			int endIndex = this.formatter.CurrentPosition - 1;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[@event.RemoveMethod] = new OffsetSpan(startIndex, endIndex);

			WriteLine();
			Statement removerBody = GetStatement(@event.RemoveMethod);

			Write(removerBody);

			WriteSpecialEndBlock(KeyWordWriter.RemoveOn);

			this.currentWritingInfo.MemberDefinitionToFoldingPositionMap.Add(@event.RemoveMethod, new OffsetSpan(startPosition, formatter.CurrentPosition - 1));
			this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(eventRemoveMethodMetadataToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));

			membersStack.Pop();
		}

		protected virtual void WriteFire(EventDefinition @event)
		{
			uint eventInvokeMethodMetadataToken = @event.InvokeMethod.MetadataToken.ToUInt32();

			int attributesStartIndex = this.formatter.CurrentPosition;
			AttributeWriter.WriteMemberAttributesAndNewLine(@event.InvokeMethod, new string[1] { "System.Runtime.CompilerServices.CompilerGeneratedAttribute" });
			AddMemberAttributesOffsetSpan(eventInvokeMethodMetadataToken, attributesStartIndex, this.formatter.CurrentPosition);

			int decompiledCodeStartIndex = this.formatter.CurrentPosition;

			WriteMethodVisibilityAndSpace(@event.InvokeMethod);

			int startIndex = this.formatter.CurrentPosition;
			WriteKeyword(KeyWordWriter.Fire);
			int endIndex = this.formatter.CurrentPosition;
			this.currentWritingInfo.MemberDeclarationToCodePostionMap[@event.InvokeMethod] = new OffsetSpan(startIndex, endIndex);

			WriteToken("(");
			WriteParameters(@event.InvokeMethod);
			WriteToken(")");

			WriteLine();
			Statement invokeBody = GetStatement(@event.InvokeMethod);

			Write(invokeBody);

			WriteSpecialEndBlock(KeyWordWriter.Fire);

			this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(eventInvokeMethodMetadataToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));
		}

		public override void VisitLambdaParameterExpression(LambdaParameterExpression node)
		{
			if (node.DisplayType)
			{
				WriteTypeAndName(node.ExpressionType, GetArgumentName(node.Parameter));
			}
			else
			{
				Write(GetArgumentName(node.Parameter));
			}
		}

		public override void VisitAwaitExpression(AwaitExpression node)
		{
			WriteKeyword(KeyWordWriter.Await);
			WriteSpace();
			Visit(node.Expression);
		}

		public void WriteBitwiseOr()
		{
			WriteKeyword(ToString(BinaryOperator.BitwiseOr));
		}

		protected abstract string ToString(BinaryOperator op, bool isOneSideNull = false);

		protected abstract string ToString(UnaryOperator op);

		protected abstract string CharStart { get; }

		protected abstract string CharEnd { get; }

		protected abstract string HexValuePrefix { get; }

		public abstract string IndexLeftBracket { get; }

		public abstract string IndexRightBracket { get; }

		protected abstract string GenericLeftBracket { get; }

		protected abstract string GenericRightBracket { get; }

		public abstract string ToTypeString(TypeReference type);

		protected abstract AttributeWriter CreateAttributeWriter();

		protected abstract IKeyWordWriter CreateKeyWordWriter();

		protected virtual void WriteVolatileType(TypeReference reference)
		{
		}

		public override void VisitArrayLengthExpression(ArrayLengthExpression node)
		{
			bool isComplexTarget = IsComplexTarget(node.Target);
			if (isComplexTarget)
			{
				WriteToken("(");
			}

			base.Visit(node.Target);

			if (isComplexTarget)
			{
				WriteToken(")");
			}
			WriteToken(".");
			WriteReference("Length", null);
		}

		public override void VisitCaseGotoStatement(CaseGotoStatement node)
		{
			VisitGotoStatement(node);
		}

		protected bool IsTypeParameterRedeclaration(GenericParameter genericParameter)
		{
			TypeReference parameterDeclarer = genericParameter.Owner as TypeReference;
			if (parameterDeclarer != null && parameterDeclarer.IsNested)
			{
				TypeReference outerType = parameterDeclarer.DeclaringType;
				if (outerType.HasGenericParameters && genericParameter.Position < outerType.GenericParameters.Count)
				{
					return true;
				}
			}
			return false;
		}

		protected bool ShouldWriteConstraintsAsComment(GenericParameter genericParameter)
		{
			TypeReference parameterDeclarer = genericParameter.Owner as TypeReference;
			if (parameterDeclarer == null)
			{
				return false;
			}
			TypeReference outerType = parameterDeclarer.DeclaringType;
			if (outerType == null || !outerType.HasGenericParameters || outerType.GenericParameters.Count <= genericParameter.Position)
			{
				return false;
			}
			GenericParameter outerParameter = outerType.GenericParameters[genericParameter.Position];
			if (outerParameter == null)
			{
				return false;
			}
			if (HaveSameConstraints(outerParameter, genericParameter))
			{
				return false;
			}
			return true;
		}

		private bool HaveSameConstraints(GenericParameter outerParameter, GenericParameter innerParameter)
		{
			if (innerParameter.HasNotNullableValueTypeConstraint ^ outerParameter.HasNotNullableValueTypeConstraint)
			{
				//only one of the types has struct constraint.
				return false;
			}

			if (innerParameter.HasReferenceTypeConstraint ^ outerParameter.HasReferenceTypeConstraint)
			{
				//only one of the types has class constraint.
				return false;
			}

			if ((innerParameter.HasDefaultConstructorConstraint && !innerParameter.HasNotNullableValueTypeConstraint) ^
				(outerParameter.HasDefaultConstructorConstraint && !outerParameter.HasNotNullableValueTypeConstraint))
			{
				//only one of the types has default constructor constraint.
				return false;
			}

			if (innerParameter.HasConstraints ^ outerParameter.HasConstraints)
			{
				return false;
			}

			if (innerParameter.Constraints.Count != outerParameter.Constraints.Count)
			{
				return false;
			}

			List<TypeReference> innerConstraints = new List<TypeReference>(innerParameter.Constraints);
			List<TypeReference> outerConstraints = new List<TypeReference>(outerParameter.Constraints);

			innerConstraints.Sort((x, y) => x.FullName.CompareTo(y.FullName));
			outerConstraints.Sort((x, y) => x.FullName.CompareTo(y.FullName));

			for (int i = 0; i < innerConstraints.Count; i++)
			{
				if (innerConstraints[i].FullName != outerConstraints[i].FullName)
				{
					return false;
				}
			}
			return true;
		}

		public override void VisitDelegateCreationExpression(DelegateCreationExpression node)
		{
			if (!node.TypeIsImplicitlyInferable)
			{
				WriteKeyword(KeyWordWriter.New);
				WriteSpace();
				WriteReference(node.Type);
				Write("(");
			}
			if (node.MethodExpression.CodeNodeType != CodeNodeType.LambdaExpression)
			{
				Write(node.Target);
				Write(".");
			}
			Write(node.MethodExpression);
			if (!node.TypeIsImplicitlyInferable)
			{
				Write(")");
			}
		}

		public override void WriteMemberNavigationName(object memberDefinition, bool renameInvalidMembers)
		{
			if (memberDefinition is MethodReference)
			{
				WriteMethodReferenceNavigationName(memberDefinition as MethodReference, renameInvalidMembers);
				return;
			}
			if (memberDefinition is TypeReference)
			{
				WriteTypeReferenceNavigationName(memberDefinition as TypeReference, renameInvalidMembers);
				return;
			}
			if (memberDefinition is IMemberDefinition) // property or event
			{
				IMemberDefinition theDefinition = memberDefinition as IMemberDefinition;
				string name = GenericHelper.GetNonGenericName(theDefinition.Name);
				if (Utilities.IsExplicitInterfaceImplementataion(theDefinition))
				{
					string[] nameParts = name.Split(new char[] { '.' });
					StringBuilder sb = new StringBuilder();
					for (int i = 0; i < nameParts.Length; i++)
					{
						if (i > 0)
						{
							sb.Append('.');
						}
						if (renameInvalidMembers)
						{
							if (!NormalizeNameIfContainingGenericSymbols(nameParts[i], sb))
							{
								sb.Append(Language.ReplaceInvalidCharactersInIdentifier(nameParts[i]));
							}
						}
						else
						{
							sb.Append(nameParts[i]);
						}

					}
					name = sb.ToString();
				}
				else if (renameInvalidMembers)
				{
					name = Language.ReplaceInvalidCharactersInIdentifier(name);
				}

				this.formatter.Write(name);

				this.formatter.Write(" : ");

				TypeReference returnType = GetMemberType(theDefinition);

				WriteTypeReferenceNavigationName(returnType, renameInvalidMembers);
			}
		}

		private void WriteTypeReferenceNavigationName(TypeReference type, bool renameInvalidMembers)
		{
			if (type.IsOptionalModifier || type.IsRequiredModifier)
			{
				WriteTypeReferenceNavigationName((type as IModifierType).ElementType, renameInvalidMembers);
				return;
			}
			if (type.IsByReference)
			{
				type = (type as ByReferenceType).ElementType;
				WriteTypeReferenceNavigationName(type, renameInvalidMembers);
				this.formatter.Write("&");
				return;
			}
			if (type.IsPointer)
			{
				type = (type as PointerType).ElementType;
				WriteTypeReferenceNavigationName(type, renameInvalidMembers);
				this.formatter.Write("*");
				return;
			}
			if (type.IsArray)
			{
				int dimentions = (type as ArrayType).Dimensions.Count;
				type = (type as ArrayType).ElementType;
				WriteTypeReferenceNavigationName(type, renameInvalidMembers);
				this.formatter.Write(IndexLeftBracket);
				this.formatter.Write(new string(',', dimentions - 1));
				this.formatter.Write(IndexRightBracket);
				return;
			}
			string name = GenericHelper.GetNonGenericName(type.Name);
			if (renameInvalidMembers)
			{
				name = Language.ReplaceInvalidCharactersInIdentifier(name);
			}

			this.formatter.Write(name);

			if (type is GenericInstanceType)
			{
				WriteGenericInstanceTypeArguments((GenericInstanceType)type, renameInvalidMembers);
			}
			else if (type.HasGenericParameters)
			{
				this.WriteGenericParametersToDefinition(type, null, renameInvalidMembers);
			}
		}

		private void WriteGenericInstanceTypeArguments(IGenericInstance genericInstance, bool renameInvalidMembers)
		{
			WriteToken(GenericLeftBracket);

			for (int i = 0; i < genericInstance.GenericArguments.Count; i++)
			{
				if (i > 0)
				{
					WriteToken(",");
					WriteSpace();
				}

				TypeReference genericArg = genericInstance.GenericArguments[i];
				if (genericInstance.PostionToArgument.ContainsKey(i))
				{
					genericArg = genericInstance.PostionToArgument[i];
				}

				WriteTypeReferenceNavigationName(genericArg, renameInvalidMembers);
			}

			WriteToken(GenericRightBracket);
		}

		private void WriteMethodReferenceNavigationName(MethodReference method, bool renameInvalidMembers)
		{
			string name = GenericHelper.GetNonGenericName(method.Name);
			string[] nameParts = name.Split(new char[] { '.' });
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < nameParts.Length; i++)
			{
				if (i > 0)
				{
					sb.Append('.');
				}
				if (renameInvalidMembers)
				{
					if (!NormalizeNameIfContainingGenericSymbols(nameParts[i], sb))
					{
						sb.Append(Language.ReplaceInvalidCharactersInIdentifier(nameParts[i]));
					}
				}
				else
				{
					sb.Append(nameParts[i]);
				}
			}
			this.formatter.Write(sb.ToString());

			if (method is GenericInstanceMethod)
			{
				this.WriteGenericInstanceTypeArguments((GenericInstanceMethod)method, renameInvalidMembers);
			}
			else if (method.HasGenericParameters)
			{
				this.WriteGenericParametersToDefinition(method, null, renameInvalidMembers);
			}

			this.formatter.Write("(");
			if (method.HasParameters)
			{
				for (int i = 0; i < method.Parameters.Count; i++)
				{
					if (i > 0)
					{
						this.formatter.Write(", ");
					}
					ParameterDefinition currentParameter = method.Parameters[i];
					WriteTypeReferenceNavigationName(currentParameter.ParameterType, renameInvalidMembers);
				}
			}
			this.formatter.Write(")");

			this.formatter.Write(" : ");

			WriteTypeReferenceNavigationName(method.FixedReturnType, renameInvalidMembers);
		}

		private bool NormalizeNameIfContainingGenericSymbols(string name, StringBuilder stringBuilder)
		{
			bool isNamePartContainingGenericSymbols = false;

			string[] namePartsLeftSplit = name.Split('<');
			string[] namePartsRightSplit = name.Split('>');

			if (namePartsLeftSplit.Length > 1)
			{
				isNamePartContainingGenericSymbols = true;

				this.NormalizeNameContainingGenericSymbols(namePartsLeftSplit, '<', stringBuilder);
			}
			else if (namePartsRightSplit.Length > 1)
			{
				isNamePartContainingGenericSymbols = true;

				this.NormalizeNameContainingGenericSymbols(namePartsRightSplit, '>', stringBuilder);
			}
			return isNamePartContainingGenericSymbols;
		}

		private void NormalizeNameContainingGenericSymbols(string[] tokensCollection, char genericSymbol, StringBuilder stringBuilder)
		{
			for (int i = 0; i < tokensCollection.Length; i++)
			{
				if (i > 0)
				{
					stringBuilder.Append(genericSymbol);
				}
				string namePart = tokensCollection[i];

				if (!NormalizeNameIfContainingGenericSymbols(namePart, stringBuilder))
				{
					string[] tokens = tokensCollection[i].Split(',');

					for (int j = 0; j < tokens.Length; j++)
					{
						if (j > 0)
						{
							stringBuilder.Append(", ");
						}
						stringBuilder.Append(Language.ReplaceInvalidCharactersInIdentifier(tokens[j]));
					}
				}
			}
		}

		private TypeReference GetMemberType(IMemberDefinition member)
		{
			if (member is MethodDefinition)
			{
				return ((MethodDefinition)member).FixedReturnType;
			}
			if (member is FieldDefinition)
			{
				return ((FieldDefinition)member).FieldType;
			}
			if (member is PropertyDefinition)
			{
				return ((PropertyDefinition)member).PropertyType;
			}
			if (member is EventDefinition)
			{
				return ((EventDefinition)member).EventType;
			}
			throw new NotSupportedException();
		}

		public override void VisitFromClause(FromClause node)
		{
			WriteKeyword(KeyWordWriter.LinqFrom);
			WriteSpace();
			Visit(node.Identifier);
			WriteSpace();
			WriteKeyword(KeyWordWriter.LinqIn);
			WriteSpace();
			Visit(node.Collection);
		}

		public override void VisitWhereClause(WhereClause node)
		{
			WriteKeyword(KeyWordWriter.LinqWhere);
			WriteSpace();
			Visit(node.Condition);
		}

		public override void VisitGroupClause(GroupClause node)
		{
			WriteKeyword(KeyWordWriter.LinqGroup);
			WriteSpace();
			Visit(node.Expression);
			WriteSpace();
			WriteKeyword(KeyWordWriter.LinqBy);
			WriteSpace();
			Visit(node.GroupKey);
		}

		public override void VisitOrderByClause(OrderByClause node)
		{
			WriteKeyword(KeyWordWriter.LinqOrderBy);
			WriteSpace();

			for (int i = 0; i < node.ExpressionToOrderDirectionMap.Count; i++)
			{
				if (i > 0)
				{
					Write(", ");
				}
				Visit(node.ExpressionToOrderDirectionMap[i].Key);
				if (node.ExpressionToOrderDirectionMap[i].Value == OrderDirection.Descending)
				{
					WriteSpace();
					WriteKeyword(KeyWordWriter.LinqDescending);
				}
			}
		}

		public override void VisitLetClause(LetClause node)
		{
			WriteKeyword(KeyWordWriter.LinqLet);
			WriteSpace();
			Visit(node.Identifier);
			Write(" = ");
			Visit(node.Expression);
		}

		public override void VisitJoinClause(JoinClause node)
		{
			WriteKeyword(KeyWordWriter.LinqJoin);
			WriteSpace();
			Visit(node.InnerIdentifier);
			WriteSpace();
			WriteKeyword(KeyWordWriter.LinqIn);
			WriteSpace();
			Visit(node.InnerCollection);
			WriteSpace();
			WriteKeyword(KeyWordWriter.LinqOn);
			WriteSpace();
			Visit(node.OuterKey);
			WriteSpace();
			WriteKeyword(KeyWordWriter.LinqEquals);
			WriteSpace();
			Visit(node.InnerKey);
		}

		public override void VisitSelectClause(SelectClause node)
		{
			WriteKeyword(KeyWordWriter.LinqSelect);
			WriteSpace();
			Visit(node.Expression);
		}

		public override void VisitIntoClause(IntoClause node)
		{
			WriteKeyword(KeyWordWriter.LinqInto);
			WriteSpace();
			Visit(node.Identifier);
		}

		public override void VisitParenthesesExpression(ParenthesesExpression parenthesesExpression)
		{
			WriteToken("(");
			Visit(parenthesesExpression.Expression);
			WriteToken(")");
		}

        public override void VisitAutoPropertyConstructorInitializerExpression(AutoPropertyConstructorInitializerExpression node)
        {
            if (node.Target != null)
            {
                if (!(node.Target is ThisReferenceExpression))
                {
                    throw new ArgumentException();
                }

                Visit(node.Target);
            }
            else
            {
                WriteReferenceAndNamespaceIfInCollision(node.Property.DeclaringType);
            }

            WriteToken(".");
            WritePropertyName(node.Property);
        }
	}
}