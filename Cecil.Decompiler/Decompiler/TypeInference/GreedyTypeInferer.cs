using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Telerik.JustDecompiler.Ast.Expressions;
using Telerik.JustDecompiler.Decompiler.DefineUseAnalysis;

namespace Telerik.JustDecompiler.Decompiler.TypeInference
{
    class GreedyTypeInferer : TypeInferer
    {
        private HashSet<VariableReference> resolvedVariables;

        public GreedyTypeInferer(DecompilationContext context, Dictionary<int, Expression> offsetToExpression)
            : base(context, offsetToExpression)
        {
            this.resolvedVariables = new HashSet<VariableReference>();
        }

        new public HashSet<VariableReference> InferTypes()
        {
            resolvedVariables = new HashSet<VariableReference>();
            bool fixedVar = false;
            do
            {
                fixedVar = false;
                IList<VariableReference> variables = GetVariablesToInfer();
                foreach (VariableReference variable in variables)
                {
                    TypeReference type;
                    if (IsOnlyAssignedOnce(variable, out type))
                    {
                        fixedVar = FixVariableType(variable, type) || fixedVar;
                    }
                    else if (IsOnlyUsedOnce(variable, out type))
                    {
                        fixedVar = FixVariableType(variable, type) || fixedVar;
                    }
                }
            } while (fixedVar);
            return resolvedVariables;
        }

        private bool IsOnlyUsedOnce(VariableReference variable, out TypeReference type)
        {
            type = null;
            StackVariableDefineUseInfo defineUseInfo;
            if (!context.MethodContext.StackData.VariableToDefineUseInfo.TryGetValue(variable.Resolve(), out defineUseInfo))
            {
                throw new Exception("Define/use info not found.");
            }

            if (defineUseInfo.UsedAt.Count != 1)
            {
                return false;
            }

            int instructionOffset = First(defineUseInfo.UsedAt);
            UsedAsTypeHelper uath = new UsedAsTypeHelper(context.MethodContext);
            Instruction instruction = context.MethodContext.ControlFlowGraph.OffsetToInstruction[instructionOffset];
            type = uath.GetUseExpressionTypeNode(instruction, offsetToExpression[instructionOffset], variable);
            return true;
        }

        private T First<T>(IEnumerable<T> collection)
        {
            using (IEnumerator<T> enumerator = collection.GetEnumerator())
            {
                enumerator.MoveNext();
                return enumerator.Current;
            }
        }

        private bool FixVariableType(VariableReference variable, TypeReference type)
        {
            variable.VariableType = type;
            if (type != null)
            {
                resolvedVariables.Add(variable);
                return true;
            }
            return false;
        }

        private bool IsOnlyAssignedOnce(VariableReference variable, out TypeReference type)
        {
            type = null;
            StackVariableDefineUseInfo defineUseInfo;
            if (!context.MethodContext.StackData.VariableToDefineUseInfo.TryGetValue(variable.Resolve(), out defineUseInfo))
            {
                throw new Exception("Define/use info not found.");
            }

            if (defineUseInfo.DefinedAt.Count != 1)
            {
                return false;
            }

            type = offsetToExpression[First(defineUseInfo.DefinedAt)].ExpressionType;
            return true;

        }

        private IList<VariableReference> GetVariablesToInfer()
        {
            List<VariableReference> result = new List<VariableReference>();
            foreach (VariableDefinition variable in context.MethodContext.StackData.VariableToDefineUseInfo.Keys)
            {
                if (variable.VariableType == null)
                {
                    result.Add(variable);
                }
                else
                {
                    resolvedVariables.Add(variable);
                }
            }
            return result;
        }
    }
}
