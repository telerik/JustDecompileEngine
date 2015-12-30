#region license
//
//	(C) 2005 - 2007 db4objects Inc. http://www.db4o.com
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

// Warning: generated do not edit

using System.Collections.Generic;
using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Telerik.JustDecompiler.Ast.Expressions
{
    public class BinaryExpression : Expression
    {
        private bool typeSet = false;
        private Expression left;
        private Expression right;
        private TypeReference type;
        private readonly TypeSystem typeSystem;
        private static Dictionary<BinaryOperator, int> operatorsPriority;
        private bool? isObjectComparison;
        public bool IsOverridenOperation { get; private set; }

        private BinaryExpression(BinaryOperator @operator, Expression left, Expression right, bool isChecked, IEnumerable<Instruction> instructions, bool isOverridenOperation)
            : base(instructions)
        {
            this.Operator = @operator;
            this.left = left;
            this.right = right;
            this.IsOverridenOperation = isOverridenOperation;
            this.IsChecked = isChecked;
            FixIfNullComparison();
        }

        private static bool DetermineIsChecked(IEnumerable<Instruction> instructions)
        {
            if (instructions == null)
            {
                return false;
            }

            foreach (Instruction instruction in instructions)
            {
                switch (instruction.OpCode.Code)
                {
                    case Code.Add_Ovf:
                    case Code.Add_Ovf_Un:
                    case Code.Sub_Ovf:
                    case Code.Sub_Ovf_Un:
                    case Code.Mul_Ovf:
                    case Code.Mul_Ovf_Un:
                        return true;
                }
            }

            return false;
        }

        public BinaryExpression(BinaryOperator @operator, Expression left, Expression right,
            TypeReference expressionType, TypeSystem typeSystem, IEnumerable<Instruction> instructions, bool isOverridenOperation = false)
            : this(@operator, left, right, DetermineIsChecked(instructions), instructions, isOverridenOperation)
        {
            this.ExpressionType = expressionType;
            this.typeSystem = typeSystem;
        }

        public BinaryExpression(BinaryOperator @operator, Expression left, Expression right, TypeSystem typeSystem, IEnumerable<Instruction> instructions, bool isOverridenOperation = false)
            : this(@operator, left, right, typeSystem, DetermineIsChecked(instructions), instructions, isOverridenOperation)
        {
        }

        public BinaryExpression(BinaryOperator @operator, Expression left, Expression right, TypeSystem typeSystem, bool isChecked, IEnumerable<Instruction> instructions, bool isOverridenOperation)
            : this(@operator, left, right, isChecked, instructions, isOverridenOperation)
        {
            this.typeSystem = typeSystem;
            UpdateType();
        }

        private void FixIfNullComparison()
        {
            if (!IsComparisonExpression || this.Operator == BinaryOperator.ValueEquality || this.Operator == BinaryOperator.ValueInequality)
            {
                return;
            }

            if (this.left.CodeNodeType == CodeNodeType.LiteralExpression && (this.left as LiteralExpression).Value == null ||
                this.right.CodeNodeType == CodeNodeType.LiteralExpression && (this.right as LiteralExpression).Value == null)
            {
                this.Operator = (this.Operator == BinaryOperator.LessThan || this.Operator == BinaryOperator.GreaterThan) ?
                    BinaryOperator.ValueInequality : BinaryOperator.ValueEquality;
            }
        }

        public override IEnumerable<ICodeNode> Children
        {
            get
            {
                yield return left;
                yield return right;
            }
        }

        public override bool Equals(Expression other)
        {
            if (!(other is BinaryExpression))
            {
                return false;
            }
            BinaryExpression binEx = other as BinaryExpression;
            if (this.Operator != binEx.Operator)
            {
                return false;
            }
            return this.Left.Equals(binEx.Left) && this.right.Equals(binEx.Right);
        }

        public override Expression Clone()
        {
            BinaryExpression result = new BinaryExpression(Operator, Left.Clone(), Right.Clone(), this.ExpressionType, typeSystem, instructions) { IsChecked = this.IsChecked, IsOverridenOperation = this.IsOverridenOperation, IsObjectComparison = this.IsObjectComparison };
            return result;
        }

        public override Expression CloneExpressionOnly()
        {
            BinaryExpression result = new BinaryExpression(Operator, Left.CloneExpressionOnly(), Right.CloneExpressionOnly(), this.ExpressionType, typeSystem, null) { IsChecked = this.IsChecked, IsOverridenOperation = this.IsOverridenOperation, IsObjectComparison = this.IsObjectComparison };
            return result;
        }

        private static void InitializeOperatorsPrecedence()
        {
            //TODO: Update with new operators
            operatorsPriority = new Dictionary<BinaryOperator, int>();
            //order of precedance taken from http://msdn.microsoft.com/en-us/library/aa691323(v=VS.71).aspx
            operatorsPriority.Add(BinaryOperator.Multiply, 1);
            operatorsPriority.Add(BinaryOperator.Divide, 1);
            operatorsPriority.Add(BinaryOperator.Modulo, 1);

            operatorsPriority.Add(BinaryOperator.Add, 2);
            operatorsPriority.Add(BinaryOperator.Subtract, 2);

            operatorsPriority.Add(BinaryOperator.LeftShift, 3);
            operatorsPriority.Add(BinaryOperator.RightShift, 3);

            operatorsPriority.Add(BinaryOperator.LessThan, 4);
            operatorsPriority.Add(BinaryOperator.LessThanOrEqual, 4);
            operatorsPriority.Add(BinaryOperator.GreaterThan, 4);
            operatorsPriority.Add(BinaryOperator.GreaterThanOrEqual, 4);

            operatorsPriority.Add(BinaryOperator.ValueEquality, 5);
            operatorsPriority.Add(BinaryOperator.ValueInequality, 5);

            operatorsPriority.Add(BinaryOperator.BitwiseAnd, 6);

            operatorsPriority.Add(BinaryOperator.BitwiseXor, 7);

            operatorsPriority.Add(BinaryOperator.BitwiseOr, 8);

            operatorsPriority.Add(BinaryOperator.LogicalAnd, 9);

            operatorsPriority.Add(BinaryOperator.LogicalOr, 10);

            operatorsPriority.Add(BinaryOperator.NullCoalesce, 11);

            operatorsPriority.Add(BinaryOperator.Assign, 12);
            operatorsPriority.Add(BinaryOperator.AddAssign, 12);
            operatorsPriority.Add(BinaryOperator.SubtractAssign, 12);

            operatorsPriority.Add(BinaryOperator.None, 13);
        }

        public void UpdateType()
        {
            if (Operator == BinaryOperator.NullCoalesce)
            {
                ExpressionType = Left.ExpressionType;
                return;
            }
            if (IsLogicalExpression || IsComparisonExpression)
            {
                type = typeSystem.Boolean;
                return;
            }
            if (Left.HasType && Right.HasType && Left.ExpressionType.FullName == Right.ExpressionType.FullName)
            {
                type = Left.ExpressionType;
                return;
            }

            if (IsAssignmentExpression)
            {
                type = left.ExpressionType;
                return;
            }

            GetExpressionType();
        }

        public BinaryOperator Operator { get; set; }

        public Expression Left
        {
            get { return left; }
            set
            {
                this.left = value;
                if (!typeSet)
                {
                    UpdateType();
                }
            }
        }

        public Expression Right
        {
            get { return right; }
            set
            {
                this.right = value;
                if (!typeSet)
                {
                    UpdateType();
                }
            }
        }

        private static readonly object locker = new object();

        public int OperatorPriority
        {
            get
            {
                lock (locker)
                {
                    if (operatorsPriority == null)
                    {
                        InitializeOperatorsPrecedence();
                    }
                }

                return operatorsPriority[this.Operator];
            }

        }

        public int CompareOperators(BinaryExpression other)
        {
            return this.OperatorPriority.CompareTo(other.OperatorPriority);
        }

        public bool IsChecked { get; private set; }

        public bool IsLogicalExpression
        {
            get
            {
                return this.Operator == BinaryOperator.LogicalAnd || this.Operator == BinaryOperator.LogicalOr;
            }
        }

        public bool IsAssignmentExpression
        {
            get
            {
                return this.Operator == BinaryOperator.Assign;
            }
        }

        public bool IsSelfAssign
        {
            get
            {
                return this.Operator == BinaryOperator.AddAssign ||
                    this.Operator == BinaryOperator.SubtractAssign ||
                    this.Operator == BinaryOperator.DivideAssign ||
                    this.Operator == BinaryOperator.MultiplyAssign ||
                    this.Operator == BinaryOperator.LeftShiftAssign ||
                    this.Operator == BinaryOperator.ModuloAssign ||
                    this.Operator == BinaryOperator.OrAssign ||
                    this.Operator == BinaryOperator.RightShiftAssign ||
                    this.Operator == BinaryOperator.AndAssign ||
                    this.Operator == BinaryOperator.XorAssign;
            }
        }

        public bool IsComparisonExpression
        {
            get
            {
                switch (Operator)
                {
                    case BinaryOperator.ValueEquality:
                    case BinaryOperator.ValueInequality:
                    case BinaryOperator.LessThan:
                    case BinaryOperator.LessThanOrEqual:
                    case BinaryOperator.GreaterThan:
                    case BinaryOperator.GreaterThanOrEqual:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public bool IsObjectComparison
        {
            get
            {
                if (!this.isObjectComparison.HasValue)
                {
                    if (!this.IsComparisonExpression || (this.Operator != BinaryOperator.ValueEquality && this.Operator != BinaryOperator.ValueInequality))
                    {
                        this.isObjectComparison = false;
                    }
                    else if (!IsValidObjectComparisonNode(this.Left.CodeNodeType) ||
                             !IsValidObjectComparisonNode(this.Right.CodeNodeType))
                    {
                        this.isObjectComparison = false;
                    }
                    // TODO: The following else-if must be removed when bug 284860 (the one with the Resolver) is fixed.
                    else if (this.Left.ExpressionType is ArrayType || this.Right.ExpressionType is ArrayType)
                    {
                        this.isObjectComparison = false;
                    }
                    else if (this.instructions.Any(i => i.OpCode.Name == "call" &&
                            ((i.Operand as MethodReference).Name == "op_Equality" || (i.Operand as MethodReference).Name == "op_Inequality")))
                    {
                        this.isObjectComparison = false;
                    }
                    else
                    {
                        bool isLeftReferenceType = !this.Left.ExpressionType.IsValueType;
                        bool isRightReferenceType = !this.Right.ExpressionType.IsValueType;
                        if (this.Left.ExpressionType.IsRequiredModifier || this.Left.ExpressionType.IsOptionalModifier)
                        {
                            TypeDefinition resolvedLeft = this.Left.ExpressionType.Resolve();
                            isLeftReferenceType = !resolvedLeft.IsValueType;
                        }

                        if (this.Right.ExpressionType.IsRequiredModifier || this.Right.ExpressionType.IsOptionalModifier)
                        {
                            TypeDefinition resolvedRight = this.Right.ExpressionType.Resolve();
                            isRightReferenceType = !resolvedRight.IsValueType;
                        }
                        this.isObjectComparison = isLeftReferenceType && isRightReferenceType;
                    }
                }

                return this.isObjectComparison.Value;
            }

            internal set
            {
                this.isObjectComparison = value;
            }
        }

        private bool IsValidObjectComparisonNode(CodeNodeType codeNodeType)
        {
            return codeNodeType == CodeNodeType.AnonymousObjectCreationExpression ||
                   codeNodeType == CodeNodeType.ArgumentReferenceExpression ||
                   codeNodeType == CodeNodeType.FieldReferenceExpression ||
                   codeNodeType == CodeNodeType.LiteralExpression ||
                   codeNodeType == CodeNodeType.MethodInvocationExpression ||
                   codeNodeType == CodeNodeType.ObjectCreationExpression ||
                   codeNodeType == CodeNodeType.PropertyReferenceExpression ||
                   codeNodeType == CodeNodeType.VariableReferenceExpression ||
                   codeNodeType == CodeNodeType.ThisReferenceExpression ||
                   codeNodeType == CodeNodeType.ArrayCreationExpression ||
                   codeNodeType == CodeNodeType.ArrayIndexerExpression ||
                   codeNodeType == CodeNodeType.LambdaExpression ||
                   codeNodeType == CodeNodeType.LinqQueryExpression ||
                   codeNodeType == CodeNodeType.UnaryExpression;
        }

        public override TypeReference ExpressionType
        {
            get
            {
                if (type == null)
                {
                    UpdateType();
                }
                return type;
            }
            set
            {
                typeSet = true;
                type = value;
            }
        }

        public override bool HasType
        {
            get
            {
                if (type == null)
                {
                    UpdateType();
                }
                return type != null;
            }
        }

        public override CodeNodeType CodeNodeType
        {
            get
            {
                return CodeNodeType.BinaryExpression;
            }
        }

        /// <summary>
        /// Resolves the type of a binary expression. Also sets the ExpressionType property of the binary expression with the found result.
        /// </summary>
        /// <param name="binary">The binary expression whose type is being resolved.</param>
        /// <param name="typeSystem">The type system for the current assembly.</param>
        /// <returns>Returns TypeReference to the type of the binary expression or null if no type can be resolved.</returns>
        private void GetExpressionType()
        {
            TypeReference result;
            result = InferForPointers(Left.ExpressionType, Right.ExpressionType);
            if (result != null)
            {
                ExpressionType = result;
                return;
            }
            TypeDefinition leftType = GetExpressionTypeDefinition(Left);
            TypeDefinition rightType = GetExpressionTypeDefinition(Right);
            if (leftType == null || rightType == null)
            {
                return;
            }
            if (leftType.FullName == rightType.FullName)
            {
                ExpressionType = leftType;
                return;
            }
            if (HandleDateTime(leftType, rightType))
            {
                return;
            }
            //check if one of the sides is of enum type
            //if so, no casts should be added at this point, since literals are not yet converted to enumeration members 
            if (leftType.IsEnum ^ rightType.IsEnum)
            {
                if (leftType.IsEnum)
                {
                    ExpressionType = leftType;
                    return;
                }
                else
                {
                    ExpressionType = rightType;
                    return;
                }
            }
            result = GetContainingType(leftType, rightType);

            ExpressionType = result;
        }

        /// <summary>
        /// Deals with pointer arithmetics.
        /// </summary>
        /// <param name="firstType">The type of the left side of the binary expression.</param>
        /// <param name="secondType">The type of the right side of the binary expression.</param>
        /// <param name="typeSystem">The type system for the current assembly</param>
        /// <returns>Returns TypeReference to the infered type, or null if no inference was made.</returns>
        private TypeReference InferForPointers(TypeReference firstType, TypeReference secondType)
        {
            if (firstType == null || secondType == null)
            {
                return null;
            }
            if ((firstType.IsPointer || firstType.IsPinned || firstType.IsByReference) && IsIntegerType(secondType))
            {
                return firstType;
            }
            if ((secondType.IsPointer || secondType.IsPinned || secondType.IsByReference) && IsIntegerType(firstType))
            {
                return secondType;
            }
            return null;
        }

        private bool IsIntegerType(TypeReference type)
        {
            if (type.FullName == "System.Int32" ||  //most common case
                type.FullName == "System.UInt32" ||	// it's negation
                type.FullName == "System.Byte" ||   // others are sorted by type size
                type.FullName == "System.SByte" ||
                type.FullName == "System.Int16" ||
                type.FullName == "System.UInt16" ||
                type.FullName == "System.Int64" ||
                type.FullName == "System.UInt64")
            {
                return true;
            }
            return false;
        }

        private bool HandleDateTime(TypeReference firstType, TypeReference secondType)
        {
            if (firstType.FullName == "System.DateTime" && secondType.FullName == "System.TimeSpan")
            {
                ExpressionType = firstType;
                return true;
            }
            if (secondType.FullName == "System.DateTime" && firstType.FullName == "System.TimeSpan")
            {
                ExpressionType = secondType;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the TypeDefinition for the type of the supplied Expression.
        /// </summary>
        /// <param name="ex">The expression.</param>
        /// <param name="typeSystem">The type system for the current assembly.</param>
        /// <returns>Returns TypeDefinition for the type of the supplied expression, or null if the definition cannot be resolved.</returns>
        private TypeDefinition GetExpressionTypeDefinition(Expression ex)
        {
            TypeReference typeRef = ex.ExpressionType;

            if (typeRef == null)
            {
                return null;
            }
            TypeDefinition type;
            if (typeRef.IsPointer)
            {
                type = typeSystem.IntPtr.Resolve();
            }
            else
            {
                type = typeRef.Resolve();
            }
            return type;
        }

        /// <summary>
        /// Works only on primitive types.
        /// </summary>
        /// <param name="leftType"></param>
        /// <param name="rightType"></param>
        /// <returns>Reference to the bigger of the supplied types.</returns>
        private TypeReference GetContainingType(TypeDefinition leftType, TypeDefinition rightType)
        {
            if (leftType == rightType)
            {
                return rightType;
            }
            if (leftType == null)
            {
                return rightType;
            }
            if (rightType == null)
            {
                return leftType;
            }

            int leftSideIndex = GetTypeIndex(leftType);
            int rightSideIndex = GetTypeIndex(rightType);
            if (leftSideIndex > rightSideIndex)
            {
                return leftType;
            }
            else
            {
                return rightType;
            }
        }

        /// <summary>
        /// Gets and integer value, representing the size of the type. Works only on primitive types and enumerations.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>Returns integer representation of the type. The bigger the integer, the bigger the type.</returns>
        private int GetTypeIndex(TypeDefinition type)
        {
            if (type.IsEnum)
            {
                FieldDefinition valueField = null;
                foreach (FieldDefinition x in type.Fields)
                {
                    if (x.Name == "value__")
                    {
                        valueField = x;
                        break;
                    }
                }
                type = valueField.FieldType.Resolve();
            }
            switch (type.FullName)
            {
                case "System.Boolean":
                    return 0;
                case "System.SByte":
                case "System.Byte":
                    return 1;
                case "System.Char":
                    return 2;
                case "System.Int16":
                    return 3;
                case "System.UInt16":
                    return 4;
                case "System.Int32":
                    return 5;
                case "System.UInt32":
                    return 6;
                case "System.IntPtr":
                case "System.Int64":
                    return 7;
                case "System.UIntPtr":
                case "System.UInt64":
                    return 8;
                case "System.Single":
                    return 9;
                case "System.Double":
                    return 10;
                case "System.Decimal":
                    return 11;
                default:
                    throw new NotSupportedException(string.Format("Not supported type {0}.", type.FullName));
            }
        }
    }
}