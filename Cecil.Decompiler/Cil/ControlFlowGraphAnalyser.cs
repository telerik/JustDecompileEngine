using System.Collections.Generic;

namespace Telerik.JustDecompiler.Cil
{
	internal class ControlFlowGraphAnalyser
	{
		ControlFlowGraph cfg;
		List<InstructionBlock> verticesThatPartitionGraph;
		uint mostDistantInstructionBlockIndexReached;

		internal ControlFlowGraphAnalyser(ControlFlowGraph TheGraph)
		{
			cfg = TheGraph;
			verticesThatPartitionGraph = new List<InstructionBlock>();
			mostDistantInstructionBlockIndexReached = 0;
			MarkPartitioningVertices(0);
		}

		private void MarkPartitioningVertices(int startInstructionBlockIndex)
		{
			InstructionBlock currentBlock = cfg.Blocks[startInstructionBlockIndex];

			InstructionBlock mostDistantSuccessor = FindMostDistantSuccessor(currentBlock);

			if (mostDistantSuccessor == null)
			{
				return;
			}

			if (mostDistantSuccessor.Index == cfg.Blocks.Length - 1)
			{
				return;
			}

			InstructionBlock mostDistantSuccessorCandidate = null;
			for (int index = currentBlock.Index + 1; index <= mostDistantSuccessor.Index; index++)
			{
				if (mostDistantSuccessorCandidate == null)
				{
					mostDistantSuccessorCandidate = FindMostDistantSuccessor(cfg.Blocks[index]);
				}
				else
				{
					InstructionBlock successor = FindMostDistantSuccessor(cfg.Blocks[index]);
					if (successor.Index > mostDistantSuccessorCandidate.Index)
					{
						mostDistantSuccessorCandidate = successor;
					}
				}
			}


			if (mostDistantSuccessor.Index >= mostDistantInstructionBlockIndexReached)
			{
				if (mostDistantSuccessorCandidate != null)
				{
					if (mostDistantSuccessorCandidate.Index <= mostDistantSuccessor.Index)
					{
						verticesThatPartitionGraph.Add(mostDistantSuccessor);
					}
				}
				else
				{
					verticesThatPartitionGraph.Add(mostDistantSuccessor);
				}
			}
			else
			{
				if (mostDistantSuccessorCandidate != null)
				{
					if (mostDistantSuccessorCandidate.Index > mostDistantInstructionBlockIndexReached)
					{
						mostDistantInstructionBlockIndexReached = (uint)mostDistantSuccessorCandidate.Index;
					}
				}				
			}

			MarkPartitioningVertices(mostDistantSuccessor.Index);
		}

		private InstructionBlock FindMostDistantSuccessor(InstructionBlock currentBlock)
		{
			InstructionBlock mostDistantSuccessor = null;
			foreach (InstructionBlock successor in currentBlock.Successors)
			{
				if (mostDistantSuccessor == null)
				{
					if (successor.Index > currentBlock.Index)
					{
						mostDistantSuccessor = successor;
					}
				}
				else
				{
					if (successor.Index > mostDistantSuccessor.Index)	
					{
						mostDistantSuccessor = successor;
					}
				}
			}
			return mostDistantSuccessor;
		}

	}
}
