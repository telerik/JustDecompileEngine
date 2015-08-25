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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Extensions;
using Mono.Collections.Generic;
using Telerik.JustDecompiler.Ast;
using Telerik.JustDecompiler.Ast.Expressions;
using Telerik.JustDecompiler.Ast.Statements;
using Telerik.JustDecompiler.Decompiler;
using Telerik.JustDecompiler.Decompiler.WriterContextServices;

namespace Telerik.JustDecompiler.Languages
{
    public abstract class BaseLanguageWriter : BaseCodeVisitor, ILanguageWriter
    {
        protected bool isStopped;
        protected IFormatter formatter;
        protected Stack<IMemberDefinition> membersStack = new Stack<IMemberDefinition>();
        protected bool isWritingComment = false;
        protected WriterContext writerContext;
        protected IWriterContextService writerContextService;
        protected TypeDefinition CurrentType { get; set; }
        protected bool WriteExceptionsAsComments { get; private set; }
        protected IExceptionFormatter exceptionFormatter;
        protected WritingInfo currentWritingInfo;
        protected List<WritingInfo> writingInfos;

        new public ILanguage Language { get; private set; }

        public BaseLanguageWriter(ILanguage language, IFormatter formatter, IExceptionFormatter exceptionFormatter, bool writeExceptionsAsComments)
        {
            this.Language = language;
            this.formatter = formatter;
            this.exceptionFormatter = exceptionFormatter;
            this.WriteExceptionsAsComments = writeExceptionsAsComments;
        }

        protected virtual AssemblySpecificContext AssemblyContext
        {
            get
            {
				return this.writerContext.AssemblyContext;
            }
        }

        protected virtual ModuleSpecificContext ModuleContext
        {
            get
            {
				return this.writerContext.ModuleContext;
            }
        }

        protected virtual TypeSpecificContext TypeContext
        {
            get
            {
                return this.writerContext.TypeContext;
            }
        }

        protected MethodSpecificContext MethodContext
        {
            get
            {
                if (membersStack.Count > 0)
                {
                    return GetMethodContext(membersStack.Peek());
                }
                else
                {
                    return null;
                }
            }
        }

        protected MethodDefinition CurrentMethod
        {
            get
            {
                if (membersStack.Count > 0 && membersStack.Peek() is MethodDefinition)
                {
                    return membersStack.Peek() as MethodDefinition;
                }

                return null;
            }
        }

        protected bool MethodContextsMissing
        {
            get
            {
                return this.writerContext.MethodContexts.Count == 0;
            }
        }

        protected MethodSpecificContext GetMethodContext(IMemberDefinition member)
        {
            string memberFullName = Utilities.GetMemberUniqueName(member);
            MethodSpecificContext methodContext;
            MethodSpecificContext result = this.writerContext.MethodContexts.TryGetValue(memberFullName, out methodContext) ? methodContext : null;
            return result;
        }

        protected Statement GetStatement(MethodDefinition method)
        {
            Statement statement;
            if (writerContext.DecompiledStatements.TryGetValue(Utilities.GetMemberUniqueName(method), out statement))
            {
                return statement;
            }
            else
            {
                return null;
            }
        }

		protected bool IsTypeInCurrentModule(TypeReference type)
		{
			TypeDefinition typeDefinition = type.Resolve();

			return typeDefinition != null && typeDefinition.Module.FilePath == this.ModuleContext.Module.FilePath;
		}

		protected string GetCurrentModuleTypeName(TypeReference type)
		{
			TypeDefinition typeDefinition = type.Resolve();
			return this.ModuleContext.RenamedMembersMap[typeDefinition.MetadataToken.ToUInt32()];
		}
		
		protected virtual string GetTypeName(TypeReference type)
		{
			if (IsTypeInCurrentModule(type))
			{
				return GetCurrentModuleTypeName(type);
			}

			return Utilities.EscapeTypeName(GenericHelper.GetNonGenericName(type.Name), this.Language);
		}
		
        protected virtual string GetMethodName(MethodReference method)
        {
            MethodDefinition methodDefinition = method.Resolve();

			if (methodDefinition != null && methodDefinition.Module.FilePath == this.ModuleContext.Module.FilePath)
			{
				if (this.TypeContext.MethodDefinitionToNameMap.ContainsKey(methodDefinition))
				{
					return this.TypeContext.MethodDefinitionToNameMap[methodDefinition];
				}

                if (this.TypeContext.GeneratedMethodDefinitionToNameMap.ContainsKey(methodDefinition))
                {
                    return this.TypeContext.GeneratedMethodDefinitionToNameMap[methodDefinition];
                }

				return this.ModuleContext.RenamedMembersMap[methodDefinition.MetadataToken.ToUInt32()];
			}

			return Utilities.EscapeName(GenericHelper.GetNonGenericName(method.Name), this.Language);
        }

