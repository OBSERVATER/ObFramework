namespace ObFramework.Utility.Pipeline
{
    public interface IPipelineMiddleware<TContext>
    {
        void Invoke(ref TContext context, ref PipelineNext<TContext> next);
    }
}