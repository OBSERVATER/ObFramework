using System.Collections.Generic;

namespace ObFramework.Utility.Pipeline
{
    public sealed class PipelineBuilder<TContext>
    {
        private readonly List<PipelineNode<TContext>> _nodes = new List<PipelineNode<TContext>>();

        public PipelineBuilder<TContext> Add(IPipelineStep<TContext> step)
        {
            _nodes.Add(new PipelineNode<TContext>(step));
            return this;
        }

        public PipelineBuilder<TContext> Use(IPipelineMiddleware<TContext> middleware)
        {
            _nodes.Add(new PipelineNode<TContext>(middleware));
            return this;
        }

        public Pipeline<TContext> Build() => new Pipeline<TContext>(_nodes.ToArray());
    }
}