		protected virtual string GetFieldName(FieldReference field)
		{
			FieldDefinition fieldDefinition = field.Resolve();

			if (fieldDefinition != null && fieldDefinition.Module.FilePath == this.ModuleContext.Module.FilePath)
			{

				if (this.TypeContext != null && this.TypeContext.BackingFieldToNameMap.ContainsKey(fieldDefinition))
				{
					return this.TypeContext.BackingFieldToNameMap[fieldDefinition];
				}

				return this.ModuleContext.RenamedMembersMap[fieldDefinition.MetadataToken.ToUInt32()];
			}

			return Utilities.EscapeName(GenericHelper.GetNonGenericName(field.Name), this.Language);
		}

		protected virtual string GetPropertyName(PropertyReference property)
		{
			PropertyDefinition propertyDefinition = property.Resolve();

			if (propertyDefinition != null && propertyDefinition.Module.FilePath == this.ModuleContext.Module.FilePath)
			{
				try
				{
					return this.ModuleContext.RenamedMembersMap[propertyDefinition.MetadataToken.ToUInt32()];
				}
				catch (Exception ex)
				{
					Write(propertyDefinition.MetadataToken.ToUInt32().ToString());
					WriteLine();
					Write(ModuleContext.RenamedMembersMap.Count.ToString());
					WriteLine();
					foreach (KeyValuePair<uint, string> pair in ModuleContext.RenamedMembersMap)
					{
						Write(pair.Key.ToString() + " " + pair.Value);
						WriteLine();
					}
					throw ex;
				}
			}

			return Utilities.EscapeName(GenericHelper.GetNonGenericName(property.Name), this.Language);
		}

		protected virtual string GetEventName(EventReference @event)
		{
			EventDefinition eventDefinition = @event.Resolve();

			if (eventDefinition != null && eventDefinition.Module.FilePath == this.ModuleContext.Module.FilePath)
			{

				return this.ModuleContext.RenamedMembersMap[eventDefinition.MetadataToken.ToUInt32()];
			}

			return Utilities.EscapeName(GenericHelper.GetNonGenericName(@event.Name), this.Language);
		}

		protected string GetMemberName(MemberReference member)
		{
			if (member is MethodReference)
			{
				return GetMethodName(member as MethodReference);
			}

			if (member is FieldReference)
			{
				return GetFieldName(member as FieldReference);
			}

			if (member is PropertyReference)
			{
				return GetPropertyName(member as PropertyReference);
			}

			if (member is EventReference)
			{
				return GetEventName(member as EventReference);
			}

			if (member is TypeReference)
			{
				return GetTypeName(member as TypeReference);
			}

			throw new NotSupportedException("Unexpected member type.");
		}

        internal virtual void WriteToken(string token)
        {
            formatter.WriteToken(token);
        }

        internal virtual void WriteSpace()
        {
            formatter.WriteSpace();
        }

        internal virtual void WriteLine()
        {
            formatter.WriteLine();
        }

        internal virtual void WriteKeyword(string keyword)
        {
            formatter.WriteKeyword(keyword);
        }

        internal virtual void Write(string str)
        {
            formatter.Write(str);
        }

        internal virtual void WriteLiteral(string literal)
        {
            formatter.WriteLiteral(literal);
        }

        internal virtual void WriteIdentifier(string name, object identifier)
        {
            formatter.WriteIdentifier(name, identifier);
        }

        internal virtual void WriteDefinition(string name, object definition)
        {
            formatter.WriteDefinition(name, definition);
        }

        internal virtual void WriteReference(string name, object reference)
        {
            formatter.WriteReference(name, reference);
        }

        internal virtual void WriteNotResolvedReference(string name, MemberReference memberReference, string errorMessage)
        {
            formatter.WriteNotResolvedReference(name, memberReference, errorMessage);
        }

        internal virtual void Indent()
        {
            formatter.Indent();
        }

        internal virtual void Outdent()
        {
            formatter.Outdent();
        }

        protected void UpdateWritingInfo(WriterContext writerContext, WritingInfo writingInfo)
        {
            foreach (MethodSpecificContext methodContext in writerContext.MethodContexts.Values)
            {
                if (methodContext != null)
                {
                    writingInfo.ControlFlowGraphs.Add(methodContext.Method, methodContext.ControlFlowGraph);
                    writingInfo.MethodsVariableDefinitionToNameMap.Add(methodContext.Method, methodContext.VariableDefinitionToNameMap);
                }
            }

			foreach (IMemberDefinition memberWithExceptionsWhileDecompiling in writerContext.TypeContext.ExceptionWhileDecompiling)
			{
				if (!writingInfo.MembersWithExceptions.Contains(memberWithExceptionsWhileDecompiling.MetadataToken.ToUInt32()))
				{
					writingInfo.MembersWithExceptions.Add(memberWithExceptionsWhileDecompiling.MetadataToken.ToUInt32());
				}
			}

            writingInfo.AutoImplementedProperties.UnionWith(writerContext.TypeContext.AutoImplementedProperties);
            writingInfo.AutoImplementedEvents.UnionWith(writerContext.TypeContext.AutoImplementedEvents);
            foreach (GeneratedMethod generatedFilterMethod in writerContext.TypeContext.GeneratedFilterMethods)
            {
                writingInfo.GeneratedFilterMethods.Add(generatedFilterMethod.Method);
            }
        }

