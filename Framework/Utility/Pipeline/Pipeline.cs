using System;

namespace ObFramework.Utility.Pipeline
{
    /// <summary>An immutable, built pipeline. Reuse with independent contexts.</summary>
    public readonly struct Pipeline<TContext>
    {
        private readonly PipelineNode<TContext>[] _nodes;

        internal Pipeline(PipelineNode<TContext>[] nodes)
        {
            _nodes = nodes;
        }

        /// <summary>
        /// Executes all reachable nodes and returns the possibly modified context.
        /// For mutable structs, assign the return value or use Execute(ref context).
        /// </summary>
        public TContext Execute(TContext context)
        {
            ExecuteFrom(0, ref context);
            return context;
        }

        /// <summary>Executes without copying the caller's context, including structs.</summary>
        public void Execute(ref TContext context) => ExecuteFrom(0, ref context);

        internal void ExecuteFrom(int index, ref TContext context)
        {
            for (var i = index; i < _nodes.Length; i++)
            {
                var node = _nodes[i];
                if (node.Step != null)
                {
                    var result = node.Step.Execute(ref context);
                    if (result == StepResult.Continue) continue;
                    if (result == StepResult.Stop) return;
                    throw new InvalidOperationException("Step returned an invalid StepResult.");
                }

                var next = new PipelineNext<TContext>(this, i + 1);
                node.Middleware.Invoke(ref context, ref next);
                return;
            }
        }
    }
}
