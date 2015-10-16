using System;
using Telerik.JustDecompiler.Decompiler.Caching;
using Telerik.JustDecompiler.Decompiler.WriterContextServices;
using Telerik.JustDecompiler.Languages.CSharp;
using Mono.Cecil;
using Telerik.JustDecompiler.Ast.Expressions;
using Telerik.JustDecompiler.Decompiler;

namespace Telerik.JustDecompiler.Languages.TestCaseWriters
{
    class IntermediateDecompilationCSharpLanguageWriter : TestCaseCSharpWriter
    {
		private readonly bool renameInvalidMembers = true;
		public IntermediateDecompilationCSharpLanguageWriter(IFormatter formatter, bool writeExceptionsAsComments)
			: base(CSharp.CSharp.GetLanguage(CSharpVersion.None), formatter, writeExceptionsAsComments) 
		{
			SimpleWriterContextService swcs = new SimpleWriterContextService(new DefaultDecompilationCacheService(), renameInvalidMembers);
			this.writerContextService = swcs;
		}

		public override void VisitVariableReferenceExpression(VariableReferenceExpression node)
		{
            Write(node.Variable.Name);
        }

        public override void VisitVariableDeclarationExpression(VariableDeclarationExpression node)
        {
            WriteTypeAndName(node.Variable.VariableType, node.Variable.Name);
        }

		protected override string GetMethodName(MethodReference method)
		{
			return method.Name;
		}

		protected override string GetFieldName(FieldReference field)
		{
			return field.Name;
		}

		protected override string GetArgumentName(ParameterReference parameter)
		{
			return parameter.Name;
		}

		protected override ModuleSpecificContext ModuleContext
		{
			get
			{
				try
				{
					return base.ModuleContext;
				}
				catch (NullReferenceException)
				{
					return new ModuleSpecificContext();
				}
			}
		}

		new public void SetContext(IMemberDefinition member)
		{
			this.writerContext = this.writerContextService.GetWriterContext(member, this.Language);
		}
    }
}