        public virtual List<WritingInfo> Write(IMemberDefinition member, IWriterContextService writerContextService, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
        {
            this.writerContextService = writerContextService;
            this.writerContext = writerContextService.GetWriterContext(member, Language);

            if (member is TypeDefinition)
            {
                this.CurrentType = member as TypeDefinition;
            }

            this.currentWritingInfo = new WritingInfo(member);
            UpdateWritingInfo(this.writerContext, this.currentWritingInfo);
            this.writingInfos = new List<WritingInfo>();
            this.writingInfos.Add(this.currentWritingInfo);

            WriteInternal(member, writeDocumentation, showCompilerGeneratedMembers);

            return writingInfos;
        }

		protected void AddMemberAttributesOffsetSpan(uint memberToken, int startIndex, int endIndex)
		{
			if (startIndex < endIndex)
			{
				this.currentWritingInfo.MemberTokenToAttributesMap.Add(memberToken, new OffsetSpan(startIndex, endIndex - 1));
			}
		}

		protected void AddMemberDocumentationOffsetSpan(uint memberToken, int startIndex, int endIndex)
		{
			if (startIndex < endIndex)
			{
				this.currentWritingInfo.MemberTokenToDocumentationMap.Add(memberToken, new OffsetSpan(startIndex, endIndex - 1));
			}
		}

        protected virtual void WriteInternal(IMemberDefinition member, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
        {
			uint memberToken = member.MetadataToken.ToUInt32();

            if (isStopped)
                return;

            membersStack.Push(member);

            if (!(member is TypeDefinition) || (member == CurrentType))
            {
                formatter.PreserveIndent(member);
            }

            try
            {
                if (writeDocumentation)
                {
					int documentationStartIndex = this.formatter.CurrentPosition;
                    WriteDocumentation(member);
					AddMemberDocumentationOffsetSpan(memberToken, documentationStartIndex, this.formatter.CurrentPosition);
                }
                if (member is TypeDefinition)
                {
                    formatter.WriteMemberDeclaration(member);

                    WriteTypeInANewWriterIfNeeded((TypeDefinition)member, writeDocumentation, showCompilerGeneratedMembers);
                }
                else
                {
					int attributesStartIndex = this.formatter.CurrentPosition;
                    WriteAttributes(member);
					AddMemberAttributesOffsetSpan(memberToken, attributesStartIndex, this.formatter.CurrentPosition);

					int decompiledCodeStartIndex = this.formatter.CurrentPosition;

                    if (member is MethodDefinition)
                    {
                        formatter.WriteMemberDeclaration(member);

                        Write((MethodDefinition)member);
                    }
                    else if (member is PropertyDefinition)
                    {
                        formatter.WriteMemberDeclaration(member);

                        Write((PropertyDefinition)member);
                    }
                    else if (member is EventDefinition)
                    {
                        formatter.WriteMemberDeclaration(member);

                        Write((EventDefinition)member);
                    }
                    else if (member is FieldDefinition)
                    {
                        formatter.WriteMemberDeclaration(member);

                        Write((FieldDefinition)member);
                    }

					this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(memberToken, new OffsetSpan(decompiledCodeStartIndex, this.formatter.CurrentPosition - 1));
                }
            }
            catch (Exception ex)
            {
                ExceptionStatement exceptionStatement = new ExceptionStatement(ex, member);
                Visit(exceptionStatement);
                formatter.RestoreIndent(member);
                this.currentWritingInfo.ExceptionsWhileWriting.Add(member);
				this.currentWritingInfo.MembersWithExceptions.Add(memberToken);

                if (member is PropertyDefinition)
                {
                    PropertyDefinition property = member as PropertyDefinition;

                    if (property.GetMethod != null)
                    {
                        this.currentWritingInfo.ExceptionsWhileWriting.Add(property.GetMethod);
                    }

                    if (property.SetMethod != null)
                    {
                        this.currentWritingInfo.ExceptionsWhileWriting.Add(property.SetMethod);
                    }
                }

                if (member is EventDefinition)
                {
                    EventDefinition @event = member as EventDefinition;

                    if (@event.AddMethod != null)
                    {
                        this.currentWritingInfo.ExceptionsWhileWriting.Add(@event.AddMethod);
                    }

                    if (@event.RemoveMethod != null)
                    {
                        this.currentWritingInfo.ExceptionsWhileWriting.Add(@event.RemoveMethod);
                    }

                    if (@event.InvokeMethod != null)
                    {
                        this.currentWritingInfo.ExceptionsWhileWriting.Add(@event.InvokeMethod);
                    }
                }
            }

            if (!(member is TypeDefinition) || (member == CurrentType))
            {
                formatter.RemovePreservedIndent(member);
            }

            membersStack.Pop();
        }

        protected virtual bool WriteMember(IMemberDefinition member, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
        {
            membersStack.Push(member);

            if (writeDocumentation)
            {
				int documentationStartIndex = this.formatter.CurrentPosition;
                WriteDocumentation(member);
				AddMemberDocumentationOffsetSpan(member.MetadataToken.ToUInt32(), documentationStartIndex, this.formatter.CurrentPosition);
            }


            // the documentation has already been written.
            WriteInternal(member, false, showCompilerGeneratedMembers: showCompilerGeneratedMembers);


            membersStack.Pop();
            return true;
        }

        protected void WriteDocumentation(IMemberDefinition member)
        {
            string memberDocumentation = XmlDocumentationReaders.DocumentationManager.GetDocumentationForMember(member);

            if (!string.IsNullOrEmpty(memberDocumentation))
            {
                WriteDocumentation(memberDocumentation);
                WriteLine();
            }
        }

        private void WriteDocumentation(string memberDocumentation)
        {
            this.formatter.WriteDocumentationStartBlock();
            bool notFirstLine = false;
            bool isDocumentTag = false;
            using (System.IO.StringReader sr = new System.IO.StringReader(memberDocumentation))
            {
                for (string currentLine = sr.ReadLine(); currentLine != null; currentLine = sr.ReadLine())
                {
                    if (notFirstLine)
                    {
                        WriteLine();
                    }
                    notFirstLine = true;
                    this.formatter.WriteDocumentationTag(Language.DocumentationLineStarter + " ");
                    isDocumentTag = WriteDocumentationLine(currentLine, isDocumentTag);
                }
            }
            this.formatter.WriteEndBlock();
        }

        private bool WriteDocumentationLine(string currentLine, bool isDocumentTag)
        {
            int tagStartIndex = -1;
            int endTagIndex = -1;

            if (isDocumentTag)
            {
                // the line is inside a documentation tag, that was opened earlier.
                for (int i = 0; i < currentLine.Length; i++)
                {
                    if (currentLine[i] == '>')
                    {
                        endTagIndex = i;
                        break;
                    }
                }

                if (endTagIndex == -1)
                {
                    // the whole line is still inside a tag
                    this.formatter.WriteDocumentationTag(currentLine);
                    return true;
                }
                else
                {
                    string tagEnd = currentLine.Substring(0, endTagIndex + 1);
                    string remaining = currentLine.Substring(endTagIndex + 1);

                    this.formatter.WriteDocumentationTag(tagEnd);
                    return WriteDocumentationLine(remaining, false);
                }
            }

            // find starting tag
            for (int i = 0; i < currentLine.Length; i++)
            {
                if (currentLine[i] == '<')
                {
                    tagStartIndex = i;
                    break;
                }
            }
            if (tagStartIndex == -1)// the whole line is just comment
            {
                this.formatter.WriteComment(currentLine);
                return false;
            }

            string beforeTag = currentLine.Substring(0, tagStartIndex);
            this.formatter.WriteComment(beforeTag);

            // find end tag
            for (int i = tagStartIndex + 1; i < currentLine.Length; i++)
            {
                if (currentLine[i] == '>')
                {
                    endTagIndex = i;
                    break;
                }
            }

            if (endTagIndex == -1)
            {
                // the tag isn't closed on this line.
                this.formatter.WriteDocumentationTag(currentLine.Substring(tagStartIndex));
                return true;
            }
            else
            {
                string tag = currentLine.Substring(tagStartIndex, (endTagIndex - tagStartIndex) + 1);
                this.formatter.WriteDocumentationTag(tag);
                string remainingLine = currentLine.Substring(endTagIndex + 1);
                return WriteDocumentationLine(remainingLine, false);
            }
        }

        public virtual void WriteTypeDeclarationsOnly(TypeDefinition member, IWriterContextService writerContextService, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
        {
            this.writerContextService = writerContextService;
            this.writerContext = writerContextService.GetWriterContext(member, Language);
            this.currentWritingInfo = new WritingInfo(member);
            WriteTypeDeclarationsOnlyInternal(member, writeDocumentation, showCompilerGeneratedMembers);
        }

        protected virtual void WriteTypeDeclarationsOnlyInternal(TypeDefinition member, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
        {
            WriteTypeNamespaceStart(member);
            WriteAttributes(member);
            WriteTypeInternal(member, (x) => WriteMemberDeclaration(x, showCompilerGeneratedMembers, writeDocumentation), showCompilerGeneratedMembers);
            WriteTypeNamespaceEnd(member);
        }

        public virtual void WriteException(Exception ex, IMemberDefinition member)
        {
            string[] exceptionLines = exceptionFormatter.Format(ex, member.FullName, member.DeclaringType.Module.FilePath);

            if (this.WriteExceptionsAsComments)
            {
                var commentedExceptionLines = exceptionLines.Select(exceptionLine => Language.CommentLineSymbol + " " + exceptionLine).ToArray();
                formatter.WriteException(commentedExceptionLines);
                var commentedMailToMessage = Language.CommentLineSymbol + " " +
#if !ENGINEONLYBUILD
 JustDecompile.Settings.Utilities.MailToMessage;
#else
                    "mailto: JustDecompilePublicFeedback@telerik.com";
#endif
                formatter.WriteExceptionMailToLink(commentedMailToMessage, exceptionLines);
            }
            else
            {
                formatter.WriteException(exceptionLines);
#if !ENGINEONLYBUILD
                formatter.WriteExceptionMailToLink(JustDecompile.Settings.Utilities.MailToMessage, exceptionLines);
#else
                formatter.WriteExceptionMailToLink("mailto: JustDecompilePublicFeedback@telerik.com", exceptionLines);
#endif
            }
        }

        protected virtual bool WriteMemberDeclaration(IMemberDefinition member, bool showCompilergenerated, bool writeDocumentation)
        {
            if (member is MethodDefinition)
            {
                WriteAttributes(member);
                WriteMethodDeclaration((MethodDefinition)member);
                WriteEndOfStatement();
                return true;
            }
            if (member is PropertyDefinition)
            {
                WriteAttributes(member);
                WritePropertyDeclaration((PropertyDefinition)member);
                WriteEndOfStatement();
                return true;
            }
            if (member is EventDefinition)
            {
                WriteAttributes(member);
                WriteEventDeclaration((EventDefinition)member);
                WriteEndOfStatement();
                return true;
            }
            if (member is FieldDefinition)
            {
                FieldDefinition fDef = (member as FieldDefinition);
                if (fDef.IsSpecialName)
                {
                    return false;
                }

                if (TryWriteEnumField(fDef))
                {
                    return true;
                }
                else
                {
                    WriteAttributes(member);
                    Write((FieldDefinition)member);
                    return true;
                }

            }
            if (member is TypeDefinition)
            {
                TypeReference memberType = (member as TypeDefinition).BaseType;
                if (memberType != null && memberType.Name == "MulticastDelegate")
                {
                    WriteAttributes(member);
                    WriteDelegate(member as TypeDefinition);
                    return true;
                }
                else
                {
                    WriteTypeDeclarationsOnlyInternal((TypeDefinition)member, writeDocumentation, showCompilergenerated);
                    return true;
                }
            }
            return true;
        }

        protected abstract void WriteDelegate(TypeDefinition delegateDefinition);

        protected virtual void WriteTypeInANewWriterIfNeeded(TypeDefinition type, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
        {
            if (this.CurrentType != type)
            {
                ILanguageWriter writer = Language.GetWriter(this.formatter, this.exceptionFormatter, this.WriteExceptionsAsComments);
                List<WritingInfo> nestedWritingInfos = writer.Write(type, writerContextService, writeDocumentation, showCompilerGeneratedMembers);
                this.writingInfos.AddRange(nestedWritingInfos);
            }
            else
            {
                WriteType(type, writeDocumentation, showCompilerGeneratedMembers);
            }
        }

		public void WriteTypeDeclaration(TypeDefinition type, IWriterContextService writerContextService, bool showCompilerGeneratedMembers = false)
		{
			this.writerContextService = writerContextService;
			this.writerContext = writerContextService.GetWriterContext(type, Language);
			this.currentWritingInfo = new WritingInfo(type);

			WriteAttributes(type, new string[] { "System.Reflection.DefaultMemberAttribute" });

			membersStack.Push(type);
			if (type.IsDelegate())
			{
				WriteDelegate(type);
				WriteLine();
				membersStack.Pop();
				return;
			}
			
			WriteTypeDeclaration(type);
			WriteLine();

			membersStack.Pop();
		}

        protected void WriteType(TypeDefinition type, bool writeDocumentation, bool showCompilerGeneratedMembers = false)
        {
            if (isStopped)
                return;

            membersStack.Push(type);

			int attributesStartIndex = this.formatter.CurrentPosition;
			// The DefaultMemberAttribute is automatically added by the compiler when creating an indexer property.
            // Writing it by hand will result in compile error, so it should be ignored.
            WriteAttributes(type, new string[] { "System.Reflection.DefaultMemberAttribute" });
			this.currentWritingInfo.MemberTokenToAttributesMap.Add(type.MetadataToken.ToUInt32(), new OffsetSpan(attributesStartIndex, this.formatter.CurrentPosition - 1));

			int decompiledCodeStartPosition = this.formatter.CurrentPosition;
            WriteTypeInternal(type, (x) => WriteMember(x, writeDocumentation, showCompilerGeneratedMembers),
                 showCompilerGeneratedMembers);
			this.currentWritingInfo.MemberTokenToDecompiledCodeMap.Add(type.MetadataToken.ToUInt32(), new OffsetSpan(decompiledCodeStartPosition, this.formatter.CurrentPosition - 1));

            membersStack.Pop();
        }

        protected virtual bool TryWriteEnumField(FieldDefinition fieldDefinition)
        {
            if (fieldDefinition.DeclaringType.IsEnum)
            {
                int startIndex = this.formatter.CurrentPosition;

				string fieldName = GetFieldName(fieldDefinition);

                WriteReference(fieldName, fieldDefinition);

                int endIndex = this.formatter.CurrentPosition - 1;
                this.currentWritingInfo.MemberDeclarationToCodePostionMap[fieldDefinition] = new OffsetSpan(startIndex, endIndex);
                if (!fieldDefinition.DeclaringType.IsDefaultEnumConstants)
                {
                    WriteLiteral(" = ");
                    WriteLiteral(string.Format(CultureInfo.InvariantCulture, "{0}", fieldDefinition.Constant.Value));
                }
                return true;
            }
            return false;
        }

        protected virtual void WriteTypeInternal(TypeDefinition type, Action<IMemberDefinition> writeMember, bool showCompilerGeneratedMembers)
        {
            string typeName = string.Empty;

            membersStack.Push(type);
            if (type.IsDelegate())
            {
                WriteDelegate(type);
                return;
            }
            typeName = WriteTypeDeclaration(type);

            int startIndex = this.formatter.CurrentPosition;

            this.formatter.WriteStartBlock();

            WriteBeginBlock();

            WriteTypeOpeningBlock(type);

            WriteTypeMembers(type, writeMember, showCompilerGeneratedMembers);

            WriteLine();
            Outdent();

            WriteEndBlock(typeName);
            membersStack.Pop();

            this.currentWritingInfo.MemberDefinitionToFoldingPositionMap[type] = new OffsetSpan(startIndex, formatter.CurrentPosition - 1);

            this.formatter.WriteEndBlock();
        }

        private void WriteTypeMembers(TypeDefinition type, Action<IMemberDefinition> writeMember, bool showCompilerGeneratedMembers,
            IEnumerable<string> attributesToSkip = null, ICollection<string> fieldsToSkip = null)
        {
            List<IMemberDefinition> members = Utilities.GetTypeMembers(type, Language, showCompilerGeneratedMembers, attributesToSkip, fieldsToSkip,
                currentWritingInfo.GeneratedFilterMethods, TypeContext.GetFieldToPropertyMap(Language).Keys);

            if (type.IsEnum)
            {
                members.Sort((x, y) => SortEnumFields(x, y));
            }

            for (int i = 0; i < members.Count; i++)
            {
                IMemberDefinition member = null;

                if (isStopped)
                {
                    return;
                }
                member = members[i];


                if (member is FieldDefinition && (member as FieldDefinition).IsSpecialName)
                {
                    continue;
                }

                membersStack.Push(member);

                bool isLastMember = (i == members.Count - 1);

                writeMember(member);

                if (type.IsEnum)
                {
                    if (!isLastMember &&
                        members[i + 1].Name != "value__"/*inserted by the compiler as last member on some enums. not written.*/)
                    {
                        WriteEnumValueSeparator();
                        WriteLine();
                    }
                }
                else
                {
                    if (!isLastMember)
                    {
                        WriteLine();
                        WriteLine();
                    }
                }

                membersStack.Pop();
            }
        }

        public override void VisitExceptionStatement(ExceptionStatement node)
        {
            WriteException(new Exception("Exception in: " + node.Member.GetFullMemberName(Language), node.ExceptionObject), node.Member);
        }

        private int SortEnumFields(IMemberDefinition first, IMemberDefinition second)
        {
            FieldDefinition firstField = first as FieldDefinition;
            FieldDefinition secondField = second as FieldDefinition;
            if (firstField == null || firstField.Constant == null || firstField.Constant.Value == null)
            {
                return 1;
            }
            if (secondField == null || secondField.Constant == null || secondField.Constant.Value == null)
            {
                return -1;
            }
            //return firstField.Constant.ToString().CompareTo(secondField.Constant.ToString());
            int valuesCompared = 0;
            if (firstField.Constant.Value.GetType().Name == "Int32")
            {
                int firstFieldValue = (int)firstField.Constant.Value;
                int secondFieldValue = (int)secondField.Constant.Value;

                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            if (firstField.Constant.Value.GetType().Name == "UInt32")
            {
                uint firstFieldValue = (uint)firstField.Constant.Value;
                uint secondFieldValue = (uint)secondField.Constant.Value;
                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            else if (firstField.Constant.Value.GetType().Name == "Int16")
            {
                short firstFieldValue = (short)firstField.Constant.Value;
                short secondFieldValue = (short)secondField.Constant.Value;
                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            else if (firstField.Constant.Value.GetType().Name == "UInt16")
            {
                ushort firstFieldValue = (ushort)firstField.Constant.Value;
                ushort secondFieldValue = (ushort)secondField.Constant.Value;
                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            else if (firstField.Constant.Value.GetType().Name == "Byte")
            {
                byte firstFieldValue = (byte)firstField.Constant.Value;
                byte secondFieldValue = (byte)secondField.Constant.Value;
                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            else if (firstField.Constant.Value.GetType().Name == "SByte")
            {
                sbyte firstFieldValue = (sbyte)firstField.Constant.Value;
                sbyte secondFieldValue = (sbyte)secondField.Constant.Value;
                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            else if (firstField.Constant.Value.GetType().Name == "Int64")
            {
                long firstFieldValue = (long)firstField.Constant.Value;
                long secondFieldValue = (long)secondField.Constant.Value;
                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            else if (firstField.Constant.Value.GetType().Name == "UInt64")
            {
                ulong firstFieldValue = (ulong)firstField.Constant.Value;
                ulong secondFieldValue = (ulong)secondField.Constant.Value;
                valuesCompared = firstFieldValue.CompareTo(secondFieldValue);
            }
            if (valuesCompared != 0)
            {
                return valuesCompared;
            }
            return firstField.Name.CompareTo(secondField.Name);
        }

        public virtual void Stop()
        {
            isStopped = true;
            Language.StopPipeline();
        }

        protected virtual void WriteTypeNamespaceStart(TypeDefinition member)
        {
        }

        protected virtual void WriteTypeNamespaceEnd(TypeDefinition member)
        {
        }

        protected virtual void WriteBeginBlock(bool inline = false)
        {
        }

        protected virtual void WriteEndBlock(string statementName)
        {
        }

        protected virtual void WriteNestedTypeWriteLine()
        {
        }

        protected virtual void WriteAttributes(IMemberDefinition member, IEnumerable<string> ignoredAttributes = null)
        {
        }

        public virtual void WriteBody(IMemberDefinition member, IWriterContextService writerContextService)
        {
            this.writerContextService = writerContextService;
            this.currentWritingInfo = new WritingInfo(member);
            this.writingInfos = new List<WritingInfo>();
            this.writingInfos.Add(this.currentWritingInfo);
            this.writerContext = writerContextService.GetWriterContext(member, Language);
            WriteBodyInternal(member);
        }

        protected virtual void WriteBodyInternal(IMemberDefinition member)
        {
        }

        protected virtual void Write(Statement statement)
        {
        }

        protected virtual void Write(Expression expression)
        {
        }

        protected virtual void WriteEndOfStatement()
        {
        }

        protected virtual void WriteEnumValueSeparator()
        {
        }

        protected virtual void WriteTypeOpeningBlock(TypeDefinition type)
        {
        }

        protected virtual void WriteEscapeLiteral(string literal)
        {
            formatter.WriteLiteral(OnConvertString(literal));
        }

        protected abstract void WriteEventDeclaration(EventDefinition @event);

        protected abstract void WriteMethodDeclaration(MethodDefinition method, bool writeDocumentation = false);

        protected abstract void WritePropertyDeclaration(PropertyDefinition property);

        protected abstract string WriteTypeDeclaration(TypeDefinition type, bool isPartial = false);

        protected abstract void Write(MethodDefinition member);

        protected abstract void Write(PropertyDefinition property);

        protected abstract void Write(EventDefinition @event);

        protected abstract void Write(FieldDefinition field);

        protected virtual void WriteNamespace(object reference, bool forceWriteNamespace = false)
        {
        }

        protected virtual string OnConvertString(string str)
        {
            return ConvertString(str);
        }

        internal static string ConvertString(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in str)
            {
                if (ch == '"')
                    sb.Append("\\\"");
                else
                    sb.Append(ConvertChar(ch));
            }
            return sb.ToString();
        }

        internal static string ConvertChar(char ch)
        {
            switch (ch)
            {
                case '\\':
                    return "\\\\";
                case '\0':
                    return "\\0";
                case '\a':
                    return "\\a";
                case '\b':
                    return "\\b";
                case '\f':
                    return "\\f";
                case '\n':
                    return "\\n";
                case '\r':
                    return "\\r";
                case '\t':
                    return "\\t";
                case '\v':
                    return "\\v";
                default:
                    if (char.IsControl(ch) || char.IsSurrogate(ch) ||
                        (char.IsWhiteSpace(ch) && ch != ' '))
                    {
                        return "\\u" + ((int)ch).ToString("x4");
                    }
                    else
                    {
                        return ch.ToString();
                    }
            }
        }

        public virtual void WriteComment(string comment)
        {
            this.formatter.WriteComment(Language.CommentLineSymbol + " " + comment);
        }

        protected virtual void StartWritingComment()
        {
            this.isWritingComment = true;
            formatter.StartWritingComment();
        }

        protected virtual void EndWritingComment()
        {
            this.isWritingComment = false;
            formatter.EndWritingComment();
        }

        #region PartialTypes

        protected void WritePartialType(TypeDefinition type, bool writeDocumentation, bool showCompilerGeneratedMembers = false, ICollection<string> fieldsToSkip = null)
        {
            if (isStopped)
                return;

            /// Remove the CodeGeneratedAttribute
            WriteAttributes(type, new string[1] { "System.CodeDom.Compiler.GeneratedCodeAttribute" });

            WritePartialType(type, (x) => WriteInternal(x, writeDocumentation, showCompilerGeneratedMembers), false,
                showCompilerGeneratedMembers, fieldsToSkip: fieldsToSkip);
        }

        protected virtual void WritePartialType(TypeDefinition type, Action<IMemberDefinition> writeMember, bool writeNewLine, bool showCompilerGeneratedMembers,
            ICollection<string> fieldsToSkip = null)
        {
            string typeName = string.Empty;

            typeName = WriteTypeDeclaration(type, true);
            WriteBeginBlock();
            WriteTypeOpeningBlock(type);

            WriteTypeMembers(type, writeMember, showCompilerGeneratedMembers,
                new string[1] { "System.Diagnostics.DebuggerNonUserCodeAttribute" }, fieldsToSkip);

            WriteLine();
            Outdent();

            WriteEndBlock(typeName);
        }
        #endregion

        public abstract void WriteMemberNavigationName(object memberDefinition, bool renameInvalidMembers);

        public void WriteMemberEscapedOnlyName(object memberDefinition)
        {
            string name = GetMemberEscapedOnlyName(memberDefinition);

            this.formatter.Write(name);
        }

        protected string GetMemberEscapedOnlyName(object memberDefinition)
        {
            if (memberDefinition == null)
            {
                return string.Empty;
            }
            if (memberDefinition is TypeReference)
            {
                var typeRef = ((TypeReference)memberDefinition).GetElementType();

                if (typeRef.IsGenericParameter)
                {
                    return GetGenericParameterName((GenericParameter)typeRef);
                }
                if (typeRef.HasGenericParameters)
                {
                    var nameBuilder = new StringBuilder(Language.ReplaceInvalidCharactersInIdentifier(typeRef.Name));

                    this.NormalizeGenericParams(typeRef.GenericParameters, nameBuilder);

                    return nameBuilder.ToString();
                }
                else if (typeRef.IsArray)
                {
                    ArrayType arrayType = typeRef as ArrayType;

                    return GetMemberEscapedOnlyName(arrayType.ElementType) + "[]";
                }
                else if (memberDefinition is TypeDefinition)
                {
                    return ((TypeDefinition)memberDefinition).GetFriendlyMemberName(Language);
                }
                else
                {
                    return typeRef.GetFriendlyTypeName(Language);
                }
            }
            else if (memberDefinition is MethodReference)
            {
                var methodRef = ((MethodReference)memberDefinition).GetElementMethod();

                if (methodRef.HasGenericParameters)
                {
                    var nameBuilder = new StringBuilder(Language.ReplaceInvalidCharactersInIdentifier(methodRef.Name));

                    this.NormalizeGenericParams(methodRef.GenericParameters, nameBuilder);
                   
                    return nameBuilder.ToString();
                }
                else if (memberDefinition is MethodDefinition)
                {
                    var methodDefinition = memberDefinition as MethodDefinition;

                    return methodDefinition.GetFriendlyMemberName(Language);
                }
                else
                {
                    return Language.ReplaceInvalidCharactersInIdentifier(((MethodReference)memberDefinition).Name);
                }
            }
            else if (memberDefinition is IMemberDefinition)
            {
                var property = memberDefinition as IMemberDefinition;

                return property.GetFriendlyMemberName(Language);
            }
            else if (memberDefinition is ParameterReference)
            {
                return Language.ReplaceInvalidCharactersInIdentifier(((ParameterReference)memberDefinition).Name);
            }
            else if (memberDefinition is MemberReference)
            {
                return Language.ReplaceInvalidCharactersInIdentifier(((MemberReference)memberDefinition).Name);
            }
            return memberDefinition.ToString();
        }

        private void NormalizeGenericParams(Collection<GenericParameter> genericCollection, StringBuilder nameBuilder)
        {
            nameBuilder.Append("<");

            for (int i = 0; i < genericCollection.Count; i++) 
            {
                GenericParameter genericParameter = genericCollection[i];

                nameBuilder.Append(GetGenericParameterName(genericParameter));

                if (i < genericCollection.Count - 1)
                {
                    nameBuilder.Append(",");
                }
            }
            nameBuilder.Append(">");
        }

        private string GetGenericParameterName(GenericParameter genericParameter)
        {
            if (genericParameter.Owner is TypeReference)
            {
                return "!" + genericParameter.Position;
            }
            else
            {
                return "!!" + genericParameter.Position;
            }
        }

        public virtual void WriteNamespaceNavigationName(string memberReferenceName, bool renameInvalidMembers)
        {
            string[] splitValues = memberReferenceName.Split(new char[] { '.' });

            StringBuilder sb = new StringBuilder(splitValues.Length);

            for (int i = 0; i < splitValues.Length; i++)
            {
                string splitValue = splitValues[i];
                if (!Language.IsValidIdentifier(splitValue) && renameInvalidMembers)
                {
                    sb.Append(Language.ReplaceInvalidCharactersInIdentifier(splitValue));
                }
                else
                {
                    sb.Append(splitValue);
                }
                if (i < splitValues.Length - 1)
                {
                    sb.Append(".");
                }
            }
            this.formatter.Write(sb.ToString());
        }

        public abstract void WriteMemberNavigationPathFullName(object member);

        public override void VisitMemberHandleExpression(MemberHandleExpression node)
        {
            throw new NotSupportedException();
        }
    }
}