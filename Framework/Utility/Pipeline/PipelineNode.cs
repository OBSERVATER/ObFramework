namespace ObFramework.Utility.Pipeline
{
    internal readonly struct PipelineNode<TContext>
    {
        internal readonly IPipelineStep<TContext> Step;
        internal readonly IPipelineMiddleware<TContext> Middleware;

        public PipelineNode(IPipelineStep<TContext> step) : this()
        {
            Middleware = null;
            Step = step;
        }

        public PipelineNode(IPipelineMiddleware<TContext> middleware) : this()
        {
            Step = null;
            Middleware = middleware;
        }
    }
}
