namespace ObFramework.Utility.Pipeline
{
    public interface IPipelineStep<TContext>
    {
        StepResult Execute(ref TContext context);
    }
}