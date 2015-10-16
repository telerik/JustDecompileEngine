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
using System;
using System.Linq;
using Telerik.JustDecompiler.Steps;
using Mono.Cecil;
using Telerik.JustDecompiler.Decompiler;
using System.Collections.Generic;
using Telerik.JustDecompiler.Decompiler.GotoElimination;
using Telerik.JustDecompiler.Steps.SwitchByString;

namespace Telerik.JustDecompiler.Languages.VisualBasic
{
    public class VisualBasic : BaseLanguage
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;
        private static Dictionary<string, string> operators;
		private static HashSet<string> operatorKeywords;

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, VisualBasic.languageSpecificGlobalKeywords);
        }

		protected override bool IsLanguageKeyword(string word, HashSet<string> globalKeywords, HashSet<string> contextKeywords)
		{
			foreach (string globalKeyword in globalKeywords)
			{
				if (globalKeyword.Equals(word, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			foreach (string contextKeyword in contextKeywords)
			{
				if (contextKeyword.Equals(word, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		protected override bool IsGlobalKeyword(string word, HashSet<string> globalKeywords)
		{
			foreach (string globalKeyword in globalKeywords)
			{
				if (globalKeyword.Equals(word, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		public override bool IsOperatorKeyword(string @operator)
		{
			return operatorKeywords.Contains(@operator);
		}

        static VisualBasic()
        {
            VisualBasic.languageSpecificGlobalKeywords = new HashSet<string>();
            VisualBasic.languageSpecificContextualKeywords = new HashSet<string>();
            VisualBasic.operators = new Dictionary<string, string>();
			VisualBasic.operatorKeywords = new HashSet<string>();
            InitializeOperators();
        }

        private static void InitializeOperators()
        {
            //find reliable source for this operators
            //TODO: test all of them
            string[,] operatorPairs = { 
                                          //unary operators
                                          //{ "Decrement", "--" }, { "Increment", "++" }, unavailable
                                          { "UnaryNegation", "-" }, { "UnaryPlus", "+" }, { "LogicalNot", "Not" }, {"True", "IsTrue"}, {"False", "IsFalse"},
                                          { "AddressOf", "&" },{"OnesComplement","Not"},{"PointerDereference","*"},
                                          //binary operators
                                          {"Addition","+"},{"Subtraction","-"},{"Multiply","*"},{"Division","/"},{"Modulus","Mod"},{"ExclusiveOr","Xor"},
                                          {"BitwiseAnd","And"},{"BitwiseOr","Or"},{"LogicalAnd","AndAlso"},{"LogicalOr","OrElse"},{"LeftShift","<<"},{"RightShift",">>"},
                                          {"Equality","="},{"GreaterThan",">"},{"LessThan","<"},{"Inequality","<>"},{"GreaterThanOrEqual",">="},{"LessThanOrEqual","<="},
                                          {"MemberSelection","->"},{"PointerToMemberSelection","->*"},{"Comma",","},//not sure if all these exist in VB
                                          //those can't be redefined, so no point looking for them
                                          //{"RightShiftAssignment",">>="},{"MultiplicationAssignment","*="},
                                          //{"SubtractionAssignment","-="},{"ExclusiveOrAssignment","^="},{"LeftShiftAssignment","<<="},{"ModulusAssignment","%="},
                                          //{"AdditionAssignment","+="},{"BitwiseAndAssignment","&="},{"BitwiseOrAssignment","|="},{"DivisionAssignment","/="},
                                          //other
                                          {"Implicit","CType"},{"Explicit","CType"}, //those are for imlicit/explicit type casts
                                      };
            for (int row = 0; row < operatorPairs.GetLength(0); row++)
            {
                operators.Add(operatorPairs[row, 0], operatorPairs[row, 1]);
            }

			string[] operatorKeywordsArray = { "And", "Or", "Xor", "AndAlso", "OrElse", "Mod", "Is", "IsNot", "Not" };
			for (int operatorKeywordIndex = 0; operatorKeywordIndex < operatorKeywordsArray.Length; operatorKeywordIndex++)
			{
				operatorKeywords.Add(operatorKeywordsArray[operatorKeywordIndex]);
			}
        }

        public override string FloatingLiteralsConstant 
        { 
            get 
            {
                return "!";
            }
        }

        public override string Name
        {
            get
            {
                return "VB.NET no transformation";
            }
        }

        public override string VSCodeFileExtension
        {
            get { return ".vb"; }
        }

        public override string VSProjectFileExtension
        {
            get { return ".vbproj"; }
        }

		public override string CommentLineSymbol
		{
			get { return "'"; }
		}

		public override string DocumentationLineStarter
		{
			get { return "'''"; }
		}

		public override StringComparer IdentifierComparer
		{
			get
			{
				return StringComparer.OrdinalIgnoreCase;
			}
		}

		public override ILanguageWriter GetWriter(IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
        {
			return new VisualBasicWriter(this, formatter, exceptionFormatter, writeExceptionsAsComments);
        }

		public override IAssemblyAttributeWriter GetAssemblyAttributeWriter(IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
		{
			return new VisualBasicAssemblyAttributeWriter(this, formatter, exceptionFormatter, writeExceptionsAsComments);
		}

		//public override DecompilationPipeline CreatePipeline(MethodDefinition method)
		//{
		//    return new DecompilationPipeline(
		//        new StatementDecompiler(BlockOptimization.Basic),
		//        CombinedTransformerStep.Instance,
		//        DeclareTopLevelVariables.Instance);
		//}

		private bool ExistsNonExplicitMember(IMemberDefinition explicitMember, string nonExplicitName)
		{
			if (explicitMember is MethodDefinition)
			{
				return explicitMember.DeclaringType.Methods.Where(t => GetMemberNonExplicitName(t) == nonExplicitName).Count() > 1;
			}

			if (explicitMember is PropertyDefinition)
			{
				return explicitMember.DeclaringType.Properties.Where(t => GetMemberNonExplicitName(t) == nonExplicitName).Count() > 1;
			}

			if (explicitMember is EventDefinition)
			{
				return explicitMember.DeclaringType.Events.Where(t => GetMemberNonExplicitName(t) == nonExplicitName).Count() > 1;
			}

			return false;
		}

		private string GetMemberSpecialExplicitName(IMemberDefinition member)
		{
			string nonExplicitName = GetMemberNonExplicitName(member);

			if (ExistsNonExplicitMember(member, nonExplicitName))
			{
				return "Explicit" + nonExplicitName;
			}
			else
			{
				return nonExplicitName;
			}
		}

		private string GetMemberNonExplicitName(IMemberDefinition member)
		{
			string memberName = member.Name;

			int lastIndex = memberName.LastIndexOf('.');
			if (lastIndex != -1)
			{
				memberName = memberName.Substring(lastIndex + 1);
			}

			return memberName;
		}

		public override string GetExplicitName(IMemberDefinition member)
		{
			string nonExplicitName = GetMemberNonExplicitName(member);

			if (ExistsNonExplicitMember(member, nonExplicitName))
			{
				return GetMemberSpecialExplicitName(member);
			}
			else
			{
				return nonExplicitName;
			}
		}

        public static ILanguage GetLanguage(VisualBasicVersion version)
        {
            switch (version)
            {
                case VisualBasicVersion.None:
                    return new VisualBasic();
                case VisualBasicVersion.V1:
                    return new VisualBasicV1();
                case VisualBasicVersion.V2:
                    return new VisualBasicV2();
                case VisualBasicVersion.V3:
                    return new VisualBasicV3();
                case VisualBasicVersion.V4:
                    return new VisualBasicV4();
                default:
                    throw new ArgumentException();
            }
        }

		public override string EscapeSymbolBeforeKeyword
		{
			get
			{
				return "[";
			}
		}

		public override string EscapeSymbolAfterKeyword
		{
			get
			{
				return "]";
			}
		}

        public override bool IsLanguageKeyword(string word)
        {
            return false;
        }

        protected override string GetCommentLine()
        {
            return @"'";
        }

        public override bool TryGetOperatorName(string operatorName, out string languageOperator)
        {
            bool result = operators.TryGetValue(operatorName, out languageOperator);
            return result;
        }

		public override bool HasOutKeyword
		{
			get
			{
				return false;
			}
		}

        public override bool SupportsGetterOnlyAutoProperties
        {
            get
            {
                return false;
            }
        }
    }

    public class VisualBasicV1 : VisualBasic
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static VisualBasicV1()
        {
            VisualBasicV1.languageSpecificGlobalKeywords = new HashSet<string>(VisualBasic.languageSpecificGlobalKeywords);
            VisualBasicV1.languageSpecificContextualKeywords = new HashSet<string>(VisualBasic.languageSpecificContextualKeywords);

            string[] GlobalKeywords ={"AddHandler","AddressOf","Alias","And","AndAlso","Ansi","As","Assembly","Auto","Boolean","ByRef","Byte","ByVal","Call","Case","Catch",
                                         "CBool","CByte","CChar","CDate","CDec","CDbl","Char","CInt","Class","CLng","CObj","Const","CShort","CSng","CStr","CType","Date",
                                         "Decimal","Declare","Default","Delegate","Dim","DirectCast","Do","Double","Each","Else","ElseIf","End","Enum","Erase","Error","Event",
                                         "Exit","False","Finally","For","Friend","Function","Get","GetType","GoSub","GoTo","Handles","If","Implements","Imports","In","Inherits",
                                         "Integer","Interface","Is","Let","Lib","Like","Long","Loop","Me","Mod","Module","MustInherit","MustOverride","MyBase","MyClass",
                                         "Namespace","New","Next","Not","Nothing","NotInheritable","NotOverridable","Object","On","Option","Optional","Or","OrElse","Overloads",
                                         "Overridable","Overrides","ParamArray","Preserve","Private","Property","Protected","Public","RaiseEvent","ReadOnly","ReDim","REM",
                                         "RemoveHandler","Resume","Return","Select","Set","Shadows","Shared","Short","Single","Static","Step","Stop","String","Structure","Sub",
                                         "SyncLock","Then","Throw","To","True","Try","TypeOf","Unicode","Until","Variant","When","While","With","WithEvents","WriteOnly","Xor",
                                         "#Const","#ExternalSource","#If","Then","#Else","#Region"};

            foreach (string word in GlobalKeywords)
            {
                languageSpecificGlobalKeywords.Add(word);
            }
        }

        public override string Name
        {
            get
            {
                return "VB.NET7";
            }
        }

		public override bool IsLanguageKeyword(string word)
		{
			return IsLanguageKeyword(word, VisualBasicV1.languageSpecificGlobalKeywords, VisualBasicV1.languageSpecificContextualKeywords);
		}

        public override DecompilationPipeline CreatePipeline(MethodDefinition method)
        {
            DecompilationPipeline result = base.CreatePipeline(method);
            result.AddSteps(VisualBasicDecompilationSteps(method, false));
            return result;
        }

        public override DecompilationPipeline CreatePipeline(MethodDefinition method, DecompilationContext context)
        {
            return CreatePipelineInternal(method, context, false);
        }

        public override DecompilationPipeline CreateLambdaPipeline(MethodDefinition method, DecompilationContext context)
        {
            return CreatePipelineInternal(method, context, true);
        }

        public override BlockDecompilationPipeline CreateFilterMethodPipeline(MethodDefinition method, DecompilationContext context)
        {
            return new BlockDecompilationPipeline(VisualBasicFilterMethodDecompilationSteps(method, false), context);
        }

        // This pipeline is used by the PropertyDecompiler to finish the decompilation of properties, which are partially decompiled
        // using the steps from the IntermediateRepresenationPipeline.
        public override BlockDecompilationPipeline CreatePropertyPipeline(MethodDefinition method, DecompilationContext context)
        {
            return new BlockDecompilationPipeline(VisualBasicDecompilationSteps(method, false), context);
        }

        private DecompilationPipeline CreatePipelineInternal(MethodDefinition method, DecompilationContext context, bool inlineAggressively)
        {
            DecompilationPipeline result = base.CreatePipeline(method, context);
            result.AddSteps(VisualBasicDecompilationSteps(method, inlineAggressively));
            return result;
        }

        private IDecompilationStep[] VisualBasicDecompilationSteps(MethodDefinition method, bool inlineAggressively)
        {
            return new IDecompilationStep[]
            {
                new TotalGotoEliminationStep(),
                new AfterGotoCleanupStep(),
                new RebuildAsyncStatementsStep(),
                new RebuildYieldStatementsStep() { Language = this },
                new RemoveDelegateCaching(),
                // RebuildAnonymousDelegatesStep needs to be executed before the RebuildLambdaExpressions step
                new RebuildAnonymousDelegatesStep() { Language = this },
                new RebuildLambdaExpressions() { Language = this, Method = method },
				
                new CombinedTransformerStep() { Language = this, Method = method },
                // new RemoveConditionOnlyVariables(),
                new MergeUnaryAndBinaryExpression(),
                new RemoveLastReturn(),
                new RebuildSwitchByString(),
                new RebuildForeachStatements(),
                new RebuildForeachArrayStatements(),
                new RebuildVBForStatements(),
				new RebuildDoWhileStatements(),
                new RebuildLockStatements(),
                new RebuildFixedStatements(),
                new RebuildUsingStatements(),
                new RenameEnumValues(),
				new FixMethodOverloadsStep(),
				new DetermineCtorInvocationStep(),
                new RebuildExpressionTreesStep(),
                new TransformMemberHandlersStep(),
				new CodePatternsStep(inlineAggressively),
                // TransformCatchClausesFilterExpressionStep needs to be after CodePatternsStep,
                // because it works only if the TernaryConditionPattern has been applied.
                new TransformCatchClausesFilterExpressionStep(),
                new DeduceImplicitDelegates(),
				new CreateIfElseIfStatementsStep(),
				new ParenthesizeExpressionsStep(),
                new RemoveUnusedVariablesStep(),
                // RebuildCatchClausesFilterStep needs to be before DeclareVariablesOnFirstAssignment and after RemoveUnusedVariablesStep.
                // RebuildCatchClausesFilterStep contains pattern matching and need to be after TransformCatchClausesFilterExpressionStep.
                new RebuildCatchClausesFilterStep() { Language = this },
                new DeclareVariablesOnFirstAssignment(),
                new DeclareTopLevelVariables(),
                // There were a lot of issues when trying to merge the SelfAssignment step with the CombinedTransformerStep.
                new SelfAssignement(),
				new RenameSplitPropertiesMethodsAndBackingFields(),
                new RenameVBVariables() { Language = this },
				new CastEnumsToIntegersStep(),
				new CastIntegersStep(),
				new ArrayVariablesStep(),
				new UnsafeMethodBodyStep(),
				new DetermineDestructorStep(),
				new DependsOnAnalysisStep(),
				new DetermineNotSupportedVBCodeStep(),
            };
        }

        private IDecompilationStep[] VisualBasicFilterMethodDecompilationSteps(MethodDefinition method, bool inlineAggressively)
        {
            return new IDecompilationStep[]
            {
                new DeclareVariablesOnFirstAssignment(),
                new DeclareTopLevelVariables(),
                // There were a lot of issues when trying to merge the SelfAssignment step with the CombinedTransformerStep.
                new SelfAssignement(),
				new RenameSplitPropertiesMethodsAndBackingFields(),
                new RenameVBVariables() { Language = this },
				new CastEnumsToIntegersStep(),
				new CastIntegersStep(),
				new ArrayVariablesStep(),
				new UnsafeMethodBodyStep(),
				new DetermineDestructorStep(),
				new DependsOnAnalysisStep(),
				new DetermineNotSupportedVBCodeStep(),
            };
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, VisualBasicV1.languageSpecificGlobalKeywords);
        }
    }

    public class VisualBasicV2 : VisualBasicV1
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static VisualBasicV2()
        {
            VisualBasicV2.languageSpecificGlobalKeywords = new HashSet<string>(VisualBasicV1.languageSpecificGlobalKeywords);
            VisualBasicV2.languageSpecificContextualKeywords = new HashSet<string>(VisualBasicV1.languageSpecificContextualKeywords);
        }

        public override string Name
        {
            get
            {
                return "VB.NET8";
            }
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, VisualBasicV2.languageSpecificGlobalKeywords);
        }
    }

    public class VisualBasicV3 : VisualBasicV2
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static VisualBasicV3()
        {
            VisualBasicV3.languageSpecificGlobalKeywords = new HashSet<string>(VisualBasicV2.languageSpecificGlobalKeywords);
            VisualBasicV3.languageSpecificContextualKeywords = new HashSet<string>(VisualBasicV2.languageSpecificContextualKeywords);
        }

        public override string Name
        {
            get
            {
                return "VB.NET9";
            }
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, VisualBasicV3.languageSpecificGlobalKeywords);
        }
    }

    public class VisualBasicV4 : VisualBasicV3
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static VisualBasicV4()
        {
            VisualBasicV4.languageSpecificGlobalKeywords = new HashSet<string>(VisualBasicV3.languageSpecificGlobalKeywords);
            VisualBasicV4.languageSpecificContextualKeywords = new HashSet<string>(VisualBasicV3.languageSpecificContextualKeywords);
        }

        public override string Name
        {
            get
            {
                return "VB.NET10";
            }
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, VisualBasicV4.languageSpecificGlobalKeywords);
        }

        public override bool SupportsGetterOnlyAutoProperties
        {
            get
            {
                // TODO: Fix when VB14 is added
                return true;
            }
        }
    }

    public enum VisualBasicVersion
    {
        None,
        V1,
        V2,
        V3,
        V4,
    }
}