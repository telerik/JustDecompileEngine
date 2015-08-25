using System;
using System.Collections.Generic;
using System.Linq;
using Telerik.JustDecompiler.Languages.CSharp;
using Telerik.JustDecompiler.Decompiler;
using Telerik.JustDecompiler.Ast.Statements;
using Mono.Cecil;
using Mono.Cecil.Extensions;
using Telerik.JustDecompiler.Ast.Expressions;

namespace Telerik.JustDecompiler.Languages.TestCaseWriters
{
	public class TestCaseCSharpWriter : CSharpWriter, ILanguageTestCaseWriter
	{
		public TestCaseCSharpWriter(ILanguage language, IFormatter formatter, bool writeExceptionsAsComments)
			: base(language, formatter, TestCaseExceptionFormatter.Instance, writeExceptionsAsComments) { }

		public void SetContext(DecompilationContext context)
		{
			this.writerContext = new WriterContext(context.AssemblyContext, context.ModuleContext, context.TypeContext, new Dictionary<string, MethodSpecificContext>(), new Dictionary<string, Statement>());
			this.writerContext.MethodContexts.Add(Utilities.GetMemberUniqueName(context.MethodContext.Method), context.MethodContext);
			membersStack.Push(context.MethodContext.Method);
		}

		public void ResetContext()
		{
			writerContext = null;
			membersStack.Clear();
		}

		protected override bool IsTypeNameInCollision(string typeName)
		{
			if (writerContext == null)
			{
				return false;
			}

			return base.IsTypeNameInCollision(typeName);
		}

		new public void WriteMethodDeclaration(MethodDefinition method, bool writeDocumentation = false)
		{
			/// this is an entry point method
			this.currentWritingInfo = new WritingInfo(method);
			base.WriteMethodDeclaration(method, writeDocumentation);
		}

		protected override string GetMethodName(MethodReference method)
		{
			if (method == null)
			{
				return null;
			}

			return (writerContext == null || writerContext.ModuleContext.Module == null) ? method.Name : base.GetMethodName(method);
		}

		protected override string GetTypeName(TypeReference type)
		{
			return (writerContext == null || writerContext.ModuleContext.Module == null) ? GenericHelper.GetNonGenericName(type.Name) : base.GetTypeName(type);
		}

		protected override string GetFieldName(FieldReference field)
		{
			return (writerContext == null || writerContext.ModuleContext.Module == null) ? field.Name : base.GetFieldName(field);
		}

		protected override string GetPropertyName(PropertyReference property)
		{
			return (writerContext == null || writerContext.ModuleContext.Module == null) ? property.Name : base.GetPropertyName(property);
		}

		protected override string GetEventName(EventReference @event)
		{
			return (writerContext == null || writerContext.ModuleContext.Module == null) ? @event.Name : base.GetEventName(@event);
		}

		new public void Write(Statement statement)
		{
			base.Write(statement);
		}

		new public void Write(Expression expression)
		{
			base.Write(expression);
		}

        public override void Visit(Ast.ICodeNode node)
        {
            WriteCodeNodeLabel(node);
            DoVisit(node);
        }

		protected override void WriteInternal(IMemberDefinition member, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
		{
			if (isStopped)
				return;

			membersStack.Push(member);

			if (!(member is TypeDefinition) || (member == CurrentType))
			{
				formatter.PreserveIndent(member);
			}

			if (writeDocumentation)
			{
				WriteDocumentation(member);
			}
			if (member is TypeDefinition)
			{
				WriteTypeInANewWriterIfNeeded((TypeDefinition)member, writeDocumentation, showCompilerGeneratedMembers);
			}
			else
			{
				WriteAttributes(member);

				if (member is MethodDefinition)
				{
					Write((MethodDefinition)member);
				}
				else if (member is PropertyDefinition)
				{
					Write((PropertyDefinition)member);
				}
				else if (member is EventDefinition)
				{
					Write((EventDefinition)member);
				}
				else if (member is FieldDefinition)
				{
					Write((FieldDefinition)member);
				}
			}

			if (!(member is TypeDefinition) || (member == CurrentType))
			{
				formatter.RemovePreservedIndent(member);
			}

			membersStack.Pop();
		}

		public void SetContext(IMemberDefinition member)
		{
			this.writerContext = this.writerContextService.GetWriterContext(member, this.Language);
		}

        public override void VisitMemberHandleExpression(MemberHandleExpression node)
        {
            WriteKeyword("__ldtoken__");
            WriteToken("(");

            if (node.MemberReference is MethodReference)
            {
                MethodReference methodRef = node.MemberReference as MethodReference;
                WriteReference(GetTypeName(methodRef.DeclaringType), methodRef.DeclaringType);
                WriteToken(".");
                WriteReference(GetMethodName(methodRef), methodRef);
            }
            else if (node.MemberReference is FieldReference)
            {
                FieldReference fieldRef = node.MemberReference as FieldReference;
                WriteReference(GetTypeName(fieldRef.DeclaringType), fieldRef.DeclaringType);
                WriteToken(".");
                WriteReference(GetFieldName(fieldRef), fieldRef);
            }
            else if (node.MemberReference is TypeReference)
            {
                TypeReference typeRef = node.MemberReference as TypeReference;
                WriteReference(GetTypeName(typeRef), typeRef);
            }
            else
            {
                Write(string.Format("Invalid member reference: {0} {1}.", node.MemberReference.GetType(), node.MemberReference.FullName));
            }

            WriteToken(")");
        }
	}
}
