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
using Mono.Cecil;
using Telerik.JustDecompiler.Decompiler;
using System.Collections.Generic;

namespace Telerik.JustDecompiler.Languages.IL
{
    public class IntermediateLanguage : BaseLanguage
    {
        new protected static HashSet<string> languageSpecificGlobalKeywords;
        new protected static HashSet<string> languageSpecificContextualKeywords;

        public override bool IsGlobalKeyword(string word)
        {
            return false;
        }

		public override bool IsOperatorKeyword(string @operator)
		{
			return false;
		}

        static IntermediateLanguage()
        {
            IntermediateLanguage.languageSpecificGlobalKeywords = new HashSet<string>();
            IntermediateLanguage.languageSpecificContextualKeywords = new HashSet<string>();
        }

        public override string VSCodeFileExtension
        {
            get { return ""; }
        }

        public override string VSProjectFileExtension
        {
            get { return ""; }
        }

        public override string Name
        {
            get
            {
                return "IL";
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

        public bool ShouldGenerateBlocks
        {
            get;
            set;
        }

		public override ILanguageWriter GetWriter(IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
        {
            return new IntermediateLanguageWriter(this, formatter, exceptionFormatter, writeExceptionsAsComments, ShouldGenerateBlocks);
        }

		public override IAssemblyAttributeWriter GetAssemblyAttributeWriter(IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
		{
			return new IntermediateLanguageAssemblyAttributeWriter(this, formatter, exceptionFormatter, writeExceptionsAsComments, ShouldGenerateBlocks);
		}

        public override DecompilationPipeline CreatePipeline(MethodDefinition method)
        {
            return new DecompilationPipeline();
        }

		public override string EscapeSymbolBeforeKeyword
		{
			get { throw new System.NotImplementedException(); }
		}

		public override string EscapeSymbolAfterKeyword
		{
			get { throw new System.NotImplementedException(); }
		}

        public override bool IsLanguageKeyword(string word)
        {
            return base.IsLanguageKeyword(word, IntermediateLanguage.languageSpecificGlobalKeywords, IntermediateLanguage.languageSpecificContextualKeywords);
        }
        
        protected override string GetCommentLine()
        {
            return "";
        }

        public override bool SupportsGetterOnlyAutoProperties
        {
            get
            {
                return false;
            }
        }
    }
}