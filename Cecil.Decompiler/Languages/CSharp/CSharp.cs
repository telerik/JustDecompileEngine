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

namespace Telerik.JustDecompiler.Languages.CSharp
{
    public class CSharp : BaseLanguage
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;
        private static Dictionary<string, string> operators;

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, CSharp.languageSpecificGlobalKeywords);
        }

        static CSharp()
        {
            CSharp.languageSpecificGlobalKeywords = new HashSet<string>();
            CSharp.languageSpecificContextualKeywords = new HashSet<string>();
            CSharp.operators = new Dictionary<string, string>();
            InitializeOperators();
        }

        private static void InitializeOperators()
        {
            //taken from ECMA-355 documentation
            //TODO: test all of them
            string[,] operatorPairs = { 
                                          //unary operators
                                          { "Decrement", "--" }, { "Increment", "++" }, { "UnaryNegation", "-" }, { "UnaryPlus", "+" }, { "LogicalNot", "!" },
                                          {"OnesComplement","~"}, {"True", "true"}, {"False", "false"},
                                          //{ "AddressOf", "&" },{"PointerDereference","*"}, to test those we will need to run unsafe code
                                          //binary operators
                                          {"Addition","+"},{"Subtraction","-"},{"Multiply","*"},{"Division","/"},{"Modulus","%"},{"ExclusiveOr","^"},
                                          {"BitwiseAnd","&"},{"BitwiseOr","|"},
                                          //{"LogicalAnd","&&"},{"LogicalOr","||"},
                                          {"LeftShift","<<"},{"RightShift",">>"},
                                          {"Equality","=="},{"GreaterThan",">"},{"LessThan","<"},{"Inequality","!="},{"GreaterThanOrEqual",">="},{"LessThanOrEqual","<="},
                                          {"MemberSelection","->"},{"PointerToMemberSelection","->*"},{"Comma",","},
                                          //those can't be redefined, so no point looking for them
                                          //{"RightShiftAssignment",">>="},{"MultiplicationAssignment","*="},
                                          //{"SubtractionAssignment","-="},{"ExclusiveOrAssignment","^="},{"LeftShiftAssignment","<<="},{"ModulusAssignment","%="},
                                          //{"AdditionAssignment","+="},{"BitwiseAndAssignment","&="},{"BitwiseOrAssignment","|="},{"DivisionAssignment","/="},
                                          //other
                                          {"Implicit",""},{"Explicit",""}, //those are for imlicit/explicit type casts
                                      };
            for (int row = 0; row < operatorPairs.GetLength(0); row++)
            {
                operators.Add(operatorPairs[row, 0], operatorPairs[row, 1]);
            }
        }

        public override string FloatingLiteralsConstant
        {
            get
            {
                return "f";
            }
        }

        public override string Name
        {
            get
            {
                return "C# no transformation";
            }
        }

        public override string VSCodeFileExtension
        {
            get
            {
                return ".cs";
            }
        }

        public override string VSProjectFileExtension
        {
            get
            {
                return ".csproj";
            }
        }

		public override string EscapeSymbolBeforeKeyword
		{
			get
			{
				return "@";
			}
		}

		public override string EscapeSymbolAfterKeyword
		{
			get
			{
				return "";
			}
		}

        public override string CommentLineSymbol
        {
            get { return "//"; }
        }

        public override string DocumentationLineStarter
        {
            get { return "///"; }
        }

        public override ILanguageWriter GetWriter(IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
        {
            return new CSharpWriter(this, formatter, exceptionFormatter, writeExceptionsAsComments);
        }

        public override IAssemblyAttributeWriter GetAssemblyAttributeWriter(IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
        {
            return new CSharpAssemblyAttributeWriter(this, formatter, exceptionFormatter, writeExceptionsAsComments);
        }

        public static ILanguage GetLanguage(CSharpVersion version)
        {
            switch (version)
            {
                case CSharpVersion.None:
                    return new CSharp();
                case CSharpVersion.V1:
                    return new CSharpV1();
                case CSharpVersion.V2:
                    return new CSharpV2();
                case CSharpVersion.V3:
                    return new CSharpV3();
                case CSharpVersion.V4:
                    return new CSharpV4();
                default:
                    throw new ArgumentException();
            }
        }

        public override bool IsLanguageKeyword(string word)
        {
            return base.IsLanguageKeyword(word, CSharp.languageSpecificGlobalKeywords, CSharp.languageSpecificContextualKeywords);
        }

		public override bool IsOperatorKeyword(string @operator)
		{
			return false;
		}

        protected override string GetCommentLine()
        {
            return @"//";
        }

        public override bool TryGetOperatorName(string operatorName, out string languageOperator)
        {
            bool result = operators.TryGetValue(operatorName, out languageOperator);
            return result;
        }

        public override bool SupportsGetterOnlyAutoProperties
        {
            get
            {
                return false;
            }
        }
    }

    public class CSharpV1 : CSharp
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static CSharpV1()
        {
            CSharpV1.languageSpecificGlobalKeywords = new HashSet<string>(CSharp.languageSpecificGlobalKeywords);

            //list taken from http://msdn.microsoft.com/en-us/library/x53a06bb.aspx -> MSDN list of C# keywords

            string[] GlobalKeywords =
            {
                "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue","decimal",
                "default","delegate","do","double","else","enum","event","explicit","extern","false","finally","fixed","float",
                "for","foreach","goto","if","implicit","in","int","interface","internal","is","lock","long","namespace","new",
                "null","object","operator","out","override","params","private","protected","public","readonly","ref","return",
                "sbyte","sealed","short","sizeof","stackalloc","static","string","struct","switch","this","throw","true","try",
                "typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while"
            };
            foreach (string word in GlobalKeywords)
            {
                CSharpV1.languageSpecificGlobalKeywords.Add(word);
            }

            CSharpV1.languageSpecificContextualKeywords = new HashSet<string>(CSharp.languageSpecificContextualKeywords);

            //list taken from http://msdn.microsoft.com/en-us/library/x53a06bb.aspx -> MSDN list of C# contextual keywords

            string[] contextualKeywords =
            {
                "add","alias","ascending","descending","dynamic","from","get","global","group","into","join","let","orderby",
                "partial","remove","select","set","value","var","where","yield"
            };
            foreach (string word in contextualKeywords)
            {
                CSharpV1.languageSpecificContextualKeywords.Add(word);
            }
        }

        public override string Name
        {
            get
            {
                return "C#1";
            }
        }

        public override bool IsLanguageKeyword(string word)
        {
            return base.IsLanguageKeyword(word, CSharpV1.languageSpecificGlobalKeywords, CSharpV1.languageSpecificContextualKeywords);
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, CSharpV1.languageSpecificGlobalKeywords);
        }

        public override DecompilationPipeline CreatePipeline(MethodDefinition method)
        {
            DecompilationPipeline result = base.CreatePipeline(method);
            result.AddSteps(CSharpDecompilationSteps(method, false));
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
            return new BlockDecompilationPipeline(CSharpFilterMethodDecompilationSteps(method, false), context);
        }

        // This pipeline is used by the PropertyDecompiler to finish the decompilation of properties, which are partially decompiled
        // using the steps from the IntermediateRepresenationPipeline.
        public override BlockDecompilationPipeline CreatePropertyPipeline(MethodDefinition method, DecompilationContext context)
        {
            return new BlockDecompilationPipeline(CSharpDecompilationSteps(method, false), context);
        }

        private DecompilationPipeline CreatePipelineInternal(MethodDefinition method, DecompilationContext context, bool inlineAggressively)
        {
            DecompilationPipeline result = base.CreatePipeline(method, context);
            result.AddSteps(CSharpDecompilationSteps(method, inlineAggressively));
            return result;
        }

        internal IDecompilationStep[] CSharpDecompilationSteps(MethodDefinition method, bool inlineAggressively)
        {
            return new IDecompilationStep[]
            {
                new OutParameterAssignmentAnalysisStep(),
                new RebuildAsyncStatementsStep(),
                new RebuildYieldStatementsStep() { Language = this },
                new RemoveDelegateCaching(),
                // RebuildAnonymousDelegatesStep needs to be executed before the RebuildLambdaExpressions step
                new RebuildAnonymousDelegatesStep() { Language = this },
                new RebuildLambdaExpressions() { Language = this, Method = method },
                new ResolveDynamicVariablesStep(),
                new GotoCancelation(),
                new CombinedTransformerStep() { Language = this, Method = method },
                new MergeUnaryAndBinaryExpression(),
                new RemoveLastReturn(),
				new RebuildSwitchByString(),
				new RebuildForeachStatements(),
                new RebuildForeachArrayStatements(),
                new RebuildForStatements(),
                new RebuildLockStatements(),
                new RebuildFixedStatements(),
                new RebuildUsingStatements(),
                new RenameEnumValues(),
				new FixMethodOverloadsStep(),
                new RebuildExpressionTreesStep(),
                new TransformMemberHandlersStep(),
				new CodePatternsStep(inlineAggressively),
                // TransformCatchClausesFilterExpressionStep needs to be after CodePatternsStep,
                // because it works only if the TernaryConditionPattern has been applied.
                new TransformCatchClausesFilterExpressionStep(),
                new DetermineCtorInvocationStep(),
                new DeduceImplicitDelegates(),
                new RebuildLinqQueriesStep(),
				new CreateIfElseIfStatementsStep(),
				new ParenthesizeExpressionsStep(),
                new RemoveUnusedVariablesStep(),
                // RebuildCatchClausesFilterStep needs to be before DeclareVariablesOnFirstAssignment and after RemoveUnusedVariablesStep.
                // RebuildCatchClausesFilterStep contains pattern matching and need to be after TransformCatchClausesFilterExpressionStep.
                new RebuildCatchClausesFilterStep() { Language = this },
                new DeclareVariablesOnFirstAssignment(),
                new DeclareTopLevelVariables(),
                new AssignOutParametersStep(),
                // There were a lot of issues when trying to merge the SelfAssignment step with the CombinedTransformerStep.
                new SelfAssignement(),
				new RenameSplitPropertiesMethodsAndBackingFields(),
                new RenameVariables() { Language = this },
				new CastEnumsToIntegersStep(),
				new CastIntegersStep(),
				new ArrayVariablesStep(),
				new CaseGotoTransformerStep(),
				new UnsafeMethodBodyStep(),
				new DetermineDestructorStep(),
                // DependsOnAnalysisStep must be always last step, because it make analysis on the final decompilation result.
				new DependsOnAnalysisStep(),
            };
        }

        private IDecompilationStep[] CSharpFilterMethodDecompilationSteps(MethodDefinition method, bool inlineAggressively)
        {
            return new IDecompilationStep[]
            {
                new DeclareVariablesOnFirstAssignment(),
                new DeclareTopLevelVariables(),
                new AssignOutParametersStep(),
                // There were a lot of issues when trying to merge the SelfAssignment step with the CombinedTransformerStep.
                new SelfAssignement(),
				new RenameSplitPropertiesMethodsAndBackingFields(),
                new RenameVariables() { Language = this },
				new CastEnumsToIntegersStep(),
				new CastIntegersStep(),
				new ArrayVariablesStep(),
				new CaseGotoTransformerStep(),
				new UnsafeMethodBodyStep(),
				new DetermineDestructorStep(),
                // DependsOnAnalysisStep must be always last step, because it make analysis on the final decompilation result.
				new DependsOnAnalysisStep(),
            };
        }
    }

    public class CSharpV2 : CSharpV1
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static CSharpV2()
        {
            CSharpV2.languageSpecificGlobalKeywords = new HashSet<string>(CSharpV1.languageSpecificGlobalKeywords);
            CSharpV2.languageSpecificContextualKeywords = new HashSet<string>(CSharpV1.languageSpecificContextualKeywords);
        }

        public override string Name
        {
            get
            {
                return "C#2";
            }
        }

        public override bool IsLanguageKeyword(string word)
        {
            return base.IsLanguageKeyword(word, CSharpV2.languageSpecificGlobalKeywords, CSharpV2.languageSpecificContextualKeywords);
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, CSharpV2.languageSpecificGlobalKeywords);
        }
    }

    public class CSharpV3 : CSharpV2
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static CSharpV3()
        {
            CSharpV3.languageSpecificGlobalKeywords = new HashSet<string>(CSharpV2.languageSpecificGlobalKeywords);
            CSharpV3.languageSpecificContextualKeywords = new HashSet<string>(CSharpV2.languageSpecificContextualKeywords);
        }

        public override string Name
        {
            get
            {
                return "C#3";
            }
        }

        public override bool IsLanguageKeyword(string word)
        {
            return base.IsLanguageKeyword(word, CSharpV3.languageSpecificGlobalKeywords, CSharpV3.languageSpecificContextualKeywords);
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, CSharpV3.languageSpecificGlobalKeywords);
        }
    }

    public class CSharpV4 : CSharpV3
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        static CSharpV4()
        {
            CSharpV4.languageSpecificGlobalKeywords = new HashSet<string>(CSharpV3.languageSpecificGlobalKeywords);
            CSharpV4.languageSpecificContextualKeywords = new HashSet<string>(CSharpV3.languageSpecificContextualKeywords);
        }
        public override string Name
        {
            get
            {
                return "C#4";
            }
        }

        public override bool IsLanguageKeyword(string word)
        {
            return base.IsLanguageKeyword(word, CSharpV4.languageSpecificGlobalKeywords, CSharpV4.languageSpecificContextualKeywords);
        }

        public override bool IsGlobalKeyword(string word)
        {
            return IsGlobalKeyword(word, CSharpV4.languageSpecificGlobalKeywords);
        }

        public override bool SupportsGetterOnlyAutoProperties
        {
            get
            {
                // TODO: Fix when csharp languages are fixed and CSharpV6 is introduced.
                return true;
            }
        }
    }

    public enum CSharpVersion
    {
        None,
        V1,
        V2,
        V3,
        V4,
    }
}