namespace ObFramework.Utility.Pipeline
{
    public class PipelineNext<TContext>
    {
        private Pipeline<TContext> pipeline;
        private int v;
        private bool _called;
        public PipelineNext(Pipeline<TContext> pipeline, int v)
        {
            this.pipeline = pipeline;
            this.v = v;
            _called = false;
        }

        public void Invoke(ref TContext context)
        {
            if (_called) { return; }
            _called = true;
            pipeline.ExecuteFrom(v, ref context);
        }
    }
}